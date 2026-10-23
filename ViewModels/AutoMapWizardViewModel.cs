using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ECCR.Models;
using ECCR.Services;

namespace ECCR.ViewModels;

/// <summary>
/// Backs the Auto-Map Configuration Wizard: preset generation for the selected device and
/// applying the generated mappings to the shared mapping list. The device list and the
/// mapping collection are shared instances owned by <see cref="MainWindowViewModel"/>, so
/// the wizard reads and writes the same data the main grid displays.
/// </summary>
public partial class AutoMapWizardViewModel : ViewModelBase
{
    private readonly Func<List<DeviceHardwareInfo>> _connectedDeviceInfo;
    private readonly ObservableCollection<MappingEntry> _mappings;

    /// <summary>
    /// Invoked after mappings are applied so the host can rebuild the grouped view,
    /// refresh the virtual device summary and auto-save the active profile.
    /// </summary>
    private readonly Action _onMappingsApplied;

    // Auto-Map Wizard Properties
    [ObservableProperty]
    private string _wizardSelectedDevice = string.Empty;

    /// <summary>Which layout newly-applied mappings use - Xbox 360 / DualShock 4 (ViGEm), DirectInput wheel (vJoy), or Combined (Xbox buttons on ViGEm + wheel axes on vJoy). See <see cref="AutoBindMode"/>.</summary>
    [ObservableProperty]
    private AutoBindMode _wizardMode = AutoBindMode.Xbox360;

    /// <summary>Why the preview looks the way it does (profile table, XInput device, generic gamepad...). Shown under the mode selector.</summary>
    [ObservableProperty]
    private string _wizardLayoutDescription = string.Empty;

    /// <summary>True when any mapping will target the vJoy wheel (DirectInput or Combined) - shows the "Assign to Wheel Slot" combo.</summary>
    public bool WizardUsesWheelSlot => WizardMode is AutoBindMode.DirectInput or AutoBindMode.Combined;

    /// <summary>True when any mapping will target the ViGEm pad (everything but DirectInput) - shows the "Assign to Player" combo.</summary>
    public bool WizardUsesPlayer => WizardMode != AutoBindMode.DirectInput;

    // RadioButton.IsChecked has no enum-equality binding, so each option reads as "is WizardMode this
    // value" and on set only reacts to becoming true (a RadioButton also fires false when deselected,
    // which must not overwrite the option the group is switching to).
    public bool IsXboxOutputSelected
    {
        get => WizardMode == AutoBindMode.Xbox360;
        set { if (value) WizardMode = AutoBindMode.Xbox360; }
    }

    public bool IsDualShock4OutputSelected
    {
        get => WizardMode == AutoBindMode.DualShock4;
        set { if (value) WizardMode = AutoBindMode.DualShock4; }
    }

    public bool IsWheelOutputSelected
    {
        get => WizardMode == AutoBindMode.DirectInput;
        set { if (value) WizardMode = AutoBindMode.DirectInput; }
    }

    public bool IsCombinedOutputSelected
    {
        get => WizardMode == AutoBindMode.Combined;
        set { if (value) WizardMode = AutoBindMode.Combined; }
    }

    partial void OnWizardModeChanged(AutoBindMode value)
    {
        OnPropertyChanged(nameof(WizardUsesWheelSlot));
        OnPropertyChanged(nameof(WizardUsesPlayer));
        OnPropertyChanged(nameof(IsXboxOutputSelected));
        OnPropertyChanged(nameof(IsDualShock4OutputSelected));
        OnPropertyChanged(nameof(IsWheelOutputSelected));
        OnPropertyChanged(nameof(IsCombinedOutputSelected));
        PopulateWizardPreset();
    }

    /// <summary>
    /// Which virtual player slot newly-applied mappings are assigned to. Defaults to Player 1
    /// but is user-selectable (see the wizard's "Assign to Player" combo) so re-running Auto-Map
    /// on a device previously bulk-assigned to P2-P4 doesn't silently reset it to P1.
    /// </summary>
    [ObservableProperty]
    private uint _wizardTargetPlayer = 1;

    /// <summary>
    /// Which Wheel Slot (vJoy device 1-8, see <see cref="MainWindowViewModel.WheelSlots"/>)
    /// newly-applied "[Wheel] ..." mappings are assigned to when <see cref="WizardTargetIsWheel"/>
    /// is true - the wheel-target counterpart of <see cref="WizardTargetPlayer"/>, kept as a
    /// separate property since a wheel slot and a Player are different numeric namespaces (see
    /// <see cref="MappingEntry.TargetDeviceId"/>'s doc comment).
    /// </summary>
    [ObservableProperty]
    private uint _wizardTargetWheelSlot = 1;

