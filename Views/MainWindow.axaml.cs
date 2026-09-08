using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using ECCR.ViewModels;

namespace ECCR.Views;

/// <summary>
/// The app's only top-level window - now an app shell (sidebar + persistent device-status bar
/// + page-switching content area, see MainWindow.axaml) rather than a single mapping-grid view
/// with overlay dialogs. Two overlays remain global here (Update prompt, Changelog popup)
/// since they're unsolicited interruptions that should render above the whole shell regardless
/// of the active page. <see cref="CalibrationDialog"/> is still a real child window opened
/// modally via <c>ShowDialog</c> for the same reason as before - it's a small per-row tool,
/// not a page.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        if (Design.IsDesignMode)
        {
            return;
        }

        // Wheel force-feedback needs a real HWND to exclusively acquire the physical wheel
        // (see WheelForceFeedbackService.SetWindowHandle) - not available yet at construction,
        // only once the window has actually been shown by the platform.
        Opened += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                vm.SetNativeWindowHandle(handle);

                // Non-modal, always-on-top, non-activating - a genuinely new window pattern in
                // this app (CalibrationDialog is the only other secondary window, and it's
                // modal). A fresh ProfileSwitchToastWindow per event rather than a reused
                // instance, since profile switches are infrequent and each toast is short-lived.
                vm.ProfileAutoSwitched += (game, profileName) =>
                {
                    if (!vm.EnableProfileSwitchNotifications) return;
                    new ProfileSwitchToastWindow().ShowToast(game, profileName);
                };
            }
        };
    }

    /// <summary>
    /// Closing the window (the 'X' button) doesn't necessarily exit the app: if the user has
    /// "close minimizes to tray" enabled, the close is cancelled and the window just hides,
    /// leaving the tray icon and all background polling/feeding running. Only a real quit
    /// (tray "Exit", or this setting being off) tears everything down and exits the process.
    /// Skipped entirely when a debugger is attached - hiding instead of exiting would strand
    /// the process in the background holding the single-instance mutex (see Program.cs), so the
    /// next debug launch would attach and immediately exit with no indication why.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            if (vm.IsShuttingDown)
            {
                base.OnClosing(e);
                return;
            }

            if (vm.RunInSystemTray && vm.CloseMinimizesToTray && !Debugger.IsAttached)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            App.PerformFullExitFor(vm);
            return;
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// Routes Esc (cancel) / Backspace (clear) to an in-progress "Click to Map" capture -
    /// see <see cref="MainWindowViewModel.StartListening"/>. Scoped to <see cref="MainWindowViewModel.IsListening"/>
    /// so this never swallows Backspace while the user is typing in a normal TextBox elsewhere
    /// in the app (profile name entry, HidHide app path, etc).
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            if (vm.IsListening)
            {
                if (e.Key == Key.Escape)
                {
                    vm.CancelListening();
                    e.Handled = true;
                }
                else if (e.Key == Key.Back)
                {
                    vm.ClearListeningMapping();
                    e.Handled = true;
                }
            }
            else if (vm.IsPickingKeyboardKey)
            {
                if (e.Key == Key.Escape)
                {
                    vm.CancelKeyPick();
                }
                else
                {
                    vm.AssignPickedKey(e.Key);
                }
                e.Handled = true;
            }
        }

        base.OnKeyDown(e);
    }

    /// <summary>Mirrors OnClosing's tray behavior for the minimize button/gesture: minimizing hides to tray instead of showing a taskbar-minimized window, when that setting is on.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            if (WindowState == WindowState.Minimized && DataContext is MainWindowViewModel vm && vm.RunInSystemTray && vm.MinimizeToTray && !Debugger.IsAttached)
            {
                Hide();
            }
        }
    }
}
