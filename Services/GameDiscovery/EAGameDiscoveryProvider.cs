using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ECCR.Models;
using Microsoft.Win32;

namespace ECCR.Services.GameDiscovery;

/// <summary>
/// Scans the registry for installed EA/Origin titles. This is best-effort and unverified against
/// a real EA App/Origin install (none was available to test against while writing this) - EA's
/// registry schema has visibly churned across launcher generations (Origin -&gt; EA Desktop -&gt; EA
/// app), so rather than hardcoding one value name for "the install directory" or "the display
/// name", each candidate title key is scanned defensively: known-likely value names are tried
/// first, and failing that every string value under the key is checked for one that happens to
/// be an existing directory. A key that yields nothing usable is skipped, never throws. If this
/// ever resolves the wrong executable or misses a title entirely, the Games page's "Edit Exe
/// Path" drawer action (or manual "+ Add Game") is the intended fix-up path, same as it already
/// is for Steam's own exe-resolution heuristic.
/// </summary>
public class EAGameDiscoveryProvider : IGameDiscoveryProvider
{
    public GameSource Source => GameSource.EA;

    private static readonly string[] CandidateRoots =
    [
        @"SOFTWARE\Electronic Arts",
        @"SOFTWARE\WOW6432Node\Electronic Arts",
        @"SOFTWARE\Origin Games",
        @"SOFTWARE\WOW6432Node\Origin Games"
    ];

    private static readonly string[] DisplayNameValueNames = ["DisplayName", "GameName", "Title"];
    private static readonly string[] InstallDirValueNames = ["Install Dir", "InstallLocation", "InstallDir"];

    public Task<List<GameInfo>> ScanAsync(CancellationToken ct)
    {
        var results = new List<GameInfo>();

        foreach (var rootPath in CandidateRoots)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var root = Registry.LocalMachine.OpenSubKey(rootPath);
                if (root == null) continue;

                foreach (var subKeyName in root.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var titleKey = root.OpenSubKey(subKeyName);
                        if (titleKey == null) continue;

                        var game = ParseTitleKey(subKeyName, titleKey);
                        if (game != null) results.Add(game);
                    }
                    catch { /* one bad key never aborts the scan */ }
                }
            }
            catch { /* this root doesn't exist / isn't readable - try the next candidate */ }
        }

        return Task.FromResult(results);
    }

    private static GameInfo? ParseTitleKey(string subKeyName, RegistryKey titleKey)
    {
        string? installDir = FindFirstValueByName(titleKey, InstallDirValueNames);
        if (installDir == null || !Directory.Exists(installDir))
        {
            installDir = FindFirstDirectoryLikeValue(titleKey);
        }
        if (installDir == null || !Directory.Exists(installDir)) return null;

        string displayName = FindFirstValueByName(titleKey, DisplayNameValueNames) ?? CleanUpKeyName(subKeyName);
        if (string.IsNullOrWhiteSpace(displayName)) return null;

        string? exePath = ExecutableResolver.ResolvePrimaryExecutable(installDir);
        if (exePath == null) return null;

        return new GameInfo
        {
            DisplayName = displayName,
            ExecutablePath = exePath,
            InstallDirectory = installDir,
            Source = GameSource.EA,
            LauncherAppId = subKeyName,
            LastSeenByScanUtc = DateTime.UtcNow
        };
    }

    private static string? FindFirstValueByName(RegistryKey key, IReadOnlyList<string> valueNames)
    {
        foreach (var name in valueNames)
        {
            if (key.GetValue(name) is string s && !string.IsNullOrWhiteSpace(s)) return s;
        }
        return null;
    }

    /// <summary>Last resort when none of the known-likely value names are present: check every string value under the key and take the first one that's an existing directory on disk.</summary>
    private static string? FindFirstDirectoryLikeValue(RegistryKey key)
    {
        foreach (var valueName in key.GetValueNames())
        {
            if (key.GetValue(valueName) is string s && !string.IsNullOrWhiteSpace(s) && Directory.Exists(s))
            {
                return s;
            }
        }
        return null;
    }

    /// <summary>EA/Origin title keys are usually named by a ContentID/GUID-like string, not a friendly name - used only when no display-name value was found at all.</summary>
    private static string CleanUpKeyName(string keyName) => keyName.Replace('_', ' ').Trim();
}
