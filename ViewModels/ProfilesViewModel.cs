using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using ECCR.Services;

namespace ECCR.ViewModels;

/// <summary>
/// Backs the Profiles page: the quick-select dropdown (still a thin forwarder to
/// <see cref="MainWindowViewModel"/>, exactly as before) plus a card gallery
/// (<see cref="ProfileCards"/>) added alongside it. Game-library management (scanning, banners,
/// auto-switch binding) still lives on the separate Games page (<see cref="GamesPageViewModel"/>)
/// - this page only needs <see cref="GameLibraryService"/>/<see cref="ProfileService"/> read-only,
/// to compute each card's bound-game count and device-type summary.
/// </summary>
public partial class ProfilesViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _host;
    private readonly GameLibraryService _gameLibraryService;
    private readonly ProfileService _profileService;

    public ObservableCollection<ProfileCardViewModel> ProfileCards { get; } = new();

    /// <summary>Design-time constructor.</summary>
    public ProfilesViewModel() : this(new MainWindowViewModel(), new GameLibraryService(), new ProfileService())
    {
    }

    public ProfilesViewModel(MainWindowViewModel host, GameLibraryService gameLibraryService, ProfileService profileService)
    {
        _host = host;
        _gameLibraryService = gameLibraryService;
        _profileService = profileService;

        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.SelectedProfile)
                or nameof(MainWindowViewModel.CustomProfileInput)
                or nameof(MainWindowViewModel.IsCreatingProfile)
                or nameof(MainWindowViewModel.CreateProfileDialogTitle)
                or nameof(MainWindowViewModel.ConfirmProfileDialogButtonText))
            {
                OnPropertyChanged(e.PropertyName);
            }

            if (e.PropertyName is nameof(MainWindowViewModel.SelectedProfile) or nameof(MainWindowViewModel.DefaultProfileName))
            {
                foreach (var card in ProfileCards) card.RefreshActiveDefaultStatus();
            }
        };

        _host.Profiles.CollectionChanged += (_, _) => RebuildProfileCards();
        _gameLibraryService.Games.CollectionChanged += (_, _) => RefreshBoundGameCounts();

        RebuildProfileCards();
    }

    private void RebuildProfileCards()
    {
        ProfileCards.Clear();
        foreach (var name in _host.Profiles)
        {
            ProfileCards.Add(new ProfileCardViewModel(name, _host, _profileService, _gameLibraryService));
        }
    }

    private void RefreshBoundGameCounts()
    {
        foreach (var card in ProfileCards) card.RefreshBoundGameCount();
    }

    public ObservableCollection<string> Profiles => _host.Profiles;

    public string SelectedProfile
    {
        get => _host.SelectedProfile;
        set => _host.SelectedProfile = value;
    }

    public string CustomProfileInput
    {
        get => _host.CustomProfileInput;
        set => _host.CustomProfileInput = value;
    }

    public bool IsCreatingProfile => _host.IsCreatingProfile;

    public string CreateProfileDialogTitle => _host.CreateProfileDialogTitle;

    public string ConfirmProfileDialogButtonText => _host.ConfirmProfileDialogButtonText;

    public string? DefaultProfileName
    {
        get => _host.DefaultProfileName;
        set => _host.DefaultProfileName = value;
    }

    public IRelayCommand SaveCurrentProfileCommand => _host.SaveCurrentProfileCommand;
    public IRelayCommand OpenCreateProfileDialogCommand => _host.OpenCreateProfileDialogCommand;
    public IRelayCommand CancelCreateProfileCommand => _host.CancelCreateProfileCommand;
    public IRelayCommand ConfirmCreateProfileCommand => _host.ConfirmCreateProfileCommand;
    public IRelayCommand DeleteCurrentProfileCommand => _host.DeleteCurrentProfileCommand;
}
