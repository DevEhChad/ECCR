using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using ECCR.Models;

namespace ECCR.ViewModels;

/// <summary>
/// Backs the Home dashboard: a device-summary panel (reads the same
/// <see cref="DeviceStatusBarViewModel"/> data the persistent top bar shows) plus a grid of
/// quick-action cards. The cards reuse <see cref="MainWindowViewModel.NavigateToCommand"/>
/// directly rather than a separate navigate-from-card mechanism.
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
    public DeviceStatusBarViewModel StatusBar { get; }
    public ObservableCollection<NavItem> QuickActionCards { get; }
    public IRelayCommand<AppPage> NavigateToCommand { get; }

    /// <summary>Design-time constructor.</summary>
    public HomeViewModel() : this(new MainWindowViewModel())
    {
    }

    private HomeViewModel(MainWindowViewModel host)
        : this(host.StatusBar, BuildDesignTimeCards(), host.NavigateToCommand)
    {
    }

    public HomeViewModel(DeviceStatusBarViewModel statusBar, ObservableCollection<NavItem> quickActionCards, IRelayCommand<AppPage> navigateToCommand)
    {
        StatusBar = statusBar;
        QuickActionCards = quickActionCards;
        NavigateToCommand = navigateToCommand;
    }

    private static ObservableCollection<NavItem> BuildDesignTimeCards() => new()
    {
        new NavItem(AppPage.ConnectedDevices, "Connected Devices", "🕹️", "View connected devices."),
        new NavItem(AppPage.Profiles, "Profiles", "📄", "Manage and switch profiles."),
        new NavItem(AppPage.Settings, "Settings", "⚙️", "Configure app preferences.")
    };
}
