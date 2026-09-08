using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ECCR.Models;
using ECCR.Services.GameDiscovery;

namespace ECCR.Services;

/// <summary>
/// Owns the persisted game library (<c>%AppData%/ECCR/games.json</c>) and orchestrates the
/// registered <see cref="IGameDiscoveryProvider"/> scanners. Separate from
/// <see cref="ProfileService"/> since games and profiles have independent lifecycles - many
/// games can point at one profile via <see cref="GameInfo.BoundProfileId"/>, and this service
/// never needs to know how a profile is loaded/saved.
/// </summary>
public class GameLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECCR", "games-debug.log");
    private const long MaxLogSizeBytes = 5 * 1024 * 1024;

    private readonly string _gamesFilePath;
    private readonly IReadOnlyList<IGameDiscoveryProvider> _providers;
    private readonly GameArtResolverService _artResolver = new();
    private readonly object _saveLock = new();

    public ObservableCollection<GameInfo> Games { get; } = new();

    /// <summary>Raised (on the UI thread) whenever <see cref="_artResolver"/> resolves art for a game already in <see cref="Games"/> - an in-place field mutation that <see cref="Games"/>' own <c>CollectionChanged</c> never fires for. <see cref="GamesPageViewModel"/> uses this to refresh just that one card.</summary>
    public event Action<GameInfo>? GameUpdated;

    public GameLibraryService() : this(string.Empty, Array.Empty<IGameDiscoveryProvider>())
    {
    }

    public GameLibraryService(string gamesFilePath, IEnumerable<IGameDiscoveryProvider> providers)
    {
        _gamesFilePath = gamesFilePath;
        _providers = providers.ToList();
        _artResolver.ArtResolved += game => Dispatcher.UIThread.Post(() =>
        {
            Save();
            GameUpdated?.Invoke(game);
        });
    }

    private static void LogDebug(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
            if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MaxLogSizeBytes)
                File.Delete(LogFilePath);
            File.AppendAllText(LogFilePath, $"[{DateTime.Now:HH:mm:ss.fff}] [GameLibrary] {message}\n");
        }
        catch { }
    }

    /// <summary>
    /// Reads and parses the games file on a background thread (this used to run synchronously on
    /// the UI thread at app startup) then populates <see cref="Games"/> back on the UI thread,
    /// since it's an ObservableCollection bound to the Games/Profiles pages. Fire-and-forget by
    /// design - the only caller is <c>MainWindowViewModel</c>'s constructor, and every page that
    /// binds to <see cref="Games"/> reacts to its CollectionChanged as entries arrive rather than
    /// needing them present synchronously.
    /// </summary>
    public void Load()
    {
        if (string.IsNullOrEmpty(_gamesFilePath) || !File.Exists(_gamesFilePath))
        {
            Games.Clear();
            return;
        }

        Task.Run(() =>
        {
            List<GameInfo>? games = null;
            try
            {
                games = JsonSerializer.Deserialize<List<GameInfo>>(File.ReadAllText(_gamesFilePath));
            }
            catch
            {
                // Missing/corrupt file -> start with an empty library rather than crash.
            }

            Dispatcher.UIThread.Post(() =>
            {
                Games.Clear();
                if (games == null) return;
                foreach (var g in games)
                {
                    if (g == null) continue;
                    Games.Add(g);
                }
            });
        });
    }

    /// <summary>
    /// Unlike most persistence in this app, failures here are logged (not fully silent) - losing
    /// the whole hand-curated game library (icons, bindings, auto-switch flags) is a bigger loss
    /// than one missed profile re-save. Snapshots <see cref="Games"/> synchronously (must happen
    /// on the caller's thread - it's an ObservableCollection bound to the UI, not safe to
    /// enumerate from a background thread while it could be mutated), then does the actual disk
    /// write on a background thread so a caller on the UI thread (most callers here, plus every
    /// per-game mutator below) never blocks on I/O. <see cref="_saveLock"/> serializes overlapping
    /// writes (e.g. several games' art resolving in quick succession each triggering a Save)
    /// so they can't interleave into a corrupt file.
    /// </summary>
    public void Save()
    {
        if (string.IsNullOrEmpty(_gamesFilePath)) return;
        var snapshot = Games.ToList();
        Task.Run(() =>
        {
            try
            {
                lock (_saveLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_gamesFilePath)!);
                    File.WriteAllText(_gamesFilePath, JsonSerializer.Serialize(snapshot, JsonOptions));
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Save failed: {ex.GetType().Name}: {ex.Message}");
            }
        });
    }

    public GameInfo AddManualGame(string exePath, string displayName, string? bannerPath)
    {
        var existing = FindByExecutablePath(exePath);
        if (existing != null)
        {
            existing.DisplayName = displayName;
            if (bannerPath != null) existing.BannerImagePath = bannerPath;
            Save();
            return existing;
        }

        string? banner = bannerPath;
        if (banner == null)
        {
            string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECCR", "GameIcons");
            string candidatePath = Path.Combine(cacheDir, $"{Guid.NewGuid()}.png");
            if (GameIconExtractor.TryExtractIcon(exePath, candidatePath)) banner = candidatePath;
        }

        var game = new GameInfo
        {
            DisplayName = displayName,
            ExecutablePath = exePath,
            InstallDirectory = Path.GetDirectoryName(exePath),
            BannerImagePath = banner,
            Source = GameSource.Custom
        };

        Games.Add(game);
        Save();
        return game;
    }

    public void RemoveGame(Guid id)
    {
        var game = Games.FirstOrDefault(g => g.Id == id);
        if (game == null) return;
        Games.Remove(game);
        Save();
    }

    public void SetBinding(Guid gameId, Guid? profileId)
    {
        var game = Games.FirstOrDefault(g => g.Id == gameId);
        if (game == null) return;
        game.BoundProfileId = profileId;
        Save();
    }

    public void SetAutoSwitch(Guid gameId, bool enabled)
    {
        var game = Games.FirstOrDefault(g => g.Id == gameId);
        if (game == null) return;
        game.AutoSwitchEnabled = enabled;
        Save();
    }

    public void UpdateExecutablePath(Guid gameId, string newPath)
    {
        var game = Games.FirstOrDefault(g => g.Id == gameId);
        if (game == null) return;
        game.ExecutablePath = newPath;
        Save();
    }

    /// <summary>Manual override for a game's banner/box-art image - the Games page's "Change Banner" button, mainly for Epic/EA titles where automatic art resolution has no reliable free source.</summary>
    public void UpdateBannerImagePath(Guid gameId, string newPath)
    {
        var game = Games.FirstOrDefault(g => g.Id == gameId);
        if (game == null) return;
        game.BannerImagePath = newPath;
        Save();
    }

    public GameInfo? FindByExecutablePath(string exePath) =>
        Games.FirstOrDefault(g => string.Equals(g.ExecutablePath, exePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Called when <see cref="ProfileService.ProfileDeleted"/> fires, so a deleted profile never leaves a dangling <see cref="GameInfo.BoundProfileId"/> behind.</summary>
    public void UnbindGamesForProfile(Guid profileId)
    {
        bool changed = false;
        foreach (var game in Games)
        {
            if (game.BoundProfileId == profileId)
            {
                game.BoundProfileId = null;
                changed = true;
            }
        }
        if (changed) Save();
    }

    /// <summary>
    /// Runs every registered provider concurrently and merges results into <see cref="Games"/>
    /// keyed by <see cref="GameInfo.ExecutablePath"/> (case-insensitive). A match updates
    /// display/banner/launcher-id/last-seen in place while preserving the existing entry's Id,
    /// binding, and auto-switch flag; no match adds a new, unbound entry. Entries missing from
    /// this scan are never deleted automatically - see <see cref="RemoveGamesNotSeenSince"/>.
    /// </summary>
    public async Task RescanAsync(CancellationToken ct = default)
    {
        var tasks = _providers.Select(async provider =>
        {
            try
            {
                return await provider.ScanAsync(ct);
            }
            catch (Exception ex)
            {
                LogDebug($"{provider.Source} scan failed: {ex.GetType().Name}: {ex.Message}");
                return new List<GameInfo>();
            }
        });

        var allResults = await Task.WhenAll(tasks);

        foreach (var scanned in allResults.SelectMany(r => r))
        {
            var existing = FindByExecutablePath(scanned.ExecutablePath);
            if (existing != null)
            {
                existing.DisplayName = scanned.DisplayName;
                existing.BannerImagePath = scanned.BannerImagePath ?? existing.BannerImagePath;
                existing.LauncherAppId = scanned.LauncherAppId;
                existing.InstallDirectory = scanned.InstallDirectory;
                existing.Source = scanned.Source;
                existing.LastSeenByScanUtc = scanned.LastSeenByScanUtc;
            }
            else
            {
                Games.Add(scanned);
            }
        }

        Save();

        // Fire-and-forget: never blocks the scan itself - cards show placeholders immediately
        // and banners pop in via GameUpdated as each game's art resolves in the background.
        _ = _artResolver.ResolveMissingArtAsync(Games.ToList());
    }

    /// <summary>Manual cleanup for "ghost" entries whose install folder disappeared - never run automatically, since a temporarily-missing install (drive unplugged, brief uninstall/reinstall) shouldn't silently lose the user's bindings. Only removes scanned entries (<see cref="GameInfo.LastSeenByScanUtc"/> non-null); Custom entries are never touched. Returns what was removed so the caller can show a confirmation before/after.</summary>
    public List<GameInfo> RemoveGamesNotSeenSince(DateTime cutoffUtc)
    {
        var stale = Games.Where(g => g.LastSeenByScanUtc.HasValue && g.LastSeenByScanUtc.Value < cutoffUtc).ToList();
        foreach (var game in stale) Games.Remove(game);
        if (stale.Count > 0) Save();
        return stale;
    }
}
