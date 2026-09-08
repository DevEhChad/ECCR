using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using ECCR.Models;
using ECCR.Services;

namespace ECCR.ViewModels;

/// <summary>
/// One card in the Games page's library grid, wrapping a single <see cref="GameInfo"/>.
/// Quick actions delegate back to the owning <see cref="GamesPageViewModel"/> since they need
/// to touch shell-wide state (the active profile, page navigation) that this card has no
/// business owning itself.
/// </summary>
public partial class GameCardViewModel : ViewModelBase
{
    private readonly GameLibraryService _gameLibraryService;
    private readonly ProfileService _profileService;
    private readonly GamesPageViewModel _page;

    public GameInfo Game { get; }

    public string DisplayName => Game.DisplayName;
    public string? BannerImagePath => Game.BannerImagePath;
    public GameSource Source => Game.Source;

    public string BoundProfileDisplay =>
        Game.BoundProfileId.HasValue ? (_profileService.GetProfileName(Game.BoundProfileId.Value) ?? "Unbound") : "Unbound";

    public bool AutoSwitchEnabled
    {
        get => Game.AutoSwitchEnabled;
        set
        {
            if (Game.AutoSwitchEnabled == value) return;
            Game.AutoSwitchEnabled = value;
            _gameLibraryService.SetAutoSwitch(Game.Id, value);
            OnPropertyChanged();
        }
    }

    public GameCardViewModel(GameInfo game, GameLibraryService gameLibraryService, ProfileService profileService, GamesPageViewModel page)
    {
        Game = game;
        _gameLibraryService = gameLibraryService;
        _profileService = profileService;
        _page = page;
    }

    public void RefreshBoundProfileDisplay() => OnPropertyChanged(nameof(BoundProfileDisplay));

    public void RefreshBannerImagePath() => OnPropertyChanged(nameof(BannerImagePath));

    [RelayCommand]
    private void Launch()
    {
        try { Process.Start(new ProcessStartInfo(Game.ExecutablePath) { UseShellExecute = true }); }
        catch { }
    }

    [RelayCommand]
    private void Test() => _page.TestGame(this);

    [RelayCommand]
    private void Calibrate() => _page.CalibrateGame(this);

    [RelayCommand]
    private void OpenAssignmentDrawer() => _page.OpenDrawer(this);
}
