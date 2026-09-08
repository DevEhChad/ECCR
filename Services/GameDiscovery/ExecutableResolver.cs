using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ECCR.Services.GameDiscovery;

/// <summary>
/// Shared "find the game's primary .exe under an install folder" heuristic - extracted out of
/// <see cref="SteamGameDiscoveryProvider"/> (whose manifests never name the exe directly) since
/// <see cref="XboxGameDiscoveryProvider"/> and <see cref="EAGameDiscoveryProvider"/> need the
/// identical guess: largest non-installer/anticheat .exe at the install root, or in one of a few
/// common subfolders if the root itself has none. Can occasionally pick the wrong file for
/// multi-exe titles - the Games page's "Edit Exe Path" drawer action is the intended fix-up path
/// for that, same as it already is for Steam.
/// </summary>
public static class ExecutableResolver
{
    private static readonly string[] ExcludedExeSubstrings =
    [
        "unins", "vc_redist", "dxsetup", "crashpad", "easyanticheat", "battleye", "dotnetfx", "directx"
    ];

    private static readonly string[] DefaultSecondaryFolders = ["bin", "Binaries", "Win64", "Win32", "x64"];

    public static string? ResolvePrimaryExecutable(string installPath, IReadOnlyList<string>? extraSecondaryFolders = null)
    {
        var candidates = new List<FileInfo>();
        try { candidates.AddRange(new DirectoryInfo(installPath).GetFiles("*.exe")); } catch { }

        if (candidates.Count == 0)
        {
            var secondaryFolders = extraSecondaryFolders == null
                ? DefaultSecondaryFolders
                : DefaultSecondaryFolders.Concat(extraSecondaryFolders);

            foreach (var sub in secondaryFolders)
            {
                try
                {
                    string subPath = Path.Combine(installPath, sub);
                    if (Directory.Exists(subPath)) candidates.AddRange(new DirectoryInfo(subPath).GetFiles("*.exe"));
                }
                catch { }
            }
        }

        var best = candidates
            .Where(f => !ExcludedExeSubstrings.Any(x => f.Name.Contains(x, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(f => f.Length)
            .FirstOrDefault();

        return best?.FullName;
    }
}
