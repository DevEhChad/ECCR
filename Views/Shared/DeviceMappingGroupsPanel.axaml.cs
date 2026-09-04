using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ECCR.Models;
using ECCR.Services;
using ECCR.ViewModels;

namespace ECCR.Views.Shared;

/// <summary>
/// The collapsible per-device binding grid (device header with rename/bulk player-assign/
/// Output-As/select/remove/+Add Key controls, then one row per <see cref="MappingEntry"/>)
/// shared by <c>ControllerMappingView</c> and <c>DeviceMappingView</c>, which were previously
/// two byte-for-byte copies of this XAML differing only in the device-badge glyph. Row commands
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
    /// <see cref="MappingEntry.ShowPlayerCombo"/>/<see cref="MappingEntry.ShowWheelSlotCombo"/> instead, since a
    /// device's rows can be a mix of "[Xbox]"/"[Wheel]"/"[Keyboard]" targets even on one page.
    /// </summary>
    public static readonly StyledProperty<bool> IsWheelPageProperty =
        AvaloniaProperty.Register<DeviceMappingGroupsPanel, bool>(nameof(IsWheelPage), defaultValue: false);

    /// <summary>Live search text filtering <see cref="FilteredGroups"/> - see <see cref="RebuildFilteredGroups"/>.</summary>
    public static readonly StyledProperty<string> SearchTextProperty =
        AvaloniaProperty.Register<DeviceMappingGroupsPanel, string>(nameof(SearchText), defaultValue: string.Empty);

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

    public string SearchText
    {
        get => GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    /// <summary>
    /// What the grid's <c>ItemsControl</c> actually binds to. Mirrors <see cref="ItemsSource"/>
    /// by reference when <see cref="SearchText"/> is empty (preserving group/row identity and
    /// expand-state exactly); otherwise holds a filtered projection - see
    /// <see cref="RebuildFilteredGroups"/>.
    /// </summary>
    public ObservableCollection<DeviceMappingGroup> FilteredGroups { get; } = new();

    private ObservableCollection<DeviceMappingGroup>? _subscribedSource;

    public DeviceMappingGroupsPanel()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsSourceProperty)
        {
            if (_subscribedSource != null) _subscribedSource.CollectionChanged -= OnSourceCollectionChanged;
            _subscribedSource = ItemsSource;
            if (_subscribedSource != null) _subscribedSource.CollectionChanged += OnSourceCollectionChanged;
            RebuildFilteredGroups();
        }
        else if (change.Property == SearchTextProperty)
        {
            RebuildFilteredGroups();
        }
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildFilteredGroups();

    /// <summary>
    /// Rebuilds <see cref="FilteredGroups"/> from <see cref="ItemsSource"/>. With no search text,
    /// mirrors the source collection by reference (same <see cref="DeviceMappingGroup"/>
    /// instances - <see cref="ItemsSource"/>'s groups are shared with <c>MainWindowViewModel</c>'s
    /// other filtered views, never copies, so nothing here may mutate their <c>Entries</c>).
    /// With search text, a whole-name-matching group passes through unchanged; otherwise a
    /// transient proxy group is built containing only the matching rows (device/input/target
    /// text), and groups with zero matches are omitted. The proxy's <c>IsExpanded</c> doesn't
    /// write back to the shared instance - it resets when the search clears, which is fine since
    /// it's just there to make a search hit immediately visible.
    /// </summary>
    private void RebuildFilteredGroups()
    {
        FilteredGroups.Clear();
        if (ItemsSource == null) return;

        string search = SearchText?.Trim() ?? string.Empty;
        if (search.Length == 0)
        {
            foreach (var g in ItemsSource) FilteredGroups.Add(g);
            return;
        }

        foreach (var group in ItemsSource)
        {
            if (group.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                FilteredGroups.Add(group);
                continue;
            }

            var matches = group.Entries.Where(entry =>
                entry.SourceDisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.TargetOutput.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

            if (matches.Count == 0) continue;

            var proxy = new DeviceMappingGroup(group.DeviceName) { DisplayName = group.DisplayName, IsExpanded = true };
            foreach (var entry in matches) proxy.Entries.Add(entry);
            FilteredGroups.Add(proxy);
        }
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

    /// <summary>Prefills the per-input rename Flyout's TextBox with the row's current display name (nickname or generated) when it opens.</summary>
    private void OnNicknameFlyoutOpened(object? sender, EventArgs e)
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

    /// <summary>Prefills the whole-device rename Flyout's TextBox - counterpart to <see cref="OnNicknameFlyoutOpened"/> for the group header.</summary>
    private void OnDeviceNicknameFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is Flyout { Content: Control content } && content.DataContext is DeviceMappingGroup group)
        {
            var textBox = content.FindDescendantOfType<TextBox>(includeSelf: true);
            if (textBox != null) textBox.Text = group.DisplayName;
        }
    }

    private void OnSaveDeviceNicknameClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not DeviceMappingGroup group) return;

        var popup = btn.FindAncestorOfType<Popup>();
        string? text = popup?.FindDescendantOfType<TextBox>()?.Text;

        if (TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel vm })
        {
            vm.RenameDevice(group.DeviceName, text);
        }

        if (popup != null) popup.IsOpen = false;
    }

    private void OnClearDeviceNicknameClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not DeviceMappingGroup group) return;

        if (TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel vm })
        {
            vm.RenameDevice(group.DeviceName, null);
        }

        var popup = btn.FindAncestorOfType<Popup>();
        if (popup != null) popup.IsOpen = false;
    }

    /// <summary>Populates/resets the Key Picker Flyout's searchable list each time it opens.</summary>
    private void OnKeyPickerFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control content }) return;

        var listBox = content.FindDescendantOfType<ListBox>();
        if (listBox != null) listBox.ItemsSource = KeyboardKeyCatalog.AllKeyNames;

        var searchBox = content.FindDescendantOfType<TextBox>();
        if (searchBox != null) searchBox.Text = string.Empty;
    }

    private void OnKeySearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        var listBox = tb.FindAncestorOfType<StackPanel>()?.FindDescendantOfType<ListBox>();
        if (listBox == null) return;

        string search = tb.Text?.Trim() ?? string.Empty;
        listBox.ItemsSource = search.Length == 0
            ? KeyboardKeyCatalog.AllKeyNames
            : KeyboardKeyCatalog.AllKeyNames.Where(k => k.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Picking a key from the searchable list assigns it directly - the row's existing TargetOutput property-changed hook handles auto-save, same as any other Target combo edit.</summary>
    private void OnKeyListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb || lb.SelectedItem is not string keyName) return;
        if (lb.DataContext is not MappingEntry entry) return;

        entry.TargetOutput = $"[Keyboard] {keyName}";

        var popup = lb.FindAncestorOfType<Popup>();
        if (popup != null) popup.IsOpen = false;
    }

    /// <summary>"Press a Key" inside the Key Picker Flyout - closes the flyout and arms the same live-capture path the keyboard sentinel selection does.</summary>
    private void OnPressKeyToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not MappingEntry entry) return;

        if (TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel vm })
        {
            vm.StartKeyPick(entry);
        }

        var popup = btn.FindAncestorOfType<Popup>();
        if (popup != null) popup.IsOpen = false;
    }

    /// <summary>The "↩" button next to a keyboard-target row - falls back to a normal virtual-gamepad default and re-reveals the ComboBox.</summary>
    private void OnResetKeyboardTargetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not MappingEntry entry) return;
        entry.TargetOutput = "[Xbox] Xbox A (Cross / South / Handbrake)";
    }

    private void OnCancelOutputAsFlyoutClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var popup = btn.FindAncestorOfType<Popup>();
        if (popup != null) popup.IsOpen = false;
    }

    private void OnConfirmOutputAsXboxClick(object? sender, RoutedEventArgs e) => ApplyOutputAs(sender, OutputCategory.Xbox);
    private void OnConfirmOutputAsDs4Click(object? sender, RoutedEventArgs e) => ApplyOutputAs(sender, OutputCategory.DualShock4);
    private void OnConfirmOutputAsWheelClick(object? sender, RoutedEventArgs e) => ApplyOutputAs(sender, OutputCategory.Wheel);

    /// <summary>
    /// The header's "Output As" switch - re-applies the Auto-Bind preset for this device in
    /// <paramref name="category"/> (see <see cref="MainWindowViewModel.ApplyOutputModeToDevice"/>),
    /// replacing its existing bindings. Defaults the new bindings to target slot 1 (Player 1 /
    /// Wheel Slot 1) - the existing "Assign to Player"/"Assign to Wheel Slot" combo right next to
    /// this control is how the user redirects that afterward, same as it already does for any
    /// other device's bindings.
    /// </summary>
    private void ApplyOutputAs(object? sender, OutputCategory category)
    {
        if (sender is not Button btn || btn.DataContext is not DeviceMappingGroup group) return;

        if (TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel vm })
        {
            vm.ApplyOutputModeToDevice(group.DeviceName, category, targetDeviceId: 1);
        }

        var popup = btn.FindAncestorOfType<Popup>();
        if (popup != null) popup.IsOpen = false;
    }
}
