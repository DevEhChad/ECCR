using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private bool _isLoadingSettings = false;
    private UpdateInfo? _availableUpdateInfo;
    private ECCR.Models.AppSettings _currentSettings = new();

    // Shutdown Signal
    [ObservableProperty]
    private bool _isShuttingDown = false;

    // Dynamic Version & Clean Window Title
    [ObservableProperty]
    private string _appVersion = "v1.0.4";

    [ObservableProperty]
    private string _displayVersionText = "Version 1.0.4";

    [ObservableProperty]
    private string _windowTitle = "EhChadsControllerRemapper - ECCR";

    // Update Modals
    [ObservableProperty]
    private bool _isUpdatePromptOpen = false;

    [ObservableProperty]
    private string _newVersionNumber = string.Empty;

    [ObservableProperty]
    private bool _isPostUpdateOpen = false;

    [ObservableProperty]
    private string _postUpdateMessage = string.Empty;

    [ObservableProperty]
    private string _mappingStatus = "Active";

    [ObservableProperty]
    private string _latency = "0.0ms";

    [ObservableProperty]
    private string _virtualDeviceSummary = "vJoy Virtual Wheel + Xbox 360 Controller (Active)";

    [ObservableProperty]
    private string _selectedProfile = "Default";

    [ObservableProperty]
    private VirtualEmulationMode _activeEmulationMode = VirtualEmulationMode.DirectInputWheel;

    [ObservableProperty]
    private string _customProfileInput = string.Empty;

    [ObservableProperty]
    private bool _isCreatingProfile = false;

    // Settings Modal & Toggles
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

    // Live Update Manager State
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

    // Auto-Bind Wizard Properties
    [ObservableProperty]
    private string _wizardSelectedDevice = string.Empty;

    [ObservableProperty]
    private bool _wizardTargetIsWheel = false;

    public ObservableCollection<PresetBindingItem> WizardBindings { get; } = new();

    // Driver Status States
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

    // HidHide Modal Controls
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

        ResolveAppVersion();

        InitializeVirtualOutputs();
        Mappings.CollectionChanged += OnMappingsCollectionChanged;

        _hidHideManager = new HidHideManager();
        IsHidHideServiceInstalled = _hidHideManager.IsInstalled;

        LoadProfileListFromDisk();
        LoadAppSettings();
        CheckPostUpdateStatus();

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

    private void ResolveAppVersion()
    {
        string resolved = string.Empty;

        try
        {
            string[] manifestCandidates = [
                Path.Combine(AppContext.BaseDirectory, "app.manifest"),
                Path.Combine(Directory.GetCurrentDirectory(), "app.manifest"),
                Path.Combine(Directory.GetCurrentDirectory(), "ECCR", "app.manifest")
            ];

            foreach (var path in manifestCandidates)
            {
                if (File.Exists(path))
                {
                    string content = File.ReadAllText(path);
                    var match = Regex.Match(content, @"<assemblyIdentity\s+version=""([^""]+)""");
                    if (match.Success)
                    {
                        string raw = match.Groups[1].Value.Trim();
                        var parts = raw.Split('.');
                        resolved = parts.Length >= 3 ? $"{parts[0]}.{parts[1]}.{parts[2]}" : raw;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(resolved))
            {
                string[] csprojCandidates = [
                    Path.Combine(Directory.GetCurrentDirectory(), "ECCR.csproj"),
                    Path.Combine(Directory.GetCurrentDirectory(), "ECCR", "ECCR.csproj"),
                    Path.Combine(AppContext.BaseDirectory, "ECCR.csproj")
                ];

                foreach (var path in csprojCandidates)
                {
                    if (File.Exists(path))
                    {
                        string content = File.ReadAllText(path);
                        var match = Regex.Match(content, @"<Version>([^<]+)</Version>");
                        if (match.Success)
                        {
                            resolved = match.Groups[1].Value.Trim();
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(resolved))
            {
                var assembly = Assembly.GetEntryAssembly() ?? typeof(MainWindowViewModel).Assembly;
                var infoVer = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(infoVer))
                {
                    var clean = infoVer.Split('+')[0].Trim().TrimStart('v', 'V');
                    if (!string.IsNullOrWhiteSpace(clean) && clean != "1.0.0")
                    {
                        resolved = clean;
                    }
                }
            }
        }
        catch { }

        if (string.IsNullOrWhiteSpace(resolved))
        {
            resolved = "1.0.4";
        }

        AppVersion = $"v{resolved}";
        DisplayVersionText = $"Version {resolved}";
        WindowTitle = "EhChadsControllerRemapper - ECCR";
    }

    private void CheckPostUpdateStatus()
    {
        if (!string.IsNullOrEmpty(_currentSettings.LastRunVersion) && _currentSettings.LastRunVersion != AppVersion)
        {
            PostUpdateMessage = $"You just updated to {AppVersion}!";
            IsPostUpdateOpen = true;
        }

        _currentSettings.LastRunVersion = AppVersion;
        SaveAppSettings();
    }

    private void InitializeVirtualOutputs()
    {
        AvailableVirtualOutputs.Clear();

        string[] xboxBinds = [
            "[Xbox] Left Stick X (Steer / Horizontal)",
            "[Xbox] Left Stick Y (Steer / Vertical)",
            "[Xbox] Right Stick X (Camera / Look Horizontal)",
            "[Xbox] Right Stick Y (Camera / Look Vertical)",
            "[Xbox] Left Trigger (LT / Brake Axis)",
            "[Xbox] Right Trigger (RT / Gas Axis)",
            "[Xbox] Xbox A (Cross / South / Handbrake)",
            "[Xbox] Xbox B (Circle / East)",
            "[Xbox] Xbox X (Square / West / Shift Down)",
            "[Xbox] Xbox Y (Triangle / North / Shift Up)",
            "[Xbox] Xbox LB (Left Bumper / L1 / Clutch)",
            "[Xbox] Xbox RB (Right Bumper / R1)",
            "[Xbox] Xbox LSB (Left Stick Click / L3)",
            "[Xbox] Xbox RSB (Right Stick Click / R3)",
            "[Xbox] D-Pad Up",
            "[Xbox] D-Pad Down",
            "[Xbox] D-Pad Left",
            "[Xbox] D-Pad Right",
            "[Xbox] Xbox Menu (Start / Options)",
            "[Xbox] Xbox View (Back / Map / Share)",
            "[Xbox] Xbox Guide (Home / Guide)"
        ];
        foreach (var bind in xboxBinds) AvailableVirtualOutputs.Add(bind);

        string[] wheelBinds = [
            "[Wheel] Steering (Axis X)",
            "[Wheel] Gas / Throttle (Axis Y)",
            "[Wheel] Brake (Axis Z)",
            "[Wheel] Clutch (Axis Rx)",
            "[Wheel] Handbrake (Axis Ry)",
            "[Wheel] Combined Slider 0",
            "[Wheel] Dual Clutch Slider 1",
            "[Wheel] Paddle Up (Right Shift)",
            "[Wheel] Paddle Down (Left Shift)",
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
    }

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
        if (IsShuttingDown) return;

        long elapsedUs = _pollStopwatch.ElapsedTicks * 1_000_000 / Stopwatch.Frequency;
        _pollStopwatch.Restart();
        double latencyMs = elapsedUs / 1000.0;

        Dispatcher.UIThread.Post(() =>
        {
            Latency = $"{latencyMs:F1}ms";
        });

        if (_listeningEntry == null)
        {
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
            return;
        }

        if (!_axisBaselines.TryGetValue(state.InstanceGuid, out var baselines))
        {
            _axisBaselines[state.InstanceGuid] = (int[])state.Axes.Clone();
            return;
        }

        for (int i = 0; i < state.Buttons.Length; i++)
        {
            if (state.Buttons[i])
            {
                string btnLabel = DevicePresetService.GetButtonDisplayName(state.DeviceName, i);
                AssignDetectedInput(_listeningEntry, state.InstanceGuid, state.DeviceName, InputType.Button, i, btnLabel);
                return;
            }
        }

        for (int i = 0; i < state.Axes.Length; i++)
        {
            if (Math.Abs(state.Axes[i] - baselines[i]) > 10000)
            {
                string axisLabel = DevicePresetService.GetAxisDisplayName(state.DeviceName, i);
                AssignDetectedInput(_listeningEntry, state.InstanceGuid, state.DeviceName, InputType.Axis, i, axisLabel);
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
            entry.IsInverted = (type == InputType.Axis && (displayName.Contains("Stick Y") || displayName.Contains("Vertical") || displayName.Contains("Brake") || displayName.Contains("Throttle")));

            RebuildGroupedMappings();
            MappingStatus = "Active";
            _listeningEntry = null;
            _axisBaselines.Clear();
            AutoSaveCurrentProfile();
        });
    }

    private static string GuessBestTargetChannel(string deviceName, InputType type, int index)
    {
        var category = DevicePresetService.DetectCategory(deviceName);

        bool isMoza = category == DeviceHardwareCategory.MozaEsxWheel || category == DeviceHardwareCategory.MozaWheel;

        if (isMoza)
        {
            if (type == InputType.Axis)
            {
                return index switch
                {
                    0 => "[Xbox] Left Stick X (Steer / Horizontal)",
                    1 => "[Xbox] Right Trigger (RT / Gas Axis)",
                    2 => "[Xbox] Left Trigger (LT / Brake Axis)",
                    3 => "[Xbox] Xbox LB (Left Bumper / L1 / Clutch)",
                    4 => "[Xbox] Xbox A (Cross / South / Handbrake)",
                    _ => "[Xbox] Left Stick X (Steer / Horizontal)"
                };
            }

            return index switch
            {
                0 => "[Xbox] Xbox A (Cross / South / Handbrake)",
                1 => "[Xbox] Xbox B (Circle / East)",
                2 => "[Xbox] Xbox X (Square / West / Shift Down)",
                3 => "[Xbox] Xbox Y (Triangle / North / Shift Up)",
                4 => "[Xbox] Xbox View (Back / Map / Share)",
                5 => "[Xbox] Xbox Menu (Start / Options)",
                6 => "[Xbox] Xbox LB (Left Bumper / L1 / Clutch)",
                7 => "[Xbox] Xbox RB (Right Bumper / R1)",
                8 => "[Xbox] Xbox LSB (Left Stick Click / L3)",
                9 => "[Xbox] Xbox RSB (Right Stick Click / R3)",
                10 => "[Xbox] Xbox Guide (Home / Guide)",
                128 => "[Xbox] D-Pad Up",
                129 => "[Xbox] D-Pad Right",
                130 => "[Xbox] D-Pad Down",
                131 => "[Xbox] D-Pad Left",
                _ => $"[Xbox] Xbox A (Cross / South / Handbrake)"
            };
        }

        bool isSimHardware = category == DeviceHardwareCategory.LogitechRig || 
                             category == DeviceHardwareCategory.FanatecRig || 
                             category == DeviceHardwareCategory.ThrustmasterRig || 
                             category == DeviceHardwareCategory.SimagicRig || 
                             category == DeviceHardwareCategory.GenericWheelOrPedals;

        if (isSimHardware)
        {
            if (type == InputType.Axis)
            {
                return index switch
                {
                    0 => "[Wheel] Steering (Axis X)",
                    1 => "[Wheel] Gas / Throttle (Axis Y)",
                    2 => "[Wheel] Brake (Axis Z)",
                    3 => "[Wheel] Clutch (Axis Rx)",
                    4 => "[Wheel] Handbrake (Axis Ry)",
                    _ => "[Wheel] Gas / Throttle (Axis Y)"
                };
            }

            return index switch
            {
                4 => "[Wheel] Paddle Down (Left Shift)",
                5 => "[Wheel] Paddle Up (Right Shift)",
                12 => "[Wheel] 1st Gear",
                13 => "[Wheel] 2nd Gear",
                14 => "[Wheel] 3rd Gear",
                15 => "[Wheel] 4th Gear",
                16 => "[Wheel] 5th Gear",
                17 => "[Wheel] 6th Gear",
                18 => "[Wheel] Reverse Gear",
                _ => $"[Wheel] Button {index + 1}"
            };
        }

        if (category == DeviceHardwareCategory.PlayStationController || category == DeviceHardwareCategory.NintendoController)
        {
            if (type == InputType.Axis)
            {
                return index switch
                {
                    0 => "[Xbox] Left Stick X (Steer / Horizontal)",
                    1 => "[Xbox] Left Stick Y (Steer / Vertical)",
                    2 => "[Xbox] Right Stick X (Camera / Look Horizontal)",
                    3 => "[Xbox] Left Trigger (LT / Brake Axis)",
                    4 => "[Xbox] Right Trigger (RT / Gas Axis)",
                    5 => "[Xbox] Right Stick Y (Camera / Look Vertical)",
                    _ => "[Xbox] Left Stick X (Steer / Horizontal)"
                };
            }

            return index switch
            {
                0 => "[Xbox] Xbox X (Square / West / Shift Down)",
                1 => "[Xbox] Xbox A (Cross / South / Handbrake)",
                2 => "[Xbox] Xbox B (Circle / East)",
                3 => "[Xbox] Xbox Y (Triangle / North / Shift Up)",
                4 => "[Xbox] Xbox LB (Left Bumper / L1 / Clutch)",
                5 => "[Xbox] Xbox RB (Right Bumper / R1)",
                8 => "[Xbox] Xbox View (Back / Map / Share)",
                9 => "[Xbox] Xbox Menu (Start / Options)",
                10 => "[Xbox] Xbox LSB (Left Stick Click / L3)",
                11 => "[Xbox] Xbox RSB (Right Stick Click / R3)",
                12 => "[Xbox] Xbox Guide (Home / Guide)",
                128 => "[Xbox] D-Pad Up",
                129 => "[Xbox] D-Pad Right",
                130 => "[Xbox] D-Pad Down",
                131 => "[Xbox] D-Pad Left",
                _ => "[Xbox] Xbox A (Cross / South / Handbrake)"
            };
        }

        if (type == InputType.Axis)
        {
            return index switch
            {
                0 => "[Xbox] Left Stick X (Steer / Horizontal)",
                1 => "[Xbox] Left Stick Y (Steer / Vertical)",
                2 => "[Xbox] Left Trigger (LT / Brake Axis)",
                3 => "[Xbox] Right Stick X (Camera / Look Horizontal)",
                4 => "[Xbox] Right Stick Y (Camera / Look Vertical)",
                5 => "[Xbox] Right Trigger (RT / Gas Axis)",
                _ => "[Xbox] Left Stick X (Steer / Horizontal)"
            };
        }

        return index switch
        {
            0 => "[Xbox] Xbox A (Cross / South / Handbrake)",
            1 => "[Xbox] Xbox B (Circle / East)",
            2 => "[Xbox] Xbox X (Square / West / Shift Down)",
            3 => "[Xbox] Xbox Y (Triangle / North / Shift Up)",
            4 => "[Xbox] Xbox LB (Left Bumper / L1 / Clutch)",
            5 => "[Xbox] Xbox RB (Right Bumper / R1)",
            6 => "[Xbox] Xbox View (Back / Map / Share)",
            7 => "[Xbox] Xbox Menu (Start / Options)",
            8 => "[Xbox] Xbox LSB (Left Stick Click / L3)",
            9 => "[Xbox] Xbox RSB (Right Stick Click / R3)",
            128 => "[Xbox] D-Pad Up",
            129 => "[Xbox] D-Pad Right",
            130 => "[Xbox] D-Pad Down",
            131 => "[Xbox] D-Pad Left",
            _ => "[Xbox] Xbox A (Cross / South / Handbrake)"
        };
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

    // Bulk Selection & Removal Commands
    [RelayCommand]
    public void SelectAllMappings()
    {
        foreach (var m in Mappings) m.IsSelected = true;
    }

    [RelayCommand]
    public void DeselectAllMappings()
    {
        foreach (var m in Mappings) m.IsSelected = false;
    }

    [RelayCommand]
    public void RemoveSelectedMappings()
    {
        var selected = Mappings.Where(m => m.IsSelected).ToList();
        if (selected.Count == 0) return;

        foreach (var item in selected)
        {
            if (_listeningEntry == item) _listeningEntry = null;
            Mappings.Remove(item);
        }

        RebuildGroupedMappings();
        AutoSaveCurrentProfile();
    }

    [RelayCommand]
    public void SelectAllInGroup(DeviceMappingGroup group)
    {
        foreach (var entry in group.Entries) entry.IsSelected = true;
    }

    [RelayCommand]
    public void DeselectAllInGroup(DeviceMappingGroup group)
    {
        foreach (var entry in group.Entries) entry.IsSelected = false;
    }

    [RelayCommand]
    public void RemoveSelectedInGroup(DeviceMappingGroup group)
    {
        var selected = group.Entries.Where(e => e.IsSelected).ToList();
        if (selected.Count == 0) return;

        foreach (var item in selected)
        {
            if (_listeningEntry == item) _listeningEntry = null;
            Mappings.Remove(item);
        }

        RebuildGroupedMappings();
        AutoSaveCurrentProfile();
    }

    // Settings Modal Commands
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
        if (_isLoadingSettings) return;
        StartupManager.SetStartup(value);
        SaveAppSettings();
    }

    partial void OnRunInSystemTrayChanged(bool value)
    {
        if (_isLoadingSettings) return;
        SaveAppSettings();
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (_isLoadingSettings) return;
        SaveAppSettings();
    }

    partial void OnCloseMinimizesToTrayChanged(bool value)
    {
        if (_isLoadingSettings) return;
        SaveAppSettings();
    }

    partial void OnAutoCheckForUpdatesChanged(bool value)
    {
        if (_isLoadingSettings) return;
        SaveAppSettings();
    }

    // Velopack Update Methods
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
                NewVersionNumber = $"v{updateInfo.TargetFullRelease.Version}";
                UpdateStatusText = $"New version available: {NewVersionNumber}";
                IsUpdatePromptOpen = true;
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
    public void DismissUpdatePrompt() => IsUpdatePromptOpen = false;

    [RelayCommand]
    public void DismissPostUpdate() => IsPostUpdateOpen = false;

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

    // Auto-Bind Wizard Commands
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

        var connectedInfo = _deviceManager.GetConnectedDevices().FirstOrDefault(d => d.InstanceName == WizardSelectedDevice);
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

        var targetDevice = _deviceManager.GetConnectedDevices().FirstOrDefault(d => d.InstanceName == WizardSelectedDevice);
        Guid devGuid = targetDevice?.InstanceGuid ?? Guid.Empty;

        var toRemove = Mappings.Where(m => m.SourceDeviceName == WizardSelectedDevice).ToList();
        foreach (var r in toRemove) Mappings.Remove(r);

        foreach (var item in WizardBindings)
        {
            bool shouldInvert = item.Type == InputType.Axis && 
                (item.PhysicalName.Contains("Vertical") || item.PhysicalName.Contains("Stick Y") || 
                 item.PhysicalName.Contains("Throttle") || item.PhysicalName.Contains("Brake"));

            Mappings.Add(new MappingEntry
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

        RebuildGroupedMappings();
        AutoSaveCurrentProfile();
    }

    // Device Grouping & Row Controls
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
        var category = DevicePresetService.DetectCategory(deviceName);

        string defaultTarget = category switch
        {
            DeviceHardwareCategory.PlayStationController or DeviceHardwareCategory.NintendoController or DeviceHardwareCategory.XboxGamepad => "[Xbox] Xbox A (Cross / South / Handbrake)",
            _ => "[Wheel] Gas / Throttle (Axis Y)"
        };

        Mappings.Add(new MappingEntry
        {
            SourceDeviceName = deviceName,
            SourceDeviceGuid = targetDevice?.InstanceGuid ?? Guid.Empty,
            SourceDisplayName = "Click to Bind",
            TargetDeviceId = 1,
            TargetOutput = defaultTarget
        });
        RebuildGroupedMappings();
        AutoSaveCurrentProfile();
    }

    [RelayCommand]
    public void AddMapping()
    {
        string defaultDevice = ConnectedDevices.Count > 0 ? ConnectedDevices[0] : "Select Device...";
        var targetDevice = _deviceManager.GetConnectedDevices().FirstOrDefault(d => d.InstanceName == defaultDevice);
        var category = DevicePresetService.DetectCategory(defaultDevice);

        string defaultTarget = category switch
        {
            DeviceHardwareCategory.PlayStationController or DeviceHardwareCategory.NintendoController or DeviceHardwareCategory.XboxGamepad => "[Xbox] Xbox A (Cross / South / Handbrake)",
            _ => "[Wheel] Gas / Throttle (Axis Y)"
        };

        Mappings.Add(new MappingEntry
        {
            SourceDeviceName = defaultDevice,
            SourceDeviceGuid = targetDevice?.InstanceGuid ?? Guid.Empty,
            SourceDisplayName = "Click to Bind",
            TargetDeviceId = 1,
            TargetOutput = defaultTarget
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

    // Dependency Controls
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

    // HidHide Controls
    [RelayCommand]
    public void OpenHidHideMenu()
    {
        RefreshHidDevices();
        RefreshHidApplications();
        IsHidHideMenuOpen = true;
    }

    [RelayCommand]
    public void CloseHidHideMenu()
    {
        IsHidHideMenuOpen = false;
        SaveAppSettings();
    }

    public void RefreshHidDevices()
    {
        HidDevices.Clear();
        foreach (var item in _hidHideManager.GetConnectedHidDevices())
        {
            if (_currentSettings.BlockedInstanceIds.Contains(item.DeviceInstanceId, StringComparer.OrdinalIgnoreCase))
            {
                item.IsHidden = true;
                _hidHideManager.ToggleDeviceHiding(item, true);
            }
            HidDevices.Add(item);
        }
    }

    public void RefreshHidApplications()
    {
        WhitelistedApplications.Clear();
        foreach (var app in _hidHideManager.GetApplicationExemptions()) WhitelistedApplications.Add(app);
        IsAppListInverted = _hidHideManager.IsAppListInverted;
    }

    [RelayCommand]
    public void ToggleHidDevice(HidDeviceItem item)
    {
        _hidHideManager.ToggleDeviceHiding(item, item.IsHidden);
        SaveAppSettings();
    }

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
        SaveAppSettings();
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
        SaveAppSettings();
    }

    partial void OnIsHidHideActiveChanged(bool value)
    {
        if (_isLoadingSettings) return;
        _hidHideManager.SetGlobalHidingState(value);
        SaveAppSettings();
    }

    partial void OnIsAppListInvertedChanged(bool value)
    {
        if (_isLoadingSettings) return;
        _hidHideManager.IsAppListInverted = value;
        SaveAppSettings();
    }

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
            SaveAppSettings();
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
            SaveAppSettings();
        }
    }

    [RelayCommand]
    public void RemoveApplicationFromHidHide(string appPath)
    {
        _hidHideManager.RemoveApplicationExemption(appPath);
        RefreshHidApplications();
        SaveAppSettings();
    }

    [RelayCommand]
    public void ClearAllApplicationsFromHidHide()
    {
        _hidHideManager.ClearAllApplicationExemptions();
        RefreshHidApplications();
        SaveAppSettings();
    }

    // Profile & Settings Persistence
    private void OnMappingsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) foreach (MappingEntry item in e.NewItems) item.PropertyChanged += OnMappingEntryPropertyChanged;
        if (e.OldItems != null) foreach (MappingEntry item in e.OldItems) item.PropertyChanged -= OnMappingEntryPropertyChanged;
    }

    private void OnMappingEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MappingEntry.LiveRawPercentage) ||
            e.PropertyName == nameof(MappingEntry.LiveOutputPercentage) ||
            e.PropertyName == nameof(MappingEntry.LatestRawReading) ||
            e.PropertyName == nameof(MappingEntry.IsSelected)) return;

        if (e.PropertyName == nameof(MappingEntry.SourceDeviceName)) RebuildGroupedMappings();
        AutoSaveCurrentProfile();
    }

    private void AutoSaveCurrentProfile()
    {
        if (_isLoadingProfile || _isLoadingSettings || string.IsNullOrWhiteSpace(SelectedProfile)) return;
        SaveProfileToDisk(SelectedProfile);
    }

    private void LoadAppSettings()
    {
        _isLoadingSettings = true;
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsFilePath));
                if (settings != null)
                {
                    _currentSettings = settings;

                    StartWithWindows = settings.StartWithWindows;
                    RunInSystemTray = settings.RunInSystemTray;
                    MinimizeToTray = settings.MinimizeToTray;
                    CloseMinimizesToTray = settings.CloseMinimizesToTray;
                    AutoCheckForUpdates = settings.AutoCheckForUpdates;

                    IsHidHideActive = settings.IsHidHideActive;
                    _hidHideManager.SetGlobalHidingState(IsHidHideActive);

                    IsAppListInverted = settings.IsAppListInverted;
                    _hidHideManager.IsAppListInverted = IsAppListInverted;

                    if (settings.BlockedInstanceIds != null && settings.BlockedInstanceIds.Count > 0)
                    {
                        _hidHideManager.ApplyBlockedInstances(settings.BlockedInstanceIds);
                    }

                    if (!string.IsNullOrWhiteSpace(settings.LastActiveProfile) && Profiles.Contains(settings.LastActiveProfile))
                    {
                        SelectedProfile = settings.LastActiveProfile;
                    }
                }
            }
        }
        catch { }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    public void SaveAppSettings()
    {
        if (_isLoadingSettings) return;
        try
        {
            _currentSettings.LastActiveProfile = SelectedProfile;
            _currentSettings.LastRunVersion = AppVersion;
            _currentSettings.StartWithWindows = StartWithWindows;
            _currentSettings.RunInSystemTray = RunInSystemTray;
            _currentSettings.MinimizeToTray = MinimizeToTray;
            _currentSettings.CloseMinimizesToTray = CloseMinimizesToTray;
            _currentSettings.AutoCheckForUpdates = AutoCheckForUpdates;
            _currentSettings.IsHidHideActive = IsHidHideActive;
            _currentSettings.IsAppListInverted = IsAppListInverted;
            _currentSettings.BlockedInstanceIds = _hidHideManager.GetBlockedInstanceIds();

            File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(_currentSettings, new JsonSerializerOptions { WriteIndented = true }));
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
        if (_isLoadingSettings) return;
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

    public void CleanupAndShutdown()
    {
        try
        {
            _deviceManager.StopPolling();
            _deviceManager.Dispose();
            _feeder.Dispose();
            SaveAppSettings();
            AutoSaveCurrentProfile();
        }
        catch { }
    }
}