using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ECCR.Models;

namespace ECCR.Services.GameDiscovery;

/// <summary>
/// Scans Epic Games Launcher's local manifest directory. Unlike Steam, each ".item" manifest
/// (plain JSON despite the extension) names the launch executable directly, so no exe-resolution
/// heuristic is needed here.
/// </summary>
public class EpicGameDiscoveryProvider : IGameDiscoveryProvider
{
    public GameSource Source => GameSource.Epic;

    public Task<List<GameInfo>> ScanAsync(CancellationToken ct)
    {
        var results = new List<GameInfo>();

        string manifestDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(manifestDir)) return Task.FromResult(results);

        string[] itemFiles;
        try { itemFiles = Directory.GetFiles(manifestDir, "*.item"); }
        catch { return Task.FromResult(results); }

        foreach (var itemFile in itemFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var game = ParseManifest(itemFile);
                if (game != null) results.Add(game);
            }
            catch { /* one bad manifest never aborts the scan */ }
        }

        return Task.FromResult(results);
    }

    private static GameInfo? ParseManifest(string itemFilePath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(itemFilePath));
        var root = doc.RootElement;

        if (!root.TryGetProperty("DisplayName", out var nameEl)) return null;
        if (!root.TryGetProperty("InstallLocation", out var installLocEl)) return null;
        if (!root.TryGetProperty("LaunchExecutable", out var exeEl)) return null;

        string? name = nameEl.GetString();
        string? installLocation = installLocEl.GetString();
        string? launchExecutable = exeEl.GetString();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installLocation) || string.IsNullOrWhiteSpace(launchExecutable)) return null;

        // LaunchExecutable sometimes uses forward slashes ("Binaries/Win64/Launcher.exe") -
        // normalize before combining so the persisted path is consistent (Windows tolerates the
        // mixed separators either way, but a clean path is worth the one extra call).
        string exePath = Path.Combine(installLocation, launchExecutable.Replace('/', '\\'));
        if (!File.Exists(exePath)) return null;

        string? appId = root.TryGetProperty("CatalogItemId", out var idEl) ? idEl.GetString() : null;

        return new GameInfo
        {
            DisplayName = name,
            ExecutablePath = exePath,
            InstallDirectory = installLocation,
            Source = GameSource.Epic,
            LauncherAppId = appId,
            BannerImagePath = FindLocalThumbnail(root),
            LastSeenByScanUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Best-effort local art lookup: Epic's ".item" manifests don't have one dependable field for
    /// this (unlike Steam's fixed librarycache convention), so this scans the manifest for any
    /// string property whose name mentions "Thumbnail"/"Icon" and only accepts it if it already
    /// points at a file that exists on disk - never a URL (no network fetch here, see
    /// <see cref="GameArtResolverService"/>). Returns null when nothing matches, letting the
    /// exe-icon-extraction fallback take over.
    /// </summary>
    private static string? FindLocalThumbnail(JsonElement root)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.String) continue;
            if (!prop.Name.Contains("Thumbnail", StringComparison.OrdinalIgnoreCase) &&
                !prop.Name.Contains("Icon", StringComparison.OrdinalIgnoreCase)) continue;

            string? value = prop.Value.GetString();
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;

            try { if (File.Exists(value)) return value; } catch { }
        }
        return null;
    }
}
