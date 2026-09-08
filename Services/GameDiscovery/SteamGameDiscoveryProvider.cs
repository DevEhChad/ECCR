using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ECCR.Models;
using Microsoft.Win32;

namespace ECCR.Services.GameDiscovery;

/// <summary>
/// Scans locally-installed Steam titles by parsing <c>libraryfolders.vdf</c> and each library's
/// <c>appmanifest_*.acf</c> files - no network calls, box art is only used if already present in
/// Steam's local cache. Steam's manifests don't name the primary executable directly, so the exe
/// is resolved with a best-effort heuristic (largest non-installer/anticheat .exe under the
/// install folder) that can occasionally pick the wrong file for multi-exe titles - the
/// Profiles page's "Edit Exe Path" affordance is the intended fix-up path for that.
/// </summary>
public class SteamGameDiscoveryProvider : IGameDiscoveryProvider
{
    public GameSource Source => GameSource.Steam;

    /// <summary>
    /// Steam's public, unauthenticated art CDN, keyed by AppID - tried in order (best first) by
    /// <see cref="GameArtResolverService"/> when <see cref="FindLocalBoxArt"/> comes up empty
    /// (most libraries never actually cache grid art locally). "{0}" is the AppID.
    /// </summary>
    internal static readonly string[] SteamCdnUrlTemplates =
    [
        "https://cdn.akamai.steamstatic.com/steam/apps/{0}/library_600x900_2x.jpg",
        "https://cdn.akamai.steamstatic.com/steam/apps/{0}/library_600x900.jpg",
        "https://cdn.akamai.steamstatic.com/steam/apps/{0}/header.jpg"
    ];

    public Task<List<GameInfo>> ScanAsync(CancellationToken ct)
    {
        var results = new List<GameInfo>();

        try
        {
            foreach (var libraryPath in FindLibraryFolders())
            {
                ct.ThrowIfCancellationRequested();
                string steamAppsDir = Path.Combine(libraryPath, "steamapps");
                if (!Directory.Exists(steamAppsDir)) continue;

                string[] manifestFiles;
                try { manifestFiles = Directory.GetFiles(steamAppsDir, "appmanifest_*.acf"); }
                catch { continue; }

                foreach (var manifestFile in manifestFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var game = ParseManifest(manifestFile, steamAppsDir);
                        if (game != null) results.Add(game);
                    }
                    catch { /* one bad manifest never aborts the scan */ }
                }
            }
        }
        catch { /* Steam not installed / no libraries at all - contribute zero games */ }

        return Task.FromResult(results);
    }

    private static IEnumerable<string> FindLibraryFolders()
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? steamRoot = GetSteamInstallPath();
        if (steamRoot != null) libraries.Add(steamRoot);

        if (steamRoot != null)
        {
            string libraryFoldersVdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraryFoldersVdf))
            {
                try
                {
                    var root = VdfParser.Parse(File.ReadAllText(libraryFoldersVdf));
                    foreach (var value in root.Values)
                    {
                        if (value is Dictionary<string, object> node && node.TryGetValue("path", out var pathObj) && pathObj is string path)
                        {
                            libraries.Add(path);
                        }
                    }
                }
                catch { }
            }
        }

        return libraries;
    }

    private static string? GetSteamInstallPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string path && Directory.Exists(path)) return path.Replace('/', '\\');
        }
        catch { }

        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(fallback) ? fallback : null;
    }

    private static GameInfo? ParseManifest(string manifestPath, string steamAppsDir)
    {
        var root = VdfParser.Parse(File.ReadAllText(manifestPath));
        if (!root.TryGetValue("appid", out var appIdObj) || appIdObj is not string appId) return null;
        if (!root.TryGetValue("name", out var nameObj) || nameObj is not string name) return null;
        if (!root.TryGetValue("installdir", out var installDirObj) || installDirObj is not string installDir) return null;

        // Steam leaves an app's appmanifest_*.acf (and sometimes even its install folder, if the
        // user manually deleted files instead of uninstalling through Steam) behind in states
        // that aren't "actually installed" - StateFlags is Steam's own authoritative record of
        // that, a bitmask where bit 4 (0x4) is "fully installed". A manifest reporting anything
        // else (uninstalled, mid-uninstall, update-required-but-never-downloaded, etc.) is
        // skipped, which is what previously let already-uninstalled games keep showing up here
        // just because a stale manifest and/or leftover folder/exe still existed on disk.
        if (root.TryGetValue("StateFlags", out var flagsObj) && flagsObj is string flagsStr &&
            int.TryParse(flagsStr, out int stateFlags) && (stateFlags & 0x4) == 0)
        {
            return null;
        }

        string installPath = Path.Combine(steamAppsDir, "common", installDir);
        if (!Directory.Exists(installPath)) return null;

        string? exePath = ExecutableResolver.ResolvePrimaryExecutable(installPath);
        if (exePath == null) return null;

        return new GameInfo
        {
            DisplayName = name,
            ExecutablePath = exePath,
            InstallDirectory = installPath,
            Source = GameSource.Steam,
            LauncherAppId = appId,
            BannerImagePath = FindBoxArt(steamAppsDir, appId),
            LastSeenByScanUtc = DateTime.UtcNow
        };
    }

    private static string? FindBoxArt(string steamAppsDir, string appId)
    {
        string cacheDir = Path.Combine(steamAppsDir, "librarycache");
        string modern = Path.Combine(cacheDir, $"{appId}_library_600x900.jpg");
        if (File.Exists(modern)) return modern;

        string legacy = Path.Combine(cacheDir, appId, "header.jpg");
        return File.Exists(legacy) ? legacy : null;
    }

    /// <summary>
    /// Re-checks every known Steam library's local grid cache for one AppID, for
    /// <see cref="GameArtResolverService"/>'s post-scan art-resolution pass - covers box art that
    /// appeared (e.g. the user opened the store page in-client) after the initial scan already
    /// ran without it.
    /// </summary>
    public static string? FindLocalBoxArt(string appId)
    {
        foreach (var libraryPath in FindLibraryFolders())
        {
            string steamAppsDir = Path.Combine(libraryPath, "steamapps");
            string? art = FindBoxArt(steamAppsDir, appId);
            if (art != null) return art;
        }
        return null;
    }
}
