using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ECCR.Models;
using ECCR.Services;
using ECCR.ViewModels;

namespace ECCR.Views.Shared;

/// <summary>
/// The Device Mappings page's "Mappings" tab list: one flat row per <see cref="MappingEntry"/> for
/// the selected device (badge + Input Name + Detected Input + Assigned Key + a primary "Map Input"
/// button, plus an overflow "⚙" flyout for Deadzone/Invert/Calibrate/Player-or-Wheel-Slot/
/// Remove) - single-device-scoped, matching the page's one-device-at-a-time layout. Row
/// commands still reach <see cref="MainWindowViewModel"/> via the "$parent[Window].((vm:
/// MainWindowViewModel)DataContext).X" compiled-binding path, same as every other mapping grid.
/// </summary>
public partial class DeviceMappingTableView : UserControl
{
    public static readonly StyledProperty<ObservableCollection<MappingEntry>?> ItemsSourceProperty =
        AvaloniaProperty.Register<DeviceMappingTableView, ObservableCollection<MappingEntry>?>(nameof(ItemsSource));

    public static readonly StyledProperty<string?> DeviceNameProperty =
        AvaloniaProperty.Register<DeviceMappingTableView, string?>(nameof(DeviceName));

    public static readonly StyledProperty<DeviceMappingGroup?> DeviceMappingGroupProperty =
        AvaloniaProperty.Register<DeviceMappingTableView, DeviceMappingGroup?>(nameof(DeviceMappingGroup));

    /// <summary>Set when the visual device mapping diagram is hidden for the current device, so this table can claim the full width - drives an "expanded" style class (larger font/padding/hitboxes) rather than any layout logic of its own.</summary>
    public static readonly StyledProperty<bool> IsExpandedLayoutProperty =
        AvaloniaProperty.Register<DeviceMappingTableView, bool>(nameof(IsExpandedLayout));

    public bool IsExpandedLayout
    {
        get => GetValue(IsExpandedLayoutProperty);
        set => SetValue(IsExpandedLayoutProperty, value);
    }

    /// <summary>The selected device's mapping group - only used as the CommandParameter for the Select All/Deselect All/Remove Selected toolbar buttons (<see cref="ViewModels.MainWindowViewModel.SelectAllInGroupCommand"/> and friends), which take a whole group rather than an individual entry.</summary>
    public DeviceMappingGroup? DeviceMappingGroup
    {
        get => GetValue(DeviceMappingGroupProperty);
        set => SetValue(DeviceMappingGroupProperty, value);
    }

    public static readonly StyledProperty<string> SearchTextProperty =
        AvaloniaProperty.Register<DeviceMappingTableView, string>(nameof(SearchText), defaultValue: string.Empty);

    /// <summary>Whether <see cref="FilteredEntries"/> currently has any rows - a StyledProperty (not a plain CLR bool) so the empty-state TextBlock's IsVisible binding gets change notifications; a plain property on a UserControl has none.</summary>
    public static readonly StyledProperty<bool> HasEntriesProperty =
        AvaloniaProperty.Register<DeviceMappingTableView, bool>(nameof(HasEntries));

    public bool HasEntries
    {
        get => GetValue(HasEntriesProperty);
        private set => SetValue(HasEntriesProperty, value);
    }

    public ObservableCollection<MappingEntry>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string? DeviceName
    {
        get => GetValue(DeviceNameProperty);
        set => SetValue(DeviceNameProperty, value);
    }

