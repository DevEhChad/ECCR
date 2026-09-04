using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ECCR.Models;

/// <summary>
/// Purely a UI grouping: the main window's mapping list is rendered per physical device
/// (one collapsible section per <see cref="DeviceName"/>) rather than as one flat list of
/// <see cref="MappingEntry"/> rows. Rebuilt from <c>MainWindowViewModel.Mappings</c> by
/// <c>MainWindowViewModel.RebuildGroupedMappings</c> whenever the underlying collection
/// changes - it is not itself persisted.
/// </summary>
public partial class DeviceMappingGroup : ObservableObject
{
    public string DeviceName { get; }

    /// <summary>
    /// The name actually shown in the UI - the user's device nickname
    /// (<see cref="WheelDeviceSettings.DeviceNickname"/>) if set, else <see cref="DeviceName"/>.
    /// <see cref="DeviceName"/> itself stays the raw DirectInput name always, since it's the
    /// lookup key used by grouping/bulk-assign/auto-bind; only this property changes for display.
    /// Set by <c>MainWindowViewModel.RebuildGroupedMappings</c> via
    /// <see cref="ECCR.Services.DevicePresetService.GetEffectiveDeviceName"/>.
    /// </summary>
    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private bool _isExpanded = true;

    public ObservableCollection<MappingEntry> Entries { get; } = new();

    public int EntryCount => Entries.Count;

    public DeviceMappingGroup(string deviceName)
    {
        DeviceName = deviceName;
        _displayName = deviceName;
        Entries.CollectionChanged += (s, e) => OnPropertyChanged(nameof(EntryCount));
    }
}