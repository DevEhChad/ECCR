using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using ECCR.Models;
using ECCR.ViewModels;

namespace ECCR.Views.Shared;

/// <summary>
/// The collapsible per-device binding grid (device header with bulk player-assign/select/
/// remove/+Add Key controls, then one row per <see cref="MappingEntry"/>) shared by
/// <c>ControllerMappingView</c> and <c>DeviceMappingView</c>, which were previously two
/// byte-for-byte copies of this XAML differing only in the device-badge glyph. Row commands
/// still reach <see cref="MainWindowViewModel"/> via the "$parent[Window].((vm:
/// MainWindowViewModel)DataContext).X" compiled-binding path used before extraction, since a
/// UserControl boundary doesn't break that walk up to the owning Window.
/// </summary>
public partial class DeviceMappingGroupsPanel : UserControl
{
    public static readonly StyledProperty<ObservableCollection<DeviceMappingGroup>?> ItemsSourceProperty =
        AvaloniaProperty.Register<DeviceMappingGroupsPanel, ObservableCollection<DeviceMappingGroup>?>(nameof(ItemsSource));

    public static readonly StyledProperty<string> DeviceIconGlyphProperty =
        AvaloniaProperty.Register<DeviceMappingGroupsPanel, string>(nameof(DeviceIconGlyph), defaultValue: "🎮");

    public ObservableCollection<DeviceMappingGroup>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string DeviceIconGlyph
    {
        get => GetValue(DeviceIconGlyphProperty);
        set => SetValue(DeviceIconGlyphProperty, value);
    }

    public DeviceMappingGroupsPanel()
    {
        InitializeComponent();
    }

    /// <summary>Handler for the device-group header's "Assign to Player" dropdown.</summary>
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
