using Avalonia.Controls;
using ECCR.Models;
using ECCR.ViewModels;

namespace ECCR.Views;

public partial class ControllerMappingView : UserControl
{
    public ControllerMappingView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Handler for the device-group header's "Assign to Player" dropdown - see
    /// <see cref="MainWindow.OnBulkTargetSelectionChanged"/> for the original rationale. Moved
    /// here unchanged since the ComboBox's DataContext (a <see cref="DeviceMappingGroup"/>) is
    /// the same regardless of which page hosts the grid.
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

            if (TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel vm })
            {
                vm.BulkChangeDeviceTarget(group.DeviceName, targetId);
            }
        }
    }
}
