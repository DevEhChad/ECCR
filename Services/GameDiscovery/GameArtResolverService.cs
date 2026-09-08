using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ECCR.Models;

namespace ECCR.Services.GameDiscovery;

/// <summary>
/// Fills in <see cref="GameInfo.BannerImagePath"/> for any game a discovery provider (or
/// <see cref="GameLibraryService.AddManualGame"/>) couldn't find art for. Tried per game, in
/// order: Steam's local grid cache, an already-downloaded art cache on disk, a network fetch
/// from Steam's public CDN or Microsoft's public catalog API (Steam/Xbox only - see
/// <see cref="ResolveOneAsync"/>), and finally an exe-icon-extraction fallback composited onto a
/// blurred portrait banner. Every network attempt is short-timeout and silently swallows any
/// failure (offline, 404, DNS failure, unexpected response shape) - the icon fallback always
/// covers it. Run by <see cref="GameLibraryService"/> as a fire-and-forget pass after
/// <see cref="GameLibraryService.RescanAsync"/>, so it never blocks the scan itself - cards show
/// placeholders immediately and banners pop in as each game resolves.
/// </summary>
public class GameArtResolverService
{
    /// <summary>Raised once per game whose <see cref="GameInfo.BannerImagePath"/> was just set - off a background thread, so subscribers that touch UI-bound state must marshal back to the UI thread themselves.</summary>
    public event Action<GameInfo>? ArtResolved;

    private static readonly HttpClient HttpClient = new();

    private static readonly string GameArtCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECCR", "GameArt");

    /// <summary>Every remote-art fetch already attempted this app run, keyed by "steam:{appid}"/"xbox:{pfn}" - keeps a game whose real art genuinely doesn't exist upstream (delisted, no catalog entry) from re-hitting the network on every manual rescan in the same session. Not persisted - a fresh app launch is allowed to try again once.</summary>
    private readonly ConcurrentDictionary<string, byte> _attemptedRemoteArt = new();

    public async Task ResolveMissingArtAsync(IEnumerable<GameInfo> games, CancellationToken ct = default)
    {
        var pending = games.Where(g => string.IsNullOrEmpty(g.BannerImagePath)).ToList();
        if (pending.Count == 0) return;

        var tasks = pending.Select(game => Task.Run(async () =>
        {
            try
            {
                await ResolveOneAsync(game, ct).ConfigureAwait(false);
            }
            catch { /* one game's failed art resolution never affects the rest of the batch */ }
        }, ct));

        await Task.WhenAll(tasks);
    }

    private async Task ResolveOneAsync(GameInfo game, CancellationToken ct)
    {
        string? banner = null;

        if (game.Source == GameSource.Steam && !string.IsNullOrEmpty(game.LauncherAppId))
        {
            banner = SteamGameDiscoveryProvider.FindLocalBoxArt(game.LauncherAppId) ?? CachedArtPathIfExists(game.LauncherAppId);

            if (banner == null && _attemptedRemoteArt.TryAdd($"steam:{game.LauncherAppId}", 0))
            {
                var urls = SteamGameDiscoveryProvider.SteamCdnUrlTemplates.Select(t => string.Format(t, game.LauncherAppId));
                banner = await TryDownloadFirstAsync(game.LauncherAppId, urls, ct).ConfigureAwait(false);
            }
        }
        else if (game.Source == GameSource.Xbox && !string.IsNullOrEmpty(game.LauncherAppId))
        {
            banner = CachedArtPathIfExists(game.LauncherAppId);

            if (banner == null && _attemptedRemoteArt.TryAdd($"xbox:{game.LauncherAppId}", 0))
            {
                banner = await TryResolveXboxArtAsync(game.LauncherAppId, ct).ConfigureAwait(false);
            }
        }

        if (banner == null && !string.IsNullOrEmpty(game.ExecutablePath))
        {
            string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECCR", "GameIcons");
            string candidatePath = Path.Combine(cacheDir, $"{game.Id}.png");
            if (GameIconExtractor.TryExtractIconAsBanner(game.ExecutablePath, candidatePath)) banner = candidatePath;
        }

        if (banner == null) return;

        game.BannerImagePath = banner;
        ArtResolved?.Invoke(game);
    }

    private static string CachePathFor(string cacheKey) => Path.Combine(GameArtCacheDir, $"{cacheKey}.jpg");

