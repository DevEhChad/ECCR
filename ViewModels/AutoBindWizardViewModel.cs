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

    /// <summary>Which virtual backend newly-applied bindings target - Xbox/DS4 (ViGEm) or Wheel (vJoy). See <see cref="ECCR.Services.OutputCategory"/>.</summary>
    [ObservableProperty]
    private OutputCategory _wizardOutputCategory = OutputCategory.Xbox;

    /// <summary>True when <see cref="WizardOutputCategory"/> is <see cref="OutputCategory.Wheel"/> - convenience for XAML bindings that only care about the Wheel-vs-everything-else split (e.g. which of <see cref="WizardTargetPlayer"/>/<see cref="WizardTargetWheelSlot"/> to show).</summary>
    public bool WizardTargetIsWheel => WizardOutputCategory == OutputCategory.Wheel;

    // Three RadioButton-friendly two-way booleans backing the wizard's Xbox/DS4/Wheel choice -
    // RadioButton.IsChecked has no built-in enum-equality binding, so each of these reads as
    // "is WizardOutputCategory this value" and, on set, only reacts to becoming true (a
    // RadioButton also fires false on the button being deselected, which must not stomp
    // whichever other option the group is switching to).
    public bool IsXboxOutputSelected
    {
        get => WizardOutputCategory == OutputCategory.Xbox;
        set { if (value) WizardOutputCategory = OutputCategory.Xbox; }
    }

    public bool IsDualShock4OutputSelected
    {
        get => WizardOutputCategory == OutputCategory.DualShock4;
        set { if (value) WizardOutputCategory = OutputCategory.DualShock4; }
    }

    public bool IsWheelOutputSelected
    {
        get => WizardOutputCategory == OutputCategory.Wheel;
        set { if (value) WizardOutputCategory = OutputCategory.Wheel; }
    }

    partial void OnWizardOutputCategoryChanged(OutputCategory value)
    {
        OnPropertyChanged(nameof(WizardTargetIsWheel));
        OnPropertyChanged(nameof(IsXboxOutputSelected));
        OnPropertyChanged(nameof(IsDualShock4OutputSelected));
        OnPropertyChanged(nameof(IsWheelOutputSelected));
    }

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
            // Pre-selects the wizard's Wheel vs. Xbox Gamepad output to match the device's
            // detected hardware family, so the common case needs no manual toggle. DS4 is never
            // auto-selected - it's an explicit user choice only.
            var cat = DevicePresetService.DetectCategory(WizardSelectedDevice);
            WizardOutputCategory = DevicePresetService.IsWheelOrPedalCategory(cat) ? OutputCategory.Wheel : OutputCategory.Xbox;
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
        bool hasPov = connectedInfo?.HasPov ?? true;

        var presets = DevicePresetService.GeneratePreset(WizardSelectedDevice, buttonCount, axisCount, WizardOutputCategory, hasPov);
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
        uint targetDeviceId = WizardTargetIsWheel ? WizardTargetWheelSlot : WizardTargetPlayer;

        DevicePresetService.ApplyPreset(_mappings, WizardSelectedDevice, devGuid, WizardBindings.ToList(), targetDeviceId);

        _onBindingsApplied();
    }
}
