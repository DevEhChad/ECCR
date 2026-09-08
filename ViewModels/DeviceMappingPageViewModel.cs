using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ECCR.Models;
using ECCR.Services;

namespace ECCR.ViewModels;

/// <summary>
/// Backs the unified Device Mappings page: one device list (every connected wheel, pedal set, and
/// gamepad, same shared instance as <see cref="ConnectedDevicesPageViewModel.Devices"/>) with
/// an inline content swap on selection, replacing the old split Controller Mapping / Device
/// Mapping pages. The "Mappings" tab (<see cref="Diagram"/> + the Device Mapping Table) is always
/// available and is the new default view; the remaining tabs are ported near-verbatim from
/// <c>ControllerMappingViewModel</c> (Deadzones/Sticks/Motion/Touchpad/MoreOptions, gamepad
/// only) and <c>DeviceMappingViewModel</c> (WheelSettings, wheel only), gated by
/// <see cref="IsSelectedDeviceGamepad"/>/<see cref="IsSelectedDeviceWheel"/>.
/// </summary>
public partial class DeviceMappingPageViewModel : ViewModelBase, INavigationAware
{
    private readonly MainWindowViewModel _host;
    private readonly ObservableCollection<DeviceMappingGroup> _gamepadGroups;
    private readonly ObservableCollection<DeviceMappingGroup> _wheelGroups;
    private System.Collections.Specialized.INotifyCollectionChanged? _subscribedGroupEntries;

    public ObservableCollection<ControllerStatus> Devices { get; }
    public bool HasDevices => Devices.Count > 0;

    public DeviceMappingDiagramViewModel Diagram { get; }

    public InputMatrixViewModel InputMatrix { get; }

    /// <summary>Controller LED state, retargeted to whichever gamepad is currently selected (see <see cref="RaiseDeviceScopedPropertiesChanged"/>) - embedded in the More Options tab instead of the old standalone Controller LED page.</summary>
    public ControllerLedViewModel ControllerLed => _host.ControllerLedViewModel;

    /// <summary>
    /// One row per real, already-bound entry for the selected device, plus (only when
    /// <see cref="IsShowAllInputsEnabled"/> is on) a synthesized placeholder for every unbound
    /// button/axis the device reports - see <see cref="RebuildDisplayRows"/>. This is what
    /// <see cref="Views.Shared.DeviceMappingTableView"/> actually binds to now, not
    /// <see cref="SelectedDeviceGroup"/>'s <c>Entries</c> directly. Placeholder rows are opt-in
    /// (default off) so the table doesn't clutter itself with every physical input a device
    /// could report the moment it's selected.
    /// </summary>
    public ObservableCollection<MappingEntry> DisplayRows { get; } = new();

    /// <summary>Starts <see cref="Diagram"/>/<see cref="InputMatrix"/>'s live-state subscriptions - see <see cref="INavigationAware"/>'s doc comment for why this can't just happen in their constructors.</summary>
    public void OnNavigatedTo()
    {
        Diagram.OnNavigatedTo();
        InputMatrix.OnNavigatedTo();
    }

    public void OnNavigatedFrom()
    {
        Diagram.OnNavigatedFrom();
        InputMatrix.OnNavigatedFrom();
    }

    [ObservableProperty]
    private DeviceMappingTab _selectedTab = DeviceMappingTab.Mappings;

    /// <summary>
    /// The selected device's instance name - validated against <see cref="Devices"/> (physically
    /// connected hardware), not against the mapping groups (which can include unbound
    /// "Uncategorized ..." placeholder groups), so an unplugged device never stays "selected"
    /// with a dead diagram.
    /// </summary>
    [ObservableProperty]
    private string? _selectedDeviceName;

    /// <summary>Design-time constructor.</summary>
    public DeviceMappingPageViewModel() : this(new MainWindowViewModel(), new ObservableCollection<ControllerStatus>(),
        new ObservableCollection<DeviceMappingGroup>(), new ObservableCollection<DeviceMappingGroup>())
    {
    }

