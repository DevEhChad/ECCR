using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ECCR.Models;
using ECCR.Services;
using ECCR.Views;
using Velopack;

namespace ECCR.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly InputDeviceManager _deviceManager;
    private readonly HidHideManager _hidHideManager;
    private readonly VirtualFeederService _feeder = new();
    private readonly UpdateService _updateService = new();
    private readonly Stopwatch _pollStopwatch = new();
    private readonly string _baseDirectory;
    private readonly string _profilesDirectory;
    private readonly string _settingsFilePath;

    private bool _isLoadingProfile = false;
    private UpdateInfo? _availableUpdateInfo;

    [ObservableProperty]
    private string _mappingStatus = "Active";

    [ObservableProperty]
    private string _latency = "0.0ms";

    [ObservableProperty]
    private string _virtualDeviceSummary = "vJoy Virtual Wheel + Xbox Controller (Active)";

    [ObservableProperty]
    private string _selectedProfile = "Default";

    [ObservableProperty]
    private VirtualEmulationMode _activeEmulationMode = VirtualEmulationMode.DirectInputWheel;

    [ObservableProperty]
    private string _customProfileInput = string.Empty;

    [ObservableProperty]
    private bool _isCreatingProfile = false;

    // --- Settings Modal & Toggles ---
    [ObservableProperty]
    private bool _isSettingsOpen = false;

    [ObservableProperty]
    private bool _startWithWindows = false;

    [ObservableProperty]
    private bool _runInSystemTray = true;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _closeMinimizesToTray = true;

    [ObservableProperty]
    private bool _autoCheckForUpdates = true;

    // --- Live Update Manager State ---
    [ObservableProperty]
    private string _updateStatusText = "App is up to date";

    [ObservableProperty]
    private bool _isCheckingForUpdates = false;

    [ObservableProperty]
    private bool _isUpdateAvailable = false;

    [ObservableProperty]
    private bool _isInstallingUpdate = false;

    [ObservableProperty]
    private int _updateDownloadProgress = 0;

    // --- Auto-Bind Wizard Properties ---
    [ObservableProperty]
    private string _wizardSelectedDevice = string.Empty;

    public ObservableCollection<PresetBindingItem> WizardBindings { get; } = new();

    // --- Driver Status States ---
    [ObservableProperty]
    private bool _isDependencyBannerOpen = false;

    [ObservableProperty]
    private bool _isViGEmInstalled = true;

    [ObservableProperty]
    private bool _isViGEmRunning = true;

    [ObservableProperty]
    private bool _isHidHideServiceInstalled = true;

    [ObservableProperty]
    private bool _isHidHideServiceRunning = true;

    [ObservableProperty]
    private bool _isVJoyInstalled = true;

    [ObservableProperty]
    private bool _isVJoyRunning = true;

    [ObservableProperty]
    private bool _isBusyWithDriverAction = false;

    // --- HidHide Modal Controls ---
    [ObservableProperty]
    private bool _isHidHideMenuOpen = false;

    [ObservableProperty]
    private bool _isHidHideActive = false;

    [ObservableProperty]
    private bool _isAppListInverted = false;

    public ObservableCollection<string> Profiles { get; } = new();
    public ObservableCollection<VirtualEmulationMode> EmulationModes { get; } = new()
    {
        VirtualEmulationMode.DirectInputWheel,
        VirtualEmulationMode.XboxController
    };

    public ObservableCollection<MappingEntry> Mappings { get; } = new();
    public ObservableCollection<DeviceMappingGroup> GroupedMappings { get; } = new();
    public ObservableCollection<string> ConnectedDevices { get; } = new();
    public ObservableCollection<string> AvailableVirtualOutputs { get; } = new();
    public ObservableCollection<uint> AvailableTargetDevices { get; } = new() { 1, 2, 3, 4, 5, 6, 7, 8 };
    public ObservableCollection<HidDeviceItem> HidDevices { get; } = new();
    public ObservableCollection<string> WhitelistedApplications { get; } = new();

    private MappingEntry? _listeningEntry;
    private readonly Dictionary<Guid, int[]> _axisBaselines = new();

    public MainWindowViewModel()
    {
        _baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECCR");
        _profilesDirectory = Path.Combine(_baseDirectory, "Profiles");
        _settingsFilePath = Path.Combine(_baseDirectory, "settings.json");

        Directory.CreateDirectory(_profilesDirectory);

        InitializeVirtualOutputs();
        Mappings.CollectionChanged += OnMappingsCollectionChanged;

        _hidHideManager = new HidHideManager();
        IsHidHideServiceInstalled = _hidHideManager.IsInstalled;
        IsHidHideActive = _hidHideManager.IsGlobalActive;
        IsAppListInverted = _hidHideManager.IsAppListInverted;

        LoadProfileListFromDisk();
        LoadAppSettings();

        _deviceManager = new InputDeviceManager();
        _deviceManager.OnDevicesRefreshed += devices =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                ConnectedDevices.Clear();
                foreach (var d in devices) ConnectedDevices.Add(d.InstanceName);
                if (ConnectedDevices.Count > 0 && string.IsNullOrEmpty(WizardSelectedDevice))
                {
                    WizardSelectedDevice = ConnectedDevices[0];
                }
            });
        };

        _deviceManager.OnInputPolled += ProcessInputPolling;

        _deviceManager.RefreshDevices();
        _pollStopwatch.Start();
        _deviceManager.StartPolling(pollIntervalMs: 4);

        LoadSelectedProfile();
        RefreshDependencyStates();

        if (AutoCheckForUpdates)
        {
            _ = CheckForUpdatesAsync(isBackgroundCheck: true);
        }
    }

    private void InitializeVirtualOutputs()
    {
        AvailableVirtualOutputs.Clear();

        string[] wheelBinds = [
            "[Wheel] Steering (Axis X)",
            "[Wheel] Gas / Throttle (Axis Y)",
            "[Wheel] Brake (Axis Z)",
            "[Wheel] Clutch (Axis Rx)",
            "[Wheel] Handbrake (Axis Ry)",
            "[Wheel] Slider 0",
            "[Wheel] Slider 1",
            "[Wheel] Paddle Up",
            "[Wheel] Paddle Down",
            "[Wheel] 1st Gear",
            "[Wheel] 2nd Gear",
            "[Wheel] 3rd Gear",
            "[Wheel] 4th Gear",
            "[Wheel] 5th Gear",
            "[Wheel] 6th Gear",
            "[Wheel] 7th Gear",
            "[Wheel] Reverse Gear"
        ];
        foreach (var bind in wheelBinds) AvailableVirtualOutputs.Add(bind);

        for (int i = 1; i <= 32; i++) AvailableVirtualOutputs.Add($"[Wheel] Button {i}");

        string[] xboxBinds = [
            "[Xbox] Left Stick X (Steer)",
            "[Xbox] Left Stick Y",
            "[Xbox] Right Stick X",
            "[Xbox] Right Stick Y",
            "[Xbox] Right Trigger (RT / Gas)",
            "[Xbox] Left Trigger (LT / Brake)",
            "[Xbox] Xbox A (Cross)",
            "[Xbox] Xbox B (Circle)",
            "[Xbox] Xbox X (Square)",
            "[Xbox] Xbox Y (Triangle)",
            "[Xbox] Xbox LB (Left Bumper)",
            "[Xbox] Xbox RB (Right Bumper)",
            "[Xbox] Xbox View (Back)",
            "[Xbox] Xbox Menu (Start)",
            "[Xbox] Xbox LSB (Left Stick Click)",
            "[Xbox] Xbox RSB (Right Stick Click)",
            "[Xbox] D-Pad Up",
            "[Xbox] D-Pad Down",
            "[Xbox] D-Pad Left",
            "[Xbox] D-Pad Right"
        ];
        foreach (var bind in xboxBinds) AvailableVirtualOutputs.Add(bind);
    }

    // --- Bulk Device Target Changer ---
    public void BulkChangeDeviceTarget(string deviceName, uint targetDeviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return;

        var entries = Mappings.Where(m => m.SourceDeviceName == deviceName).ToList();
        foreach (var entry in entries)
        {
            entry.TargetDeviceId = targetDeviceId;
        }

        AutoSaveCurrentProfile();
    }

    private void ProcessInputPolling(ECCR.Services.RawDeviceInputState state)
    {
        long elapsedUs = _pollStopwatch.ElapsedTicks * 1_000_000 / Stopwatch.Frequency;
        _pollStopwatch.Restart();
        double latencyMs = elapsedUs / 1000.0;

        Dispatcher.UIThread.Post(() =>
        {
            Latency = $"{latencyMs:F1}ms";
        });

        for (int i = 0; i < Mappings.Count; i++)
        {
            var mapping = Mappings[i];

            bool isMatchingDevice = mapping.SourceDeviceGuid == state.InstanceGuid ||
                (mapping.SourceDeviceGuid == Guid.Empty && string.Equals(mapping.SourceDeviceName, state.DeviceName, StringComparison.OrdinalIgnoreCase)) ||
                (string.Equals(mapping.SourceDeviceName, state.DeviceName, StringComparison.OrdinalIgnoreCase));

            if (!isMatchingDevice) continue;

            if (mapping.SourceDeviceGuid != state.InstanceGuid)
            {
                mapping.SourceDeviceGuid = state.InstanceGuid;
            }

            if (mapping.SourceType == InputType.Axis)
            {
                if (mapping.SourceIndex >= 0 && mapping.SourceIndex < state.Axes.Length)
                {
                    double calibratedVal = mapping.CalculateCalibratedValue(state.Axes[mapping.SourceIndex]);
                    _feeder.UpdateAxis(mapping.TargetDeviceId, mapping.TargetOutput, calibratedVal);
                }
            }
            else if (mapping.SourceType == InputType.Button)
            {
                if (mapping.SourceIndex >= 0 && mapping.SourceIndex < state.Buttons.Length)
                {
                    _feeder.UpdateButton(mapping.TargetDeviceId, mapping.TargetOutput, state.Buttons[mapping.SourceIndex]);
                }
            }
        }

        if (_listeningEntry == null) return;

        if (!_axisBaselines.TryGetValue(state.InstanceGuid, out var baselines))
        {
            _axisBaselines[state.InstanceGuid] = (int[])state.Axes.Clone();
            return;
        }

        for (int i = 0; i < state.Buttons.Length; i++)
        {
            if (state.Buttons[i])
            {
                string btnLabel = i switch
                {
                    128 => "D-Pad Up",
                    129 => "D-Pad Right",
                    130 => "D-Pad Down",
                    131 => "D-Pad Left",
                    _ => $"B{i + 1}"
                };
                AssignDetectedInput(_listeningEntry, state.InstanceGuid, state.DeviceName, InputType.Button, i, btnLabel);
                return;
            }
        }

        string[] axisNames = ["Axis-X", "Axis-Y", "Axis-Z", "Axis-Rx", "Axis-Ry", "Axis-Rz", "Slider-1", "Slider-2"];
        for (int i = 0; i < state.Axes.Length; i++)
        {
            if (Math.Abs(state.Axes[i] - baselines[i]) > 9800)
            {
                AssignDetectedInput(_listeningEntry, state.InstanceGuid, state.DeviceName, InputType.Axis, i, axisNames[i]);
                return;
            }
        }
    }

    private void AssignDetectedInput(MappingEntry entry, Guid deviceGuid, string deviceName, InputType type, int index, string displayName)
    {
        string guessedTarget = GuessBestTargetChannel(deviceName, type, index);

        Dispatcher.UIThread.Post(() =>
        {
            entry.SourceDeviceGuid = deviceGuid;
            entry.SourceDeviceName = deviceName;
            entry.SourceType = type;
            entry.SourceIndex = index;
            entry.SourceDisplayName = displayName;
            entry.TargetOutput = guessedTarget;

            RebuildGroupedMappings();
            MappingStatus = "Active";
            _listeningEntry = null;
            _axisBaselines.Clear();
            AutoSaveCurrentProfile();
        });
    }

    private static string GuessBestTargetChannel(string deviceName, InputType type, int index)
    {
        string dev = deviceName.ToLowerInvariant();
        bool isPlayStation = dev.Contains("dualsense") || dev.Contains("dualshock") || dev.Contains("sony") || dev.Contains("wireless controller");

        if (isPlayStation)
        {
            if (type == InputType.Axis)
            {
                return index switch
                {
                    0 => "[Xbox] Left Stick X (Steer)",
                    1 => "[Xbox] Left Stick Y",
                    2 => "[Xbox] Left Trigger (LT / Brake)",
                    5 => "[Xbox] Right Trigger (RT / Gas)",
                    _ => "[Xbox] Left Stick X (Steer)"
                };
            }

            return index switch
            {
                0 => "[Xbox] Xbox X (Square)",
                1 => "[Xbox] Xbox A (Cross)",
                2 => "[Xbox] Xbox B (Circle)",
                3 => "[Xbox] Xbox Y (Triangle)",
                4 => "[Xbox] Xbox LB (Left Bumper)",
                5 => "[Xbox] Xbox RB (Right Bumper)",
                8 => "[Xbox] Xbox View (Back)",
                9 => "[Xbox] Xbox Menu (Start)",
                _ => "[Xbox] Xbox A (Cross)"
            };
        }

        if (type == InputType.Axis)
        {
            if (dev.Contains("handbrake") || dev.Contains("ebrake")) return "[Wheel] Handbrake (Axis Ry)";
            return index switch
            {
                0 => "[Wheel] Steering (Axis X)",
                1 => "[Wheel] Gas / Throttle (Axis Y)",
                3 => "[Wheel] Brake (Axis Z)",
                6 => "[Wheel] Clutch (Axis Rx)",
                _ => "[Wheel] Gas / Throttle (Axis Y)"
            };
        }

        if (type == InputType.Button)
        {
            if (index >= 11 && index <= 17)
            {
                return index switch
                {
                    11 => "[Wheel] Reverse Gear",
                    12 => "[Wheel] 1st Gear",
                    13 => "[Wheel] 2nd Gear",
                    14 => "[Wheel] 3rd Gear",
                    15 => "[Wheel] 4th Gear",
                    16 => "[Wheel] 5th Gear",
                    17 => "[Wheel] 6th Gear",
                    _ => $"[Wheel] Button {index + 1}"
                };
            }
            if (index == 4) return "[Wheel] Paddle Down";
            if (index == 5) return "[Wheel] Paddle Up";
            return $"[Wheel] Button {index + 1}";
        }

        return "[Wheel] Button 1";
    }

    [RelayCommand]
    public void StartListening(MappingEntry entry)
    {
        if (_listeningEntry == entry)
        {
            entry.SourceDisplayName = "Click to Bind";
            MappingStatus = "Active";
            _listeningEntry = null;
            _axisBaselines.Clear();
            return;
        }

        _axisBaselines.Clear();
        entry.SourceDisplayName = "Listening...";
        MappingStatus = $"Listening ({entry.TargetOutput})";
        _listeningEntry = entry;
    }

    [RelayCommand]
    public async Task OpenCalibration(MappingEntry entry)
    {
        var dialog = new CalibrationDialog { DataContext = entry };
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            await dialog.ShowDialog(desktop.MainWindow);
            AutoSaveCurrentProfile();
        }
    }

    // --- Settings Modal Commands ---
    [RelayCommand]
    public void OpenSettings() => IsSettingsOpen = true;

    [RelayCommand]
    public void CloseSettings()
    {
        IsSettingsOpen = false;
        SaveAppSettings();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        StartupManager.SetStartup(value);
        SaveAppSettings();
    }

    partial void OnRunInSystemTrayChanged(bool value) => SaveAppSettings();
    partial void OnMinimizeToTrayChanged(bool value) => SaveAppSettings();
    partial void OnCloseMinimizesToTrayChanged(bool value) => SaveAppSettings();
    partial void OnAutoCheckForUpdatesChanged(bool value) => SaveAppSettings();

    // --- Velopack Update Methods ---
    [RelayCommand]
    public async Task CheckForUpdatesManual()
    {
        await CheckForUpdatesAsync(isBackgroundCheck: false);
    }

    public async Task CheckForUpdatesAsync(bool isBackgroundCheck = false)
    {
        IsCheckingForUpdates = true;
        UpdateStatusText = "Checking GitHub for updates...";
        IsUpdateAvailable = false;

        try
        {
            var updateInfo = await _updateService.CheckForUpdatesAsync();
            if (updateInfo != null && updateInfo.TargetFullRelease != null)
            {
                _availableUpdateInfo = updateInfo;
                IsUpdateAvailable = true;
                UpdateStatusText = $"New version available: v{updateInfo.TargetFullRelease.Version}";
            }
            else
            {
                _availableUpdateInfo = null;
                IsUpdateAvailable = false;
                UpdateStatusText = "You have the latest version installed.";
            }
        }
        catch
        {
            if (!isBackgroundCheck)
            {
                UpdateStatusText = "Could not reach update server.";
            }
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    public async Task InstallUpdateAndRestart()
    {
        if (_availableUpdateInfo == null) return;

        IsInstallingUpdate = true;
        UpdateStatusText = "Downloading update...";
        UpdateDownloadProgress = 0;

        bool success = await _updateService.DownloadAndApplyAsync(_availableUpdateInfo, progress =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                UpdateDownloadProgress = progress;
                UpdateStatusText = $"Downloading: {progress}%";
            });
        });

        if (!success)
        {
            IsInstallingUpdate = false;
            UpdateStatusText = "Update installation failed.";
        }
    }

    // --- Auto-Bind Wizard Commands ---
    [RelayCommand]
    public async Task OpenAutoBindWizard()
    {
        if (ConnectedDevices.Count > 0 && string.IsNullOrEmpty(WizardSelectedDevice))
        {
            WizardSelectedDevice = ConnectedDevices[0];
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

        var connectedInfo = _deviceManager.GetConnectedDevices().FirstOrDefault(d => d.InstanceName == WizardSelectedDevice);
        int buttonCount = connectedInfo?.ButtonCount ?? 32;
        int axisCount = connectedInfo?.AxisCount ?? 8;

        var presets = DevicePresetService.GeneratePreset(WizardSelectedDevice, buttonCount, axisCount);
        foreach (var item in presets)
        {
            WizardBindings.Add(item);
        }
    }

    [RelayCommand]
    public void ApplyWizardBindings()
    {
        if (string.IsNullOrWhiteSpace(WizardSelectedDevice) || WizardBindings.Count == 0) return;

        var targetDevice = _deviceManager.GetConnectedDevices().FirstOrDefault(d => d.InstanceName == WizardSelectedDevice);
        Guid devGuid = targetDevice?.InstanceGuid ?? Guid.Empty;

        var toRemove = Mappings.Where(m => m.SourceDeviceName == WizardSelectedDevice).ToList();
        foreach (var r in toRemove) Mappings.Remove(r);

        foreach (var item in WizardBindings)
        {
            Mappings.Add(new MappingEntry
            {
                SourceDeviceName = WizardSelectedDevice,
                SourceDeviceGuid = devGuid,
                SourceType = item.Type,
                SourceIndex = item.PhysicalIndex,
                SourceDisplayName = item.PhysicalName,
                TargetDeviceId = 1,
                TargetOutput = item.DefaultTargetOutput,
                Deadzone = 0.0,
                RawMin = 0,
                RawMax = 65535,
                IsInverted = item.PhysicalName.Contains("Brake") || item.PhysicalName.Contains("Gas") || item.PhysicalName.Contains("Throttle")
            });
        }

        RebuildGroupedMappings();
        AutoSaveCurrentProfile();
    }

    // --- Device Grouping & Row Controls ---
    public void RebuildGroupedMappings()
    {
        var expandedStates = GroupedMappings.ToDictionary(g => g.DeviceName, g => g.IsExpanded, StringComparer.OrdinalIgnoreCase);
        GroupedMappings.Clear();

        var grouped = Mappings.GroupBy(m => string.IsNullOrWhiteSpace(m.SourceDeviceName) ? "Unassigned Device" : m.SourceDeviceName).OrderBy(g => g.Key);
        foreach (var group in grouped)
        {
            var deviceGroup = new DeviceMappingGroup(group.Key);
            if (expandedStates.TryGetValue(group.Key, out bool wasExpanded))
                deviceGroup.IsExpanded = wasExpanded;

            foreach (var mapping in group) deviceGroup.Entries.Add(mapping);
            GroupedMappings.Add(deviceGroup);
        }
    }

    [RelayCommand]
    public void AddMappingToDevice(string deviceName)
    {
        var targetDevice = _deviceManager.GetConnectedDevices().FirstOrDefault(d => d.InstanceName == deviceName);
        Mappings.Add(new MappingEntry
        {
            SourceDeviceName = deviceName,
            SourceDeviceGuid = targetDevice?.InstanceGuid ?? Guid.Empty,
            SourceDisplayName = "Click to Bind",
            TargetDeviceId = 1,
            TargetOutput = "[Wheel] Gas / Throttle (Axis Y)"
        });
        RebuildGroupedMappings();
        AutoSaveCurrentProfile();
    }

    [RelayCommand]
    public void AddMapping()
    {
        string defaultDevice = ConnectedDevices.Count > 0 ? ConnectedDevices[0] : "Select Device...";
        var targetDevice = _deviceManager.GetConnectedDevices().FirstOrDefault(d => d.InstanceName == defaultDevice);
        Mappings.Add(new MappingEntry
        {
            SourceDeviceName = defaultDevice,
            SourceDeviceGuid = targetDevice?.InstanceGuid ?? Guid.Empty,
            SourceDisplayName = "Click to Bind",
            TargetDeviceId = 1,
            TargetOutput = "[Wheel] Gas / Throttle (Axis Y)"
        });
        RebuildGroupedMappings();
        AutoSaveCurrentProfile();
    }

    [RelayCommand]
    public void RemoveMapping(MappingEntry entry)
    {
        if (_listeningEntry == entry) _listeningEntry = null;
        Mappings.Remove(entry);
        RebuildGroupedMappings();
        AutoSaveCurrentProfile();
    }

    // --- Dependency Controls ---
    public void RefreshDependencyStates()
    {
        var status = DependencyManager.GetCurrentStatus();
        IsViGEmInstalled = status.ViGEm.IsInstalled;
        IsViGEmRunning = status.ViGEm.IsRunning;
        IsHidHideServiceInstalled = status.HidHide.IsInstalled;
        IsHidHideServiceRunning = status.HidHide.IsRunning;
        IsVJoyInstalled = status.VJoy.IsInstalled;
        IsVJoyRunning = status.VJoy.IsRunning;
        IsDependencyBannerOpen = status.HasIssues;
    }

    [RelayCommand]
    public async Task InstallViGEm()
    {
        IsBusyWithDriverAction = true;
        await DependencyManager.DownloadAndInstallViGEmAsync();
        IsBusyWithDriverAction = false;
        RefreshDependencyStates();
    }

    [RelayCommand]
    public async Task StartViGEmService()
    {
        IsBusyWithDriverAction = true;
        await DependencyManager.StartDriverServiceAsync("ViGEmBus");
        IsBusyWithDriverAction = false;
        RefreshDependencyStates();
    }

    [RelayCommand]
    public async Task InstallHidHide()
    {
        IsBusyWithDriverAction = true;
        await DependencyManager.DownloadAndInstallHidHideAsync();
        IsBusyWithDriverAction = false;
        RefreshDependencyStates();
    }

    [RelayCommand]
    public async Task StartHidHideService()
    {
        IsBusyWithDriverAction = true;
        await DependencyManager.StartDriverServiceAsync("HidHide");
        IsBusyWithDriverAction = false;
        RefreshDependencyStates();
    }

    [RelayCommand]
    public async Task InstallVJoy()
    {
        IsBusyWithDriverAction = true;
        await DependencyManager.DownloadAndInstallVJoyAsync();
        IsBusyWithDriverAction = false;
        RefreshDependencyStates();
    }

    [RelayCommand]
    public async Task StartVJoyService()
    {
        IsBusyWithDriverAction = true;
        await DependencyManager.StartDriverServiceAsync("vjoy");
        IsBusyWithDriverAction = false;
        RefreshDependencyStates();
    }

    [RelayCommand]
    public void DismissDependencyBanner() => IsDependencyBannerOpen = false;

    // --- HidHide Controls ---
    [RelayCommand]
    public void OpenHidHideMenu()
    {
        RefreshHidDevices();
        RefreshHidApplications();
        IsHidHideMenuOpen = true;
    }

    [RelayCommand]
    public void CloseHidHideMenu() => IsHidHideMenuOpen = false;

    public void RefreshHidDevices()
    {
        HidDevices.Clear();
        foreach (var item in _hidHideManager.GetConnectedHidDevices()) HidDevices.Add(item);
    }

    public void RefreshHidApplications()
    {
        WhitelistedApplications.Clear();
        foreach (var app in _hidHideManager.GetApplicationExemptions()) WhitelistedApplications.Add(app);
        IsAppListInverted = _hidHideManager.IsAppListInverted;
    }

    [RelayCommand]
    public void ToggleHidDevice(HidDeviceItem item) => _hidHideManager.ToggleDeviceHiding(item, item.IsHidden);

    [RelayCommand]
    public void SelectAllHidDevices()
    {
        foreach (var device in HidDevices)
        {
            if (!device.IsHidden)
            {
                device.IsHidden = true;
                _hidHideManager.ToggleDeviceHiding(device, true);
            }
        }
    }

    [RelayCommand]
    public void DeselectAllHidDevices()
    {
        foreach (var device in HidDevices)
        {
            if (device.IsHidden)
            {
                device.IsHidden = false;
                _hidHideManager.ToggleDeviceHiding(device, false);
            }
        }
    }

    partial void OnIsHidHideActiveChanged(bool value) => _hidHideManager.SetGlobalHidingState(value);
    partial void OnIsAppListInvertedChanged(bool value) => _hidHideManager.IsAppListInverted = value;

    [RelayCommand]
    public async Task AddApplicationToHidHide(IStorageProvider? storageProvider)
    {
        if (storageProvider == null) return;
        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Game Executable",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Executables") { Patterns = ["*.exe"] }]
        });
        if (files.Count > 0)
        {
            foreach (var f in files) _hidHideManager.AddApplicationExemption(f.Path.LocalPath);
            RefreshHidApplications();
        }
    }

    [RelayCommand]
    public async Task AddDirectoryToHidHide(IStorageProvider? storageProvider)
    {
        if (storageProvider == null) return;
        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Game Folder" });
        if (folders.Count > 0)
        {
            _hidHideManager.AddDirectoryExemptions(folders[0].Path.LocalPath);
            RefreshHidApplications();
        }
    }

    [RelayCommand]
    public void RemoveApplicationFromHidHide(string appPath)
    {
        _hidHideManager.RemoveApplicationExemption(appPath);
        RefreshHidApplications();
    }

    [RelayCommand]
    public void ClearAllApplicationsFromHidHide()
    {
        _hidHideManager.ClearAllApplicationExemptions();
        RefreshHidApplications();
    }

    // --- Profile & Settings Persistence ---
    private void OnMappingsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) foreach (MappingEntry item in e.NewItems) item.PropertyChanged += OnMappingEntryPropertyChanged;
        if (e.OldItems != null) foreach (MappingEntry item in e.OldItems) item.PropertyChanged -= OnMappingEntryPropertyChanged;
    }

    private void OnMappingEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MappingEntry.LiveRawPercentage) ||
            e.PropertyName == nameof(MappingEntry.LiveOutputPercentage) ||
            e.PropertyName == nameof(MappingEntry.LatestRawReading)) return;

        if (e.PropertyName == nameof(MappingEntry.SourceDeviceName)) RebuildGroupedMappings();
        AutoSaveCurrentProfile();
    }

    private void AutoSaveCurrentProfile()
    {
        if (_isLoadingProfile || string.IsNullOrWhiteSpace(SelectedProfile)) return;
        SaveProfileToDisk(SelectedProfile);
    }

    private void LoadAppSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsFilePath));
                if (settings != null)
                {
                    if (!string.IsNullOrWhiteSpace(settings.LastActiveProfile) && Profiles.Contains(settings.LastActiveProfile))
                    {
                        SelectedProfile = settings.LastActiveProfile;
                    }

                    StartWithWindows = StartupManager.IsStartupEnabled();
                    RunInSystemTray = settings.RunInSystemTray;
                    MinimizeToTray = settings.MinimizeToTray;
                    CloseMinimizesToTray = settings.CloseMinimizesToTray;
                    AutoCheckForUpdates = settings.AutoCheckForUpdates;
                }
            }
        }
        catch { }
    }

    public void SaveAppSettings()
    {
        try
        {
            var settings = new AppSettings
            {
                LastActiveProfile = SelectedProfile,
                StartWithWindows = StartWithWindows,
                RunInSystemTray = RunInSystemTray,
                MinimizeToTray = MinimizeToTray,
                CloseMinimizesToTray = CloseMinimizesToTray,
                AutoCheckForUpdates = AutoCheckForUpdates
            };
            File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void LoadProfileListFromDisk()
    {
        Profiles.Clear();
        foreach (var file in Directory.GetFiles(_profilesDirectory, "*.json"))
            Profiles.Add(Path.GetFileNameWithoutExtension(file));

        if (Profiles.Count == 0)
        {
            Profiles.Add("Default");
            SaveProfileToDisk("Default");
        }
    }

    partial void OnSelectedProfileChanged(string value)
    {
        SaveAppSettings();
        LoadSelectedProfile();
    }

    [RelayCommand]
    public void SaveCurrentProfile()
    {
        if (!string.IsNullOrWhiteSpace(SelectedProfile)) SaveProfileToDisk(SelectedProfile);
    }

    [RelayCommand]
    public void OpenCreateProfileDialog()
    {
        CustomProfileInput = string.Empty;
        IsCreatingProfile = true;
    }

    [RelayCommand]
    public void CancelCreateProfile()
    {
        IsCreatingProfile = false;
        CustomProfileInput = string.Empty;
    }

    [RelayCommand]
    public void ConfirmCreateProfile()
    {
        if (string.IsNullOrWhiteSpace(CustomProfileInput)) return;
        string cleanName = CustomProfileInput.Trim();
        if (!Profiles.Contains(cleanName)) Profiles.Add(cleanName);

        SelectedProfile = cleanName;
        SaveProfileToDisk(cleanName);
        SaveAppSettings();
        IsCreatingProfile = false;
        CustomProfileInput = string.Empty;
    }

    [RelayCommand]
    public void DeleteCurrentProfile()
    {
        if (Profiles.Count <= 1) return;
        string path = Path.Combine(_profilesDirectory, $"{SelectedProfile}.json");
        if (File.Exists(path)) File.Delete(path);

        string current = SelectedProfile;
        Profiles.Remove(current);
        SelectedProfile = Profiles.FirstOrDefault() ?? "Default";
    }

    private void SaveProfileToDisk(string profileName)
    {
        try
        {
            var profile = new UserProfile
            {
                ProfileName = profileName,
                OutputMode = VirtualEmulationMode.DirectInputWheel,
                Mappings = Mappings.ToList()
            };
            File.WriteAllText(Path.Combine(_profilesDirectory, $"{profileName}.json"), JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void LoadSelectedProfile()
    {
        _isLoadingProfile = true;
        try
        {
            string path = Path.Combine(_profilesDirectory, $"{SelectedProfile}.json");
            if (File.Exists(path))
            {
                var profile = JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(path));
                if (profile != null)
                {
                    Mappings.Clear();
                    foreach (var m in profile.Mappings) Mappings.Add(m);
                    RebuildGroupedMappings();
                    return;
                }
            }

            Mappings.Clear();
            AddMapping();
            RebuildGroupedMappings();
        }
        finally
        {
            _isLoadingProfile = false;
        }
    }
}