using System;

namespace ECCR.Models;

/// <summary>Where a <see cref="GameInfo"/> entry was discovered from.</summary>
public enum GameSource
{
    Steam,
    Epic,
    Xbox,
    EA,
    Custom
}

/// <summary>
/// One entry in the game library, serialized as part of <c>%AppData%/ECCR/games.json</c> (see
/// <see cref="ECCR.Services.GameLibraryService"/>). Scanned entries (<see cref="Source"/> other
/// than <see cref="GameSource.Custom"/>) are produced by an <see cref="ECCR.Services.GameDiscovery.IGameDiscoveryProvider"/>
/// and merged in by <see cref="ECCR.Services.GameLibraryService.RescanAsync"/>; manually-added
/// entries are created directly via <see cref="ECCR.Services.GameLibraryService.AddManualGame"/>.
/// The association to a mapping profile lives here (<see cref="BoundProfileId"/>) rather than in
/// a separate join table, since the relationship is naturally many-games-to-one-profile and the
/// UI always edits it from the game's own card.
/// </summary>
public class GameInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Resolved absolute path to the game's executable - the merge key used by rescans and the lookup key <see cref="ECCR.Services.GameLibraryService.FindByExecutablePath"/> will use for auto-switching (Phase 3).</summary>
    public string ExecutablePath { get; set; } = string.Empty;
    public string? InstallDirectory { get; set; }

    /// <summary>Absolute path to a locally-cached banner/icon image, or null to fall back to a placeholder in the UI.</summary>
    public string? BannerImagePath { get; set; }

    public GameSource Source { get; set; } = GameSource.Custom;

    /// <summary>Steam AppID / Epic catalog item id / Xbox Package Family Name (see <see cref="ECCR.Services.GameDiscovery.XboxGameDiscoveryProvider"/>) / null for Custom or when a scanner couldn't resolve one.</summary>
    public string? LauncherAppId { get; set; }

    /// <summary>Which <see cref="UserProfile.Id"/> this game is bound to, or null if unbound.</summary>
    public Guid? BoundProfileId { get; set; }

    /// <summary>Stored now; inert until the auto-switching engine (a later phase) reads it.</summary>
    public bool AutoSwitchEnabled { get; set; } = false;

    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Null for manually-added games. Stamped by a scanner every time this entry is still found on a rescan, so a temporarily-missing install (folder deleted, drive unplugged) can be told apart from one that's simply never been rescanned - see <see cref="ECCR.Services.GameLibraryService.RemoveGamesNotSeenSince"/>.</summary>
    public DateTime? LastSeenByScanUtc { get; set; }
}
