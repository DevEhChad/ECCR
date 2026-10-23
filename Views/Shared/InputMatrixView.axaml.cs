using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ECCR.ViewModels;

namespace ECCR.Views.Shared;

/// <summary>
/// Code-behind for the Input Matrix tab's tile/axis grids. Owns only the per-tile nickname
/// editor flyout's and per-axis quick-assign flyout's staged-not-committed state - the grids
/// themselves (<see cref="InputMatrixViewModel.Tiles"/>/<see cref="InputMatrixViewModel.AxisTiles"/>)
/// and live press/value state are fully data-driven, no code-behind needed there. Mirrors
/// <see cref="DeviceMappingTableView"/>'s Map/nickname flyout convention: plain event handlers,
/// fields cached in <c>Opened</c> and used by the Cancel/Apply/Clear/Enter/selection paths,
/// cleared in <c>Closed</c> so a light-dismiss (Escape, click-outside) never leaves stale state.
/// </summary>
public partial class InputMatrixView : UserControl
{
    private Flyout? _openTileFlyout;
    private InputMatrixTileViewModel? _tile;
    private TextBox? _tileTextBox;

    private Flyout? _openAxisFlyout;
    private InputMatrixAxisTileViewModel? _axisTile;

    private Flyout? _openAxisNicknameFlyout;
    private InputMatrixAxisTileViewModel? _axisNicknameTile;
    private TextBox? _axisNicknameTextBox;

    public InputMatrixView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachStorageProviderResolver();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachStorageProviderResolver();
    }

    /// <summary>Lets the view model open Export/Import's file pickers via the hosting TopLevel, without depending on any specific window - same pattern as <see cref="HidHideView"/>.</summary>
    private void AttachStorageProviderResolver()
    {
        if (DataContext is InputMatrixViewModel vm)
        {
            vm.StorageProviderResolver = () => TopLevel.GetTopLevel(this)?.StorageProvider;
        }
    }

    // --- Auto-Map layout flyout ---

    private Flyout? _openAutoMapFlyout;

    private void OnAutoMapFlyoutOpened(object? sender, EventArgs e)
    {
        _openAutoMapFlyout = sender as Flyout;
        (DataContext as InputMatrixViewModel)?.PrepareAutoMap();
    }

    private void OnAutoMapFlyoutClosed(object? sender, EventArgs e) => _openAutoMapFlyout = null;

    private void OnAutoMapCancelClick(object? sender, RoutedEventArgs e) => _openAutoMapFlyout?.Hide();

    private void OnAutoMapApplyClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as InputMatrixViewModel)?.ApplyAutoMap();
        _openAutoMapFlyout?.Hide();
    }

    // --- Button/POV tile nickname editor flyout ---

    private void OnTileFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control content } flyout) return;

        _openTileFlyout = flyout;
        _tile = content.DataContext as InputMatrixTileViewModel;
        _tileTextBox = content.FindDescendantOfType<TextBox>();

        if (_tileTextBox != null)
        {
            _tileTextBox.Text = _tile?.Nickname ?? string.Empty;
            _tileTextBox.Focus();
            _tileTextBox.SelectAll();
        }
    }

    private void OnTileFlyoutClosed(object? sender, EventArgs e)
    {
        _openTileFlyout = null;
        _tile = null;
        _tileTextBox = null;
    }

    private void OnTileFlyoutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitNickname(_tileTextBox?.Text);
        e.Handled = true;
    }

    private void OnTileCancelClick(object? sender, RoutedEventArgs e) => _openTileFlyout?.Hide();

    private void OnTileClearClick(object? sender, RoutedEventArgs e) => CommitNickname(null);

    private void OnTileApplyClick(object? sender, RoutedEventArgs e) => CommitNickname(_tileTextBox?.Text);

    private void CommitNickname(string? nickname)
    {
        _tile?.CommitNickname(nickname);
        _openTileFlyout?.Hide();
    }

    // --- Axis quick-assign flyout ---

    /// <summary>Same searchable-target vocabulary as the Device Mapping Table's Map Input popup (<c>DeviceMappingTableView.AllBindableTargets</c>) - every real Xbox/DS4/Wheel target, minus the Keyboard sentinel (axes have no keyboard-key concept).</summary>
    private static readonly List<string> AllAxisTargets =
        MainWindowViewModel.AvailableVirtualOutputs.Where(o => o != MainWindowViewModel.KeyboardTargetSentinel).ToList();

    private void OnAxisFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control content } flyout) return;

        _openAxisFlyout = flyout;
        _axisTile = content.DataContext as InputMatrixAxisTileViewModel;

        var listBox = content.FindDescendantOfType<ListBox>();
        if (listBox != null)
        {
            listBox.ItemsSource = AllAxisTargets;
            listBox.SelectedItem = null;
        }

        var searchBox = content.FindDescendantOfType<TextBox>();
        if (searchBox != null) searchBox.Text = string.Empty;
    }

    private void OnAxisFlyoutClosed(object? sender, EventArgs e)
    {
        _openAxisFlyout = null;
        _axisTile = null;
    }

    private void OnAxisSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        var listBox = tb.FindAncestorOfType<StackPanel>()?.FindDescendantOfType<ListBox>();
        if (listBox == null) return;

        string search = tb.Text?.Trim() ?? string.Empty;
        listBox.ItemsSource = search.Length == 0
            ? AllAxisTargets
            : AllAxisTargets.Where(k => k.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Selecting a target commits and closes immediately - a single-click "quick assign", unlike the Map Input popup's stage-then-Accept flow, since there's nothing else to configure here.</summary>
    private void OnAxisTargetSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: string targetOutput } || _axisTile == null) return;

        _axisTile.AssignTarget(targetOutput);
        _openAxisFlyout?.Hide();
    }

    private void OnAxisCancelClick(object? sender, RoutedEventArgs e) => _openAxisFlyout?.Hide();

    // --- Axis alias editor flyout (the "✎" icon button) ---

    private void OnAxisNicknameFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control content } flyout) return;

        _openAxisNicknameFlyout = flyout;
        _axisNicknameTile = content.DataContext as InputMatrixAxisTileViewModel;
        _axisNicknameTextBox = content.FindDescendantOfType<TextBox>();

        if (_axisNicknameTextBox != null)
        {
            _axisNicknameTextBox.Text = _axisNicknameTile?.Nickname ?? string.Empty;
            _axisNicknameTextBox.Focus();
            _axisNicknameTextBox.SelectAll();
        }
    }

    private void OnAxisNicknameFlyoutClosed(object? sender, EventArgs e)
    {
        _openAxisNicknameFlyout = null;
        _axisNicknameTile = null;
        _axisNicknameTextBox = null;
    }

    private void OnAxisNicknameFlyoutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitAxisNickname(_axisNicknameTextBox?.Text);
        e.Handled = true;
    }

    private void OnAxisNicknameCancelClick(object? sender, RoutedEventArgs e) => _openAxisNicknameFlyout?.Hide();

    private void OnAxisNicknameClearClick(object? sender, RoutedEventArgs e) => CommitAxisNickname(null);

    private void OnAxisNicknameApplyClick(object? sender, RoutedEventArgs e) => CommitAxisNickname(_axisNicknameTextBox?.Text);

    private void CommitAxisNickname(string? nickname)
    {
        _axisNicknameTile?.CommitNickname(nickname);
        _openAxisNicknameFlyout?.Hide();
    }
}
