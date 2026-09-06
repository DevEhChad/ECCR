using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ECCR.Models;

namespace ECCR.Services.GameDiscovery;

/// <summary>
/// Best-effort, fully local/offline pass that fills in <see cref="GameInfo.BannerImagePath"/> for
/// any game a discovery provider (or <see cref="GameLibraryService.AddManualGame"/>) couldn't find
/// art for. Tried per game, in order: Steam's local grid cache (in case it appeared after the
/// initial scan already ran), then an exe-icon-extraction fallback composited onto a blurred
/// portrait banner. Never makes a network call. Run by <see cref="GameLibraryService"/> as a
/// fire-and-forget pass after <see cref="GameLibraryService.RescanAsync"/>, so it never blocks the
/// scan itself - cards show placeholders immediately and banners pop in as each game resolves.
/// </summary>
public class GameArtResolverService
{
    /// <summary>Raised once per game whose <see cref="GameInfo.BannerImagePath"/> was just set - off a background thread, so subscribers that touch UI-bound state must marshal back to the UI thread themselves.</summary>
    public event Action<GameInfo>? ArtResolved;

    public async Task ResolveMissingArtAsync(IEnumerable<GameInfo> games, CancellationToken ct = default)
    {
        var pending = games.Where(g => string.IsNullOrEmpty(g.BannerImagePath)).ToList();
        if (pending.Count == 0) return;

        var tasks = pending.Select(game => Task.Run(() =>
        {
            try
            {
                ResolveOne(game);
            }
            catch { /* one game's failed art resolution never affects the rest of the batch */ }
        }, ct));

        await Task.WhenAll(tasks);
    }

    private void ResolveOne(GameInfo game)
    {
        string? banner = null;

        if (game.Source == GameSource.Steam && !string.IsNullOrEmpty(game.LauncherAppId))
        {
            banner = SteamGameDiscoveryProvider.FindLocalBoxArt(game.LauncherAppId);
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
}
