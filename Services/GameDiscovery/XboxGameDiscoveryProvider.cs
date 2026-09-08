using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
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
            LauncherAppId = TryResolvePackageFamilyName(titleFolder),
            LastSeenByScanUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Resolves this install's Package Family Name (a stable identifier <see cref="GameArtResolverService"/>
    /// can use to query Microsoft's public catalog API for real box art) from the MSIX
    /// <c>AppxManifest.xml</c> that ships alongside every Xbox/Game Pass PC install. Never
    /// enumerates installed packages system-wide (that's what <see cref="XboxGameDiscoveryProvider"/>'s
    /// own class doc comment explains was deliberately avoided) - this only reads one already-known
    /// title's own manifest file directly off disk.
    /// </summary>
    private static string? TryResolvePackageFamilyName(string titleFolder)
    {
        string? manifestPath = FindAppxManifest(titleFolder);
        if (manifestPath == null) return null;

        try
        {
            var doc = XDocument.Load(manifestPath);
            var identity = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Identity");
            string? name = identity?.Attribute("Name")?.Value;
            string? publisher = identity?.Attribute("Publisher")?.Value;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(publisher)) return null;

            return PackageFamilyNameResolver.TryCompute(name, publisher);
        }
        catch { return null; }
    }

    /// <summary>
    /// Looks for <c>AppxManifest.xml</c> at the title folder's root, then one level of immediate
    /// subfolders (some MSIX installs nest it under a version-named folder) - deliberately not a
    /// deep recursive walk, to avoid a slow scan of a potentially large game install.
    /// </summary>
    private static string? FindAppxManifest(string titleFolder)
    {
        try
        {
            string direct = Path.Combine(titleFolder, "AppxManifest.xml");
            if (File.Exists(direct)) return direct;

            foreach (var sub in Directory.GetDirectories(titleFolder))
            {
                string candidate = Path.Combine(sub, "AppxManifest.xml");
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch { }
        return null;
    }
}

/// <summary>
/// Computes a Package Family Name from an MSIX identity's Name+Publisher via the plain Win32
/// <c>PackageFamilyNameFromId</c> API (kernel32.dll) - deliberately not a hand-rolled
/// reimplementation of Microsoft's undocumented publisher-id hashing scheme, and not the WinRT
/// <c>PackageManager</c> API either (which would need a new WinRT/CsWinRT package reference and
/// possibly a Windows-versioned TFM change; this project targets plain <c>net10.0-windows</c>).
/// This Win32 API needs no elevation/package identity of its own and works fine from a normal
/// unpackaged desktop process.
/// </summary>
internal static class PackageFamilyNameResolver
{
    private const int ErrorInsufficientBuffer = 122;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PACKAGE_ID
    {
        public uint Reserved;
        public uint ProcessorArchitecture;
        public ulong Version;
        public IntPtr Name;
        public IntPtr Publisher;
        public IntPtr ResourceId;
        public IntPtr PublisherId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int PackageFamilyNameFromId(ref PACKAGE_ID packageId, ref uint packageFamilyNameLength, StringBuilder? packageFamilyName);

    public static string? TryCompute(string name, string publisher)
    {
        IntPtr namePtr = IntPtr.Zero;
        IntPtr publisherPtr = IntPtr.Zero;
        try
        {
            namePtr = Marshal.StringToHGlobalUni(name);
            publisherPtr = Marshal.StringToHGlobalUni(publisher);

            var id = new PACKAGE_ID { Name = namePtr, Publisher = publisherPtr };

            uint length = 0;
            int result = PackageFamilyNameFromId(ref id, ref length, null);
            if (result != ErrorInsufficientBuffer || length == 0) return null;

            var sb = new StringBuilder((int)length);
            result = PackageFamilyNameFromId(ref id, ref length, sb);
            return result == 0 ? sb.ToString() : null;
        }
        catch { return null; }
        finally
        {
            if (namePtr != IntPtr.Zero) Marshal.FreeHGlobal(namePtr);
            if (publisherPtr != IntPtr.Zero) Marshal.FreeHGlobal(publisherPtr);
        }
    }
}
