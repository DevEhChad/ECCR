using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace ECCR.ViewModels;

/// <summary>
/// Thin forwarder backing the Profiles page: every piece of state and every command here is
/// owned by <see cref="MainWindowViewModel"/> (its <c>Profiles</c>/<c>SelectedProfile</c>/
/// <c>CustomProfileInput</c>/<c>IsCreatingProfile</c> and the profile CRUD commands) - this
/// class only proxies to it and re-raises property-changed notifications for the properties it
/// wraps, so the page's bindings stay in sync without duplicating any profile logic.
/// </summary>
public partial class ProfilesViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _host;

    /// <summary>Design-time constructor.</summary>
    public ProfilesViewModel() : this(new MainWindowViewModel())
    {
    }

    public ProfilesViewModel(MainWindowViewModel host)
    {
        _host = host;
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.SelectedProfile)
                or nameof(MainWindowViewModel.CustomProfileInput)
                or nameof(MainWindowViewModel.IsCreatingProfile))
            {
                OnPropertyChanged(e.PropertyName);
            }
        };
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

    public IRelayCommand SaveCurrentProfileCommand => _host.SaveCurrentProfileCommand;
    public IRelayCommand OpenCreateProfileDialogCommand => _host.OpenCreateProfileDialogCommand;
    public IRelayCommand CancelCreateProfileCommand => _host.CancelCreateProfileCommand;
    public IRelayCommand ConfirmCreateProfileCommand => _host.ConfirmCreateProfileCommand;
    public IRelayCommand DeleteCurrentProfileCommand => _host.DeleteCurrentProfileCommand;
}
