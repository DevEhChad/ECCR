using CommunityToolkit.Mvvm.Input;

namespace ECCR.ViewModels;

/// <summary>
/// Thin forwarder backing the Settings page - every toggle, driver status flag, update-flow
/// property and command here is owned by <see cref="MainWindowViewModel"/>; this class only
/// proxies to it and re-raises property-changed notifications for the properties it wraps.
/// Same rationale as <see cref="ProfilesViewModel"/>. The app version display
/// (<see cref="DisplayVersionText"/>) stays on this page per product decision, not About.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _host;

    /// <summary>Design-time constructor.</summary>
    public SettingsViewModel() : this(new MainWindowViewModel())
    {
    }

    public SettingsViewModel(MainWindowViewModel host)
    {
        _host = host;
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is
                nameof(MainWindowViewModel.StartWithWindows) or
                nameof(MainWindowViewModel.RunInSystemTray) or
                nameof(MainWindowViewModel.MinimizeToTray) or
                nameof(MainWindowViewModel.CloseMinimizesToTray) or
                nameof(MainWindowViewModel.AutoCheckForUpdates) or
                nameof(MainWindowViewModel.ShowChangelogOnUpdate) or
                nameof(MainWindowViewModel.DriverInstallProgressText) or
                nameof(MainWindowViewModel.IsBusyWithDriverAction) or
                nameof(MainWindowViewModel.IsViGEmInstalled) or
                nameof(MainWindowViewModel.IsHidHideServiceInstalled) or
                nameof(MainWindowViewModel.IsVJoyInstalled) or
                nameof(MainWindowViewModel.UpdateStatusText) or
                nameof(MainWindowViewModel.IsInstallingUpdate) or
                nameof(MainWindowViewModel.UpdateDownloadProgress) or
                nameof(MainWindowViewModel.IsCheckingForUpdates) or
                nameof(MainWindowViewModel.IsUpdateAvailable) or
                nameof(MainWindowViewModel.DisplayVersionText))
            {
                OnPropertyChanged(e.PropertyName);
            }
        };
    }

    public bool StartWithWindows
    {
        get => _host.StartWithWindows;
        set => _host.StartWithWindows = value;
    }

    public bool RunInSystemTray
    {
        get => _host.RunInSystemTray;
        set => _host.RunInSystemTray = value;
    }

    public bool MinimizeToTray
    {
        get => _host.MinimizeToTray;
        set => _host.MinimizeToTray = value;
    }

    public bool CloseMinimizesToTray
    {
        get => _host.CloseMinimizesToTray;
        set => _host.CloseMinimizesToTray = value;
    }

    public bool AutoCheckForUpdates
    {
        get => _host.AutoCheckForUpdates;
        set => _host.AutoCheckForUpdates = value;
    }

    public bool ShowChangelogOnUpdate
    {
        get => _host.ShowChangelogOnUpdate;
        set => _host.ShowChangelogOnUpdate = value;
    }

    public string DriverInstallProgressText => _host.DriverInstallProgressText;
    public bool IsBusyWithDriverAction => _host.IsBusyWithDriverAction;
    public bool IsViGEmInstalled => _host.IsViGEmInstalled;
    public bool IsHidHideServiceInstalled => _host.IsHidHideServiceInstalled;
    public bool IsVJoyInstalled => _host.IsVJoyInstalled;

    public string UpdateStatusText => _host.UpdateStatusText;
    public bool IsInstallingUpdate => _host.IsInstallingUpdate;
    public int UpdateDownloadProgress => _host.UpdateDownloadProgress;
    public bool IsCheckingForUpdates => _host.IsCheckingForUpdates;
    public bool IsUpdateAvailable => _host.IsUpdateAvailable;
    public string DisplayVersionText => _host.DisplayVersionText;

    public IAsyncRelayCommand<string> InstallDriverCommand => _host.InstallDriverCommand;
    public IAsyncRelayCommand<string> UninstallDriverCommand => _host.UninstallDriverCommand;
    public IAsyncRelayCommand CheckForUpdatesManualCommand => _host.CheckForUpdatesManualCommand;
    public IAsyncRelayCommand InstallUpdateAndRestartCommand => _host.InstallUpdateAndRestartCommand;
    public IRelayCommand OpenChangelogCommand => _host.OpenChangelogCommand;
    public IRelayCommand OpenChangelogOnGitHubCommand => _host.OpenChangelogOnGitHubCommand;
    public IRelayCommand ResetAllSettingsCommand => _host.ResetAllSettingsCommand;
    public IRelayCommand CloseSettingsCommand => _host.CloseSettingsCommand;
}