    public string SearchText
    {
        get => GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public ObservableCollection<MappingEntry> FilteredEntries { get; } = new();

    private ObservableCollection<MappingEntry>? _subscribedSource;
    private MainWindowViewModel? _hostVm;

    public DeviceMappingTableView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Closes two gaps between this table and the Visual Device Mapping diagram: scrolling/highlighting
    /// to whichever row is currently armed for capture (<see cref="MainWindowViewModel.ListeningEntry"/>,
    /// settable from either this table's own "Detected Input" buttons or a diagram hotspot), and
    /// live per-row "physical button held down" feedback (<see cref="MainWindowViewModel.OnLiveDeviceStateUpdated"/>),
    /// mirroring <c>DeviceMappingDiagramViewModel.HandleLiveDeviceState</c>'s existing pattern.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel vm })
        {
            _hostVm = vm;
            _hostVm.PropertyChanged += OnHostVmPropertyChanged;
            _hostVm.OnLiveDeviceStateUpdated += OnLiveDeviceStateUpdated;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_hostVm != null)
        {
            _hostVm.PropertyChanged -= OnHostVmPropertyChanged;
            _hostVm.OnLiveDeviceStateUpdated -= OnLiveDeviceStateUpdated;
        }
        _hostVm = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnHostVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.ListeningEntry)) return;

        var entry = _hostVm?.ListeningEntry;
        if (entry == null) return;

        int index = FilteredEntries.IndexOf(entry);
        if (index < 0) return; // this table instance doesn't hold the armed entry - correct no-op

        // Posted at Loaded priority: ArmHotspot/ArmWheelHotspot can create a brand-new
        // MappingEntry and add it to Mappings in the same call that arms it, so its row may not
        // be realized in the panel yet on this same layout pass.
        Dispatcher.UIThread.Post(() => EntriesItemsControl.ScrollIntoView(index), DispatcherPriority.Loaded);
    }

    private void OnLiveDeviceStateUpdated(LiveDeviceInputState state)
    {
        foreach (var entry in FilteredEntries)
        {
            if (entry.SourceType != InputType.Button) continue;
            if (!string.Equals(entry.SourceDeviceName, state.DeviceName, StringComparison.OrdinalIgnoreCase))
            {
                entry.IsLivePressed = false;
                continue;
            }
            entry.IsLivePressed = entry.SourceIndex >= 0 && entry.SourceIndex < state.Buttons.Length && state.Buttons[entry.SourceIndex];
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsSourceProperty)
        {
            if (_subscribedSource != null) _subscribedSource.CollectionChanged -= OnSourceCollectionChanged;
            _subscribedSource = ItemsSource;
            if (_subscribedSource != null) _subscribedSource.CollectionChanged += OnSourceCollectionChanged;
            RebuildFilteredEntries();
        }
        else if (change.Property == SearchTextProperty)
        {
            RebuildFilteredEntries();
        }
        else if (change.Property == IsExpandedLayoutProperty)
        {
            Classes.Set("expanded", change.GetNewValue<bool>());
        }
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildFilteredEntries();

    private void RebuildFilteredEntries()
    {
        FilteredEntries.Clear();
        if (ItemsSource == null) return;

        string search = SearchText?.Trim() ?? string.Empty;
        foreach (var entry in ItemsSource)
        {
            if (search.Length == 0 ||
                entry.SourceDisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.TargetOutput.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (entry.NicknameDisplay != null && entry.NicknameDisplay.Contains(search, StringComparison.OrdinalIgnoreCase)))
            {
                FilteredEntries.Add(entry);
            }
        }

        HasEntries = FilteredEntries.Count > 0;
    }

    /// <summary>
    /// Every bindable target a row's Map Input popup can search/select - the full vocabulary
    /// <c>MainWindowViewModel.AvailableVirtualOutputs</c> already defines for the (previously
    /// unused here) Target dropdown: Xbox, DS4 (PlayStation), Wheel, and Keyboard entries, minus
    /// the Keyboard sentinel (that one exists only to reveal the "Press a Key" flow elsewhere,
    /// never to be selected directly from a list).
    /// </summary>
    private static readonly List<string> AllMappableTargets =
        MainWindowViewModel.AvailableVirtualOutputs.Where(o => o != MainWindowViewModel.KeyboardTargetSentinel).ToList();

    /// <summary>
    /// The currently-open Map Input flyout (captured in <see cref="OnAssignFlyoutOpened"/>) and the
    /// entry it's mapping. Only one row's flyout can ever be open at a time in this UI, so a
    /// single field is safe despite every row's Flyout being a separate instance (one per
    /// DataTemplate-realized row). Closing it always goes through <see cref="FlyoutBase.Hide"/>
    /// rather than hunting for its hosting <see cref="Popup"/> via <c>FindAncestorOfType</c> -
    /// that used to silently no-op, since a Flyout's content is hosted in a separate
    /// <c>PopupRoot</c> disconnected from the main window's VISUAL tree (only the LOGICAL tree is
    /// bridged across that boundary), so <c>FindAncestorOfType&lt;Popup&gt;()</c> (a visual-tree
    /// walk) starting from a button inside the flyout's content never actually found it - Cancel
    /// and Accept looked like they did nothing.
    /// </summary>
    private Flyout? _openMappingFlyout;
    private MappingEntry? _mappingEntry;

    /// <summary>Staged-but-not-yet-committed target selection from the searchable list - see <see cref="OnKeyListSelectionChanged"/>/<see cref="CommitPendingAssignment"/>. Selecting an item no longer commits instantly; only Accept/Enter does, and Cancel (or dismissing the flyout any other way) discards it untouched.</summary>
    private string? _pendingTargetOutput;

    /// <summary>Populates/resets the Map Input popup's searchable target list each time it opens.</summary>
    private void OnAssignFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control content } flyout) return;

        _openMappingFlyout = flyout;
        _mappingEntry = content.DataContext as MappingEntry;
        _pendingTargetOutput = null;

        var listBox = content.FindDescendantOfType<ListBox>();
        if (listBox != null)
        {
            listBox.ItemsSource = AllMappableTargets;
            listBox.SelectedItem = null;
        }

        var searchBox = content.FindDescendantOfType<TextBox>();
        if (searchBox != null) searchBox.Text = string.Empty;
    }

    /// <summary>
    /// Fires no matter how the flyout closed - Cancel, Accept, Escape, or clicking outside the
    /// popup (Avalonia's built-in light-dismiss closes it directly, without routing through
    /// either button's click handler) - so an in-progress keyboard-key capture is always aborted
    /// and any staged-but-unapplied target selection is always discarded, regardless of how the
    /// dialog was dismissed.
    /// </summary>
    private void OnAssignFlyoutClosed(object? sender, EventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel vm } && vm.IsPickingKeyboardKey)
        {
            vm.CancelKeyPick();
        }

        _openMappingFlyout = null;
        _mappingEntry = null;
        _pendingTargetOutput = null;
    }

    private void OnKeySearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        var listBox = tb.FindAncestorOfType<StackPanel>()?.FindDescendantOfType<ListBox>();
        if (listBox == null) return;

        string search = tb.Text?.Trim() ?? string.Empty;
        listBox.ItemsSource = search.Length == 0
            ? AllMappableTargets
            : AllMappableTargets.Where(k => k.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Selecting a target (Xbox/DS4/Wheel) from the searchable list stages it as a preview (the ListBox's own highlight) rather than committing immediately - see <see cref="CommitPendingAssignment"/> for the actual commit (Accept button or Enter), <see cref="OnAssignCancelClick"/>/<see cref="OnAssignFlyoutClosed"/> for discarding it.</summary>
    private void OnKeyListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb || lb.SelectedItem is not string targetOutput) return;
        _pendingTargetOutput = targetOutput;
    }

    /// <summary>"Press a Key" inside the Map Input popup - closes the flyout and arms the same live-capture path the searchable list selection does. Not staged like the list (see <see cref="OnKeyListSelectionChanged"/>): it hands off to the same always-auto-commits-on-detect keyboard capture pipeline as every other "press a key" affordance in the app (status text, Esc-to-cancel, 7s timeout), which stays authoritative once this flyout is gone.</summary>
    private void OnPressKeyToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not MappingEntry entry) return;

        if (TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel vm })
        {
            vm.EnsureMappingRegistered(entry);
            vm.StartKeyPick(entry);
        }

        _openMappingFlyout?.Hide();
    }

    /// <summary>Discards any staged target selection and aborts an in-progress keyboard capture without committing anything, then dismisses the flyout - the Cancel button's routine, also triggered by Escape (Avalonia's built-in light-dismiss) via <see cref="OnAssignFlyoutClosed"/>.</summary>
    private void OnAssignCancelClick(object? sender, RoutedEventArgs e)
    {
        _pendingTargetOutput = null;
        _openMappingFlyout?.Hide();
    }

    private void OnAssignAcceptClick(object? sender, RoutedEventArgs e) => CommitPendingAssignment();

    /// <summary>Enter commits the staged selection exactly like Accept, but only while not actively capturing a keyboard key (Enter is itself a legitimate key someone might be trying to map at that moment).</summary>
    private void OnAssignFlyoutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel vm } && vm.IsPickingKeyboardKey) return;

        CommitPendingAssignment();
        e.Handled = true;
    }

    /// <summary>Commits whatever target is currently staged in the searchable list (if any) to the row being rebound, then closes the flyout - the Accept/Enter path. A no-op close if nothing was ever selected.</summary>
    private void CommitPendingAssignment()
    {
        if (_pendingTargetOutput != null && _mappingEntry != null &&
            TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel vm })
        {
            vm.EnsureMappingRegistered(_mappingEntry);
            _mappingEntry.TargetOutput = _pendingTargetOutput;
        }

        _openMappingFlyout?.Hide();
    }

    // --- Nickname editor flyout (the "✎" button) ---

    private Flyout? _openNicknameFlyout;
    private MappingEntry? _nicknameEntry;
    private TextBox? _nicknameTextBox;

    /// <summary>Pre-fills, focuses, and select-alls the nickname text box - mirrors <see cref="OnAssignFlyoutOpened"/>'s setup-on-open convention.</summary>
    private void OnNicknameFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control content } flyout) return;

        _openNicknameFlyout = flyout;
        _nicknameEntry = content.DataContext as MappingEntry;
        _nicknameTextBox = content.FindDescendantOfType<TextBox>();

        if (_nicknameTextBox != null)
        {
            _nicknameTextBox.Text = _nicknameEntry?.NicknameDisplay ?? string.Empty;
            _nicknameTextBox.Focus();
            _nicknameTextBox.SelectAll();
        }
    }

    private void OnNicknameFlyoutClosed(object? sender, EventArgs e)
    {
        _openNicknameFlyout = null;
        _nicknameEntry = null;
        _nicknameTextBox = null;
    }

    /// <summary>Enter commits and closes, same as the Map Input flyout's Enter-to-accept convention.</summary>
    private void OnNicknameFlyoutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitNickname(_nicknameTextBox?.Text);
        e.Handled = true;
    }

    /// <summary>Discards any edit and dismisses the flyout without touching the saved nickname - also reached via Escape (Avalonia's built-in light-dismiss) through <see cref="OnNicknameFlyoutClosed"/>.</summary>
    private void OnNicknameCancelClick(object? sender, RoutedEventArgs e) => _openNicknameFlyout?.Hide();

    private void OnNicknameClearClick(object? sender, RoutedEventArgs e) => CommitNickname(null);

    private void OnNicknameApplyClick(object? sender, RoutedEventArgs e) => CommitNickname(_nicknameTextBox?.Text);

    /// <summary>Saves (or, given null/blank, clears) the selected device's nickname for this row's physical input in the global <see cref="HardwareProfileService"/> store, updates the row directly so its label reflects the change immediately, then closes the flyout.</summary>
    private void CommitNickname(string? nickname)
    {
        if (_nicknameEntry != null && _hostVm != null)
        {
            var hwProfile = HardwareProfileService.GetOrCreate(_hostVm, _nicknameEntry.SourceDeviceName);
            hwProfile.SetButtonNickname(_nicknameEntry.SourceType, _nicknameEntry.SourceIndex, nickname);
            HardwareProfileService.Save();
            _nicknameEntry.NicknameDisplay = string.IsNullOrWhiteSpace(nickname) ? null : nickname.Trim();

            if (DataContext is DeviceMappingPageViewModel pageVm) pageVm.RefreshNicknameDisplaysForDevice(_nicknameEntry.SourceDeviceName);
        }

        _openNicknameFlyout?.Hide();
    }
}
