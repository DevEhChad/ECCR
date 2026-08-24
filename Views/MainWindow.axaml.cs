using System;
using Avalonia;
using Avalonia.Controls;
using ECCR.ViewModels;

namespace ECCR.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            // If already shutting down, proceed with closure
            if (vm.IsShuttingDown)
            {
                base.OnClosing(e);
                return;
            }

            // If configured to minimize to system tray, cancel close and hide
            if (vm.CloseMinimizesToTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            // If close minimizer is off, treat 'X' as a full application exit
            vm.IsShuttingDown = true;
            vm.CleanupAndShutdown();
            Environment.Exit(0);
            return;
        }

        base.OnClosing(e);
    }

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

    private void OnBulkTargetSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is uint targetId && cb.DataContext is Models.DeviceMappingGroup group)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.BulkChangeDeviceTarget(group.DeviceName, targetId);
            }
        }
    }
}