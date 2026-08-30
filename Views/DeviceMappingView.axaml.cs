using Avalonia.Controls;
using ECCR.Models;
using ECCR.ViewModels;

namespace ECCR.Views;

public partial class DeviceMappingView : UserControl
{
    public DeviceMappingView()
    {
        InitializeComponent();
    }

    /// <summary>See <see cref="ControllerMappingView.OnBulkTargetSelectionChanged"/>.</summary>
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
