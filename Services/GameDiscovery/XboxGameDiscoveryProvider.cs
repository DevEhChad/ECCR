using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ECCR.Models;

namespace ECCR.Services.GameDiscovery;

/// <summary>
/// Scans every fixed drive's <c>&lt;Drive&gt;:\XboxGames\</c> folder - Microsoft's documented
/// default (and user-relocatable) install root for PC Game Pass / Xbox app titles - rather than
/// enumerating installed MSIX/UWP packages via the registry or <c>PackageManager</c>. That
/// alternative was deliberately rejected: it would surface every installed system app (not just
/// games) and most package display names are indirect "ms-resource:" references that need a
/// full resource-index parser to resolve into anything readable, which this app doesn't carry.
/// Each immediate subfolder of "XboxGames" is one installed title, named by the Xbox app with the
/// real game title (more reliable here than parsing a MicrosoftGame.config's "Identity Name",
/// which is a code-like package identifier, not a friendly name). No local box art convention is
/// known for these installs - <see cref="GameArtResolverService"/>'s exe-icon-extraction fallback
/// covers it.
/// <para>
/// Known limitation: only finds installs under a literally-named "XboxGames" folder at a drive
/// root. A title relocated to a differently-named folder needs the Games page's manual "+ Add
/// Game" flow instead.
/// </para>
/// </summary>
public class XboxGameDiscoveryProvider : IGameDiscoveryProvider
{
    public GameSource Source => GameSource.Xbox;

    public Task<List<GameInfo>> ScanAsync(CancellationToken ct)
    {
        var results = new List<GameInfo>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                ct.ThrowIfCancellationRequested();
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;

                string xboxGamesRoot = Path.Combine(drive.RootDirectory.FullName, "XboxGames");
                if (!Directory.Exists(xboxGamesRoot)) continue;

                string[] titleFolders;
                try { titleFolders = Directory.GetDirectories(xboxGamesRoot); }
                catch { continue; }

                foreach (var titleFolder in titleFolders)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var game = ParseTitleFolder(titleFolder);
                        if (game != null) results.Add(game);
                    }
                    catch { /* one bad title folder never aborts the scan */ }
                }
            }
        }
        catch { /* no fixed/ready drives at all - contribute zero games */ }

        return Task.FromResult(results);
    }

    private static GameInfo? ParseTitleFolder(string titleFolder)
    {
        string displayName = Path.GetFileName(titleFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(displayName)) return null;

        string contentRoot = Path.Combine(titleFolder, "Content");
        string searchRoot = Directory.Exists(contentRoot) ? contentRoot : titleFolder;

        string? exePath = ExecutableResolver.ResolvePrimaryExecutable(searchRoot);
        if (exePath == null) return null;

        return new GameInfo
        {
            DisplayName = displayName,
            ExecutablePath = exePath,
            InstallDirectory = searchRoot,
            Source = GameSource.Xbox,
            LastSeenByScanUtc = DateTime.UtcNow
        };
    }
}