    public ObservableCollection<PresetMappingItem> WizardMappings { get; } = new();

    /// <summary>Shared with the host view model; populated as devices are detected.</summary>
    public ObservableCollection<string> ConnectedDevices { get; }

    /// <summary>Shared with the host view model; backs the "Assign to Player" combo.</summary>
    public ObservableCollection<PlayerTargetOption> PlayerTargets { get; }

    /// <summary>Shared with the host view model; backs the "Assign to Wheel Slot" combo shown when <see cref="WizardTargetIsWheel"/> is true.</summary>
    public ObservableCollection<WheelDeviceSlotOption> WheelSlots { get; }

    /// <summary>Design-time constructor.</summary>
    public AutoMapWizardViewModel()
        : this(new ObservableCollection<string>(), () => new List<DeviceHardwareInfo>(), new ObservableCollection<MappingEntry>(), new ObservableCollection<PlayerTargetOption>(), new ObservableCollection<WheelDeviceSlotOption>(), () => { })
    {
    }

    public AutoMapWizardViewModel(
        ObservableCollection<string> connectedDevices,
        Func<List<DeviceHardwareInfo>> connectedDeviceInfo,
        ObservableCollection<MappingEntry> mappings,
        ObservableCollection<PlayerTargetOption> playerTargets,
        ObservableCollection<WheelDeviceSlotOption> wheelSlots,
        Action onMappingsApplied)
    {
        ConnectedDevices = connectedDevices;
        _connectedDeviceInfo = connectedDeviceInfo;
        _mappings = mappings;
        PlayerTargets = playerTargets;
        WheelSlots = wheelSlots;
        _onMappingsApplied = onMappingsApplied;
    }

    /// <summary>
    /// Preselects a device and the layout that suits it (Combined for a wheel with Xbox buttons,
    /// DirectInput for a wheel without, Xbox 360 for a gamepad), and regenerates the preview - run
    /// every time the Auto-Map page is navigated to, since the page is always part of the shell.
    /// </summary>
    public void PrepareForNavigation()
    {
        if (ConnectedDevices.Count > 0 && string.IsNullOrEmpty(WizardSelectedDevice))
        {
            WizardSelectedDevice = ConnectedDevices[0];
        }

        if (!string.IsNullOrWhiteSpace(WizardSelectedDevice))
        {
            var info = _connectedDeviceInfo().FirstOrDefault(d => d.InstanceName == WizardSelectedDevice);
            var vidPid = info != null ? (info.VendorId, info.ProductId) : ((int, int)?)null;
            // DualShock 4 is never auto-selected - it is an explicit choice only.
            WizardMode = DevicePresetService.SuggestAutoBindMode(WizardSelectedDevice, vidPid, info?.ExposesXInput ?? false);
        }

        PopulateWizardPreset();
    }

    partial void OnWizardSelectedDeviceChanged(string value) => PopulateWizardPreset();

    [RelayCommand]
    public void PopulateWizardPreset()
    {
        WizardMappings.Clear();
        WizardLayoutDescription = string.Empty;
        if (string.IsNullOrWhiteSpace(WizardSelectedDevice)) return;

        var info = _connectedDeviceInfo().FirstOrDefault(d => d.InstanceName == WizardSelectedDevice);
        var vidPid = info != null ? (info.VendorId, info.ProductId) : ((int, int)?)null;

        var result = DevicePresetService.GenerateAutoBindPreset(
            WizardSelectedDevice, info?.ButtonCount ?? 32, info?.AxisSlotMask ?? 0xFF, info?.HasPov ?? true,
            WizardMode, vidPid, info?.ExposesXInput ?? false);

        foreach (var item in result.Items)
        {
            WizardMappings.Add(item);
        }
        WizardLayoutDescription = $"Detected as: {result.LayoutDescription}";
    }

    [RelayCommand]
    public void ApplyWizardMappings()
    {
        if (string.IsNullOrWhiteSpace(WizardSelectedDevice) || WizardMappings.Count == 0) return;

        var targetDevice = _connectedDeviceInfo().FirstOrDefault(d => d.InstanceName == WizardSelectedDevice);
        Guid devGuid = targetDevice?.InstanceGuid ?? Guid.Empty;

        // Pure DirectInput has only wheel targets, so the wheel slot doubles as the default id; otherwise
        // the player id is the default and [Wheel] items are redirected to the wheel slot.
        uint defaultId = WizardMode == AutoBindMode.DirectInput ? WizardTargetWheelSlot : WizardTargetPlayer;
        DevicePresetService.ApplyPreset(_mappings, WizardSelectedDevice, devGuid, WizardMappings.ToList(), defaultId, WizardTargetWheelSlot);

        _onMappingsApplied();
    }
}
