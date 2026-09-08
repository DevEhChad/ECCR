namespace ECCR.ViewModels;

/// <summary>
/// Opt-in for a page (or a page's child view model) that needs to start/stop live work - event
/// subscriptions to <see cref="MainWindowViewModel.OnLiveDeviceStateUpdated"/>, timers - only
/// while its page is actually the one being shown. Every page ViewModel in
/// <see cref="MainWindowViewModel"/>'s <c>_pageViewModels</c> lives for the app's entire lifetime
/// (built once in <c>InitializeShellNavigation</c>), so without this a subscription made in a
/// constructor keeps firing forever, even while a completely different page is on screen.
/// <see cref="MainWindowViewModel.NavigateTo"/> calls <see cref="OnNavigatedFrom"/> on the page
/// being left and <see cref="OnNavigatedTo"/> on the page being shown, for whichever of the two
/// implements this.
/// </summary>
public interface INavigationAware
{
    void OnNavigatedTo();
    void OnNavigatedFrom();
}
