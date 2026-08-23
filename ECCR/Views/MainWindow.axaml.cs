using Avalonia.Controls;
using ECCR.Models;
using ECCR.ViewModels;

namespace ECCR.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnWindowClosing;
    }

    public void OnBulkTargetSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is uint targetId)
        {
            if (cb.DataContext is DeviceMappingGroup group && DataContext is MainWindowViewModel vm)
            {
                vm.BulkChangeDeviceTarget(group.DeviceName, targetId);
            }
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            if (vm.CloseMinimizesToTray && vm.RunInSystemTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            vm.CleanupAndShutdown();
        }
    }
}