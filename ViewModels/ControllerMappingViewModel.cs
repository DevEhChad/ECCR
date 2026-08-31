using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ECCR.Models;

namespace ECCR.ViewModels;

/// <summary>
/// Backs the redesigned, tabbed Controller Mapping page. Still exposes
/// <see cref="GroupEntries"/> (<see cref="MainWindowViewModel.GamepadGroupedMappings"/>, the
/// same shared instance, not a copy) for the Button Mapping tab, but also forwards profile
/// switching from <see cref="MainWindowViewModel"/> (same pattern as
/// <see cref="ProfilesViewModel"/>) and owns the page-local tab/device-selection state the
/// Deadzones/Sticks/More Options tabs need.
/// </summary>
public partial class ControllerMappingViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _host;

    public ObservableCollection<DeviceMappingGroup> GroupEntries { get; }

    [ObservableProperty]
    private MappingTab _selectedTab = MappingTab.ButtonMapping;

    /// <summary>
    /// The device the Deadzones/Sticks/More Options tabs are scoped to. Deliberately a name,
    /// never a <see cref="DeviceMappingGroup"/> reference:
    /// <see cref="MainWindowViewModel.RebuildGroupedMappings"/> constructs brand-new group
    /// instances on every rebuild, so holding onto one would go stale the next time any
    /// mapping anywhere changes.
    /// </summary>
    [ObservableProperty]
    private string? _selectedDeviceName;

    /// <summary>Design-time constructor.</summary>
    public ControllerMappingViewModel() : this(new MainWindowViewModel(), new ObservableCollection<DeviceMappingGroup>())
    {
    }

    public ControllerMappingViewModel(MainWindowViewModel host, ObservableCollection<DeviceMappingGroup> groupEntries)
    {
        _host = host;
        GroupEntries = groupEntries;

        GroupEntries.CollectionChanged += (_, _) => RaiseDeviceScopedPropertiesChanged();
        EnsureSelectedDeviceIsValid();

        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.SelectedProfile)
                or nameof(MainWindowViewModel.CustomProfileInput)
                or nameof(MainWindowViewModel.IsCreatingProfile))
            {
                OnPropertyChanged(e.PropertyName);
            }

            if (e.PropertyName is nameof(MainWindowViewModel.GyroAimEnabled) or nameof(MainWindowViewModel.GyroAimSourceDeviceName))
            {
                OnPropertyChanged(nameof(IsGyroAimEnabledForSelectedDevice));
            }
            if (e.PropertyName == nameof(MainWindowViewModel.GyroAimTargetPlayer))
            {
                OnPropertyChanged(nameof(GyroAimTargetPlayer));
            }
            if (e.PropertyName is nameof(MainWindowViewModel.TouchAimEnabled) or nameof(MainWindowViewModel.TouchAimSourceDeviceName))
            {
                OnPropertyChanged(nameof(IsTouchAimEnabledForSelectedDevice));
            }
            if (e.PropertyName == nameof(MainWindowViewModel.TouchAimTargetPlayer))
            {
                OnPropertyChanged(nameof(TouchAimTargetPlayer));
            }
        };

        _host.StatusBar.GamepadStatuses.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SelectedDeviceStatus));
    }

    // --- Profile switching, forwarded from MainWindowViewModel (see ProfilesViewModel) ---

    public ObservableCollection<string> Profiles => _host.Profiles;

    public string SelectedProfile
    {
        get => _host.SelectedProfile;
        set => _host.SelectedProfile = value;
    }

    public string CustomProfileInput
    {
        get => _host.CustomProfileInput;
        set => _host.CustomProfileInput = value;
    }

    public bool IsCreatingProfile => _host.IsCreatingProfile;

    public IRelayCommand SaveCurrentProfileCommand => _host.SaveCurrentProfileCommand;
    public IRelayCommand OpenCreateProfileDialogCommand => _host.OpenCreateProfileDialogCommand;
    public IRelayCommand CancelCreateProfileCommand => _host.CancelCreateProfileCommand;
    public IRelayCommand ConfirmCreateProfileCommand => _host.ConfirmCreateProfileCommand;
    public IRelayCommand DeleteCurrentProfileCommand => _host.DeleteCurrentProfileCommand;

    public IRelayCommand<AppPage> NavigateToCommand => _host.NavigateToCommand;

    // --- Tab strip ---

    [RelayCommand]
    private void SelectTab(MappingTab tab) => SelectedTab = tab;

    public bool IsButtonMappingTabSelected => SelectedTab == MappingTab.ButtonMapping;
    public bool IsDeadzonesTabSelected => SelectedTab == MappingTab.Deadzones;
    public bool IsMotionTabSelected => SelectedTab == MappingTab.Motion;
    public bool IsSticksTabSelected => SelectedTab == MappingTab.Sticks;
    public bool IsTouchpadTabSelected => SelectedTab == MappingTab.Touchpad;
    public bool IsMoreOptionsTabSelected => SelectedTab == MappingTab.MoreOptions;

    partial void OnSelectedTabChanged(MappingTab value)
    {
        OnPropertyChanged(nameof(IsButtonMappingTabSelected));
        OnPropertyChanged(nameof(IsDeadzonesTabSelected));
        OnPropertyChanged(nameof(IsMotionTabSelected));
        OnPropertyChanged(nameof(IsSticksTabSelected));
        OnPropertyChanged(nameof(IsTouchpadTabSelected));
        OnPropertyChanged(nameof(IsMoreOptionsTabSelected));
    }

    // --- Device selection (Deadzones / Sticks / More Options tabs) ---

    partial void OnSelectedDeviceNameChanged(string? value) => RaiseDeviceScopedPropertiesChanged();

    private void EnsureSelectedDeviceIsValid()
    {
        if (SelectedDeviceName != null && GroupEntries.Any(g => g.DeviceName == SelectedDeviceName)) return;
        SelectedDeviceName = GroupEntries.Count > 0 ? GroupEntries[0].DeviceName : null;
    }

    private void RaiseDeviceScopedPropertiesChanged()
    {
        EnsureSelectedDeviceIsValid();
        OnPropertyChanged(nameof(SelectedDeviceGroup));
        OnPropertyChanged(nameof(AxisEntries));
        OnPropertyChanged(nameof(HasAxisEntries));
        OnPropertyChanged(nameof(LeftStickAxes));
        OnPropertyChanged(nameof(HasLeftStickAxes));
        OnPropertyChanged(nameof(RightStickAxes));
        OnPropertyChanged(nameof(HasRightStickAxes));
        OnPropertyChanged(nameof(OtherAxes));
        OnPropertyChanged(nameof(HasOtherAxes));
        OnPropertyChanged(nameof(SelectedDeviceStatus));
        OnPropertyChanged(nameof(HasSelectedDeviceStatus));
        OnPropertyChanged(nameof(IsMotionSupportedForSelectedDevice));
        OnPropertyChanged(nameof(IsGyroAimEnabledForSelectedDevice));
        OnPropertyChanged(nameof(IsTouchAimEnabledForSelectedDevice));
    }

    public DeviceMappingGroup? SelectedDeviceGroup =>
        GroupEntries.FirstOrDefault(g => g.DeviceName == SelectedDeviceName);

    // --- Deadzones tab ---

    public IEnumerable<MappingEntry> AxisEntries =>
        SelectedDeviceGroup?.Entries.Where(e => e.IsAxis) ?? Enumerable.Empty<MappingEntry>();

    public bool HasAxisEntries => AxisEntries.Any();

    // --- Sticks tab: grouped by physical stick via SourceDisplayName, which
    // DevicePresetService.GetAxisDisplayName already labels distinctly per stick. ---

    public IEnumerable<MappingEntry> LeftStickAxes => AxisEntries.Where(e => e.SourceDisplayName.Contains("Left Stick"));
    public bool HasLeftStickAxes => LeftStickAxes.Any();

    public IEnumerable<MappingEntry> RightStickAxes => AxisEntries.Where(e => e.SourceDisplayName.Contains("Right Stick"));
    public bool HasRightStickAxes => RightStickAxes.Any();

    public IEnumerable<MappingEntry> OtherAxes => AxisEntries.Where(e =>
        !e.SourceDisplayName.Contains("Left Stick") && !e.SourceDisplayName.Contains("Right Stick"));
    public bool HasOtherAxes => OtherAxes.Any();

    // --- More Options tab ---

    public ControllerStatus? SelectedDeviceStatus =>
        _host.StatusBar.GamepadStatuses.FirstOrDefault(s => s.DeviceInstanceName == SelectedDeviceName);

    public bool HasSelectedDeviceStatus => SelectedDeviceStatus != null;

    // --- Motion / Touchpad tabs: live gyro/touch readout (from the same ControllerStatus
    // SelectedDeviceStatus already exposes - ControllerHidService fills these in at ~30Hz) plus
    // the Gyro-Aim/Touch-Aim toggles, which apply to whichever device is selected above rather
    // than exposing a separate device picker. See MainWindowViewModel.ApplyMotionAndTouchAim
    // for what these toggles actually drive. ---

    public bool IsMotionSupportedForSelectedDevice => SelectedDeviceStatus?.IsMotionSupported ?? false;

    public bool IsGyroAimEnabledForSelectedDevice
    {
        get => _host.GyroAimEnabled && _host.GyroAimSourceDeviceName == SelectedDeviceName;
        set
        {
            if (value)
            {
                _host.GyroAimSourceDeviceName = SelectedDeviceName;
                _host.GyroAimEnabled = true;
            }
            else if (_host.GyroAimSourceDeviceName == SelectedDeviceName)
            {
                _host.GyroAimEnabled = false;
            }
            OnPropertyChanged();
        }
    }

    public bool IsTouchAimEnabledForSelectedDevice
    {
        get => _host.TouchAimEnabled && _host.TouchAimSourceDeviceName == SelectedDeviceName;
        set
        {
            if (value)
            {
                _host.TouchAimSourceDeviceName = SelectedDeviceName;
                _host.TouchAimEnabled = true;
            }
            else if (_host.TouchAimSourceDeviceName == SelectedDeviceName)
            {
                _host.TouchAimEnabled = false;
            }
            OnPropertyChanged();
        }
    }

    public uint GyroAimTargetPlayer
    {
        get => _host.GyroAimTargetPlayer;
        set => _host.GyroAimTargetPlayer = value;
    }

    public uint TouchAimTargetPlayer
    {
        get => _host.TouchAimTargetPlayer;
        set => _host.TouchAimTargetPlayer = value;
    }
}
