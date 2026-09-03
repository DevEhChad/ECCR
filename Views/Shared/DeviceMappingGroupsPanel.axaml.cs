using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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

    /// <summary>
    /// True on the Wheel Mapping page, false (default) on Controller Mapping - swaps the
    /// header's bulk "Assign to Player/Wheel Slot" combo between <c>MainWindowViewModel.PlayerTargets</c>
    /// and <c>WheelSlots</c>, same mechanism as <see cref="DeviceIconGlyph"/>. Individual rows
    /// don't need this: they pick their own Player-vs-Wheel-Slot combo per-row from
    /// <see cref="MappingEntry.IsWheelTarget"/> instead, since a device's rows can be a mix of
    /// "[Xbox]" and "[Wheel]" targets even on one page.
    /// </summary>
    public static readonly StyledProperty<bool> IsWheelPageProperty =
        AvaloniaProperty.Register<DeviceMappingGroupsPanel, bool>(nameof(IsWheelPage), defaultValue: false);

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

    public bool IsWheelPage
    {
        get => GetValue(IsWheelPageProperty);
        set => SetValue(IsWheelPageProperty, value);
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
            else if (cb.SelectedItem is WheelDeviceSlotOption wheelOpt)
            {
                targetId = wheelOpt.Id;
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

    /// <summary>Prefills the rename Flyout's TextBox with the row's current display name (nickname or generated) when it opens.</summary>
    private void OnNicknameFlyoutOpened(object? sender, System.EventArgs e)
    {
        if (sender is Flyout { Content: Control content } && content.DataContext is MappingEntry entry)
        {
            var textBox = content.FindDescendantOfType<TextBox>(includeSelf: true);
            if (textBox != null) textBox.Text = entry.SourceDisplayName;
        }
    }

    private void OnSaveNicknameClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not MappingEntry entry) return;

        var popup = btn.FindAncestorOfType<Popup>();
        string? text = popup?.FindDescendantOfType<TextBox>()?.Text;

        if (TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel vm })
        {
            vm.RenameBoundInput(entry, text);
        }

        if (popup != null) popup.IsOpen = false;
    }

    private void OnClearNicknameClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not MappingEntry entry) return;

        if (TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel vm })
        {
            vm.RenameBoundInput(entry, null);
        }

        var popup = btn.FindAncestorOfType<Popup>();
        if (popup != null) popup.IsOpen = false;
    }
}
