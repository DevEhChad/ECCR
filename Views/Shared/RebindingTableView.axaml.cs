using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ECCR.Models;
using ECCR.ViewModels;

namespace ECCR.Views.Shared;

/// <summary>
/// The Binder page's "Bindings" tab list: one flat row per <see cref="MappingEntry"/> for the
/// selected device (badge + Input Name + Detected Input + Assigned Key + a primary "Rebind"
/// button, plus an overflow "⚙" flyout for Deadzone/Invert/Calibrate/Player-or-Wheel-Slot/
/// Remove) - single-device-scoped, matching the Binder page's one-device-at-a-time layout. Row
/// commands still reach <see cref="MainWindowViewModel"/> via the "$parent[Window].((vm:
/// MainWindowViewModel)DataContext).X" compiled-binding path, same as every other mapping grid.
/// </summary>
public partial class RebindingTableView : UserControl
{
    public static readonly StyledProperty<ObservableCollection<MappingEntry>?> ItemsSourceProperty =
        AvaloniaProperty.Register<RebindingTableView, ObservableCollection<MappingEntry>?>(nameof(ItemsSource));

    public static readonly StyledProperty<string?> DeviceNameProperty =
        AvaloniaProperty.Register<RebindingTableView, string?>(nameof(DeviceName));

    public static readonly StyledProperty<DeviceMappingGroup?> DeviceMappingGroupProperty =
        AvaloniaProperty.Register<RebindingTableView, DeviceMappingGroup?>(nameof(DeviceMappingGroup));

    /// <summary>The selected device's mapping group - only used as the CommandParameter for the Select All/Deselect All/Remove Selected toolbar buttons (<see cref="ViewModels.MainWindowViewModel.SelectAllInGroupCommand"/> and friends), which take a whole group rather than an individual entry.</summary>
    public DeviceMappingGroup? DeviceMappingGroup
    {
        get => GetValue(DeviceMappingGroupProperty);
        set => SetValue(DeviceMappingGroupProperty, value);
    }

    public static readonly StyledProperty<string> SearchTextProperty =
        AvaloniaProperty.Register<RebindingTableView, string>(nameof(SearchText), defaultValue: string.Empty);

    /// <summary>Whether <see cref="FilteredEntries"/> currently has any rows - a StyledProperty (not a plain CLR bool) so the empty-state TextBlock's IsVisible binding gets change notifications; a plain property on a UserControl has none.</summary>
    public static readonly StyledProperty<bool> HasEntriesProperty =
        AvaloniaProperty.Register<RebindingTableView, bool>(nameof(HasEntries));

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

    public RebindingTableView()
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
            RebuildFilteredEntries();
        }
        else if (change.Property == SearchTextProperty)
        {
            RebuildFilteredEntries();
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
                entry.TargetOutput.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                FilteredEntries.Add(entry);
            }
        }

        HasEntries = FilteredEntries.Count > 0;
    }

    /// <summary>
    /// Every bindable target a row's Rebind popup can search/select - the full vocabulary
    /// <c>MainWindowViewModel.AvailableVirtualOutputs</c> already defines for the (previously
    /// unused here) Target dropdown: Xbox, DS4 (PlayStation), Wheel, and Keyboard entries, minus
    /// the Keyboard sentinel (that one exists only to reveal the "Press a Key" flow elsewhere,
    /// never to be selected directly from a list).
    /// </summary>
    private static readonly List<string> AllBindableTargets =
        MainWindowViewModel.AvailableVirtualOutputs.Where(o => o != MainWindowViewModel.KeyboardTargetSentinel).ToList();

    /// <summary>Populates/resets the Rebind popup's searchable target list each time it opens.</summary>
    private void OnRebindFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control content }) return;

        var listBox = content.FindDescendantOfType<ListBox>();
        if (listBox != null) listBox.ItemsSource = AllBindableTargets;

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
            ? AllBindableTargets
            : AllBindableTargets.Where(k => k.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Selecting any target (Xbox/DS4/Wheel/Keyboard) from the searchable list assigns it directly - the list items are already fully-prefixed target strings, not bare key names.</summary>
    private void OnKeyListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb || lb.SelectedItem is not string targetOutput) return;
        if (lb.DataContext is not MappingEntry entry) return;

        if (TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel vm })
        {
            vm.EnsureMappingRegistered(entry);
        }

        entry.TargetOutput = targetOutput;

        var popup = lb.FindAncestorOfType<Popup>();
        if (popup != null) popup.IsOpen = false;
    }

    /// <summary>"Press a Key" inside the Rebind popup - closes the flyout and arms the same live-capture path the searchable list selection does.</summary>
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

    private void OnRebindCancelClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel vm } && vm.IsPickingKeyboardKey)
        {
            vm.CancelKeyPick();
        }

        var popup = (sender as Button)?.FindAncestorOfType<Popup>();
        if (popup != null) popup.IsOpen = false;
    }

    private void OnRebindAcceptClick(object? sender, RoutedEventArgs e)
    {
        var popup = (sender as Button)?.FindAncestorOfType<Popup>();
        if (popup != null) popup.IsOpen = false;
    }
}
