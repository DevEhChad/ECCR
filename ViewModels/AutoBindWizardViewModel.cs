using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ECCR.Models;
using ECCR.Services;
using ECCR.Views;

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

    public ObservableCollection<PresetBindingItem> WizardBindings { get; } = new();

    /// <summary>Shared with the host view model; populated as devices are detected.</summary>
    public ObservableCollection<string> ConnectedDevices { get; }

    /// <summary>Design-time constructor.</summary>
    public AutoBindWizardViewModel()
        : this(new ObservableCollection<string>(), () => new List<DeviceHardwareInfo>(), new ObservableCollection<MappingEntry>(), () => { })
    {
    }

    public AutoBindWizardViewModel(
        ObservableCollection<string> connectedDevices,
        Func<List<DeviceHardwareInfo>> connectedDeviceInfo,
        ObservableCollection<MappingEntry> mappings,
        Action onBindingsApplied)
    {
        ConnectedDevices = connectedDevices;
        _connectedDeviceInfo = connectedDeviceInfo;
        _mappings = mappings;
        _onBindingsApplied = onBindingsApplied;
    }

    [RelayCommand]
    public async Task OpenAutoBindWizard()
    {
        if (ConnectedDevices.Count > 0 && string.IsNullOrEmpty(WizardSelectedDevice))
        {
            WizardSelectedDevice = ConnectedDevices[0];
        }

        if (!string.IsNullOrWhiteSpace(WizardSelectedDevice))
        {
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

        var wizard = new AutoBindWizardWindow
        {
            DataContext = this
        };

        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            await wizard.ShowDialog(desktop.MainWindow);
        }
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
                TargetDeviceId = 1,
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
