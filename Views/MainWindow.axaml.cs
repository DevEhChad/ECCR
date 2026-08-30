using System;
using Avalonia;
using Avalonia.Controls;
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
    }

    /// <summary>
    /// Closing the window (the 'X' button) doesn't necessarily exit the app: if the user has
    /// "close minimizes to tray" enabled, the close is cancelled and the window just hides,
    /// leaving the tray icon and all background polling/feeding running. Only a real quit
    /// (tray "Exit", or this setting being off) tears everything down and exits the process.
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

            if (vm.CloseMinimizesToTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            vm.IsShuttingDown = true;
            vm.CleanupAndShutdown();
            Environment.Exit(0);
            return;
        }

        base.OnClosing(e);
    }

    /// <summary>Mirrors OnClosing's tray behavior for the minimize button/gesture: minimizing hides to tray instead of showing a taskbar-minimized window, when that setting is on.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            if (WindowState == WindowState.Minimized && DataContext is MainWindowViewModel vm && vm.MinimizeToTray)
            {
                Hide();
            }
        }
    }
}
