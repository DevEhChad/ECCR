using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ECCR.Models;

namespace ECCR.ViewModels;

/// <summary>
/// Backs the Device Mapping (Direct Input/Wheels) page. Still exposes <see cref="GroupEntries"/>
/// (<see cref="MainWindowViewModel.WheelGroupedMappings"/>, the same shared instance, not a
/// copy) for the Button Mapping tab, but also owns the page-local tab/device-selection state the
/// new Wheel Settings tab needs - same pattern as <see cref="ControllerMappingViewModel"/>'s
/// Deadzones/Sticks/More Options tabs, just for wheel-specific tuning instead.
/// </summary>
public partial class DeviceMappingViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _host;

    public ObservableCollection<DeviceMappingGroup> GroupEntries { get; }

    [ObservableProperty]
    private DeviceMappingTab _selectedTab = DeviceMappingTab.ButtonMapping;

    /// <summary>
    /// The device the Wheel Settings tab is scoped to. A name, not a
    /// <see cref="DeviceMappingGroup"/> reference - see <see cref="ControllerMappingViewModel.SelectedDeviceName"/>
    /// for why (group instances are rebuilt from scratch on every mapping change).
    /// </summary>
    [ObservableProperty]
    private string? _selectedDeviceName;

    /// <summary>Design-time constructor.</summary>
    public DeviceMappingViewModel() : this(new MainWindowViewModel(), new ObservableCollection<DeviceMappingGroup>())
    {
    }

    public DeviceMappingViewModel(MainWindowViewModel host, ObservableCollection<DeviceMappingGroup> groupEntries)
    {
        _host = host;
        GroupEntries = groupEntries;

        GroupEntries.CollectionChanged += (_, _) => RaiseDeviceScopedPropertiesChanged();
        EnsureSelectedDeviceIsValid();
    }

    public IRelayCommand<AppPage> NavigateToCommand => _host.NavigateToCommand;

    // --- Tab strip ---

    [RelayCommand]
    private void SelectTab(DeviceMappingTab tab) => SelectedTab = tab;

    public bool IsButtonMappingTabSelected => SelectedTab == DeviceMappingTab.ButtonMapping;
    public bool IsWheelSettingsTabSelected => SelectedTab == DeviceMappingTab.WheelSettings;

    partial void OnSelectedTabChanged(DeviceMappingTab value)
    {
        OnPropertyChanged(nameof(IsButtonMappingTabSelected));
        OnPropertyChanged(nameof(IsWheelSettingsTabSelected));
    }

    // --- Device selection (Wheel Settings tab) ---

    partial void OnSelectedDeviceNameChanged(string? value) => RaiseDeviceScopedPropertiesChanged();

    private void EnsureSelectedDeviceIsValid()
    {
        if (SelectedDeviceName != null && GroupEntries.Any(g => g.DeviceName == SelectedDeviceName)) return;
        SelectedDeviceName = GroupEntries.Count > 0 ? GroupEntries[0].DeviceName : null;
    }

    private void RaiseDeviceScopedPropertiesChanged()
    {
        EnsureSelectedDeviceIsValid();
        OnPropertyChanged(nameof(SelectedWheelSettings));
        OnPropertyChanged(nameof(HasSelectedDevice));
        OnPropertyChanged(nameof(IsSelectedDeviceForceFeedbackCapable));
    }

    // --- Wheel Settings tab ---

    public bool HasSelectedDevice => SelectedDeviceName != null;

    public WheelDeviceSettings? SelectedWheelSettings =>
        SelectedDeviceName == null ? null : _host.GetOrCreateWheelSettings(SelectedDeviceName);

    /// <summary>
    /// Best-effort DirectInput capability probe (see <see cref="Services.WheelForceFeedbackService.DeviceSupportsForceFeedback"/>)
    /// backing the "this wheel isn't reporting FFB support" banner. Resolved from whichever
    /// mapping entry for this device carries a GUID, since <see cref="DeviceMappingGroup"/> only
    /// tracks a display name.
    /// </summary>
    public bool IsSelectedDeviceForceFeedbackCapable
    {
        get
        {
            var group = GroupEntries.FirstOrDefault(g => g.DeviceName == SelectedDeviceName);
            var guid = group?.Entries.FirstOrDefault(e => e.SourceDeviceGuid != System.Guid.Empty)?.SourceDeviceGuid;
            return guid != null && _host.WheelForceFeedback.DeviceSupportsForceFeedback(guid.Value);
        }
    }

    [RelayCommand]
    private void OpenHidHide() => _host.NavigateToCommand.Execute(AppPage.HidHide);
}
