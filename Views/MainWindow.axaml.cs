using System;
using Avalonia;
using Avalonia.Controls;
using ECCR.Models;
using ECCR.ViewModels;

namespace ECCR.Views;

/// <summary>
/// The app's only top-level window. Most dialogs (Settings, HidHide, the update prompts,
/// Create Profile) are <c>IsVisible</c>-toggled overlay <c>Border</c>s layered inside this
/// same window rather than separate popups. <see cref="AutoBindWizardWindow"/> and
/// <see cref="CalibrationDialog"/> are the exceptions - real child windows opened modally
/// via <c>ShowDialog</c>, which fully blocks this window while they're open.
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

    /// <summary>
    /// Handler for the device-group header's "Assign to Player" dropdown - reassigns every
    /// mapping under that physical device to the chosen player target in one action. Plain
    /// code-behind rather than a bindable command because the ComboBox lives inside a
    /// DataTemplate whose DataContext is the <see cref="DeviceMappingGroup"/>, not the main
    /// view model, so the selection needs to be routed back up manually.
    /// </summary>
    private void OnBulkTargetSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.DataContext is DeviceMappingGroup group)
        {
            uint targetId = 1;
            if (cb.SelectedItem is PlayerTargetOption opt)
            {
                targetId = opt.Id;
            }
            else if (cb.SelectedItem is uint u)
            {
                targetId = u;
            }
            else
            {
                return;
            }

            if (DataContext is MainWindowViewModel vm)
            {
                vm.BulkChangeDeviceTarget(group.DeviceName, targetId);
            }
        }
    }
}