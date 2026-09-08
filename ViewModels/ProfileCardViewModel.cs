using System.Linq;
using CommunityToolkit.Mvvm.Input;
using ECCR.Models;
using ECCR.Services;

namespace ECCR.ViewModels;

/// <summary>
/// One card in the Profiles page's gallery, wrapping a single profile name. Mirrors
/// <see cref="GameCardViewModel"/>'s shape: quick actions delegate back to <see cref="_host"/>
/// since they need shell-wide state (the active profile, navigation) this card has no business
/// owning itself. <see cref="IsActive"/>/<see cref="IsDefault"/> aren't self-updating - the owning
/// page (<see cref="ProfilesViewModel"/>) calls <see cref="RefreshActiveDefaultStatus"/> on every
/// card when <see cref="MainWindowViewModel.SelectedProfile"/>/<see cref="MainWindowViewModel.DefaultProfileName"/>
/// change, the same external-refresh pattern already used for <see cref="GameCardViewModel.RefreshBannerImagePath"/>.
/// </summary>
public partial class ProfileCardViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _host;
    private readonly ProfileService _profileService;
    private readonly GameLibraryService _gameLibraryService;

    public string ProfileName { get; }

    public bool IsActive => _host.SelectedProfile == ProfileName;

    /// <summary>Get/set, mirroring <see cref="GameCardViewModel.AutoSwitchEnabled"/>'s shape - lets the card's toggle bind two-way directly. Setting it clears the default when unchecked, or pins this profile when checked; every other card's own <see cref="IsDefault"/> is re-raised afterward by the owning page (see <see cref="ProfilesViewModel"/>'s host-PropertyChanged subscription), since pinning a new default un-defaults whichever one held it before.</summary>
    public bool IsDefault
    {
        get => _host.DefaultProfileName == ProfileName;
        set => _host.DefaultProfileName = value ? ProfileName : null;
    }

    public int BoundGameCount
    {
        get
        {
            var profileId = _profileService.GetProfileId(ProfileName);
            if (profileId == null) return 0;
            return _gameLibraryService.Games.Count(g => g.BoundProfileId == profileId.Value);
        }
    }

    /// <summary>"Gamepad", "Wheel", "Mixed", or "Empty" - a one-time peek at the profile's saved mappings, computed at card-build time (not live-refreshed while this profile isn't the active one).</summary>
    public string DeviceTypeSummary
    {
        get
        {
            var (mappingCount, hasGamepad, hasWheel) = _profileService.PeekProfileSummary(ProfileName);
            if (mappingCount == 0) return "Empty";
            if (hasGamepad && hasWheel) return "Mixed";
            return hasWheel ? "Wheel" : "Gamepad";
        }
    }

    public ProfileCardViewModel(string profileName, MainWindowViewModel host, ProfileService profileService, GameLibraryService gameLibraryService)
    {
        ProfileName = profileName;
        _host = host;
        _profileService = profileService;
        _gameLibraryService = gameLibraryService;
    }

    /// <summary>Re-raises <see cref="IsActive"/>/<see cref="IsDefault"/> - see this class's doc comment for who calls this and when.</summary>
    public void RefreshActiveDefaultStatus()
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsDefault));
    }

    /// <summary>Re-raises <see cref="BoundGameCount"/> - called by the page when the game library changes (a binding added/removed elsewhere).</summary>
    public void RefreshBoundGameCount() => OnPropertyChanged(nameof(BoundGameCount));

    [RelayCommand]
    private void Apply() => _host.SelectedProfile = ProfileName;

    [RelayCommand]
    private void Edit()
    {
        _host.SelectedProfile = ProfileName;
        _host.NavigateToCommand.Execute(AppPage.DeviceMapping);
    }

    [RelayCommand]
    private void Duplicate() => _host.OpenDuplicateProfileDialogCommand.Execute(ProfileName);

    [RelayCommand]
    private void Delete() => _host.DeleteProfile(ProfileName);
}