    public DeviceMappingPageViewModel(MainWindowViewModel host, ObservableCollection<ControllerStatus> devices,
        ObservableCollection<DeviceMappingGroup> gamepadGroups, ObservableCollection<DeviceMappingGroup> wheelGroups)
    {
        _host = host;
        Devices = devices;
        _gamepadGroups = gamepadGroups;
        _wheelGroups = wheelGroups;

        Diagram = new DeviceMappingDiagramViewModel(host, this);
        InputMatrix = new InputMatrixViewModel(host, this);

        Devices.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasDevices));
            RaiseDeviceScopedPropertiesChanged();
        };
        _gamepadGroups.CollectionChanged += (_, _) => RaiseDeviceScopedPropertiesChanged();
        _wheelGroups.CollectionChanged += (_, _) => RaiseDeviceScopedPropertiesChanged();
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
    }

    public IRelayCommand<AppPage> NavigateToCommand => _host.NavigateToCommand;
    public IRelayCommand<MappingEntry> StartListeningCommand => _host.StartListeningCommand;
    public IRelayCommand<MappingEntry> RemoveMappingCommand => _host.RemoveMappingCommand;
    public IRelayCommand<string> AddMappingToDeviceCommand => _host.AddMappingToDeviceCommand;

    // --- Device selection ---

    [RelayCommand]
    private void SelectDevice(string deviceName) => SelectedDeviceName = deviceName;

    public DeviceHardwareCategory SelectedDeviceCategory =>
        DevicePresetService.DetectCategory(SelectedDeviceName ?? string.Empty);

    public bool IsSelectedDeviceWheel => SelectedDeviceName != null && DevicePresetService.IsWheelOrPedalCategory(SelectedDeviceCategory);

    /// <summary>Gates the More Options tab's Touchpad Click Mode card - only PlayStation-category devices report a touchpad at all.</summary>
    public bool IsSelectedDeviceTouchpadCapable => SelectedDeviceCategory == DeviceHardwareCategory.PlayStationController;

    /// <summary>Every value of <see cref="TouchpadClickMode"/>, for the Touchpad Click Mode ComboBox's ItemsSource - same plain-enum-values ComboBox convention already used for <c>ControllerLedViewModel.EffectModes</c>.</summary>
    public static IReadOnlyList<TouchpadClickMode> TouchpadClickModes { get; } = System.Enum.GetValues<TouchpadClickMode>();
    public bool IsSelectedDeviceGamepad => SelectedDeviceName != null && !IsSelectedDeviceWheel;

    public string SelectedDeviceDisplayName => SelectedDeviceName ?? string.Empty;

    public DeviceMappingGroup? SelectedDeviceGroup =>
        (IsSelectedDeviceWheel ? _wheelGroups : _gamepadGroups).FirstOrDefault(g => g.DeviceName == SelectedDeviceName);

    partial void OnSelectedDeviceNameChanged(string? value) => RaiseDeviceScopedPropertiesChanged();

    /// <summary>
    /// Only picks a fallback device when nothing is selected yet - a hotplug refresh
    /// (<see cref="InputDeviceManager"/>'s periodic re-enumeration) can now make
    /// <see cref="Devices"/> shrink and grow live mid-session, and reassigning
    /// <see cref="SelectedDeviceName"/> just because it's momentarily missing would silently
    /// yank the user to a different device while they're mid-edit. A device that's unplugged
    /// stays "selected" (see <see cref="HasSelectedDeviceStatus"/>'s doc comment for how that's
    /// surfaced) so its last-known mappings stay visible, and reappears automatically once the
    /// same-named device is reacquired.
    /// </summary>
    private void EnsureSelectedDeviceIsValid()
    {
        if (!string.IsNullOrEmpty(SelectedDeviceName)) return;
        SelectedDeviceName = Devices.Count > 0 ? Devices[0].DeviceInstanceName : null;
    }

    private void RaiseDeviceScopedPropertiesChanged()
    {
        EnsureSelectedDeviceIsValid();
        OnPropertyChanged(nameof(SelectedDeviceCategory));
        OnPropertyChanged(nameof(IsSelectedDeviceWheel));
        OnPropertyChanged(nameof(IsSelectedDeviceTouchpadCapable));
        OnPropertyChanged(nameof(IsSelectedDeviceGamepad));
        OnPropertyChanged(nameof(SelectedDeviceDisplayName));
        OnPropertyChanged(nameof(SelectedDeviceGroup));
        OnPropertyChanged(nameof(AxisEntries));
        OnPropertyChanged(nameof(HasAxisEntries));
        OnPropertyChanged(nameof(LeftStickAxes));
        OnPropertyChanged(nameof(HasLeftStickAxes));
        OnPropertyChanged(nameof(RightStickAxes));
        OnPropertyChanged(nameof(HasRightStickAxes));
        OnPropertyChanged(nameof(OtherAxes));
        OnPropertyChanged(nameof(HasOtherAxes));
        OnPropertyChanged(nameof(WheelAxisEntries));
        OnPropertyChanged(nameof(HasWheelAxisEntries));
        OnPropertyChanged(nameof(LeftTriggerEntry));
        OnPropertyChanged(nameof(RightTriggerEntry));
        OnPropertyChanged(nameof(ThrottlePedalEntry));
        OnPropertyChanged(nameof(BrakePedalEntry));
        OnPropertyChanged(nameof(ClutchPedalEntry));
        OnPropertyChanged(nameof(SelectedDeviceStatus));
        OnPropertyChanged(nameof(HasSelectedDeviceStatus));
        OnPropertyChanged(nameof(IsMotionSupportedForSelectedDevice));
        OnPropertyChanged(nameof(IsGyroAimEnabledForSelectedDevice));
        OnPropertyChanged(nameof(IsTouchAimEnabledForSelectedDevice));
        OnPropertyChanged(nameof(SelectedWheelSettings));
        OnPropertyChanged(nameof(IsSelectedDeviceForceFeedbackCapable));
        OnPropertyChanged(nameof(SelectedGamepadSettings));
        OnPropertyChanged(nameof(IsVisualDeviceMappingHidden));
        OnPropertyChanged(nameof(IsShowAllInputsEnabled));

        ResubscribeDisplayRowSource();
        RebuildDisplayRows();

        ControllerLed.SetActiveDevice(SelectedDeviceStatus, IsSelectedDeviceGamepad ? SelectedGamepadSettings : null);
    }

    /// <summary>
    /// Re-subscribes <see cref="RebuildDisplayRows"/> to the newly-selected device's
    /// <see cref="DeviceMappingGroup.Entries"/> collection - a fresh instance every time
    /// <see cref="MainWindowViewModel.RebuildGroupedMappings"/> runs (see
    /// <see cref="Models.DeviceMappingGroup"/>), so a one-time subscription would go stale the
    /// first time any mapping anywhere changes.
    /// </summary>
    private void ResubscribeDisplayRowSource()
    {
        if (_subscribedGroupEntries != null) _subscribedGroupEntries.CollectionChanged -= OnDisplayRowSourceChanged;
        _subscribedGroupEntries = SelectedDeviceGroup?.Entries;
        if (_subscribedGroupEntries != null) _subscribedGroupEntries.CollectionChanged += OnDisplayRowSourceChanged;
    }

    private void OnDisplayRowSourceChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RebuildDisplayRows();

    /// <summary>
    /// Rebuilds <see cref="DisplayRows"/>: every real, already-bound-or-in-progress entry for
    /// the selected device, plus - only when <see cref="IsShowAllInputsEnabled"/> is on - a
    /// synthesized <see cref="MappingEntry.IsPlaceholder"/> row for every physical button/axis/
    /// POV-direction the device reports (per <see cref="MainWindowViewModel.GetDeviceHardwareInfo"/>)
    /// that has no entry yet. Placeholders are opt-in so selecting a device doesn't immediately
    /// dump every input it could theoretically report into the table.
    /// </summary>
    private void RebuildDisplayRows()
    {
        DisplayRows.Clear();

        var realEntries = SelectedDeviceGroup?.Entries;
        if (realEntries != null)
        {
            foreach (var entry in realEntries) DisplayRows.Add(entry);
        }

        if (string.IsNullOrEmpty(SelectedDeviceName)) return;

        var hwProfile = HardwareProfileService.GetOrCreate(_host, SelectedDeviceName);
        foreach (var row in DisplayRows) row.NicknameDisplay = hwProfile.GetButtonNickname(row.SourceType, row.SourceIndex);

        OnPropertyChanged(nameof(HasHiddenInputs));
        if (!hwProfile.ShowAllPhysicalInputs) return;

        var info = _host.GetDeviceHardwareInfo(SelectedDeviceName);
        int buttonCount = info?.ButtonCount ?? 32;
        int axisCount = info?.AxisCount ?? 8;
        bool hasPov = info?.HasPov ?? true;

        var covered = new HashSet<(InputType, int)>(
            (realEntries ?? Enumerable.Empty<MappingEntry>())
                .Where(e => e.SourceDeviceName == SelectedDeviceName)
                .Select(e => (e.SourceType, e.SourceIndex)));

        for (int i = 0; i < buttonCount; i++)
        {
            if (covered.Contains((InputType.Button, i)) || hwProfile.IsInputHidden(InputType.Button, i)) continue;
            DisplayRows.Add(BuildPlaceholderRow(InputType.Button, i));
        }

        if (hasPov)
        {
            for (int i = 128; i <= 131; i++)
            {
                if (covered.Contains((InputType.Button, i)) || hwProfile.IsInputHidden(InputType.Button, i)) continue;
                DisplayRows.Add(BuildPlaceholderRow(InputType.Button, i));
            }
        }

        for (int i = 0; i < axisCount; i++)
        {
            if (covered.Contains((InputType.Axis, i)) || hwProfile.IsInputHidden(InputType.Axis, i)) continue;
            DisplayRows.Add(BuildPlaceholderRow(InputType.Axis, i));
        }
    }

    /// <summary>Whether the selected device is currently showing unbound placeholder rows - drives the "Show All Inputs" toolbar toggle.</summary>
    public bool IsShowAllInputsEnabled
    {
        get => SelectedDeviceName != null && HardwareProfileService.GetOrCreate(_host, SelectedDeviceName).ShowAllPhysicalInputs;
        set
        {
            if (SelectedDeviceName == null) return;
            HardwareProfileService.GetOrCreate(_host, SelectedDeviceName).ShowAllPhysicalInputs = value;
            HardwareProfileService.Save();
            OnPropertyChanged();
            RebuildDisplayRows();
        }
    }

    /// <summary>Whether the selected device has any placeholder rows dismissed via <see cref="HidePlaceholderInput"/> - drives the "Restore Hidden Inputs" toolbar button.</summary>
    public bool HasHiddenInputs => SelectedDeviceName != null && HardwareProfileService.GetOrCreate(_host, SelectedDeviceName).HiddenPlaceholderInputs.Count > 0;

    /// <summary>Dismisses an auto-listed, not-yet-bound placeholder row (see <see cref="MappingEntry.IsPlaceholder"/>) from the table - it reappears only if <see cref="RestoreHiddenInputs"/> is used. No-op for a real, already-bound row.</summary>
    [RelayCommand]
    private void HidePlaceholderInput(MappingEntry entry)
    {
        if (!entry.IsPlaceholder || SelectedDeviceName == null) return;
        HardwareProfileService.GetOrCreate(_host, SelectedDeviceName).SetInputHidden(entry.SourceType, entry.SourceIndex, true);
        HardwareProfileService.Save();
        RebuildDisplayRows();
    }

    /// <summary>Un-dismisses every placeholder row hidden for the selected device.</summary>
    [RelayCommand]
    private void RestoreHiddenInputs()
    {
        if (SelectedDeviceName == null) return;
        HardwareProfileService.GetOrCreate(_host, SelectedDeviceName).ClearHiddenPlaceholderInputs();
        HardwareProfileService.Save();
        RebuildDisplayRows();
    }

    /// <summary>
    /// Re-reads <see cref="MappingEntry.NicknameDisplay"/> for every current <see cref="DisplayRows"/>
    /// row belonging to <paramref name="deviceName"/> - called after a nickname edit (from either
    /// the Input Matrix tab or the Device Mapping Table's own rename button) so the Device Mapping Table's
    /// labels update immediately without a full <see cref="RebuildDisplayRows"/> pass, which would
    /// otherwise disturb the table's scroll position/selection.
    /// </summary>
    public void RefreshNicknameDisplaysForDevice(string deviceName)
    {
        if (deviceName != SelectedDeviceName) return;
        var hwProfile = HardwareProfileService.GetOrCreate(_host, deviceName);
        foreach (var row in DisplayRows) row.NicknameDisplay = hwProfile.GetButtonNickname(row.SourceType, row.SourceIndex);
    }

    private MappingEntry BuildPlaceholderRow(InputType type, int index) => new()
    {
        SourceDeviceName = SelectedDeviceName!,
        SourceType = type,
        SourceIndex = index,
        SourceDisplayName = type == InputType.Axis
            ? DevicePresetService.GetAxisDisplayName(SelectedDeviceName!, index)
            : DevicePresetService.GetButtonDisplayName(SelectedDeviceName!, index),
        TargetOutput = string.Empty,
        IsPlaceholder = true,
        NicknameDisplay = HardwareProfileService.GetOrCreate(_host, SelectedDeviceName!).GetButtonNickname(type, index)
    };

    // --- Tab strip ---

    [RelayCommand]
    private void SelectTab(DeviceMappingTab tab) => SelectedTab = tab;

    public bool IsMappingsTabSelected => SelectedTab == DeviceMappingTab.Mappings;
    public bool IsInputMatrixTabSelected => SelectedTab == DeviceMappingTab.InputMatrix;
    public bool IsDeadzonesTabSelected => SelectedTab == DeviceMappingTab.Deadzones;
    public bool IsSticksTabSelected => SelectedTab == DeviceMappingTab.Sticks;
    public bool IsTriggersTabSelected => SelectedTab == DeviceMappingTab.Triggers;
    public bool IsMotionTabSelected => SelectedTab == DeviceMappingTab.Motion;
    public bool IsTouchpadTabSelected => SelectedTab == DeviceMappingTab.Touchpad;
    public bool IsWheelSettingsTabSelected => SelectedTab == DeviceMappingTab.WheelSettings;
    public bool IsCalibrationTabSelected => SelectedTab == DeviceMappingTab.Calibration;
    public bool IsPedalSettingsTabSelected => SelectedTab == DeviceMappingTab.PedalSettings;
    public bool IsMoreOptionsTabSelected => SelectedTab == DeviceMappingTab.MoreOptions;

    partial void OnSelectedTabChanged(DeviceMappingTab value)
    {
        OnPropertyChanged(nameof(IsMappingsTabSelected));
        OnPropertyChanged(nameof(IsInputMatrixTabSelected));
        OnPropertyChanged(nameof(IsDeadzonesTabSelected));
        OnPropertyChanged(nameof(IsSticksTabSelected));
        OnPropertyChanged(nameof(IsTriggersTabSelected));
        OnPropertyChanged(nameof(IsMotionTabSelected));
        OnPropertyChanged(nameof(IsTouchpadTabSelected));
        OnPropertyChanged(nameof(IsWheelSettingsTabSelected));
        OnPropertyChanged(nameof(IsCalibrationTabSelected));
        OnPropertyChanged(nameof(IsPedalSettingsTabSelected));
        OnPropertyChanged(nameof(IsMoreOptionsTabSelected));
    }

    // --- Profile switching, forwarded from MainWindowViewModel (gamepad-only UI) ---

    public ObservableCollection<string> Profiles => _host.Profiles;

    public string SelectedProfile
    {
        get => _host.SelectedProfile;
        set => _host.SelectedProfile = value;
    }

    /// <summary>
    /// Per-gamepad-device state (see <see cref="Models.GamepadDeviceSettings.IsVisualDeviceMappingHidden"/>)
    /// rather than a global app setting - switching devices shows each one's own saved layout
    /// choice. Wheels never show the diagram at all (unreachable/hidden for that device type), so
    /// there's no wheel-settings equivalent. Falls back to <c>false</c> (diagram shown) when no
    /// device is selected yet.
    /// </summary>
    public bool IsVisualDeviceMappingHidden
    {
        get => SelectedGamepadSettings?.IsVisualDeviceMappingHidden ?? false;
        set
        {
            if (SelectedGamepadSettings == null) return;
            SelectedGamepadSettings.IsVisualDeviceMappingHidden = value;
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void ToggleVisualDeviceMappingHidden() => IsVisualDeviceMappingHidden = !IsVisualDeviceMappingHidden;

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

    // --- Deadzones tab (gamepad) ---

    public IEnumerable<MappingEntry> AxisEntries =>
        SelectedDeviceGroup?.Entries.Where(e => e.IsAxis) ?? Enumerable.Empty<MappingEntry>();

    public bool HasAxisEntries => AxisEntries.Any();

    // --- Sticks tab (gamepad) ---

    public IEnumerable<MappingEntry> LeftStickAxes => AxisEntries.Where(e => e.SourceDisplayName.Contains("Left Stick"));
    public bool HasLeftStickAxes => LeftStickAxes.Any();

    public IEnumerable<MappingEntry> RightStickAxes => AxisEntries.Where(e => e.SourceDisplayName.Contains("Right Stick"));
    public bool HasRightStickAxes => RightStickAxes.Any();

    public IEnumerable<MappingEntry> OtherAxes => AxisEntries.Where(e =>
        !e.SourceDisplayName.Contains("Left Stick") && !e.SourceDisplayName.Contains("Right Stick"));
    public bool HasOtherAxes => OtherAxes.Any();

    // --- Triggers tab (gamepad) ---

    public MappingEntry? LeftTriggerEntry => AxisEntries.FirstOrDefault(e => e.TargetOutput.Contains("Left Trigger"));
    public MappingEntry? RightTriggerEntry => AxisEntries.FirstOrDefault(e => e.TargetOutput.Contains("Right Trigger"));

    // --- More Options tab (gamepad) ---

    public ControllerStatus? SelectedDeviceStatus =>
        Devices.FirstOrDefault(s => s.DeviceInstanceName == SelectedDeviceName);

    /// <summary>
    /// Also doubles as "is the selected device currently connected" - false the moment a hotplug
    /// refresh removes it from <see cref="Devices"/>, true again the instant it's reacquired
    /// under the same name. <see cref="Views.DeviceMappingPageView"/>'s disconnected banner binds to
    /// this directly rather than a separate property.
    /// </summary>
    public bool HasSelectedDeviceStatus => SelectedDeviceStatus != null;

    // --- Motion / Touchpad tabs (gamepad, PlayStation only) ---

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

    // --- Wheel Settings tab (wheel) ---

    public WheelDeviceSettings? SelectedWheelSettings =>
        SelectedDeviceName == null ? null : _host.GetOrCreateWheelSettings(SelectedDeviceName);

    /// <summary>Best-effort DirectInput capability probe backing the "this wheel isn't reporting FFB support" banner - resolved from whichever mapping entry for this device carries a GUID, since <see cref="DeviceMappingGroup"/> only tracks a display name.</summary>
    public bool IsSelectedDeviceForceFeedbackCapable
    {
        get
        {
            var guid = SelectedDeviceGroup?.Entries.FirstOrDefault(e => e.SourceDeviceGuid != System.Guid.Empty)?.SourceDeviceGuid;
            return guid != null && _host.WheelForceFeedback.DeviceSupportsForceFeedback(guid.Value);
        }
    }

    /// <summary>Gates the "Synthesize FFB from Rumble" (Mode B) toggle's practical relevance - true when this device's steering axis is routed to an "[Xbox]"/"[DS4]" target rather than vJoy's "[Wheel] Steering", which Mode A's real DirectInput passthrough already covers.</summary>
    public bool IsSelectedDeviceRoutedToGamepadOutput =>
        SelectedDeviceGroup?.Entries.Any(e =>
            e.TargetOutput.StartsWith("[Xbox] Left Stick X", System.StringComparison.OrdinalIgnoreCase) ||
            e.TargetOutput.StartsWith("[DS4] Left Stick X", System.StringComparison.OrdinalIgnoreCase)) ?? false;

    // --- Calibration tab (wheel) ---

    /// <summary>Every bound wheel axis for the selected device - one live calibration card per entry on the Calibration tab.</summary>
    public IEnumerable<MappingEntry> WheelAxisEntries => AxisEntries;
    public bool HasWheelAxisEntries => WheelAxisEntries.Any();

    // --- Pedal Settings tab (wheel) ---

    public MappingEntry? ThrottlePedalEntry => WheelAxisEntries.FirstOrDefault(e =>
        e.TargetOutput.Contains("Throttle") || e.TargetOutput.Contains("Gas"));
    public MappingEntry? BrakePedalEntry => WheelAxisEntries.FirstOrDefault(e => e.TargetOutput.Contains("Brake") && !e.TargetOutput.Contains("Handbrake"));
    public MappingEntry? ClutchPedalEntry => WheelAxisEntries.FirstOrDefault(e => e.TargetOutput.Contains("Clutch"));
    public MappingEntry? HandbrakePedalEntry => WheelAxisEntries.FirstOrDefault(e => e.TargetOutput.Contains("Handbrake"));

    public bool HasNoPedalsMapped => ThrottlePedalEntry == null && BrakePedalEntry == null && ClutchPedalEntry == null && HandbrakePedalEntry == null;

    // --- More Options tab: Rumble/Haptics + LED (gamepad) ---

    public GamepadDeviceSettings? SelectedGamepadSettings =>
        SelectedDeviceName == null ? null : _host.GetOrCreateGamepadSettings(SelectedDeviceName);

    [RelayCommand]
    private void OpenHidHide() => _host.NavigateToCommand.Execute(AppPage.HidHide);

    [RelayCommand]
    private void TestVibration()
    {
        if (SelectedDeviceName != null) _host.TestVibration(SelectedDeviceName);
    }

    [RelayCommand]
    private void TestFfbLeft()
    {
        if (SelectedDeviceName != null) _host.TestFfbDirectionalPulse(SelectedDeviceName, right: false);
    }

    [RelayCommand]
    private void TestFfbRight()
    {
        if (SelectedDeviceName != null) _host.TestFfbDirectionalPulse(SelectedDeviceName, right: true);
    }

    [RelayCommand]
    private void TestFfbTexture()
    {
        if (SelectedDeviceName != null) _host.TestFfbTexturePulse(SelectedDeviceName);
    }

    [RelayCommand]
    private void TestFfbSoftLock()
    {
        if (SelectedDeviceName != null) _host.TestFfbSoftLock(SelectedDeviceName);
    }
}