    private static string? CachedArtPathIfExists(string cacheKey)
    {
        string path = CachePathFor(cacheKey);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Tries each URL in order (best quality first), first 200 wins. Steam's CDN 404s for a variant it doesn't have rather than erroring, so this is the normal way a fallback triggers, not just an error path.</summary>
    private static async Task<string?> TryDownloadFirstAsync(string cacheKey, IEnumerable<string> urls, CancellationToken ct)
    {
        string destPath = CachePathFor(cacheKey);
        foreach (var url in urls)
        {
            if (await TryDownloadOneAsync(url, destPath, ct).ConfigureAwait(false)) return destPath;
        }
        return null;
    }

    /// <summary>Downloads one URL straight to its final cache path, writing atomically (temp file + move) so an app-kill mid-download can never leave a corrupt file that a later disk-cache check would treat as permanently valid.</summary>
    private static async Task<bool> TryDownloadOneAsync(string url, string destPath, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(6));

            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return false;

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            if (bytes.Length == 0) return false;

            Directory.CreateDirectory(GameArtCacheDir);
            string tempPath = destPath + ".tmp";
            await File.WriteAllBytesAsync(tempPath, bytes, ct).ConfigureAwait(false);
            File.Move(tempPath, destPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Best-effort real box art for an Xbox/Game Pass title: queries Microsoft's public (but
    /// unofficial/undocumented) catalog API by Package Family Name. This endpoint's exact shape
    /// isn't guaranteed to stay stable - every property access below is defensive
    /// (<c>TryGetProperty</c>, never direct indexing past a length check), and any failure at any
    /// step - timeout, non-2xx, unexpected JSON shape - just returns null and falls through to the
    /// icon-composite banner in <see cref="ResolveOneAsync"/>, same as an offline Steam fetch would.
    /// </summary>
    private static async Task<string?> TryResolveXboxArtAsync(string packageFamilyName, CancellationToken ct)
    {
        string? imageUrl = await TryFetchXboxImageUrlAsync(packageFamilyName, ct).ConfigureAwait(false);
        if (imageUrl == null) return null;

        string destPath = CachePathFor(packageFamilyName);
        return await TryDownloadOneAsync(imageUrl, destPath, ct).ConfigureAwait(false) ? destPath : null;
    }

    private static async Task<string?> TryFetchXboxImageUrlAsync(string packageFamilyName, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(6));

            string url = "https://displaycatalog.mp.microsoft.com/v7.0/products/lookup" +
                         "?market=US&languages=en-us&alternateId=PackageFamilyName&value=" + Uri.EscapeDataString(packageFamilyName);

            using var stream = await HttpClient.GetStreamAsync(url, cts.Token).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token).ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("Products", out var products) || products.ValueKind != JsonValueKind.Array || products.GetArrayLength() == 0)
                return null;

            var product = products[0];
            if (!product.TryGetProperty("LocalizedProperties", out var localizedProps) || localizedProps.ValueKind != JsonValueKind.Array || localizedProps.GetArrayLength() == 0)
                return null;

            var localized = localizedProps[0];
            if (!localized.TryGetProperty("Images", out var images) || images.ValueKind != JsonValueKind.Array)
                return null;

            // Prefer the largest portrait-shaped image (box art/poster) over any landscape hero/screenshot entries.
            string? bestUrl = null;
            long bestArea = -1;
            foreach (var img in images.EnumerateArray())
            {
                if (!img.TryGetProperty("Uri", out var uriEl) || uriEl.ValueKind != JsonValueKind.String) continue;
                string? imgUri = uriEl.GetString();
                if (string.IsNullOrWhiteSpace(imgUri)) continue;

                int width = img.TryGetProperty("Width", out var w) && w.TryGetInt32(out var wi) ? wi : 0;
                int height = img.TryGetProperty("Height", out var h) && h.TryGetInt32(out var hi) ? hi : 0;
                if (height <= width || width <= 0) continue;

                long area = (long)width * height;
                if (area <= bestArea) continue;
                bestArea = area;
                bestUrl = imgUri;
            }

            if (bestUrl == null) return null;
            if (bestUrl.StartsWith("//", StringComparison.Ordinal)) bestUrl = "https:" + bestUrl;
            return bestUrl;
        }
        catch
        {
            return null;
        }
    }
}
