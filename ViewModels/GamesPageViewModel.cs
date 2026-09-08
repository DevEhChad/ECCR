using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ECCR.Models;
using ECCR.Services;

namespace ECCR.ViewModels;

/// <summary>
/// Backs the Games page: the scanned/manual game library, filter tabs, the assignment drawer,
/// and the master auto-switching toggle (<see cref="IsAutoSwitchingEnabled"/>, forwarded from
/// <see cref="MainWindowViewModel"/> like the profile CRUD members on <see cref="ProfilesViewModel"/>
/// are). Profile CRUD itself lives on the separate, simpler Profiles page
/// (<see cref="ProfilesViewModel"/>) - this page only deals with games and their bindings.
/// </summary>
public partial class GamesPageViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _host;
    private readonly GameLibraryService _gameLibraryService;
    private readonly ProfileService _profileService;
    private DateTime _lastScanStartUtc = DateTime.MinValue;
    private string? _pendingGameBannerPath;

    /// <summary>Set by <see cref="Views.GamesPageView"/>'s code-behind, mirroring <see cref="HidHideViewModel.StorageProviderResolver"/>.</summary>
    public Func<IStorageProvider?>? StorageProviderResolver { get; set; }

    public ObservableCollection<GameCardViewModel> GameCards { get; } = new();
    public ObservableCollection<string> ProfileNames => _profileService.ProfileNames;

    /// <summary>Master switch for the whole auto-switching feature - forwarded to <see cref="MainWindowViewModel.IsAutoSwitchingEnabled"/> (persisted in settings.json). Individual games still need their own <see cref="GameCardViewModel.AutoSwitchEnabled"/> opted in on top of this; the per-game toggles are disabled in the UI while this is off, per <c>Views\GamesPageView.axaml</c>.</summary>
    public bool IsAutoSwitchingEnabled
    {
        get => _host.IsAutoSwitchingEnabled;
        set
        {
            if (_host.IsAutoSwitchingEnabled == value) return;
            _host.IsAutoSwitchingEnabled = value;
            _host.SaveAppSettings();
        }
    }

    [ObservableProperty]
    private string _activeSourceFilter = "All";

    public bool IsAllTabSelected => ActiveSourceFilter == "All";
    public bool IsSteamTabSelected => ActiveSourceFilter == "Steam";
    public bool IsEpicTabSelected => ActiveSourceFilter == "Epic";
    public bool IsXboxTabSelected => ActiveSourceFilter == "Xbox";
    public bool IsEaTabSelected => ActiveSourceFilter == "EA";
    public bool IsCustomTabSelected => ActiveSourceFilter == "Custom";

    partial void OnActiveSourceFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsAllTabSelected));
        OnPropertyChanged(nameof(IsSteamTabSelected));
        OnPropertyChanged(nameof(IsEpicTabSelected));
        OnPropertyChanged(nameof(IsXboxTabSelected));
        OnPropertyChanged(nameof(IsEaTabSelected));
        OnPropertyChanged(nameof(IsCustomTabSelected));
        RebuildGameCards();
    }

    [RelayCommand]
    private void SelectTab(string sourceFilter) => ActiveSourceFilter = sourceFilter;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isTestBannerVisible;

    [ObservableProperty]
    private string _testBannerText = string.Empty;

    // --- Assignment drawer ---

    [ObservableProperty]
    private bool _isAssignmentDrawerOpen;

    [ObservableProperty]
    private GameCardViewModel? _drawerTargetGame;

    [ObservableProperty]
    private string _drawerNewProfileName = string.Empty;

    [ObservableProperty]
    private string? _drawerSelectedExistingProfile;

    // --- Add-game (manual) flow ---

    [ObservableProperty]
    private bool _isAddGameDialogOpen;

    [ObservableProperty]
    private string _pendingGameExePath = string.Empty;

    [ObservableProperty]
    private string _pendingGameDisplayName = string.Empty;

    /// <summary>Design-time constructor.</summary>
    public GamesPageViewModel() : this(new MainWindowViewModel(), new GameLibraryService(), new ProfileService())
    {
    }

    public GamesPageViewModel(MainWindowViewModel host, GameLibraryService gameLibraryService, ProfileService profileService)
    {
        _host = host;
        _gameLibraryService = gameLibraryService;
        _profileService = profileService;
        _gameLibraryService.Games.CollectionChanged += (_, _) => RebuildGameCards();
        _gameLibraryService.GameUpdated += game => GameCards.FirstOrDefault(c => c.Game.Id == game.Id)?.RefreshBannerImagePath();
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsAutoSwitchingEnabled)) OnPropertyChanged(nameof(IsAutoSwitchingEnabled));
        };
        RebuildGameCards();
    }

    private void RebuildGameCards()
    {
        GameCards.Clear();
        var filtered = ActiveSourceFilter switch
        {
            "Steam" => _gameLibraryService.Games.Where(g => g.Source == GameSource.Steam),
            "Epic" => _gameLibraryService.Games.Where(g => g.Source == GameSource.Epic),
            "Xbox" => _gameLibraryService.Games.Where(g => g.Source == GameSource.Xbox),
            "EA" => _gameLibraryService.Games.Where(g => g.Source == GameSource.EA),
            "Custom" => _gameLibraryService.Games.Where(g => g.Source == GameSource.Custom),
            _ => _gameLibraryService.Games.AsEnumerable()
        };

        foreach (var game in filtered.OrderBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            GameCards.Add(new GameCardViewModel(game, _gameLibraryService, _profileService, this));
        }
    }

    // --- Quick actions, called from GameCardViewModel ---

    public void TestGame(GameCardViewModel card)
    {
        if (!card.Game.BoundProfileId.HasValue) return;
        var name = _profileService.GetProfileName(card.Game.BoundProfileId.Value);
        if (name == null) return;

        _host.SelectedProfile = name;
        TestBannerText = $"Testing: {card.DisplayName} - mappings from \"{name}\" are now active";
        IsTestBannerVisible = true;
    }

    [RelayCommand]
    private void DismissTestBanner() => IsTestBannerVisible = false;

    public void CalibrateGame(GameCardViewModel card)
    {
        if (card.Game.BoundProfileId.HasValue)
        {
            var name = _profileService.GetProfileName(card.Game.BoundProfileId.Value);
            if (name != null) _host.SelectedProfile = name;
        }
        _host.NavigateToCommand.Execute(AppPage.DeviceMapping);
    }

    // --- Assignment drawer ---

    public void OpenDrawer(GameCardViewModel card)
    {
        DrawerTargetGame = card;
        DrawerNewProfileName = string.Empty;
        DrawerSelectedExistingProfile = card.Game.BoundProfileId.HasValue
            ? _profileService.GetProfileName(card.Game.BoundProfileId.Value)
            : null;
        IsAssignmentDrawerOpen = true;
    }

    [RelayCommand]
    private void CloseDrawer()
    {
        IsAssignmentDrawerOpen = false;
        DrawerTargetGame = null;
    }

    [RelayCommand]
    private void AssignExistingProfile()
    {
        if (DrawerTargetGame == null || string.IsNullOrWhiteSpace(DrawerSelectedExistingProfile)) return;
        var id = _profileService.GetProfileId(DrawerSelectedExistingProfile);
        if (id == null) return;

        _gameLibraryService.SetBinding(DrawerTargetGame.Game.Id, id);
        DrawerTargetGame.RefreshBoundProfileDisplay();
        IsAssignmentDrawerOpen = false;
    }

    [RelayCommand]
    private void CreateAndAssignProfile()
    {
        if (DrawerTargetGame == null || string.IsNullOrWhiteSpace(DrawerNewProfileName)) return;
        string name = DrawerNewProfileName.Trim();

        _profileService.CreateProfile(name);
        var id = _profileService.GetProfileId(name);
        if (id == null) return;

        _gameLibraryService.SetBinding(DrawerTargetGame.Game.Id, id);
        DrawerTargetGame.RefreshBoundProfileDisplay();
        DrawerNewProfileName = string.Empty;
        IsAssignmentDrawerOpen = false;
    }

    [RelayCommand]
    private void UnbindDrawerTarget()
    {
        if (DrawerTargetGame == null) return;
        _gameLibraryService.SetBinding(DrawerTargetGame.Game.Id, null);
        DrawerTargetGame.RefreshBoundProfileDisplay();
        IsAssignmentDrawerOpen = false;
    }

    [RelayCommand]
    private async Task EditDrawerTargetExePath(IStorageProvider? storageProvider)
    {
        if (DrawerTargetGame == null) return;
        storageProvider ??= StorageProviderResolver?.Invoke();
        if (storageProvider == null) return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Game Executable",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Executables") { Patterns = ["*.exe"] }]
        });
        if (files.Count == 0) return;

        _gameLibraryService.UpdateExecutablePath(DrawerTargetGame.Game.Id, files[0].Path.LocalPath);
    }

    /// <summary>Manual "Change Banner" - lets the user hand-pick a banner/box-art image for any game (scanned or custom), the escape hatch for Epic/EA titles where automatic art resolution has no reliable free source.</summary>
    [RelayCommand]
    private async Task EditDrawerTargetBanner(IStorageProvider? storageProvider)
    {
        if (DrawerTargetGame == null) return;
        storageProvider ??= StorageProviderResolver?.Invoke();
        if (storageProvider == null) return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Banner Image",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.ico"] }]
        });
        if (files.Count == 0) return;

        _gameLibraryService.UpdateBannerImagePath(DrawerTargetGame.Game.Id, files[0].Path.LocalPath);
        DrawerTargetGame.RefreshBannerImagePath();
    }

    // --- Manual game addition ---

    [RelayCommand]
    private async Task AddManualGame(IStorageProvider? storageProvider)
    {
        storageProvider ??= StorageProviderResolver?.Invoke();
        if (storageProvider == null) return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Game Executable",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Executables") { Patterns = ["*.exe"] }]
        });
        if (files.Count == 0) return;

        string exePath = files[0].Path.LocalPath;
        PendingGameExePath = exePath;
        PendingGameDisplayName = Path.GetFileNameWithoutExtension(exePath);
        _pendingGameBannerPath = null;
        IsAddGameDialogOpen = true;
    }

    [RelayCommand]
    private async Task PickPendingGameBanner(IStorageProvider? storageProvider)
    {
        storageProvider ??= StorageProviderResolver?.Invoke();
        if (storageProvider == null) return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Banner Image",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.ico"] }]
        });
        if (files.Count > 0) _pendingGameBannerPath = files[0].Path.LocalPath;
    }

    [RelayCommand]
    private void ConfirmAddManualGame()
    {
        if (string.IsNullOrWhiteSpace(PendingGameDisplayName) || string.IsNullOrWhiteSpace(PendingGameExePath)) return;

        _gameLibraryService.AddManualGame(PendingGameExePath, PendingGameDisplayName.Trim(), _pendingGameBannerPath);
        CancelAddManualGame();
    }

    [RelayCommand]
    private void CancelAddManualGame()
    {
        IsAddGameDialogOpen = false;
        PendingGameExePath = string.Empty;
        PendingGameDisplayName = string.Empty;
        _pendingGameBannerPath = null;
    }

    // --- Rescan / cleanup ---

    /// <summary>Gates the "Remove Uninstalled" button - it needs at least one rescan this session to know which entries weren't found, see <see cref="RemoveUninstalledGames"/>.</summary>
    [ObservableProperty]
    private bool _hasScannedThisSession = false;

    [RelayCommand]
    private async Task RescanLibraries()
    {
        if (IsScanning) return;
        IsScanning = true;
        try
        {
            _lastScanStartUtc = DateTime.UtcNow;
            await _gameLibraryService.RescanAsync();
            HasScannedThisSession = true;
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void RemoveUninstalledGames()
    {
        if (_lastScanStartUtc == DateTime.MinValue) return;
        _gameLibraryService.RemoveGamesNotSeenSince(_lastScanStartUtc);
    }
}
