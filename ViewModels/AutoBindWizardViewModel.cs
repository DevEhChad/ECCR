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
/// Backs the Auto-Bind Configuration Wizard: preset generation for the selected device and
/// applying the generated bindings to the shared mapping list. The device list and the
/// mapping collection are shared instances owned by <see cref="MainWindowViewModel"/>, so
/// the wizard reads and writes the same data the main grid displays.
/// </summary>
public partial class AutoBindWizardViewModel : ViewModelBase
{
    private readonly Func<List<DeviceHardwareInfo>> _connectedDeviceInfo;
    private readonly ObservableCollection<MappingEntry> _mappings;

    /// <summary>
    /// Invoked after bindings are applied so the host can rebuild the grouped view,
    /// refresh the virtual device summary and auto-save the active profile.
    /// </summary>
    private readonly Action _onBindingsApplied;

    // Auto-Bind Wizard Properties
    [ObservableProperty]
    private string _wizardSelectedDevice = string.Empty;

    [ObservableProperty]
    private bool _wizardTargetIsWheel = false;

    /// <summary>
    /// Which virtual player slot newly-applied bindings are assigned to. Defaults to Player 1
    /// but is user-selectable (see the wizard's "Assign to Player" combo) so re-running Auto
    /// Bind on a device previously bulk-assigned to P2-P4 doesn't silently reset it to P1.
    /// </summary>
    [ObservableProperty]
    private uint _wizardTargetPlayer = 1;

    /// <summary>
    /// Which Wheel Slot (vJoy device 1-8, see <see cref="MainWindowViewModel.WheelSlots"/>)
    /// newly-applied "[Wheel] ..." bindings are assigned to when <see cref="WizardTargetIsWheel"/>
    /// is true - the wheel-target counterpart of <see cref="WizardTargetPlayer"/>, kept as a
    /// separate property since a wheel slot and a Player are different numeric namespaces (see
    /// <see cref="MappingEntry.TargetDeviceId"/>'s doc comment).
    /// </summary>
    [ObservableProperty]
    private uint _wizardTargetWheelSlot = 1;

    public ObservableCollection<PresetBindingItem> WizardBindings { get; } = new();

    /// <summary>Shared with the host view model; populated as devices are detected.</summary>
    public ObservableCollection<string> ConnectedDevices { get; }

    /// <summary>Shared with the host view model; backs the "Assign to Player" combo.</summary>
    public ObservableCollection<PlayerTargetOption> PlayerTargets { get; }

    /// <summary>Shared with the host view model; backs the "Assign to Wheel Slot" combo shown when <see cref="WizardTargetIsWheel"/> is true.</summary>
    public ObservableCollection<WheelDeviceSlotOption> WheelSlots { get; }

    /// <summary>Design-time constructor.</summary>
    public AutoBindWizardViewModel()
        : this(new ObservableCollection<string>(), () => new List<DeviceHardwareInfo>(), new ObservableCollection<MappingEntry>(), new ObservableCollection<PlayerTargetOption>(), new ObservableCollection<WheelDeviceSlotOption>(), () => { })
    {
    }

    public AutoBindWizardViewModel(
        ObservableCollection<string> connectedDevices,
        Func<List<DeviceHardwareInfo>> connectedDeviceInfo,
        ObservableCollection<MappingEntry> mappings,
        ObservableCollection<PlayerTargetOption> playerTargets,
        ObservableCollection<WheelDeviceSlotOption> wheelSlots,
        Action onBindingsApplied)
    {
        ConnectedDevices = connectedDevices;
        _connectedDeviceInfo = connectedDeviceInfo;
        _mappings = mappings;
        PlayerTargets = playerTargets;
        WheelSlots = wheelSlots;
        _onBindingsApplied = onBindingsApplied;
    }

    /// <summary>
    /// Preselects a device and its DirectInput Wheel vs. Xbox Gamepad target, and regenerates
    /// the preset preview - run every time the Auto-Bind page is navigated to (see
    /// <c>MainWindowViewModel.OnCurrentPageChanged</c>) rather than behind an explicit "open"
    /// action, since the page is now always part of the shell instead of a popup wizard.
    /// </summary>
    public void PrepareForNavigation()
    {
        if (ConnectedDevices.Count > 0 && string.IsNullOrEmpty(WizardSelectedDevice))
        {
            WizardSelectedDevice = ConnectedDevices[0];
        }

        if (!string.IsNullOrWhiteSpace(WizardSelectedDevice))
        {
            // Pre-selects the wizard's DirectInput Wheel vs. Xbox Gamepad radio to match the
            // device's detected hardware family, so the common case needs no manual toggle.
            // Note: this repeats the same category list as
            // DevicePresetService.IsWheelOrPedalCategory inline instead of calling it.
            var cat = DevicePresetService.DetectCategory(WizardSelectedDevice);
            WizardTargetIsWheel = (cat == DeviceHardwareCategory.MozaEsxWheel ||
                                   cat == DeviceHardwareCategory.MozaWheel ||
                                   cat == DeviceHardwareCategory.LogitechRig ||
                                   cat == DeviceHardwareCategory.FanatecRig ||
                                   cat == DeviceHardwareCategory.ThrustmasterRig ||
                                   cat == DeviceHardwareCategory.SimagicRig ||
                                   cat == DeviceHardwareCategory.GenericWheelOrPedals);
        }

        PopulateWizardPreset();
    }

    [RelayCommand]
    public void PopulateWizardPreset()
    {
        WizardBindings.Clear();
        if (string.IsNullOrWhiteSpace(WizardSelectedDevice)) return;

        var connectedInfo = _connectedDeviceInfo().FirstOrDefault(d => d.InstanceName == WizardSelectedDevice);
        int buttonCount = connectedInfo?.ButtonCount ?? 32;
        int axisCount = connectedInfo?.AxisCount ?? 8;

        var presets = DevicePresetService.GeneratePreset(WizardSelectedDevice, buttonCount, axisCount, WizardTargetIsWheel);
        foreach (var item in presets)
        {
            WizardBindings.Add(item);
        }
    }

    [RelayCommand]
    public void ApplyWizardBindings()
    {
        if (string.IsNullOrWhiteSpace(WizardSelectedDevice) || WizardBindings.Count == 0) return;

        var targetDevice = _connectedDeviceInfo().FirstOrDefault(d => d.InstanceName == WizardSelectedDevice);
        Guid devGuid = targetDevice?.InstanceGuid ?? Guid.Empty;

        var toRemove = _mappings.Where(m => m.SourceDeviceName == WizardSelectedDevice).ToList();
        foreach (var r in toRemove) _mappings.Remove(r);

        foreach (var item in WizardBindings)
        {
            bool shouldInvert = item.Type == InputType.Axis && 
                (item.PhysicalName.Contains("Vertical") || item.PhysicalName.Contains("Stick Y") || 
                 item.PhysicalName.Contains("Throttle") || item.PhysicalName.Contains("Brake"));

            _mappings.Add(new MappingEntry
            {
                SourceDeviceName = WizardSelectedDevice,
                SourceDeviceGuid = devGuid,
                SourceType = item.Type,
                SourceIndex = item.PhysicalIndex,
                SourceDisplayName = item.PhysicalName,
                TargetDeviceId = WizardTargetIsWheel ? WizardTargetWheelSlot : WizardTargetPlayer,
                TargetOutput = item.DefaultTargetOutput,
                Deadzone = item.Type == InputType.Axis ? 0.08 : 0.0,
                RawMin = 0,
                RawMax = 65535,
                IsInverted = shouldInvert
            });
        }

        _onBindingsApplied();
    }
}
