namespace ECCR.Models;

/// <summary>
/// Every navigable page in the app shell's sidebar. Backs <c>MainWindowViewModel.CurrentPage</c>
/// and keys the <c>_pageViewModels</c> dictionary each page view model is registered under.
/// </summary>
public enum AppPage
{
    Home,
    ConnectedDevices,
    Binder,
    Profiles,
    HidHide,
    AutoBind,
    About,
    Settings
}
