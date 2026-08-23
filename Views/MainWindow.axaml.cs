using System.ComponentModel;
using Avalonia.Controls;
using ECCR.Models;
using ECCR.ViewModels;

namespace ECCR.Views;

public partial class MainWindow : Window
{
    public bool AllowFullClose { get; set; } = false;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnBulkTargetSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && 
            comboBox.SelectedItem is uint targetDeviceId &&
            comboBox.DataContext is DeviceMappingGroup group &&
            DataContext is MainWindowViewModel vm)
        {
            vm.BulkChangeDeviceTarget(group.DeviceName, targetDeviceId);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!AllowFullClose && DataContext is MainWindowViewModel vm && vm.CloseMinimizesToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty && 
            WindowState == WindowState.Minimized && 
            DataContext is MainWindowViewModel vm && 
            vm.MinimizeToTray)
        {
            Hide();
        }
    }
}