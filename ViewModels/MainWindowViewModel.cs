using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ECCR.Models;
using ECCR.Services;
using ECCR.Services.GameDiscovery;
using ECCR.Views;
using Velopack;

namespace ECCR.ViewModels;

/// <summary>
/// The app's central view model and the hub everything else plugs into. Owns the live
/// <see cref="Mappings"/> list (the single source of truth for every axis/button mapping),
/// drives the DirectInput poll loop via <see cref="InputDeviceManager"/> and feeds calibrated
/// values to <see cref="IVirtualFeeder"/>, and hosts <see cref="HidHide"/> /
/// <see cref="AutoMap"/> as sub-view-models that share this instance's device list and
/// mapping collection rather than owning their own. Persistence is split two ways: app-wide
/// preferences live in <c>%AppData%/ECCR/settings.json</c> (<see cref="SaveAppSettings"/> /
/// <see cref="LoadAppSettings"/>), while the actual mappings for the active profile live in
/// <c>%AppData%/ECCR/Profiles/{name}.json</c> (<see cref="SaveProfileToDisk"/> /
/// <see cref="LoadSelectedProfile"/>) and are auto-saved on essentially every edit via
/// <see cref="AutoSaveCurrentProfile"/>.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly InputDeviceManager? _deviceManager;
    private readonly GameProcessWatcherService? _gameProcessWatcher;
    private readonly DependencyManager _dependencyManager = new();
    private readonly IVirtualFeeder _feeder = new CompositeFeederService();
    private readonly WheelForceFeedbackService _wheelForceFeedback = new();

    /// <summary>Physical device GUIDs currently registered for Mode B rumble-synthesis (see <see cref="SyncXInputFfbSynthesisMappings"/>) - tracked here since <see cref="WheelForceFeedbackService"/> doesn't expose its internal registration set, only add/remove calls.</summary>
    private readonly HashSet<Guid> _synthesizedWheelGuids = new();
    private readonly UpdateService _updateService = new();
    private readonly Stopwatch _pollStopwatch = new();
    private readonly Stopwatch _latencyDispatchStopwatch = Stopwatch.StartNew();

    /// <summary>Last <see cref="LiveDeviceInputState"/> actually dispatched per device, keyed by <see cref="LiveDeviceInputState.InstanceGuid"/> - lets <see cref="ProcessInputPolling"/> skip the UI-thread dispatch entirely when nothing meaningfully changed since the last tick. Only ever touched from the single-threaded polling loop, so no locking needed.</summary>
    private readonly Dictionary<Guid, LiveDeviceInputState> _lastDispatchedLiveState = new();

    /// <summary>Below this raw delta (out of a 0-65535 axis range, ~0.15%) an axis move is treated as noise, not a real change worth an extra UI-thread dispatch.</summary>
    private const int AxisDispatchDeadband = 96;
    private IDisposable? _highResTimerHandle;
    private readonly string _baseDirectory;
    private readonly string _profilesDirectory;
    private readonly string _settingsFilePath;

    /// <summary>Owns profile load/save/CRUD - see its own doc comment for why load/save state (<c>_isLoadingProfile</c>) now lives there instead of here.</summary>
    private readonly ProfileService _profileService;

    /// <summary>Owns the persisted game library and its Steam/Epic scanners - see its own doc comment.</summary>
    private readonly GameLibraryService _gameLibraryService;

    private bool _isLoadingSettings = false;
    private UpdateInfo? _availableUpdateInfo;
    private ECCR.Models.AppSettings _currentSettings = new();

    // Shutdown Signal
    [ObservableProperty]
    private bool _isShuttingDown = false;

    // Virtual Output Master Toggle
    [ObservableProperty]
    private bool _isVirtualOutputActive = true;

    // Gyro-aim / touch-aim: see AppSettings' matching fields for persistence and
    // ApplyMotionAndTouchAim for how these drive the virtual Right Stick.
    [ObservableProperty]
    private bool _gyroAimEnabled;

    [ObservableProperty]
    private string? _gyroAimSourceDeviceName;

    [ObservableProperty]
    private uint _gyroAimTargetPlayer = 1;

    [ObservableProperty]
    private bool _touchAimEnabled;

    [ObservableProperty]
    private string? _touchAimSourceDeviceName;

    [ObservableProperty]
    private uint _touchAimTargetPlayer = 1;

    // Dynamic Version & Clean Window Title
    [ObservableProperty]
    private string _appVersion = string.Empty;

    [ObservableProperty]
    private string _displayVersionText = string.Empty;

    [ObservableProperty]
    private string _windowTitle = "EhChads Controller Remapper - ECCR";

    // Update Modals
    [ObservableProperty]
    private bool _isUpdatePromptOpen = false;

    [ObservableProperty]
    private string _newVersionNumber = string.Empty;

    // Changelog Viewer (also doubles as the post-update "what's new" popup)
    [ObservableProperty]
    private bool _isChangelogOpen = false;

    [ObservableProperty]
    private string _changelogSubtitle = string.Empty;

    [ObservableProperty]
    private bool _showChangelogOnUpdate = true;

    public ObservableCollection<ChangelogBlock> ChangelogEntries { get; } = new();

    /// <summary>Master switch for automatic profile switching, surfaced on the Games page (<see cref="GamesPageViewModel.IsAutoSwitchingEnabled"/>) rather than Settings since it's part of the game-binding workflow. Per-game opt-in (<see cref="Models.GameInfo.AutoSwitchEnabled"/>) still applies on top of this.</summary>
    [ObservableProperty]
    private bool _isAutoSwitchingEnabled = false;

    /// <summary>Whether <see cref="Views.ProfileSwitchToastWindow"/> appears when auto-switching changes the active profile - surfaced in Settings alongside the other app-wide notification-style toggles. The switch itself still happens when this is off.</summary>
    [ObservableProperty]
    private bool _enableProfileSwitchNotifications = true;

    [ObservableProperty]
    private string _mappingStatus = "Active";

    [ObservableProperty]
    private string _latency = "0.0ms";

    [ObservableProperty]
    private string _virtualDeviceSummary = "Player 1 (Active)";

    [ObservableProperty]
    private string _selectedProfile = "Default";

    [ObservableProperty]
    private VirtualEmulationMode _activeEmulationMode = VirtualEmulationMode.DirectInputWheel;

    [ObservableProperty]
    private string _customProfileInput = string.Empty;

    [ObservableProperty]
    private bool _isCreatingProfile = false;

    /// <summary>Profile that loads automatically at app startup instead of <see cref="AppSettings.LastActiveProfile"/>, when set and still present - see the Profiles page's "Set as Global Default" card action. Null means no default is pinned; startup falls back to last-used exactly as before this existed.</summary>
    [ObservableProperty]
    private string? _defaultProfileName;

    /// <summary>Set while the Create-Profile overlay (<see cref="IsCreatingProfile"/>) is open for a Duplicate action rather than a brand-new profile - read by <see cref="ConfirmCreateProfile"/> to route to <see cref="ProfileService.DuplicateProfile"/> instead of the normal blank-create path. Reuses the one overlay/dialog for both flows.</summary>
    private string? _duplicateSourceProfileName;

    // Settings Modal & Toggles
    [ObservableProperty]
    private bool _isSettingsOpen = false;

    [ObservableProperty]
    private bool _startWithWindows = false;

    [ObservableProperty]
    private bool _runInSystemTray = false;

    [ObservableProperty]
    private bool _minimizeToTray = false;

    [ObservableProperty]
    private bool _closeMinimizesToTray = false;

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

    // Driver Status States
    [ObservableProperty]
    private bool _isDependencyBannerOpen = false;

    [ObservableProperty]
    private bool _isViGEmInstalled = false;

    [ObservableProperty]
    private bool _isViGEmRunning = false;

    [ObservableProperty]
    private bool _isHidHideServiceInstalled = false;

    [ObservableProperty]
    private bool _isHidHideServiceRunning = false;

    [ObservableProperty]
    private bool _isVJoyInstalled = false;

    [ObservableProperty]
    private bool _isVJoyRunning = false;

    [ObservableProperty]
    private bool _isBusyWithDriverAction = false;

    [ObservableProperty]
    private string _driverInstallProgressText = string.Empty;

    public bool HasMissingDependencies => !IsViGEmInstalled || !IsHidHideServiceInstalled || !IsVJoyInstalled;

    public ObservableCollection<string> Profiles => _profileService.ProfileNames;
    public ObservableCollection<VirtualEmulationMode> EmulationModes { get; } = new()
    {
        VirtualEmulationMode.DirectInputWheel,
        VirtualEmulationMode.XboxController
    };

    public ObservableCollection<MappingEntry> Mappings { get; } = new();
    public ObservableCollection<DeviceMappingGroup> GroupedMappings { get; } = new();

    /// <summary>
    /// <see cref="GroupedMappings"/> filtered to gamepad-class devices (PlayStation/Xbox/
    /// Nintendo/generic gamepad) - backs the Controller Mapping page. Same
    /// <see cref="DeviceMappingGroup"/> instances as <see cref="GroupedMappings"/>, not copies.
    /// </summary>
    public ObservableCollection<DeviceMappingGroup> GamepadGroupedMappings { get; } = new();

    /// <summary>
    /// <see cref="GroupedMappings"/> filtered to wheel/pedal-class devices - backs the Device
    /// Mapping (Direct Input/Wheels) page. Same <see cref="DeviceMappingGroup"/> instances as
    /// <see cref="GroupedMappings"/>, not copies.
    /// </summary>
    public ObservableCollection<DeviceMappingGroup> WheelGroupedMappings { get; } = new();
    public ObservableCollection<string> ConnectedDevices { get; } = new();

    /// <summary>
    /// Static (not per-instance): there is only ever one of these app-wide, both are built
    /// once and never rebuilt/replaced afterward, and XAML rows deep inside nested
    /// DataTemplates bind to them via "{x:Static vm:MainWindowViewModel.X}" instead of the
    /// "$parent[Window].((vm:MainWindowViewModel)DataContext).X" ancestor-walk pattern used
    /// for everything else on this page. That distinction matters: a ComboBox's SelectedItem/
    /// SelectedValue binds directly off its row's own immediate DataContext (available the
    /// instant the row's DataTemplate is realized), while an ItemsSource reached via a
    /// $parent[Window] ancestor walk only resolves once the row is attached to the live visual
    /// tree - a moment later. Avalonia's ComboBox does not retroactively re-match a
    /// SelectedItem/SelectedValue that arrived before ItemsSource had anything to match against,
    /// so every Channel/Virtual Player Slot dropdown rendered blank despite the underlying
    /// MappingEntry data being correct (confirmed via UI Automation: item lists populated fine,
    /// but no item ever reported IsSelected=true). An x:Static binding has no such race - it
    /// resolves synchronously at compile/load time with no ambient-DataContext or
    /// visual-tree dependency.
    /// </summary>
    public static ObservableCollection<string> AvailableVirtualOutputs { get; } = BuildAvailableVirtualOutputs();

    public static ObservableCollection<PlayerTargetOption> PlayerTargets { get; } = new()
    {
        new PlayerTargetOption { Id = 1, DisplayName = "Player 1 (Primary / Rig)", ShortBadge = "P1", Description = "Combined Wheel, Pedals, Shifter & Controller", TextColorHex = "#5CFFBE", BgColorHex = "#122A1E", BorderColorHex = "#00E599" },
        new PlayerTargetOption { Id = 2, DisplayName = "Player 2 (Split-Screen)", ShortBadge = "P2", Description = "Independent Virtual Controller #2", TextColorHex = "#FFAF70", BgColorHex = "#2B1D12", BorderColorHex = "#FA8B3E" },
        new PlayerTargetOption { Id = 3, DisplayName = "Player 3", ShortBadge = "P3", Description = "Independent Virtual Controller #3", TextColorHex = "#DB70FF", BgColorHex = "#26142E", BorderColorHex = "#C33EFA" },
        new PlayerTargetOption { Id = 4, DisplayName = "Player 4", ShortBadge = "P4", Description = "Independent Virtual Controller #4", TextColorHex = "#FF7088", BgColorHex = "#2A141A", BorderColorHex = "#FA3E5E" }
    };

    /// <summary>
    /// "Assign to Wheel Slot" dropdown options for <c>[Wheel]</c>-prefixed mapping targets -
    /// independent vJoy device numbers (1-8), decoupled from <see cref="PlayerTargets"/> so a
    /// rig with multiple wheel-class peripherals (e.g. a wheel+pedals on one vJoy device and a
    /// separate shifter on another) isn't limited to the 4 Xbox Player slots. 8 covers realistic
    /// multi-peripheral setups; the user's vJoy driver install still needs to be configured for
    /// however many of these 8 slots are actually used.
    /// </summary>
    public static ObservableCollection<WheelDeviceSlotOption> WheelSlots { get; } = new()
    {
        new WheelDeviceSlotOption { Id = 1, DisplayName = "Wheel Slot 1", ShortBadge = "W1", Description = "vJoy Device 1", TextColorHex = "#5CD8FF", BgColorHex = "#122730", BorderColorHex = "#00B8E5" },
        new WheelDeviceSlotOption { Id = 2, DisplayName = "Wheel Slot 2", ShortBadge = "W2", Description = "vJoy Device 2", TextColorHex = "#5CD8FF", BgColorHex = "#122730", BorderColorHex = "#00B8E5" },
        new WheelDeviceSlotOption { Id = 3, DisplayName = "Wheel Slot 3", ShortBadge = "W3", Description = "vJoy Device 3", TextColorHex = "#5CD8FF", BgColorHex = "#122730", BorderColorHex = "#00B8E5" },
        new WheelDeviceSlotOption { Id = 4, DisplayName = "Wheel Slot 4", ShortBadge = "W4", Description = "vJoy Device 4", TextColorHex = "#5CD8FF", BgColorHex = "#122730", BorderColorHex = "#00B8E5" },
        new WheelDeviceSlotOption { Id = 5, DisplayName = "Wheel Slot 5", ShortBadge = "W5", Description = "vJoy Device 5", TextColorHex = "#5CD8FF", BgColorHex = "#122730", BorderColorHex = "#00B8E5" },
        new WheelDeviceSlotOption { Id = 6, DisplayName = "Wheel Slot 6", ShortBadge = "W6", Description = "vJoy Device 6", TextColorHex = "#5CD8FF", BgColorHex = "#122730", BorderColorHex = "#00B8E5" },
        new WheelDeviceSlotOption { Id = 7, DisplayName = "Wheel Slot 7", ShortBadge = "W7", Description = "vJoy Device 7", TextColorHex = "#5CD8FF", BgColorHex = "#122730", BorderColorHex = "#00B8E5" },
        new WheelDeviceSlotOption { Id = 8, DisplayName = "Wheel Slot 8", ShortBadge = "W8", Description = "vJoy Device 8", TextColorHex = "#5CD8FF", BgColorHex = "#122730", BorderColorHex = "#00B8E5" }
    };

    /// <summary>All HidHide device-cloaking state, commands and UI logic.</summary>
    public HidHideViewModel HidHide { get; }

    /// <summary>Auto-Map wizard state and preset generation.</summary>
    public AutoMapWizardViewModel AutoMap { get; }

    /// <summary>Persistent top device-status bar state (battery/connection for gamepad-class devices), visible on every page.</summary>
    public DeviceStatusBarViewModel StatusBar { get; } = new();

    private readonly ControllerHidService _controllerHidService = new();
    private readonly Dictionary<AppPage, ViewModelBase> _pageViewModels = new();
    private ControllerLedViewModel? _controllerLedViewModel;
    private DeviceMappingPageViewModel? _deviceMappingPageViewModel;

    /// <summary>Controller LED state - embedded per-device in the Device Mappings page's More Options tab (see <see cref="DeviceMappingPageViewModel.ControllerLed"/>) rather than a standalone nav page. Constructed eagerly in <see cref="InitializeShellNavigation"/> (before <see cref="LoadSelectedProfile"/> runs), same lifetime as before this became per-device - only the standalone page/nav registration was removed.</summary>
    public ControllerLedViewModel ControllerLedViewModel => _controllerLedViewModel!;

    // Shell navigation
    [ObservableProperty]
    private AppPage _currentPage = AppPage.Home;

    [ObservableProperty]
    private ViewModelBase? _currentPageViewModel;

    [ObservableProperty]
    private bool _isSidebarCollapsed = false;


    public ObservableCollection<NavItem> PrimaryNavItems { get; } = new();
    public ObservableCollection<NavItem> UtilityNavItems { get; } = new();

    [ObservableProperty]
    private MappingEntry? _listeningEntry;

    /// <summary>Keeps <see cref="MappingEntry.IsArmed"/> in sync with whichever entry (if any) is currently being captured, regardless of whether it was armed from the Device Mapping Table or a Visual Device Mapping hotspot - both call through <see cref="StartListening"/>, which is the single writer of <see cref="ListeningEntry"/>.</summary>
    partial void OnListeningEntryChanged(MappingEntry? oldValue, MappingEntry? newValue)
    {
        if (oldValue != null) oldValue.IsArmed = false;
        if (newValue != null) newValue.IsArmed = true;
        OnPropertyChanged(nameof(IsListening));
    }

    /// <summary>
    /// Fired once per physical device per poll tick with a live, calibration-independent
    /// snapshot of its raw button/stick state - purely for UI animation (the Visual Device Mapping's
    /// moving stick graphics and pressed-button highlights), not consumed by the feeder path.
    /// </summary>
    public event Action<LiveDeviceInputState>? OnLiveDeviceStateUpdated;
    private string _preListenDisplayName = "Click to Map";
    private DispatcherTimer? _listenTimeoutTimer;
    private DispatcherTimer? _xInputStatusTimer;
    private readonly Dictionary<Guid, int[]> _axisBaselines = new();

    /// <summary>Per-device snapshot of every button's held/released state taken on the same first post-arm poll tick as <see cref="_axisBaselines"/> - lets capture require a down-EDGE (see <see cref="ProcessInputPolling"/>) instead of sustained state, so a button already held when listening starts is ignored until released and pressed again.</summary>
    private readonly Dictionary<Guid, bool[]> _buttonBaselines = new();

    /// <summary>USB VID/PID per connected device instance, refreshed alongside <see cref="ConnectedDevices"/> by <see cref="InputDeviceManager.OnDevicesRefreshed"/> - a <see cref="ConcurrentDictionary{TKey,TValue}"/> since it's written from the hotplug-refresh timer thread and read from the ~4ms poll thread in <see cref="ProcessInputPolling"/>. Lets live capture pass a VID/PID into <see cref="DevicePresetService.GetButtonDisplayName"/>/<see cref="DevicePresetService.GetAxisDisplayName"/> for more reliable naming than the device-name-substring fallback.</summary>
    private readonly ConcurrentDictionary<Guid, (int VendorId, int ProductId)> _deviceVidPidByGuid = new();

    /// <summary>
    /// Cached result of the last <see cref="InputDeviceManager.OnDevicesRefreshed"/> firing (startup,
    /// then every 1.5s from the hotplug watcher) - what <see cref="GetDeviceHardwareInfo"/> reads
    /// instead of calling <see cref="InputDeviceManager.GetConnectedDevices"/> fresh. That method
    /// does a full DirectInput re-enumeration plus a per-device <c>Joystick</c> construction and
    /// capability probe, which is too slow to call synchronously on the UI thread from as many
    /// places as <see cref="GetDeviceHardwareInfo"/> is (see its own doc comment). Plain field, not
    /// a <see cref="ConcurrentDictionary{TKey,TValue}"/>: always replaced wholesale by reference,
    /// never mutated in place, so a reader on the UI thread can never observe a half-updated list.
    /// </summary>
    private List<DeviceHardwareInfo> _cachedDevices = new();

    /// <summary>
    /// Guards against the background poll loop (~4ms interval) enqueueing more than one
    /// capture/save for the same "Click to Map" session: <see cref="ProcessInputPolling"/>
    /// clears <see cref="ListeningEntry"/> only inside an async <c>Dispatcher.UIThread.Post</c>
    /// continuation (see <see cref="AssignDetectedInput"/>), so several poll ticks can still see
    /// it non-null and re-detect the same held input before that continuation runs. Claimed with
    /// <see cref="Interlocked.CompareExchange(ref int, int, int)"/> the instant a match is found,
    /// *before* dispatching; reset in <see cref="StopListening"/>.
    /// </summary>
    private int _captureClaimed;

    /// <summary>
    /// Set by <see cref="ProcessInputPolling"/> when a captured input is already mapped to a
    /// different row: holds the physical input so a second press of the *same* input confirms
    /// the swap (clearing the other row), while a different input, Esc, Backspace, or the 7s
    /// timeout leaves both mappings untouched. Reset in <see cref="StopListening"/>.
    /// </summary>
    private (Guid DeviceGuid, string DeviceName, InputType Type, int Index)? _pendingConflict;

    /// <summary>
    /// Rotation/force-feedback tuning per wheel device name, keyed and persisted the same way
    /// as <see cref="UserProfile.WheelSettings"/> (round-tripped in <see cref="SaveProfileToDisk"/>/
    /// <see cref="LoadSelectedProfile"/>). Entries are created lazily via
    /// <see cref="GetOrCreateWheelSettings"/> - the Wheel Settings tab (<see cref="DeviceMappingPageViewModel"/>)
    /// is the only consumer today.
    /// </summary>
    public Dictionary<string, WheelDeviceSettings> WheelSettings { get; private set; } = new();

    /// <summary>
    /// Rumble/LED tuning per gamepad device name, keyed and persisted the same way as
    /// <see cref="WheelSettings"/> (round-tripped in <see cref="SaveProfileToDisk"/>/
    /// <see cref="LoadSelectedProfile"/>). Entries are created lazily via
    /// <see cref="GetOrCreateGamepadSettings"/>.
    /// </summary>
    public Dictionary<string, GamepadDeviceSettings> GamepadSettings { get; private set; } = new();

    /// <summary>Underlying real force-feedback relay (vJoy PID packets -> real wheel effects) - see its own doc comment.</summary>
    public WheelForceFeedbackService WheelForceFeedback => _wheelForceFeedback;

    public WheelDeviceSettings GetOrCreateWheelSettings(string deviceName)
    {
        if (WheelSettings.TryGetValue(deviceName, out var existing)) return existing;

        var settings = new WheelDeviceSettings();
        settings.PropertyChanged += OnWheelSettingsPropertyChanged;
        WheelSettings[deviceName] = settings;
        return settings;
    }

    private void OnWheelSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Clipping-meter telemetry changes on every incoming FFB packet - never persisted and
        // never worth an FFB resync, so it's excluded here the same way MappingEntry's own live
        // polling properties are excluded from its property-changed handler.
        if (e.PropertyName is nameof(WheelDeviceSettings.LiveOutputForcePercent) or nameof(WheelDeviceSettings.IsClipping)) return;

        SyncWheelForceFeedbackMappings();
        SyncXInputFfbSynthesisMappings();
        AutoSaveCurrentProfile();
    }

    public GamepadDeviceSettings GetOrCreateGamepadSettings(string deviceName)
    {
        if (GamepadSettings.TryGetValue(deviceName, out var existing)) return existing;

        var settings = new GamepadDeviceSettings();
        settings.PropertyChanged += OnGamepadSettingsPropertyChanged;
        GamepadSettings[deviceName] = settings;
        return settings;
    }

    private void OnGamepadSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is GamepadDeviceSettings changed && e.PropertyName is nameof(GamepadDeviceSettings.LeftTrigger) or nameof(GamepadDeviceSettings.RightTrigger))
        {
            string? deviceName = GamepadSettings.FirstOrDefault(kv => ReferenceEquals(kv.Value, changed)).Key;
            if (deviceName != null) ApplyTriggerEffects(deviceName, changed);
        }

        AutoSaveCurrentProfile();
    }

    /// <summary>
    /// Pushes one gamepad's saved adaptive-trigger effects to the physical pad. A no-op for a device
    /// without adaptive triggers (everything but a DualSense) or one the hotplug cache doesn't know
    /// yet - the latter is covered by the re-apply when it appears in <c>ConnectedDevices</c>.
    /// </summary>
    private void ApplyTriggerEffects(string deviceName, GamepadDeviceSettings settings)
    {
        var info = GetDeviceHardwareInfo(deviceName);
        if (info == null || !ControllerHidService.SupportsAdaptiveTriggers(info.VendorId, info.ProductId)) return;

        try
        {
            _controllerHidService.SetTriggerEffects(info.VendorId, info.ProductId,
                DualSenseTriggerEffect.Encode(settings.LeftTrigger), DualSenseTriggerEffect.Encode(settings.RightTrigger));
        }
        catch { }
    }

    /// <summary>Swaps in a freshly-deserialized <see cref="GamepadSettings"/> dictionary, re-subscribing each entry the same way <see cref="GetOrCreateGamepadSettings"/> does for one created on demand, then applies each pad's trigger effects (a profile switch can change them).</summary>
    private void LoadGamepadSettings(Dictionary<string, GamepadDeviceSettings> settings)
    {
        foreach (var existing in GamepadSettings.Values) existing.PropertyChanged -= OnGamepadSettingsPropertyChanged;

        GamepadSettings = settings;
        foreach (var entry in GamepadSettings.Values) entry.PropertyChanged += OnGamepadSettingsPropertyChanged;

        foreach (var (deviceName, entry) in GamepadSettings) ApplyTriggerEffects(deviceName, entry);
    }

    /// <summary>
    /// One-shot migration for profiles saved before LED color became a per-device setting: if
    /// no gamepad has a <see cref="GamepadDeviceSettings"/> entry yet but the profile's legacy
    /// whole-profile <see cref="UserProfile.LedRed"/>/<see cref="UserProfile.LedGreen"/>/
    /// <see cref="UserProfile.LedBlue"/>/<see cref="UserProfile.LedMode"/>/<see cref="UserProfile.LedSpeed"/>
    /// aren't at their defaults, seed every currently-known gamepad device's new setting from
    /// them - same idea as <see cref="MigrateLegacyWheelRotationDegrees"/>.
    /// </summary>
    private void MigrateLegacyLedColor(byte r, byte g, byte b, LedEffectMode mode, double speed)
    {
        if (GamepadSettings.Count > 0) return;
        bool isDefault = r == 0 && g == 122 && b == 255 && mode == LedEffectMode.Static && Math.Abs(speed - 1.0) < 0.001;
        if (isDefault) return;

        foreach (var device in StatusBar.GamepadStatuses)
        {
            var settings = GetOrCreateGamepadSettings(device.DeviceInstanceName);
            settings.LedRed = r;
            settings.LedGreen = g;
            settings.LedBlue = b;
            settings.LedMode = mode;
            settings.LedSpeed = speed;
        }
    }

    /// <summary>
    /// Resolves, for each Wheel Slot (vJoy device ID 1-8, see <see cref="WheelSlots"/>),
    /// whichever physical wheel currently feeds that slot's "[Wheel] Steering" target - the one
    /// axis mapping that unambiguously identifies "the" wheel for FFB purposes - and pushes it
    /// into <see cref="_wheelForceFeedback"/>. Called whenever a mapping or a wheel's
    /// <see cref="WheelDeviceSettings"/> changes; idempotent.
    /// </summary>
    private void SyncWheelForceFeedbackMappings()
    {
        for (uint targetDeviceId = 1; targetDeviceId <= 8; targetDeviceId++)
        {
            var steeringEntry = Mappings.FirstOrDefault(m =>
                m.TargetDeviceId == targetDeviceId &&
                m.SourceDeviceGuid != Guid.Empty &&
                m.TargetOutput.StartsWith("[Wheel] Steering", StringComparison.OrdinalIgnoreCase));

            if (steeringEntry == null)
            {
                _wheelForceFeedback.RemoveWheelMapping(targetDeviceId);
                continue;
            }

            var settings = GetOrCreateWheelSettings(steeringEntry.SourceDeviceName);
            _wheelForceFeedback.UpdateWheelMapping(targetDeviceId, steeringEntry.SourceDeviceGuid, settings);
        }
    }

    /// <summary>
    /// Mode B counterpart to <see cref="SyncWheelForceFeedbackMappings"/>: for every wheel/pedal
    /// device whose steering axis is routed to an "[Xbox]"/"[DS4]" target instead of vJoy's
    /// "[Wheel] Steering" (so the game only ever sends simple rumble, never real DirectInput FFB)
    /// and who has opted in via <see cref="WheelDeviceSettings.SynthesizeFfbFromRumbleEnabled"/>,
    /// registers it with <see cref="_wheelForceFeedback"/> so the <c>RumbleReceived</c> handler
    /// below can synthesize wheel forces from that rumble. Called alongside
    /// <see cref="SyncWheelForceFeedbackMappings"/> everywhere that's already called.
    /// </summary>
    private void SyncXInputFfbSynthesisMappings()
    {
        var steeringByDevice = Mappings
            .Where(m => m.SourceDeviceGuid != Guid.Empty &&
                (m.TargetOutput.StartsWith("[Xbox] Left Stick X", StringComparison.OrdinalIgnoreCase) ||
                 m.TargetOutput.StartsWith("[DS4] Left Stick X", StringComparison.OrdinalIgnoreCase)))
            .GroupBy(m => m.SourceDeviceGuid)
            .Select(g => g.First())
            .ToList();

        var activeGuids = new HashSet<Guid>();

        foreach (var entry in steeringByDevice)
        {
            if (!DevicePresetService.IsWheelOrPedalCategory(DevicePresetService.DetectCategory(entry.SourceDeviceName))) continue;

            var settings = GetOrCreateWheelSettings(entry.SourceDeviceName);
            if (!settings.SynthesizeFfbFromRumbleEnabled) continue;

            activeGuids.Add(entry.SourceDeviceGuid);
            _wheelForceFeedback.UpdateSynthMapping(entry.SourceDeviceGuid, settings);
        }

        foreach (var guid in _synthesizedWheelGuids.Where(g => !activeGuids.Contains(g)).ToList())
        {
            _wheelForceFeedback.RemoveSynthMapping(guid);
        }

        _synthesizedWheelGuids.Clear();
        foreach (var guid in activeGuids) _synthesizedWheelGuids.Add(guid);
    }

    /// <summary>Forwarded from <c>MainWindow.OnOpened</c> once a real platform window handle exists - see <see cref="WheelForceFeedbackService.SetWindowHandle"/>.</summary>
    public void SetNativeWindowHandle(IntPtr handle) => _wheelForceFeedback.SetWindowHandle(handle);

    public MainWindowViewModel()
    {
        _baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECCR");
        _profilesDirectory = Path.Combine(_baseDirectory, "Profiles");
        _settingsFilePath = Path.Combine(_baseDirectory, "settings.json");

        // The Avalonia XAML previewer instantiates this via <Design.DataContext> without a
        // real window/desktop lifetime, so it must not touch DirectInput, drivers, or the
        // filesystem - just enough state to make the designer render something sensible.
        if (Design.IsDesignMode)
        {
            AppVersion = "v1.0.7";
            DisplayVersionText = "Version 1.0.7";
            WindowTitle = "EhChads Controller Remapper - ECCR";
            _deviceManager = null;
            HidHide = new HidHideViewModel();
            AutoMap = new AutoMapWizardViewModel();
            _profileService = new ProfileService();
            _gameLibraryService = new GameLibraryService();
            return;
        }

        bool isFirstRun = !File.Exists(_settingsFilePath);

        Directory.CreateDirectory(_profilesDirectory);

        ResolveAppVersion();
        Mappings.CollectionChanged += OnMappingsCollectionChanged;

        HidHide = new HidHideViewModel(() => _currentSettings, SaveAppSettings);
        IsHidHideServiceInstalled = HidHide.IsDriverInstalled;

        _profileService = new ProfileService(
            _profilesDirectory,
            captureCurrentStateAsProfile: (name, id) => new UserProfile
            {
                ProfileName = name,
                Id = id,
                OutputMode = VirtualEmulationMode.DirectInputWheel,
                Mappings = Mappings.ToList(),
                LedRed = (byte)(_controllerLedViewModel?.Red ?? 0),
                LedGreen = (byte)(_controllerLedViewModel?.Green ?? 122),
                LedBlue = (byte)(_controllerLedViewModel?.Blue ?? 255),
                LedMode = _controllerLedViewModel?.EffectMode ?? LedEffectMode.Static,
                LedSpeed = _controllerLedViewModel?.EffectSpeed ?? 1.0,
                WheelSettings = WheelSettings,
                GamepadSettings = GamepadSettings
            },
            applyLoadedProfileToLiveState: profile =>
            {
                if (profile != null)
                {
                    Mappings.Clear();
                    foreach (var m in profile.Mappings)
                    {
                        // See LoadSelectedProfile's old doc comment: defends against a corrupt/
                        // hand-edited profile file (or a past bug).
                        if (m == null) continue;
                        if (string.IsNullOrWhiteSpace(m.TargetOutput)) m.TargetOutput = "[Xbox] Xbox A (Cross / South / Handbrake)";
                        Mappings.Add(m);
                    }
                    RebuildGroupedMappings();
                    UpdateVirtualDeviceSummary();
                    ApplyProfileLedColor(profile.LedRed, profile.LedGreen, profile.LedBlue, profile.LedMode, profile.LedSpeed);
                    LoadWheelSettings(profile.WheelSettings);
                    LoadGamepadSettings(profile.GamepadSettings);
                    HardwareProfileService.MigrateFromLegacyIfNeeded(profile.ProfileName, WheelSettings, GetDeviceHardwareInfo);
                    MigrateLegacyWheelRotationDegrees();
                    MigrateLegacyLedColor(profile.LedRed, profile.LedGreen, profile.LedBlue, profile.LedMode, profile.LedSpeed);
                    SyncHidHideCloakingForAllMappings();
                }
                else
                {
                    Mappings.Clear();
                    AddUncategorizedMapping(isWheel: false);
                    RebuildGroupedMappings();
                    UpdateVirtualDeviceSummary();
                    ApplyProfileLedColor(0, 122, 255, LedEffectMode.Static, 1.0);
                    LoadWheelSettings(new Dictionary<string, WheelDeviceSettings>());
                    LoadGamepadSettings(new Dictionary<string, GamepadDeviceSettings>());
                }
            });

        _gameLibraryService = new GameLibraryService(
            Path.Combine(_baseDirectory, "games.json"),
            new List<IGameDiscoveryProvider>
            {
                new SteamGameDiscoveryProvider(),
                new EpicGameDiscoveryProvider(),
                new XboxGameDiscoveryProvider(),
                new EAGameDiscoveryProvider()
            });
        _gameLibraryService.Load();
        _profileService.ProfileDeleted += id => _gameLibraryService.UnbindGamesForProfile(id);

        AutoMap = CreateAutoMapWizard();

        InitializeShellNavigation();

        _profileService.LoadProfileListFromDisk();
        LoadAppSettings();
        CheckPostUpdateStatus();

        // Fire-and-forget: populates the Profiles page's game grid from Steam/Epic without
        // making first launch wait on disk/registry scans.
        _ = _gameLibraryService.RescanAsync();

        _gameProcessWatcher = new GameProcessWatcherService();
        _gameProcessWatcher.ForegroundExecutableChanged += OnForegroundExecutableChanged;
        _gameProcessWatcher.Start();

        _deviceManager = new InputDeviceManager();
        _deviceManager.OnDevicesRefreshed += devices =>
        {
            // Plain reference swap - safe to read from the UI thread (GetDeviceHardwareInfo)
            // while written here from the hotplug-refresh timer thread, same as every other
            // cross-thread field in this handler; see _cachedDevices' own doc comment.
            _cachedDevices = devices;

            // Off the UI thread (this event can fire from the hotplug-refresh timer), but
            // ConcurrentDictionary is safe to write here while ProcessInputPolling reads it on
            // the separate poll thread - see _deviceVidPidByGuid's doc comment.
            var currentGuids = new HashSet<Guid>(devices.Select(d => d.InstanceGuid));
            foreach (var guid in _deviceVidPidByGuid.Keys.Where(g => !currentGuids.Contains(g)).ToList())
            {
                _deviceVidPidByGuid.TryRemove(guid, out _);
            }
            foreach (var d in devices)
            {
                _deviceVidPidByGuid[d.InstanceGuid] = (d.VendorId, d.ProductId);

                // Fire-and-forget Tier 1 (online registry) lookup - a no-op today (see
                // DeviceMetadataLookupService's doc comment) until a registry URL is configured,
                // and self-deduplicating per VID/PID even once one is, so this is safe to call
                // on every hotplug refresh.
                if (d.VendorId != 0) _ = DeviceMetadataLookupService.PrefetchAsync(d.VendorId, d.ProductId);
            }

            Dispatcher.UIThread.Post(() =>
            {
                // Diff instead of unconditional Clear()+re-add: a name that's still present
                // must never be removed-then-re-added within the same tick, since
                // ConnectedDevices is two-way bound to AutoMap.WizardSelectedDevice (and other
                // pages) - a momentary empty collection would otherwise snap the user's
                // selection back to whatever ends up first in the rebuilt list.
                var newNames = devices.Select(d => d.InstanceName).ToList();
                for (int i = ConnectedDevices.Count - 1; i >= 0; i--)
                {
                    if (!newNames.Contains(ConnectedDevices[i])) ConnectedDevices.RemoveAt(i);
                }
                foreach (var name in newNames)
                {
                    if (ConnectedDevices.Contains(name)) continue;

                    ConnectedDevices.Add(name);

                    // A pad that just (re)connected has lost any adaptive-trigger effect it had.
                    if (GamepadSettings.TryGetValue(name, out var connectedSettings)) ApplyTriggerEffects(name, connectedSettings);
                }

                if (ConnectedDevices.Count > 0 && string.IsNullOrEmpty(AutoMap.WizardSelectedDevice))
                {
                    AutoMap.WizardSelectedDevice = ConnectedDevices[0];
                }

                StatusBar.SyncConnectedDevices(devices);
            });
        };

        _deviceManager.OnInputPolled += ProcessInputPolling;

        // Raise the OS timer resolution for the lifetime of the app - see WinmmTimer's doc
        // comment. Covers both this poll loop and ControllerHidService's below with one call.
        _highResTimerHandle = WinmmTimer.Request(1);

        _deviceManager.RefreshDevices();
        _pollStopwatch.Start();
        _deviceManager.StartPolling(pollIntervalMs: 4);
        _deviceManager.StartHotplugWatcher();

        _controllerHidService.OnStatusUpdated += statuses =>
        {
            // Fires on ControllerHidService's poll Timer thread. ApplyMotionAndTouchAim runs
            // right here rather than being marshaled onto the UI thread - queuing behind
            // whatever else is on the Dispatcher (layout, rendering, other posts) was adding
            // visible extra latency to the live gyro-aim/touch-aim output on top of the poll
            // period itself. It's safe to call from any thread (see its own doc comment).
            // StatusBar.ApplyHidStatuses still needs the UI thread, since it mutates the same
            // ObservableCollection-backed ControllerStatus instances the UI thread reads/binds.
            ApplyMotionAndTouchAim(statuses);
            Dispatcher.UIThread.Post(() => StatusBar.ApplyHidStatuses(statuses));
        };
        _controllerHidService.Start();

        // Xbox-style pads report connection type and battery through XInput, not the Sony HID service
        // above. Battery changes slowly, so a 3s poll is plenty.
        _xInputStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _xInputStatusTimer.Tick += (_, _) => StatusBar.ApplyXInputStatuses();
        _xInputStatusTimer.Start();

        // Forwards a game's rumble feedback (via either virtual pad type) to whichever
        // physical device(s) are mapped into that player slot - the reverse direction of the
        // axis/button feed above. Scaled (or silenced) per-device, per-motor by
        // GamepadDeviceSettings' Rumble toggle/Heavy-Light intensities, same per-device
        // convention WheelDeviceSettings already uses for force-feedback strength.
        // ControllerHidService.SetRumble reaches Sony pads (raw HID report); XInputRumbleService
        // reaches real Xbox-brand pads (XInputSetState) - both are safe to call unconditionally,
        // since each already no-ops for a device it doesn't recognize.
        _feeder.RumbleReceived += (targetDeviceId, largeMotor, smallMotor) =>
        {
            var sources = Mappings
                .Where(m => m.TargetDeviceId == targetDeviceId && m.SourceDeviceGuid != Guid.Empty)
                .Select(m => (m.SourceDeviceGuid, m.SourceDeviceName))
                .Distinct();

            foreach (var (guid, deviceName) in sources)
            {
                // Mode B: this physical wheel's steering is routed to this Xbox/DS4 slot instead
                // of vJoy's "[Wheel] Steering", and the user opted in to synthesizing wheel forces
                // from the game's rumble signal (see SyncXInputFfbSynthesisMappings) - a wheel has
                // no rumble motors of its own, so route there instead of the gamepad-rumble sinks.
                if (_synthesizedWheelGuids.Contains(guid))
                {
                    _wheelForceFeedback.OnSynthRumble(guid, largeMotor, smallMotor);
                    continue;
                }

                var (vendorId, productId) = ControllerHidService.GetVidPid(guid);
                var settings = GetOrCreateGamepadSettings(deviceName);

                if (!settings.RumbleEnabled)
                {
                    _controllerHidService.SetRumble(vendorId, productId, 0, 0);
                    XInputRumbleService.SetRumble(vendorId, productId, 0, 0);
                    continue;
                }

                double heavyScale = Math.Clamp(settings.HeavyMotorIntensityPercent, 0, 100) / 100.0;
                double lightScale = Math.Clamp(settings.LightMotorIntensityPercent, 0, 100) / 100.0;
                byte scaledLarge = (byte)Math.Round(largeMotor * heavyScale);
                byte scaledSmall = (byte)Math.Round(smallMotor * lightScale);
                _controllerHidService.SetRumble(vendorId, productId, scaledLarge, scaledSmall);
                XInputRumbleService.SetRumble(vendorId, productId, scaledLarge, scaledSmall);
            }
        };

        _profileService.SelectProfile(SelectedProfile);
        RefreshDependencyStates();
        UpdateVirtualDeviceSummary();

        if (isFirstRun && HasMissingDependencies)
        {
            _ = AutoInstallMissingDriversAsync();
        }

        if (AutoCheckForUpdates)
        {
            _ = CheckForUpdatesAsync(isBackgroundCheck: true);
        }
    }

    /// <summary>
    /// Builds every sidebar page view model (eagerly, once, so in-progress page state like
    /// HidHide's loaded device list survives navigating away and back - same lifetime pattern
    /// as <see cref="HidHide"/>/<see cref="AutoMap"/> themselves), the primary/utility nav
    /// item lists, and selects Home as the initial page.
    /// </summary>
    private void InitializeShellNavigation()
    {
        var homeIcon = LoadIcon("Full-HomeLogo.png");
        var devicesIcon = LoadIcon("Full-DevicesLogo.png");
        var controllerIcon = LoadIcon("Full-ControllerLogo.png");
        var hidHideIcon = LoadIcon("Full-HidHide.png");

        _controllerLedViewModel = new ControllerLedViewModel(StatusBar.GamepadStatuses, _controllerHidService, AutoSaveCurrentProfile);

        _pageViewModels[AppPage.Home] = new HomeViewModel(StatusBar, BuildQuickActionCards(devicesIcon, hidHideIcon), NavigateToCommand);
        _pageViewModels[AppPage.ConnectedDevices] = new ConnectedDevicesPageViewModel(this, StatusBar.AllDeviceStatuses);
        _deviceMappingPageViewModel = new DeviceMappingPageViewModel(this, StatusBar.AllDeviceStatuses, GamepadGroupedMappings, WheelGroupedMappings);
        _pageViewModels[AppPage.DeviceMapping] = _deviceMappingPageViewModel;
        _pageViewModels[AppPage.Profiles] = new ProfilesViewModel(this, _gameLibraryService, _profileService);
        _pageViewModels[AppPage.Games] = new GamesPageViewModel(this, _gameLibraryService, _profileService);
        _pageViewModels[AppPage.HidHide] = HidHide;
        _pageViewModels[AppPage.AutoMap] = AutoMap;
        _pageViewModels[AppPage.About] = new AboutViewModel(AppVersion);
        _pageViewModels[AppPage.Settings] = new SettingsViewModel(this);

        PrimaryNavItems.Add(new NavItem(AppPage.Home, "Home", "🏠", iconImage: homeIcon));
        PrimaryNavItems.Add(new NavItem(AppPage.ConnectedDevices, "Connected Devices", "🕹️", iconImage: devicesIcon));
        PrimaryNavItems.Add(new NavItem(AppPage.DeviceMapping, "Device Mappings", "🧷", iconImage: controllerIcon));
        PrimaryNavItems.Add(new NavItem(AppPage.Profiles, "Profiles", "📄"));
        PrimaryNavItems.Add(new NavItem(AppPage.Games, "Games", "🎮"));

        UtilityNavItems.Add(new NavItem(AppPage.HidHide, "HidHide", "🛡", iconImage: hidHideIcon));
        UtilityNavItems.Add(new NavItem(AppPage.AutoMap, "Auto Map", "⚡"));
        UtilityNavItems.Add(new NavItem(AppPage.About, "About", "ℹ️"));
        UtilityNavItems.Add(new NavItem(AppPage.Settings, "Settings", "⚙️"));

        CurrentPageViewModel = _pageViewModels[AppPage.Home];
        foreach (var item in PrimaryNavItems) item.IsSelected = item.Page == AppPage.Home;
    }

    private static Bitmap LoadIcon(string fileName) =>
        new(AssetLoader.Open(new Uri($"avares://ECCR/Assets/UILogos/{fileName}")));

    private static ObservableCollection<NavItem> BuildQuickActionCards(Bitmap devicesIcon, Bitmap hidHideIcon) => new()
    {
        new NavItem(AppPage.ConnectedDevices, "Connected Devices", "🕹️", "View connected controllers, wheels, and pedals.", devicesIcon),
        new NavItem(AppPage.Profiles, "Profiles", "📄", "Manage and switch profiles."),
        new NavItem(AppPage.Games, "Games", "🎮", "Assign installed games to profiles."),
        new NavItem(AppPage.HidHide, "HidHide", "🛡", "Hide physical devices from games.", hidHideIcon),
        new NavItem(AppPage.AutoMap, "Auto Map", "⚡", "Generate a starting set of mappings."),
        new NavItem(AppPage.Settings, "Settings", "⚙️", "Manage app settings."),
        new NavItem(AppPage.About, "About", "ℹ️", "Changelog and links.")
    };

    [RelayCommand]
    public void NavigateTo(AppPage page)
    {
        if (_pageViewModels.TryGetValue(CurrentPage, out var leavingVm) && leavingVm is INavigationAware leavingAware)
        {
            leavingAware.OnNavigatedFrom();
        }

        CurrentPage = page;
        CurrentPageViewModel = _pageViewModels[page];

        if (CurrentPageViewModel is INavigationAware enteringAware) enteringAware.OnNavigatedTo();

        foreach (var item in PrimaryNavItems) item.IsSelected = item.Page == page;
        foreach (var item in UtilityNavItems) item.IsSelected = item.Page == page;

        // Replaces the old Open*Menu()-triggered refresh/preselect behavior now that HidHide
        // and Auto-Map are always-present pages instead of explicitly opened dialogs.
        if (page == AppPage.HidHide)
        {
            HidHide.RefreshHidDevices();
            HidHide.RefreshHidApplications();
        }
        else if (page == AppPage.AutoMap)
        {
            AutoMap.PrepareForNavigation();
        }
    }

    /// <summary>
    /// Row-click handler for the Connected Devices page: routes to the mapping page that fits
    /// the clicked device's category and pre-selects it there, without disturbing
    /// <see cref="NavigateToCommand"/> (still page-only, still used everywhere else).
    /// </summary>
    [RelayCommand]
    public void NavigateToDeviceMapping(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return;

        if (_deviceMappingPageViewModel != null)
        {
            _deviceMappingPageViewModel.SelectedDeviceName = deviceName;
            _deviceMappingPageViewModel.SelectTabCommand.Execute(DeviceMappingTab.Mappings);
        }
        NavigateTo(AppPage.DeviceMapping);
    }

    [RelayCommand]
    public void ToggleSidebarCollapsed() => IsSidebarCollapsed = !IsSidebarCollapsed;

    partial void OnIsSidebarCollapsedChanged(bool value)
    {
        if (_isLoadingSettings) return;
        SaveAppSettings();
    }

    private async Task AutoInstallMissingDriversAsync()
    {
        if (!IsViGEmInstalled) await InstallDriver("ViGEmBus");
        if (!IsHidHideServiceInstalled) await InstallDriver("HidHide");
        if (!IsVJoyInstalled) await InstallDriver("VJoy");
    }

    private void ResolveAppVersion()
    {
        string resolved = "1.0.0";

        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(MainWindowViewModel).Assembly;

            var infoVer = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(infoVer))
            {
                resolved = infoVer.Split('+')[0].Trim().TrimStart('v', 'V');
            }
            else if (assembly.GetName().Version is { } ver)
            {
                resolved = $"{ver.Major}.{ver.Minor}.{ver.Build}";
            }
        }
        catch
        {
            resolved = "1.0.0";
        }

        AppVersion = $"v{resolved}";
        DisplayVersionText = $"Version {resolved}";
        WindowTitle = $"EhChads Controller Remapper - {AppVersion}";
    }

    /// <summary>
    /// Detects "the version that just launched differs from the version that last ran" (i.e.
    /// an update just applied) and, if <see cref="ShowChangelogOnUpdate"/> is enabled, pops
    /// the changelog viewer open scoped to just the new version's notes. When the toggle is
    /// off, the update is silent - no popup at all - the user can still open the full
    /// changelog from Settings whenever they want.
    /// </summary>
    private void CheckPostUpdateStatus()
    {
        if (!string.IsNullOrEmpty(_currentSettings.LastRunVersion) && _currentSettings.LastRunVersion != AppVersion && ShowChangelogOnUpdate)
        {
            ShowChangelogForVersion(AppVersion, $"🎉 You just updated to {AppVersion}!");
        }

        _currentSettings.LastRunVersion = AppVersion;
        SaveAppSettings();
    }

    /// <summary>Loads and opens the changelog overlay scoped to one version's section (used by the post-update popup).</summary>
    private void ShowChangelogForVersion(string version, string subtitle)
    {
        string? markdown = ChangelogService.LoadBundledMarkdown();
        if (markdown == null) return;

        string cleanVersion = version.TrimStart('v', 'V');
        string? section = ChangelogService.ExtractVersionSection(markdown, cleanVersion) ?? markdown;

        ChangelogEntries.Clear();
        foreach (var block in ChangelogService.Parse(section)) ChangelogEntries.Add(block);

        ChangelogSubtitle = subtitle;
        IsChangelogOpen = true;
    }

    [RelayCommand]
    public void OpenChangelog()
    {
        string? markdown = ChangelogService.LoadBundledMarkdown();
        if (markdown == null) return;

        ChangelogEntries.Clear();
        foreach (var block in ChangelogService.Parse(markdown)) ChangelogEntries.Add(block);

        ChangelogSubtitle = "Full Version History";
        IsChangelogOpen = true;
    }

    [RelayCommand]
    public void CloseChangelog() => IsChangelogOpen = false;

    [RelayCommand]
    public void OpenChangelogOnGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/DevEhChad/ECCR/blob/main/CHANGELOG.md") { UseShellExecute = true });
        }
        catch { }
    }

    [ObservableProperty]
    private string _controllerSkin = "White-Red-Faced";

    partial void OnControllerSkinChanged(string value) => SaveAppSettings();

    partial void OnGyroAimEnabledChanged(bool value) => SaveAppSettings();
    partial void OnGyroAimSourceDeviceNameChanged(string? value) => SaveAppSettings();
    partial void OnGyroAimTargetPlayerChanged(uint value) => SaveAppSettings();
    partial void OnTouchAimEnabledChanged(bool value) => SaveAppSettings();
    partial void OnTouchAimSourceDeviceNameChanged(string? value) => SaveAppSettings();
    partial void OnTouchAimTargetPlayerChanged(uint value) => SaveAppSettings();

    partial void OnIsVirtualOutputActiveChanged(bool value)
    {
        _feeder.SetActive(value);
        _wheelForceFeedback.SetActive(value);
        HidHide.OnVirtualDevicesToggled(value);
        UpdateVirtualDeviceSummary();
        MappingStatus = value ? "Active" : "Standby (Disabled)";
        SaveAppSettings();
    }

    public void UpdateVirtualDeviceSummary()
    {
        if (!IsVirtualOutputActive)
        {
            VirtualDeviceSummary = "Virtual Devices Disabled (Standby)";
            return;
        }

        var activeTargetCounts = Mappings
            .GroupBy(m => m.TargetDeviceId)
            .OrderBy(g => g.Key)
            .Select(g => $"P{g.Key} ({g.Select(e => e.SourceDeviceName).Distinct().Count()} dev)")
            .ToList();

        if (activeTargetCounts.Count == 0)
        {
            VirtualDeviceSummary = "P1 (Standby)";
        }
        else
        {
            VirtualDeviceSummary = string.Join(" • ", activeTargetCounts) + " Active";
        }
    }

    /// <summary>
    /// Builds the fixed list backing every "Channel" dropdown in the app - the complete
    /// vocabulary of target strings the feeders/converters pattern-match against. This is
    /// the one place that vocabulary is defined; adding a new virtual output channel means
    /// adding it here *and* teaching <see cref="ECCR.Services.VirtualFeederService"/> or
    /// <see cref="ECCR.Services.VJoyFeederService"/> (whichever backend it belongs to) to
    /// recognize the new string. Static because <see cref="AvailableVirtualOutputs"/> is
    /// static (see that property's doc comment) - built once at type-load time.
    /// </summary>
    private static ObservableCollection<string> BuildAvailableVirtualOutputs()
    {
        var outputs = new ObservableCollection<string>();

        string[] xboxTargets = [
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
        foreach (var target in xboxTargets) outputs.Add(target);

        string[] wheelTargets = [
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
        foreach (var target in wheelTargets) outputs.Add(target);

        for (int i = 1; i <= 32; i++) outputs.Add($"[Wheel] Button {i}");

        string[] ds4Targets = [
            "[DS4] Left Stick X (Steer / Horizontal)",
            "[DS4] Left Stick Y (Steer / Vertical)",
            "[DS4] Right Stick X (Camera / Look Horizontal)",
            "[DS4] Right Stick Y (Camera / Look Vertical)",
            "[DS4] L2 Trigger (Brake Axis)",
            "[DS4] R2 Trigger (Gas Axis)",
            "[DS4] Cross (X / South)",
            "[DS4] Circle (O / East)",
            "[DS4] Square (Sq / West)",
            "[DS4] Triangle (Tri / North)",
            "[DS4] L1 (Left Bumper / Clutch)",
            "[DS4] R1 (Right Bumper)",
            "[DS4] L3 (Left Stick Click)",
            "[DS4] R3 (Right Stick Click)",
            "[DS4] D-Pad Up",
            "[DS4] D-Pad Down",
            "[DS4] D-Pad Left",
            "[DS4] D-Pad Right",
            "[DS4] Options (Start)",
            "[DS4] Share (Back)",
            "[DS4] PS (Guide)",
            "[DS4] Touchpad Click"
        ];
        foreach (var target in ds4Targets) outputs.Add(target);

        // Sentinel: selecting it from the row's Target combo reveals the Key Picker Flyout
        // button (see MappingEntry.IsKeyboardTarget) for actually choosing a key. Never
        // persisted as a final TargetOutput itself.
        outputs.Add(KeyboardTargetSentinel);

        // Every real "[Keyboard] <Key>" value a row's TargetOutput can hold must also be listed
        // here, even though nobody picks one directly off this ~190-item combo (the Key Picker
        // Flyout's searchable list is how that's actually done) - a ComboBox's two-way
        // SelectedItem binding stays live even while the control is IsVisible=false, and if the
        // bound value isn't present in ItemsSource, Avalonia can't resolve a SelectedItem for it
        // and pushes null back into the source property, silently corrupting the entry the
        // instant a keyboard target is assigned. Omitting these was the actual cause of the
        // "binding keyboard keys crashes the app" bug - not the native SendInput/keybd_event call.
        foreach (var keyName in KeyboardKeyCatalog.AllKeyNames) outputs.Add($"[Keyboard] {keyName}");

        return outputs;
    }

    /// <summary>Placeholder entry in <see cref="AvailableVirtualOutputs"/> that opens the Key Picker Flyout rather than ever being persisted as a real <c>TargetOutput</c>.</summary>
    public const string KeyboardTargetSentinel = "[Keyboard] (Click to choose a key…)";

    /// <summary>
    /// Drives Gyro-Aim/Touch-Aim (see the matching properties and <c>AppSettings</c> fields):
    /// when enabled, feeds the chosen source device's gyro yaw/pitch or touch X/Y straight to
    /// the Right Stick of the chosen Player's virtual Xbox 360 controller, bypassing the
    /// MappingEntry/profile system entirely - this is a lightweight, always-Xbox-360, always-
    /// Right-Stick feature rather than a fully generalized bindable channel, deliberately kept
    /// simple. Runs on <see cref="ControllerHidService"/>'s own background poll thread, not the
    /// UI thread - <see cref="_feeder"/> is safe to call from any thread, and the source-device
    /// lookup below goes through <see cref="DeviceStatusBarViewModel.TryGetGamepadVidPid"/>
    /// rather than enumerating <see cref="StatusBar"/>'s <c>GamepadStatuses</c> directly, since
    /// that collection is only safe to enumerate on the UI thread.
    /// </summary>
    private void ApplyMotionAndTouchAim(List<ControllerStatus> statuses)
    {
        if (GyroAimEnabled && !string.IsNullOrEmpty(GyroAimSourceDeviceName))
        {
            var hid = StatusBar.TryGetGamepadVidPid(GyroAimSourceDeviceName, out var vendorId, out var productId)
                ? statuses.FirstOrDefault(s => s.VendorId == vendorId && s.ProductId == productId)
                : null;
            if (hid != null && hid.IsMotionSupported)
            {
                _feeder.UpdateAxis(GyroAimTargetPlayer, "[Xbox] Right Stick X (Camera / Look Horizontal)", (hid.GyroYaw + 1.0) / 2.0);
                _feeder.UpdateAxis(GyroAimTargetPlayer, "[Xbox] Right Stick Y (Camera / Look Vertical)", (hid.GyroPitch + 1.0) / 2.0);
            }
        }

        if (TouchAimEnabled && !string.IsNullOrEmpty(TouchAimSourceDeviceName))
        {
            var hid = StatusBar.TryGetGamepadVidPid(TouchAimSourceDeviceName, out var vendorId, out var productId)
                ? statuses.FirstOrDefault(s => s.VendorId == vendorId && s.ProductId == productId)
                : null;
            if (hid is { IsMotionSupported: true, TouchActive: true })
            {
                _feeder.UpdateAxis(TouchAimTargetPlayer, "[Xbox] Right Stick X (Camera / Look Horizontal)", hid.TouchX);
                _feeder.UpdateAxis(TouchAimTargetPlayer, "[Xbox] Right Stick Y (Camera / Look Vertical)", hid.TouchY);
            }
        }
    }

    public void BulkChangeDeviceTarget(string deviceName, uint targetDeviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return;

        var entries = Mappings.Where(m => m.SourceDeviceName == deviceName).ToList();
        foreach (var entry in entries)
        {
            entry.TargetDeviceId = targetDeviceId;
        }

        UpdateVirtualDeviceSummary();
        AutoSaveCurrentProfile();
    }

    /// <summary>
    /// Builds an <see cref="AutoMapWizardViewModel"/> over this instance's shared device list and mapping
    /// collection. The Auto Map page owns one (<see cref="AutoMap"/>); the Input Matrix tab's Auto-Map
    /// flyout owns a second so picking a device/layout there doesn't change the page's own selection.
    /// </summary>
    public AutoMapWizardViewModel CreateAutoMapWizard() => new(
        ConnectedDevices,
        () => _cachedDevices,
        Mappings,
        PlayerTargets,
        WheelSlots,
        () =>
        {
            RebuildGroupedMappings();
            UpdateVirtualDeviceSummary();
            AutoSaveCurrentProfile();
        });

    /// <summary>Finds-or-creates the <see cref="MappingEntry"/> at one physical axis slot for a device and points it at <paramref name="targetOutput"/> - the Input Matrix axis gauge's click-to-quick-assign action. Lives here (not on <c>InputMatrixViewModel</c>) since it needs <see cref="AutoSaveCurrentProfile"/>, which is private.</summary>
    public void QuickAssignAxis(string deviceName, int axisIndex, string targetOutput)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return;

        var entry = Mappings.FirstOrDefault(m =>
            m.SourceDeviceName == deviceName && m.SourceType == InputType.Axis && m.SourceIndex == axisIndex);

        if (entry == null)
        {
            var info = GetDeviceHardwareInfo(deviceName);
            entry = new MappingEntry
            {
                SourceDeviceName = deviceName,
                SourceDeviceGuid = info?.InstanceGuid ?? Guid.Empty,
                SourceType = InputType.Axis,
                SourceIndex = axisIndex,
                SourceDisplayName = DevicePresetService.GetAxisDisplayName(deviceName, axisIndex, info != null ? (info.VendorId, info.ProductId) : null),
                TargetDeviceId = 1,
                Deadzone = 0.08,
                RawMin = 0,
                RawMax = 65535
            };
            Mappings.Add(entry);
        }

        entry.TargetOutput = targetOutput;
        RebuildGroupedMappings();
        UpdateVirtualDeviceSummary();
        AutoSaveCurrentProfile();
    }

    /// <summary>
    /// Public wrapper around the same connected-device capability lookup the Auto-Map wizard already does inline - lets <see cref="ViewModels.DeviceMappingPageViewModel"/> synthesize placeholder rows for every physical input a device reports (see <c>DeviceMappingPageViewModel.RebuildDisplayRows</c>).
    ///
    /// <para>
    /// Reads <see cref="_cachedDevices"/> instead of calling <see cref="InputDeviceManager.GetConnectedDevices"/>
    /// directly - that method does a full DirectInput re-enumeration plus a fresh <c>Joystick</c>
    /// construction and capability probe for every attached device, all synchronous. This is the
    /// single choke point nearly every hot user action in the Device Mappings page runs through
    /// (selecting a device, opening the Input Matrix tab, "Add Input", quick-assign, test
    /// vibration/FFB) - calling the real enumeration from here, on the UI thread, on every one of
    /// those was the actual cause of the app freezing on "Add Input" and on selecting/clicking
    /// into a device. <see cref="_cachedDevices"/> is kept fresh by <see cref="InputDeviceManager.OnDevicesRefreshed"/>
    /// (already firing every 1.5s via the hotplug watcher, and once at startup), so this is now
    /// just an in-memory lookup.
    /// </para>
    /// </summary>
    public DeviceHardwareInfo? GetDeviceHardwareInfo(string deviceName) =>
        _cachedDevices.FirstOrDefault(d => d.InstanceName == deviceName);

    /// <summary>
    /// Fires a short rumble pulse directly at the named device's real hardware, bypassing the
    /// game-feedback path entirely (<c>_feeder.RumbleReceived</c> above) - the "Test Vibration"
    /// button's command, so a user can preview their configured Heavy/Light intensities without a
    /// game running. Tries both physical rumble sinks (Sony HID and Xbox-brand XInput); each
    /// already no-ops for a device it doesn't recognize.
    /// </summary>
    public void TestVibration(string deviceName)
    {
        var info = GetDeviceHardwareInfo(deviceName);
        if (info == null) return;

        var settings = GetOrCreateGamepadSettings(deviceName);
        if (!settings.RumbleEnabled) return;

        byte heavy = (byte)Math.Round(255 * (Math.Clamp(settings.HeavyMotorIntensityPercent, 0, 100) / 100.0));
        byte light = (byte)Math.Round(255 * (Math.Clamp(settings.LightMotorIntensityPercent, 0, 100) / 100.0));
        int vendorId = info.VendorId;
        int productId = info.ProductId;

        _controllerHidService.SetRumble(vendorId, productId, heavy, light);
        XInputRumbleService.SetRumble(vendorId, productId, heavy, light);

        _ = Task.Delay(400).ContinueWith(_ =>
        {
            _controllerHidService.SetRumble(vendorId, productId, 0, 0);
            XInputRumbleService.SetRumble(vendorId, productId, 0, 0);
        });
    }

    /// <summary>"Test Left/Right Force" button - fires a brief directional constant-force pulse directly at the named wheel, bypassing any game/rumble signal, to verify torque delivery and polarity.</summary>
    public void TestFfbDirectionalPulse(string deviceName, bool right)
    {
        var info = GetDeviceHardwareInfo(deviceName);
        if (info == null) return;
        _wheelForceFeedback.TestDirectionalPulse(info.InstanceGuid, right);
    }

    /// <summary>"Test Rumble/Texture" button - fires a 1-second periodic buzz to verify motor oscillation.</summary>
    public void TestFfbTexturePulse(string deviceName)
    {
        var info = GetDeviceHardwareInfo(deviceName);
        if (info == null) return;
        _wheelForceFeedback.TestTexturePulse(info.InstanceGuid);
    }

    /// <summary>"Test Soft-Lock" button - briefly engages a full-strength spring wall to verify resistance engagement.</summary>
    public void TestFfbSoftLock(string deviceName)
    {
        var info = GetDeviceHardwareInfo(deviceName);
        if (info == null) return;
        _wheelForceFeedback.TestSoftLock(info.InstanceGuid);
    }

    /// <summary>Fired (on the UI thread) whenever <see cref="OnForegroundExecutableChanged"/> actually switches the active profile for a recognized, auto-switch-enabled game - <see cref="Views.ProfileSwitchToastWindow"/> subscribes to this to show its notification.</summary>
    public event Action<GameInfo, string>? ProfileAutoSwitched;

    /// <summary>
    /// <see cref="_gameProcessWatcher"/>'s callback - the first real consumer of
    /// <see cref="IsAutoSwitchingEnabled"/>/<see cref="GameInfo.AutoSwitchEnabled"/>, both of
    /// which were previously stored config only. Runs on the watcher's own timer thread, so every
    /// mutation here is posted to the UI thread.
    /// </summary>
    private void OnForegroundExecutableChanged(string? exePath)
    {
        if (!IsAutoSwitchingEnabled) return;

        GameInfo? game = exePath != null ? _gameLibraryService.FindByExecutablePath(exePath) : null;
        bool matched = game is { AutoSwitchEnabled: true, BoundProfileId: not null };
        string? targetProfileName = matched ? _profileService.GetProfileName(game!.BoundProfileId!.Value) : null;

        Dispatcher.UIThread.Post(() =>
        {
            if (matched && targetProfileName != null)
            {
                if (SelectedProfile != targetProfileName)
                {
                    SelectedProfile = targetProfileName;
                    ProfileAutoSwitched?.Invoke(game!, targetProfileName);
                }
            }
            else if (!string.IsNullOrEmpty(DefaultProfileName) && SelectedProfile != DefaultProfileName)
            {
                // Foreground app isn't a tracked, auto-switch-enabled game - fall back to the
                // pinned default profile (if one is set) rather than leaving whatever game
                // profile was last active engaged while the user is elsewhere (desktop, browser,
                // an untracked app).
                SelectedProfile = DefaultProfileName;
            }
        });
    }

    /// <summary>
    /// Adds <paramref name="entry"/> to <see cref="Mappings"/> if it isn't already there - a
    /// no-op for every existing call path (an entry created via "+ Add Input", Auto-Map, or a
    /// diagram hotspot is already registered), and the promotion seam for a placeholder row
    /// synthesized by <c>DeviceMappingPageViewModel.RebuildDisplayRows</c> (see <see cref="MappingEntry.IsPlaceholder"/>):
    /// called as the first step of both <see cref="StartListening"/> and <see cref="StartKeyPick"/>
    /// so a placeholder becomes a real, persisted mapping the instant the user starts
    /// interacting with it, before its <see cref="MappingEntry.TargetOutput"/>/capture result is
    /// even known.
    /// </summary>
    public void EnsureMappingRegistered(MappingEntry? entry)
    {
        if (entry == null || Mappings.Contains(entry)) return;
        entry.IsPlaceholder = false;
        Mappings.Add(entry);
        RebuildGroupedMappings();
        UpdateVirtualDeviceSummary();
    }

    /// <summary>
    /// Fired once per physical device per DirectInput poll tick (every ~4ms, see
    /// <c>InputDeviceManager.StartPolling</c>). Runs in one of two mutually exclusive modes
    /// depending on <see cref="_listeningEntry"/>: normal feed-through (find every mapping
    /// mapped to this device and push calibrated values to <see cref="_feeder"/>), or capture
    /// mode while the "Click to Map" listener is armed (watch for the next button press or
    /// large axis movement on any device and map that to the listening entry). The two
    /// never run in the same tick so a listening session can't also be feeding stale output.
    /// </summary>
    /// <summary>
    /// Reserved synthetic touchpad-click zone indices - one layer above InputDeviceManager's own
    /// 128-131 D-Pad-from-POV expansion, since the deciding factor here (finger touch position)
    /// isn't a DirectInput concept at all and only exists in ControllerHidService's separate raw
    /// HID parse. 132 is deliberately left unused so 133-135 read as an obviously-grouped block.
    /// </summary>
    private const int TouchpadClickFullIndex = 13;
    private const int TouchpadClickLeftIndex = 133;
    private const int TouchpadClickRightIndex = 134;
    private const int TouchpadClickTopIndex = 135;
    private const int ExpandedButtonCount = 136;

    /// <summary>
    /// Resolves the physical touchpad-click bit (index 13) against the device's configured
    /// <see cref="TouchpadClickMode"/> and the latest finger touch position bridged over from
    /// <see cref="_controllerHidService"/> (a separate poll thread - see
    /// <see cref="ControllerHidService.TryGetLatestTouch"/>), producing a widened copy of
    /// <paramref name="state"/>'s button array with index 13 itself made "smart" (full-panel
    /// only in Unified mode, or as the no-touch fallback) and the three new synthetic zone
    /// indices populated. Every other consumer (live output, live diagram feedback, capture
    /// scanning) reads this widened array exactly like any other button index - no other
    /// special-casing needed, the same way D-Pad's synthetic indices already work generically
    /// everywhere once InputDeviceManager expands them.
    /// </summary>
    private bool[] ResolveTouchpadZones(ECCR.Services.RawDeviceInputState state)
    {
        if (state.Buttons.Length <= TouchpadClickFullIndex) return state.Buttons;

        var category = DevicePresetService.DetectCategory(state.DeviceName);
        if (category != DeviceHardwareCategory.PlayStationController) return state.Buttons;

        bool rawClick = state.Buttons[TouchpadClickFullIndex];
        bool full = false, left = false, right = false, top = false;

        if (rawClick)
        {
            var settings = GetOrCreateGamepadSettings(state.DeviceName);
            if (settings.TouchpadClickMode == TouchpadClickMode.Unified)
            {
                full = true;
            }
            else
            {
                var (vendorId, productId) = ControllerHidService.GetVidPid(state.InstanceGuid);
                bool hasTouch = _controllerHidService.TryGetLatestTouch(vendorId, productId, out var touch) && touch.Active;

                if (!hasTouch)
                {
                    full = true; // No-touch fallback (gloved hand, edge contact, etc.)
                }
                else if (settings.TouchpadClickMode == TouchpadClickMode.MultiZone && touch.Y < settings.TouchpadTopSplitFraction)
                {
                    top = true;
                }
                else if (touch.X < 0.5)
                {
                    left = true;
                }
                else
                {
                    right = true;
                }
            }
        }

        var expanded = new bool[ExpandedButtonCount];
        Array.Copy(state.Buttons, expanded, Math.Min(state.Buttons.Length, ExpandedButtonCount));
        expanded[TouchpadClickFullIndex] = full;
        expanded[TouchpadClickLeftIndex] = left;
        expanded[TouchpadClickRightIndex] = right;
        expanded[TouchpadClickTopIndex] = top;
        return expanded;
    }

    /// <summary>True if this device's buttons/POV differ at all, or any axis moved past <see cref="AxisDispatchDeadband"/>, since the last dispatched <see cref="LiveDeviceInputState"/> for it - see <see cref="_lastDispatchedLiveState"/>.</summary>
    private bool HasLiveStateChangedSinceLastDispatch(ECCR.Services.RawDeviceInputState state, bool[] buttons)
    {
        if (!_lastDispatchedLiveState.TryGetValue(state.InstanceGuid, out var last)) return true;

        if (last.PovAngleHundredths != state.PovAngleHundredths) return true;

        if (last.Buttons.Length != buttons.Length) return true;
        for (int i = 0; i < buttons.Length; i++)
        {
            if (last.Buttons[i] != buttons[i]) return true;
        }

        if (last.RawAxes.Length != state.Axes.Length) return true;
        for (int i = 0; i < state.Axes.Length; i++)
        {
            if (Math.Abs(last.RawAxes[i] - state.Axes[i]) > AxisDispatchDeadband) return true;
        }

        return false;
    }

    private void ProcessInputPolling(ECCR.Services.RawDeviceInputState state)
    {
        if (IsShuttingDown) return;

        long elapsedUs = _pollStopwatch.ElapsedTicks * 1_000_000 / Stopwatch.Frequency;
        _pollStopwatch.Restart();
        double latencyMs = elapsedUs / 1000.0;

        // Throttled to ~10Hz - a per-tick dispatch here (potentially 250Hz+) would burn UI-thread
        // time on a value nobody can read that fast anyway; the status bar only shows one decimal.
        if (_latencyDispatchStopwatch.ElapsedMilliseconds >= 100)
        {
            _latencyDispatchStopwatch.Restart();
            Dispatcher.UIThread.Post(() =>
            {
                Latency = $"{latencyMs:F1}ms";
            });
        }

        bool[] buttons = ResolveTouchpadZones(state);

        // Only the Device Mappings page (Diagram/InputMatrix/table row highlights) consumes this
        // event - see OnLiveDeviceStateUpdated's subscribers, all gated by INavigationAware to
        // only be attached while that page is active. Skipping the whole snapshot+dispatch while
        // it isn't active avoids needless allocation and UI-thread traffic on every other page.
        if (ListeningEntry == null)
        {
            if (OnLiveDeviceStateUpdated != null && CurrentPage == AppPage.DeviceMapping && HasLiveStateChangedSinceLastDispatch(state, buttons))
            {
                var (lx, ly, rx, ry) = DevicePresetService.GetStickAxisIndices(state.DeviceName);
                var live = new LiveDeviceInputState
                {
                    DeviceName = state.DeviceName,
                    InstanceGuid = state.InstanceGuid,
                    Buttons = (bool[])buttons.Clone(),
                    NormalizedAxes = state.Axes.Select(a => a / 65535.0).ToArray(),
                    RawAxes = (int[])state.Axes.Clone(),
                    PovAngleHundredths = state.PovAngleHundredths,
                    HasSticks = lx >= 0,
                    LeftStickX = lx >= 0 ? state.Axes[lx] / 65535.0 : 0.5,
                    LeftStickY = ly >= 0 ? state.Axes[ly] / 65535.0 : 0.5,
                    RightStickX = rx >= 0 ? state.Axes[rx] / 65535.0 : 0.5,
                    RightStickY = ry >= 0 ? state.Axes[ry] / 65535.0 : 0.5,
                };
                _lastDispatchedLiveState[state.InstanceGuid] = live;
                Dispatcher.UIThread.Post(() => OnLiveDeviceStateUpdated?.Invoke(live));
            }

            for (int i = 0; i < Mappings.Count; i++)
            {
                var mapping = Mappings[i];

                // A mapping loaded from a profile has a device name but no live GUID yet
                // (DirectInput instance GUIDs aren't stable across reconnects/reboots), so
                // match by name until the device is seen once, then pin to its GUID below -
                // this also lets the fallback name match keep working if the same device
                // happens to enumerate under a different GUID after a replug.
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
                        // Always recompute the calibrated value - it also drives the Calibration
                        // dialog's live raw/output readouts and wheel dial, which must keep
                        // moving even while Virtual Output is toggled off. Only the actual feed
                        // to the virtual device is gated on IsVirtualOutputActive below.
                        double calibratedVal = mapping.CalculateCalibratedValue(state.Axes[mapping.SourceIndex]);
                        if (IsVirtualOutputActive)
                            _feeder.UpdateAxis(mapping.TargetDeviceId, mapping.TargetOutput, calibratedVal);
                    }
                }
                else if (mapping.SourceType == InputType.Button)
                {
                    if (IsVirtualOutputActive && mapping.SourceIndex >= 0 && mapping.SourceIndex < buttons.Length)
                    {
                        _feeder.UpdateButton(mapping.TargetDeviceId, mapping.TargetOutput, buttons[mapping.SourceIndex]);
                    }
                }
            }
            return;
        }

        // Capture mode: the first poll after StartListening just records each axis's current
        // resting value and each button's current held/released state as a baseline (rather than
        // binding on either), since without a baseline an already-off-zero axis - or an
        // already-held button - would immediately "trigger" a mapping the instant listening starts.
        if (!_axisBaselines.TryGetValue(state.InstanceGuid, out var baselines) || !_buttonBaselines.TryGetValue(state.InstanceGuid, out var buttonBaselines))
        {
            _axisBaselines[state.InstanceGuid] = (int[])state.Axes.Clone();
            _buttonBaselines[state.InstanceGuid] = (bool[])buttons.Clone();
            return;
        }

        _deviceVidPidByGuid.TryGetValue(state.InstanceGuid, out var vidPidTuple);
        (int VendorId, int ProductId)? vidPid = vidPidTuple == default ? null : vidPidTuple;

        // Buttons are checked before axes: a firm button press is an unambiguous signal,
        // while a wheel/pedal can drift enough on its own to cross the axis threshold below,
        // so preferring buttons avoids accidentally capturing axis noise when the user meant
        // to press a button that happens to share a poll tick with it. Requires a down-EDGE
        // (false at the arm-time baseline, true now) rather than sustained state, so a button
        // already held down the moment listening starts is ignored until released and pressed
        // again - see _buttonBaselines' doc comment.
        for (int i = 0; i < buttons.Length; i++)
        {
            bool wasDownAtBaseline = i < buttonBaselines.Length && buttonBaselines[i];
            if (!buttons[i] || wasDownAtBaseline) continue;

            if (TryFlagOrResolveConflict(ListeningEntry, state.InstanceGuid, state.DeviceName, InputType.Button, i)) return;

            if (Interlocked.CompareExchange(ref _captureClaimed, 1, 0) != 0) return;
            string btnLabel = DevicePresetService.GetButtonDisplayName(state.DeviceName, i, vidPid);
            AssignDetectedInput(ListeningEntry, state.InstanceGuid, state.DeviceName, InputType.Button, i, btnLabel);
            return;
        }

        // 10000 out of a 0-65535 raw range (~15%) is the movement threshold for "the user is
        // actually moving this axis" vs. normal jitter on an unmoved one.
        for (int i = 0; i < state.Axes.Length; i++)
        {
            if (Math.Abs(state.Axes[i] - baselines[i]) <= 10000) continue;

            if (TryFlagOrResolveConflict(ListeningEntry, state.InstanceGuid, state.DeviceName, InputType.Axis, i)) return;

            if (Interlocked.CompareExchange(ref _captureClaimed, 1, 0) != 0) return;
            string axisLabel = DevicePresetService.GetAxisDisplayName(state.DeviceName, i, vidPid);
            AssignDetectedInput(ListeningEntry, state.InstanceGuid, state.DeviceName, InputType.Axis, i, axisLabel);
            return;
        }
    }

    /// <summary>
    /// AutoMap duplicate-conflict protection: called from <see cref="ProcessInputPolling"/> the
    /// instant a candidate physical input passes its edge/threshold check, before it's actually
    /// assigned. If no other row already uses this exact device+type+index, clears any stale
    /// <see cref="_pendingConflict"/> and returns false so the caller proceeds to assign normally.
    /// Otherwise:
    /// <list type="bullet">
    /// <item>Axes never auto-confirm (a re-crossing threshold has no natural "press again" edge
    /// like a button does) - always blocks and reports the conflict, returning true.</item>
    /// <item>Buttons/POV get a two-step confirm: the first press on a conflicting input just
    /// flags <see cref="_pendingConflict"/> and warns in the row's status text, returning true
    /// (stay armed, nothing assigned yet); a second press of that *same* input clears the other
    /// row's mapping (<see cref="ClearMapping"/>) and returns false so the caller proceeds to
    /// assign the armed entry this same tick.</item>
    /// </list>
    /// Any other outcome - Esc, Backspace, the 7s timeout, or the user instead pressing a
    /// different input - leaves both mappings untouched, since only a matching second press ever
    /// reaches the confirm branch.
    /// </summary>
    private bool TryFlagOrResolveConflict(MappingEntry listeningEntry, Guid deviceGuid, string deviceName, InputType type, int index)
    {
        var conflict = Mappings.FirstOrDefault(m => !ReferenceEquals(m, listeningEntry) && m.SourceType == type && m.SourceIndex == index &&
            (m.SourceDeviceGuid == deviceGuid || string.Equals(m.SourceDeviceName, deviceName, StringComparison.OrdinalIgnoreCase)));

        if (conflict == null)
        {
            _pendingConflict = null;
            return false;
        }

        if (type == InputType.Button && _pendingConflict is { } pending && pending.DeviceGuid == deviceGuid && pending.Type == type && pending.Index == index)
        {
            _pendingConflict = null;
            Dispatcher.UIThread.Post(() => ClearMapping(conflict));
            return false;
        }

        if (type == InputType.Button)
        {
            _pendingConflict = (deviceGuid, deviceName, type, index);
        }

        Dispatcher.UIThread.Post(() =>
        {
            listeningEntry.SourceDisplayName = type == InputType.Button
                ? $"Already mapped to '{conflict.TargetOutput}' - press again to confirm swap, Esc to cancel"
                : $"Already mapped to '{conflict.TargetOutput}' - move a different axis, or Esc/Backspace to cancel";
            MappingStatus = $"Conflict: '{conflict.TargetOutput}' already uses this input";

            _listenTimeoutTimer?.Stop();
            _listenTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
            _listenTimeoutTimer.Tick += (_, _) => CancelListening();
            _listenTimeoutTimer.Start();
        });
        return true;
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

            // Belt-and-suspenders: entries captured via the Device Mapping Table/Auto-Map never get
            // SemanticHotspot set by anything else, which used to make the Device Mapping diagram's
            // exact-match lookup always call them "unbound" (see DeviceMappingDiagramViewModel.RefreshBoundEntries'
            // physical-index fallback, the actual fix for already-saved profiles). Setting it here
            // too just lets freshly-captured mappings hit the cheap exact-match path immediately.
            // Never overwrites an entry the diagram itself already armed.
            entry.SemanticHotspot ??= TryResolveSemanticHotspot(deviceName, type, index);

            RebuildGroupedMappings();
            UpdateVirtualDeviceSummary();
            StopListening();
            AutoSaveCurrentProfile();
        });
    }

    /// <summary>Reverse lookup for <see cref="AssignDetectedInput"/>: given the physical device+index a capture just landed on, finds the matching gamepad/wheel hotspot's semantic name, if any.</summary>
    private static string? TryResolveSemanticHotspot(string deviceName, InputType type, int index)
    {
        var category = DevicePresetService.DetectCategory(deviceName);

        if (DevicePresetService.IsWheelOrPedalCategory(category))
        {
            var wheelMatch = WheelHotspotService.Hotspots.FirstOrDefault(h =>
                (h.IsAxis ? InputType.Axis : InputType.Button) == type &&
                WheelHotspotService.GetWheelSemanticIndex(category, h.Semantic) == index);
            return wheelMatch?.Semantic.ToString();
        }

        if (type != InputType.Button) return null;
        var buttonMatch = ControllerHotspotService.Hotspots.FirstOrDefault(h =>
            ControllerHotspotService.GetSemanticButtonIndex(category, h.Semantic) == index);
        return buttonMatch?.Semantic.ToString();
    }

    /// <summary>
    /// Picks a default target channel the instant an axis/button is captured via
    /// "Click to Map" (see <see cref="AssignDetectedInput"/>), so a new mapping is never
    /// left pointed at nothing before the user gets a chance to change it. This is a
    /// separate, simpler per-index guess from the full <see cref="DevicePresetService.GenerateAutoBindPreset"/>
    /// table the Auto-Map Wizard uses - it only needs one reasonable answer per index
    /// rather than a complete preset for every input at once.
    /// </summary>
    private static string GuessBestTargetChannel(string deviceName, InputType type, int index)
    {
        var category = DevicePresetService.DetectCategory(deviceName);

        if (category == DeviceHardwareCategory.PlayStationController)
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

        // Wheel/rig categories: no per-brand button table. Which raw index is a paddle, a
        // rotary click, or a menu button shifts every time a rim is swapped, so guessing a
        // vehicle function from the index is wrong as often as it's right - every button
        // defaults to its own identity slot instead, and the user reassigns it explicitly (see
        // the Identify Input flow: press the physical control and watch which Device Mapping Table
        // row lights up). The five wheel/pedal axes keep their brand-independent identity guess
        // (steering is always DirectInput axis X on a device DirectInput classifies as a wheel;
        // the further axes are this app's own virtual-wheel axis contract, not a brand guess).
        if (DevicePresetService.IsWheelOrPedalCategory(category))
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

            return $"[Wheel] Button {index + 1}";
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

    /// <summary>True while a "Click to Map" capture is armed - drives Esc/Backspace routing in <c>MainWindow.OnKeyDown</c> and the hint banner's active state.</summary>
    public bool IsListening => ListeningEntry != null;

    /// <summary>
    /// Arms (or, if already armed for this entry, cancels) capture mode - see
    /// <see cref="ProcessInputPolling"/> for what happens on the polling thread while armed.
    /// Mapped to each mapping row's physical-input button in the main grid. Auto-cancels after
    /// 7 seconds of no input (<see cref="_listenTimeoutTimer"/>); Esc (<see cref="CancelListening"/>)
    /// and Backspace (<see cref="ClearListeningMapping"/>) are routed in from <c>MainWindow.OnKeyDown</c>
    /// while <see cref="IsListening"/> is true.
    /// </summary>
    [RelayCommand]
    public void StartListening(MappingEntry entry)
    {
        EnsureMappingRegistered(entry);

        if (ListeningEntry == entry)
        {
            CancelListening();
            return;
        }

        if (ListeningEntry != null) CancelListening();

        _preListenDisplayName = entry.SourceDisplayName;
        _axisBaselines.Clear();
        _buttonBaselines.Clear();
        _pendingConflict = null;
        Interlocked.Exchange(ref _captureClaimed, 0);
        entry.SourceDisplayName = "Listening... (Esc to cancel, Backspace to clear, 7s timeout)";
        MappingStatus = $"Listening ({entry.TargetOutput})";
        ListeningEntry = entry;

        _listenTimeoutTimer?.Stop();
        _listenTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _listenTimeoutTimer.Tick += (_, _) => CancelListening();
        _listenTimeoutTimer.Start();
    }

    /// <summary>Aborts an in-progress capture without touching the entry's existing mapping - restores whatever label it showed before listening started.</summary>
    public void CancelListening()
    {
        if (ListeningEntry == null) return;
        ListeningEntry.SourceDisplayName = _preListenDisplayName;
        StopListening();
    }

    /// <summary>Wipes the currently-listening entry back to a fully unbound state.</summary>
    public void ClearListeningMapping()
    {
        if (ListeningEntry == null) return;
        var entry = ListeningEntry;
        StopListening();
        ClearMapping(entry);
    }

    /// <summary>
    /// Wipes one entry back to a fully unmapped "Click to Map" state without deleting it from
    /// <see cref="Mappings"/> - the row stays visible (now under an "Uncategorized ..." group)
    /// so the user can re-capture a different physical input for the same target/customization
    /// rather than starting over. Reachable from the Device Mapping Table's gear flyout "Clear"
    /// button as well as <see cref="ClearListeningMapping"/> (the Backspace-while-listening
    /// shortcut), which previously was the only way to reach this behavior.
    /// </summary>
    [RelayCommand]
    public void ClearMapping(MappingEntry? entry)
    {
        if (entry == null) return;
        if (ListeningEntry == entry) StopListening();

        // Falls back into whichever "Uncategorized ..." group matches the section the entry
        // was already in (by its old device name's category) rather than always landing in the
        // gamepad section - clearing a wheel mapping should keep it visible on the wheel page.
        bool wasWheel = DevicePresetService.IsWheelOrPedalCategory(DevicePresetService.DetectCategory(entry.SourceDeviceName));
        entry.SourceDeviceName = wasWheel ? UncategorizedWheelDeviceName : UncategorizedGamepadDeviceName;
        entry.SourceDeviceGuid = Guid.Empty;
        entry.SourceIndex = 0;
        entry.SourceDisplayName = "Click to Map";
        RebuildGroupedMappings();
        AutoSaveCurrentProfile();
    }

    private void StopListening()
    {
        _listenTimeoutTimer?.Stop();
        _listenTimeoutTimer = null;
        ListeningEntry = null;
        _axisBaselines.Clear();
        _buttonBaselines.Clear();
        _pendingConflict = null;
        Interlocked.Exchange(ref _captureClaimed, 0);
        MappingStatus = IsVirtualOutputActive ? "Active" : "Standby (Disabled)";
    }

    private MappingEntry? _keyPickEntry;

    /// <summary>
    /// True while the Key Picker Flyout's "Press a Key" capture is armed - routed from
    /// <c>MainWindow.OnKeyDown</c> the same way <see cref="IsListening"/> routes Esc/Backspace,
    /// but capturing any key (mutually exclusive with device-input listening: a keyboard key
    /// press is meaningful input to both, so only one can be armed at a time).
    /// </summary>
    public bool IsPickingKeyboardKey => _keyPickEntry != null;

    /// <summary>Arms in-window keyboard capture for the given row's Key Picker Flyout "Press a Key" toggle.</summary>
    public void StartKeyPick(MappingEntry entry)
    {
        EnsureMappingRegistered(entry);
        if (ListeningEntry != null) CancelListening();
        _keyPickEntry = entry;
        OnPropertyChanged(nameof(IsPickingKeyboardKey));
    }

    public void CancelKeyPick()
    {
        _keyPickEntry = null;
        OnPropertyChanged(nameof(IsPickingKeyboardKey));
    }

    /// <summary>Assigns a captured keyboard key as the currently-armed row's target output, then disarms capture.</summary>
    public void AssignPickedKey(Key key)
    {
        var entry = _keyPickEntry;
        if (entry == null) return;

        string? friendlyName = KeyboardKeyCatalog.GetFriendlyName(key);
        CancelKeyPick();
        if (friendlyName == null) return; // unrecognized/modifier-only key - ignore, stay armed-off, user can retry

        entry.TargetOutput = $"[Keyboard] {friendlyName}";
        UpdateVirtualDeviceSummary();
        AutoSaveCurrentProfile();
    }

    [RelayCommand]
    public async Task OpenCalibration(MappingEntry entry)
    {
        var dialog = new CalibrationDialog { DataContext = entry, WheelSettings = GetOrCreateWheelSettings(entry.SourceDeviceName) };
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            await dialog.ShowDialog(desktop.MainWindow);
            AutoSaveCurrentProfile();
        }
    }

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
            if (ListeningEntry == item) StopListening();
            Mappings.Remove(item);
        }

        RebuildGroupedMappings();
        UpdateVirtualDeviceSummary();
        AutoSaveCurrentProfile();
    }

    [RelayCommand]
    public void SelectAllInGroup(DeviceMappingGroup? group)
    {
        if (group?.Entries == null) return;
        foreach (var entry in group.Entries) entry.IsSelected = true;
    }

    [RelayCommand]
    public void DeselectAllInGroup(DeviceMappingGroup? group)
    {
        if (group?.Entries == null) return;
        foreach (var entry in group.Entries) entry.IsSelected = false;
    }

    [RelayCommand]
    public void RemoveSelectedInGroup(DeviceMappingGroup? group)
    {
        if (group?.Entries == null) return;

        var selected = group.Entries.Where(e => e.IsSelected).ToList();
        if (selected.Count == 0) return;

        foreach (var item in selected)
        {
            if (ListeningEntry == item) StopListening();
            Mappings.Remove(item);
        }

        RebuildGroupedMappings();
        UpdateVirtualDeviceSummary();
        AutoSaveCurrentProfile();
    }

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

    partial void OnShowChangelogOnUpdateChanged(bool value)
    {
        if (_isLoadingSettings) return;
        SaveAppSettings();
    }

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
                // UpdateService.CheckForUpdatesAsync catches its own failures and returns null
                // the same as a genuine "no update available" - LastError is what distinguishes
                // the two, so a manual check reports the real reason instead of a false "latest
                // version" for e.g. a timeout or a GitHub rate limit.
                if (_updateService.LastError != null && !isBackgroundCheck)
                {
                    UpdateStatusText = _updateService.LastError;
                }
                else
                {
                    UpdateStatusText = "You have the latest version installed.";
                }
            }
        }
        catch
        {
            // UpdateService.CheckForUpdatesAsync already catches everything itself; this remains
            // only as defense in depth against something unexpected in the branch above.
            if (!isBackgroundCheck)
            {
                UpdateStatusText = _updateService.LastError ?? "Could not reach update server.";
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
            UpdateStatusText = _updateService.LastError ?? "Update installation failed.";
        }
    }

    /// <summary>
    /// Recomputes <see cref="GroupedMappings"/> (the per-device sections the main grid
    /// renders) from the flat <see cref="Mappings"/> list. Called after any add/remove/rename
    /// that could change grouping rather than kept incrementally in sync, since the mapping
    /// list is small and this is cheap; each group's expand/collapse state is preserved
    /// across the rebuild by name so re-grouping doesn't visually collapse everything.
    /// </summary>
    public void RebuildGroupedMappings()
    {
        var expandedStates = GroupedMappings.ToDictionary(g => g.DeviceName, g => g.IsExpanded, StringComparer.OrdinalIgnoreCase);
        GroupedMappings.Clear();
        GamepadGroupedMappings.Clear();
        WheelGroupedMappings.Clear();

        var grouped = Mappings.GroupBy(m => string.IsNullOrWhiteSpace(m.SourceDeviceName) ? UncategorizedGamepadDeviceName : m.SourceDeviceName).OrderBy(g => g.Key);
        foreach (var group in grouped)
        {
            var deviceGroup = new DeviceMappingGroup(group.Key);
            if (expandedStates.TryGetValue(group.Key, out bool wasExpanded))
                deviceGroup.IsExpanded = wasExpanded;

            foreach (var mapping in group) deviceGroup.Entries.Add(mapping);
            GroupedMappings.Add(deviceGroup);

            var category = DevicePresetService.DetectCategory(group.Key);
            if (DevicePresetService.IsWheelOrPedalCategory(category))
            {
                WheelGroupedMappings.Add(deviceGroup);
            }
            else
            {
                GamepadGroupedMappings.Add(deviceGroup);
            }
        }
    }

    [RelayCommand]
    public void AddMappingToDevice(string deviceName)
    {
        var targetDevice = GetDeviceHardwareInfo(deviceName);
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
            SourceDisplayName = "Click to Map",
            TargetDeviceId = 1,
            TargetOutput = defaultTarget
        });
        RebuildGroupedMappings();
        UpdateVirtualDeviceSummary();
        AutoSaveCurrentProfile();
    }

    /// <summary>
    /// Placeholder "device name" for a mapping that hasn't been mapped to a real physical device
    /// yet - <see cref="DevicePresetService.DetectCategory"/> falls through to
    /// <see cref="DeviceHardwareCategory.GenericGamepad"/> for both of these (neither contains
    /// a recognized brand/wheel keyword), which is what routes them to
    /// <see cref="GamepadGroupedMappings"/>/<see cref="WheelGroupedMappings"/> respectively in
    /// <see cref="RebuildGroupedMappings"/> - no separate "which section did this come from"
    /// field needed. Once <see cref="AssignDetectedInput"/> captures a real input, it overwrites
    /// <see cref="MappingEntry.SourceDeviceName"/> unconditionally, so the entry moves into (or
    /// creates) that device's own group automatically on the next rebuild.
    /// </summary>
    private const string UncategorizedGamepadDeviceName = "Uncategorized Device";

    /// <summary>Wheel-section counterpart of <see cref="UncategorizedGamepadDeviceName"/> - contains "wheel" so <see cref="DevicePresetService.DetectCategory"/> routes it to <see cref="WheelGroupedMappings"/> instead.</summary>
    private const string UncategorizedWheelDeviceName = "Uncategorized Wheel Device";

    [RelayCommand]
    public void AddGamepadMapping() => AddUncategorizedMapping(isWheel: false);

    [RelayCommand]
    public void AddWheelMapping() => AddUncategorizedMapping(isWheel: true);

    /// <summary>
    /// Adds a brand-new, not-yet-mapped entry to whichever section (<see cref="AddGamepadMapping"/>
    /// vs <see cref="AddWheelMapping"/>) its "+ Add Input" button was clicked from - it shows
    /// up under an "Uncategorized ..." group in that section only, never guessing a real
    /// connected device the way this used to (which is what caused a brand-new mapping to land
    /// under an arbitrary already-mapped device's group, sometimes in the wrong section
    /// entirely). Click-to-Map on the resulting row still works exactly as before.
    /// </summary>
    private void AddUncategorizedMapping(bool isWheel)
    {
        Mappings.Add(new MappingEntry
        {
            SourceDeviceName = isWheel ? UncategorizedWheelDeviceName : UncategorizedGamepadDeviceName,
            SourceDisplayName = "Click to Map",
            TargetDeviceId = 1,
            TargetOutput = isWheel ? "[Wheel] Gas / Throttle (Axis Y)" : "[Xbox] Xbox A (Cross / South / Handbrake)"
        });
        RebuildGroupedMappings();
        UpdateVirtualDeviceSummary();
        AutoSaveCurrentProfile();
    }

    [RelayCommand]
    public void RemoveMapping(MappingEntry entry)
    {
        if (ListeningEntry == entry) StopListening();
        Mappings.Remove(entry);
        RebuildGroupedMappings();
        UpdateVirtualDeviceSummary();
        AutoSaveCurrentProfile();
    }

    public void RefreshDependencyStates()
    {
        var viGEm = _dependencyManager.CheckViGEmBus();
        var hidHide = _dependencyManager.CheckHidHide();
        var vJoy = _dependencyManager.CheckVJoy();

        IsViGEmInstalled = viGEm.IsInstalled;
        IsViGEmRunning = viGEm.IsInstalled;

        IsHidHideServiceInstalled = hidHide.IsInstalled;
        IsHidHideServiceRunning = hidHide.IsInstalled;

        IsVJoyInstalled = vJoy.IsInstalled;
        IsVJoyRunning = vJoy.IsInstalled;

        IsDependencyBannerOpen = HasMissingDependencies;
        OnPropertyChanged(nameof(HasMissingDependencies));
    }

    [RelayCommand]
    public async Task InstallDriver(string driverName)
    {
        if (IsBusyWithDriverAction || !Enum.TryParse<DriverType>(driverName, true, out var driver)) return;

        IsBusyWithDriverAction = true;
        var progress = new Progress<string>(msg => DriverInstallProgressText = msg);

        await _dependencyManager.InstallDriverAsync(driver, progress);

        RefreshDependencyStates();
        DriverInstallProgressText = string.Empty;
        IsBusyWithDriverAction = false;
    }

    [RelayCommand]
    public async Task UninstallDriver(string driverName)
    {
        if (IsBusyWithDriverAction || !Enum.TryParse<DriverType>(driverName, true, out var driver)) return;

        IsBusyWithDriverAction = true;
        DriverInstallProgressText = $"Uninstalling {driverName}...";

        await _dependencyManager.UninstallDriverAsync(driver);

        RefreshDependencyStates();
        DriverInstallProgressText = string.Empty;
        IsBusyWithDriverAction = false;
    }

    [RelayCommand]
    public async Task InstallViGEm() => await InstallDriver("ViGEmBus");

    [RelayCommand]
    public async Task InstallHidHide() => await InstallDriver("HidHide");

    [RelayCommand]
    public async Task InstallVJoy() => await InstallDriver("VJoy");

    [RelayCommand]
    public void DismissDependencyBanner() => IsDependencyBannerOpen = false;

    [RelayCommand]
    public void ResetAllSettings()
    {
        _currentSettings = new ECCR.Models.AppSettings();
        SaveAppSettings();

        _profileService.LoadProfileListFromDisk();
        SelectedProfile = Profiles.FirstOrDefault() ?? "Default";
        _profileService.SelectProfile(SelectedProfile);

        RefreshDependencyStates();
    }

    private void OnMappingsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (MappingEntry item in e.NewItems)
            {
                item.PropertyChanged += OnMappingEntryPropertyChanged;

                // A newly-added entry can already have its device GUID set via object
                // initializer (AddMappingToDevice, the Auto-Map wizard) - that never raises
                // PropertyChanged, so OnMappingEntryPropertyChanged's cloak hook alone would
                // miss it. Skipped during profile load: SyncHidHideCloakingForAllMappings()
                // already covers that in one deduplicated pass after the whole profile is in.
                if (!_profileService.IsLoading && item.SourceDeviceGuid != Guid.Empty)
                {
                    HidHide.EnsureDeviceCloaked(item.SourceDeviceGuid, item.SourceDeviceName);
                }
            }
        }
        if (e.OldItems != null) foreach (MappingEntry item in e.OldItems) item.PropertyChanged -= OnMappingEntryPropertyChanged;
        SyncWheelForceFeedbackMappings();
        SyncXInputFfbSynthesisMappings();
    }

    private void OnMappingEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MappingEntry.LiveRawPercentage) ||
            e.PropertyName == nameof(MappingEntry.LiveOutputPercentage) ||
            e.PropertyName == nameof(MappingEntry.LatestRawReading) ||
            e.PropertyName == nameof(MappingEntry.IsSelected)) return;

        if (e.PropertyName == nameof(MappingEntry.SourceDeviceName)) RebuildGroupedMappings();
        if (e.PropertyName == nameof(MappingEntry.TargetDeviceId)) UpdateVirtualDeviceSummary();

        if (e.PropertyName is nameof(MappingEntry.TargetOutput) or nameof(MappingEntry.TargetDeviceId)
            or nameof(MappingEntry.SourceDeviceGuid) or nameof(MappingEntry.SourceDeviceName))
        {
            SyncWheelForceFeedbackMappings();
            SyncXInputFfbSynthesisMappings();
        }

        // A device becoming a mapping source is exactly when it needs to stop being visible to
        // other apps (games) - otherwise a game can see both the raw device and ECCR's virtual
        // output at once, which reads as doubled/fighting input. This can fire from the
        // DirectInput polling thread (see ProcessInputPolling's SourceDeviceGuid re-pin), so
        // hop to the UI thread before touching HidHide/its observable device list.
        if (e.PropertyName == nameof(MappingEntry.SourceDeviceGuid) && sender is MappingEntry { SourceDeviceGuid: var guid } changedEntry && guid != Guid.Empty)
        {
            Dispatcher.UIThread.Post(() => HidHide.EnsureDeviceCloaked(guid, changedEntry.SourceDeviceName));
        }

        AutoSaveCurrentProfile();
    }

    /// <summary>
    /// There's no explicit "Save Profile" step for most edits - almost every mutation to
    /// <see cref="Mappings"/> or a mapping's properties calls this. The loading guards exist
    /// because both profile load and settings load populate/mutate observable
    /// collections/properties that would otherwise re-trigger this and overwrite the file
    /// that's still mid-read with (at that point) incomplete in-memory data.
    /// </summary>
    private void AutoSaveCurrentProfile()
    {
        if (_profileService.IsLoading || _isLoadingSettings || string.IsNullOrWhiteSpace(SelectedProfile)) return;
        _profileService.SaveCurrentProfile(SelectedProfile);
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

                    // Source from the actual registry Run key, not the cached JSON flag - if the
                    // key write ever silently failed, or the user removed it via Task Manager's
                    // Startup tab, the toggle would otherwise keep lying about the real state.
                    StartWithWindows = StartupManager.IsStartupEnabled();
                    if (StartWithWindows)
                    {
                        // Re-register so the stored command line always points at this process's
                        // current exe path, in case a Velopack update moved it since the entry
                        // was last written.
                        StartupManager.SetStartup(true);
                    }
                    RunInSystemTray = settings.RunInSystemTray;
                    MinimizeToTray = settings.MinimizeToTray;
                    CloseMinimizesToTray = settings.CloseMinimizesToTray;
                    AutoCheckForUpdates = settings.AutoCheckForUpdates;
                    ShowChangelogOnUpdate = settings.ShowChangelogOnUpdate;

                    // Must run before IsVirtualOutputActive is assigned below: that assignment
                    // fires OnIsVirtualOutputActiveChanged -> HidHide.OnVirtualDevicesToggled,
                    // which needs HidHide's own settings (IsHidHideActive, LinkCloakingToVirtualDevices)
                    // already loaded rather than still at their default-constructed values.
                    HidHide.ApplySettings(settings);

                    IsVirtualOutputActive = settings.IsVirtualOutputActive;
                    _feeder.SetActive(IsVirtualOutputActive);

                    IsSidebarCollapsed = settings.IsSidebarCollapsed;
                    ControllerSkin = settings.ControllerSkin;
                    IsAutoSwitchingEnabled = settings.AutoSwitchingEnabled;
                    EnableProfileSwitchNotifications = settings.EnableProfileSwitchNotifications;

                    GyroAimEnabled = settings.GyroAimEnabled;
                    GyroAimSourceDeviceName = settings.GyroAimSourceDeviceName;
                    GyroAimTargetPlayer = settings.GyroAimTargetPlayer;
                    TouchAimEnabled = settings.TouchAimEnabled;
                    TouchAimSourceDeviceName = settings.TouchAimSourceDeviceName;
                    TouchAimTargetPlayer = settings.TouchAimTargetPlayer;

                    DefaultProfileName = settings.DefaultProfileName;

                    // A pinned default profile wins at startup over "whatever was last selected" -
                    // manual switching mid-session is untouched and still always updates
                    // LastActiveProfile (OnSelectedProfileChanged), so removing the default later
                    // falls back to last-used exactly as it did before DefaultProfileName existed.
                    if (!string.IsNullOrWhiteSpace(settings.DefaultProfileName) && Profiles.Contains(settings.DefaultProfileName))
                    {
                        SelectedProfile = settings.DefaultProfileName;
                    }
                    else if (!string.IsNullOrWhiteSpace(settings.LastActiveProfile) && Profiles.Contains(settings.LastActiveProfile))
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
            _currentSettings.DefaultProfileName = DefaultProfileName;
            _currentSettings.LastRunVersion = AppVersion;
            _currentSettings.StartWithWindows = StartWithWindows;
            _currentSettings.RunInSystemTray = RunInSystemTray;
            _currentSettings.MinimizeToTray = MinimizeToTray;
            _currentSettings.CloseMinimizesToTray = CloseMinimizesToTray;
            _currentSettings.AutoCheckForUpdates = AutoCheckForUpdates;
            _currentSettings.ShowChangelogOnUpdate = ShowChangelogOnUpdate;
            _currentSettings.IsVirtualOutputActive = IsVirtualOutputActive;
            _currentSettings.IsSidebarCollapsed = IsSidebarCollapsed;
            _currentSettings.ControllerSkin = ControllerSkin;
            _currentSettings.AutoSwitchingEnabled = IsAutoSwitchingEnabled;
            _currentSettings.EnableProfileSwitchNotifications = EnableProfileSwitchNotifications;
            _currentSettings.GyroAimEnabled = GyroAimEnabled;
            _currentSettings.GyroAimSourceDeviceName = GyroAimSourceDeviceName;
            _currentSettings.GyroAimTargetPlayer = GyroAimTargetPlayer;
            _currentSettings.TouchAimEnabled = TouchAimEnabled;
            _currentSettings.TouchAimSourceDeviceName = TouchAimSourceDeviceName;
            _currentSettings.TouchAimTargetPlayer = TouchAimTargetPlayer;
            HidHide.WriteSettings(_currentSettings);

            File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(_currentSettings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    partial void OnSelectedProfileChanged(string value)
    {
        if (_isLoadingSettings) return;
        SaveAppSettings();
        _profileService.SelectProfile(value);
    }

    partial void OnDefaultProfileNameChanged(string? value)
    {
        if (_isLoadingSettings) return;
        SaveAppSettings();
    }

    [RelayCommand]
    public void SaveCurrentProfile()
    {
        if (!string.IsNullOrWhiteSpace(SelectedProfile)) _profileService.SaveCurrentProfile(SelectedProfile);
    }

    /// <summary>Drives the Create-Profile overlay's title so the one dialog reads correctly for both flows it now serves.</summary>
    public string CreateProfileDialogTitle => _duplicateSourceProfileName != null ? "Duplicate Profile" : "Create New Profile";

    /// <summary>Drives the overlay's confirm button label - same "one dialog, two flows" reasoning as <see cref="CreateProfileDialogTitle"/>.</summary>
    public string ConfirmProfileDialogButtonText => _duplicateSourceProfileName != null ? "Duplicate & Save" : "Create & Save";

    private void RaiseCreateProfileDialogTextChanged()
    {
        OnPropertyChanged(nameof(CreateProfileDialogTitle));
        OnPropertyChanged(nameof(ConfirmProfileDialogButtonText));
    }

    [RelayCommand]
    public void OpenCreateProfileDialog()
    {
        _duplicateSourceProfileName = null;
        CustomProfileInput = string.Empty;
        IsCreatingProfile = true;
        RaiseCreateProfileDialogTextChanged();
    }

    /// <summary>Opens the same overlay as <see cref="OpenCreateProfileDialog"/>, tagged as a duplicate of <paramref name="sourceName"/> - used by the Profiles page's per-card "Duplicate" action, which (unlike the toolbar's New button) needs to clone a specific card, not necessarily the currently-selected profile.</summary>
    [RelayCommand]
    public void OpenDuplicateProfileDialog(string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName)) return;
        _duplicateSourceProfileName = sourceName;
        CustomProfileInput = string.Empty;
        IsCreatingProfile = true;
        RaiseCreateProfileDialogTextChanged();
    }

    [RelayCommand]
    public void CancelCreateProfile()
    {
        IsCreatingProfile = false;
        CustomProfileInput = string.Empty;
        _duplicateSourceProfileName = null;
    }

    [RelayCommand]
    public void ConfirmCreateProfile()
    {
        if (string.IsNullOrWhiteSpace(CustomProfileInput)) return;
        string cleanName = CustomProfileInput.Trim();

        if (_duplicateSourceProfileName != null)
        {
            _profileService.DuplicateProfile(_duplicateSourceProfileName, cleanName);
            _duplicateSourceProfileName = null;
            IsCreatingProfile = false;
            CustomProfileInput = string.Empty;
            return;
        }

        _profileService.RegisterName(cleanName);

        // Setting SelectedProfile triggers OnSelectedProfileChanged -> ProfileService.SelectProfile,
        // which (since no file exists yet for a genuinely new name) seeds a placeholder mapping
        // via the apply-callback's fallback branch - preserved from the original behavior here,
        // not a "clone current state" flow.
        SelectedProfile = cleanName;
        _profileService.SaveCurrentProfile(cleanName);
        SaveAppSettings();
        IsCreatingProfile = false;
        CustomProfileInput = string.Empty;
    }

    [RelayCommand]
    public void DeleteCurrentProfile() => DeleteProfile(SelectedProfile);

    /// <summary>Backs both the toolbar's Delete button (always the current profile) and the Profiles page's per-card Delete action (any profile). Refuses the last remaining profile (existing guard) and the pinned <see cref="DefaultProfileName"/> (new guard - "prevent deletion of the active fallback default").</summary>
    public void DeleteProfile(string name)
    {
        if (Profiles.Count <= 1) return;
        if (!string.IsNullOrEmpty(DefaultProfileName) && name == DefaultProfileName) return;

        _profileService.DeleteProfile(name);
        if (name == SelectedProfile) SelectedProfile = Profiles.FirstOrDefault() ?? "Default";
    }

    /// <summary>New: no equivalent existed before this overhaul.</summary>
    [RelayCommand]
    public void RenameSelectedProfile(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        string cleanNew = newName.Trim();
        _profileService.RenameProfile(SelectedProfile, cleanNew);
        if (Profiles.Contains(cleanNew)) SelectedProfile = cleanNew;
        SaveAppSettings();
    }

    /// <summary>New: no equivalent existed before this overhaul.</summary>
    [RelayCommand]
    public void DuplicateSelectedProfile(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        _profileService.DuplicateProfile(SelectedProfile, newName.Trim());
    }

    /// <summary>
    /// Reconciles HidHide's live blocklist against every mapping's already-resolved device
    /// GUID right after a profile loads - <see cref="OnMappingEntryPropertyChanged"/>'s
    /// per-change hook only fires when <c>SourceDeviceGuid</c> actually changes, which a
    /// profile round-tripping the same (still-valid) GUID never does, so a freshly-loaded
    /// profile's devices wouldn't otherwise get cloaked until each one happened to reconnect
    /// with a new GUID.
    /// </summary>
    private void SyncHidHideCloakingForAllMappings()
    {
        foreach (var guid in Mappings.Where(m => m.SourceDeviceGuid != Guid.Empty).Select(m => m.SourceDeviceGuid).Distinct())
        {
            var entry = Mappings.First(m => m.SourceDeviceGuid == guid);
            HidHide.EnsureDeviceCloaked(guid, entry.SourceDeviceName);
        }
    }

    /// <summary>
    /// One-shot migration for profiles saved before wheel rotation became a device-level
    /// setting (<see cref="WheelDeviceSettings.RotationDegrees"/>): the old per-mapping
    /// <c>MappingEntry.WheelRotationDegrees</c> field is now display-only, superseded by the
    /// per-device value. If a steering mapping's legacy value was actually customized (not the
    /// 900 default) and the device's new setting is still at its own default, seed the device
    /// setting from the mapping's old value - first steering entry found for a device wins,
    /// since rotation is a whole-wheel property and a profile only ever has one meaningful
    /// steering mapping per wheel (same assumption <see cref="SyncWheelForceFeedbackMappings"/>
    /// already makes). No-op on every subsequent load once the device setting has been touched.
    /// </summary>
    private void MigrateLegacyWheelRotationDegrees()
    {
        foreach (var entry in Mappings)
        {
            if (!entry.TargetOutput.Contains("Steering", StringComparison.OrdinalIgnoreCase)) continue;
#pragma warning disable CS0618 // Type or member is obsolete - this is the one-shot migration reading it
            if (entry.WheelRotationDegrees == 900) continue;
            var settings = GetOrCreateWheelSettings(entry.SourceDeviceName);
            if (settings.RotationDegrees == 900)
            {
                settings.RotationDegrees = entry.WheelRotationDegrees;
            }
#pragma warning restore CS0618
        }
    }

    /// <summary>Swaps in a freshly-deserialized <see cref="WheelSettings"/> dictionary, re-subscribing each entry the same way <see cref="GetOrCreateWheelSettings"/> does for one created on demand.</summary>
    private void LoadWheelSettings(Dictionary<string, WheelDeviceSettings> settings)
    {
        foreach (var existing in WheelSettings.Values) existing.PropertyChanged -= OnWheelSettingsPropertyChanged;

        WheelSettings = settings;
        foreach (var entry in WheelSettings.Values) entry.PropertyChanged += OnWheelSettingsPropertyChanged;

        SyncWheelForceFeedbackMappings();
        SyncXInputFfbSynthesisMappings();
    }

    /// <summary>
    /// Restores the just-loaded profile's saved LED color into the Controller LED page, and
    /// best-effort re-applies it to a connected supported device (e.g. so switching from a
    /// "Racing" profile to a "Default" profile also switches the lightbar color) - failure here
    /// (no supported device connected, HID write error) never blocks profile loading itself.
    /// </summary>
    private void ApplyProfileLedColor(byte r, byte g, byte b, LedEffectMode mode = LedEffectMode.Static, double speed = 1.0)
    {
        if (_controllerLedViewModel == null) return;

        _controllerLedViewModel.SetColorFromProfile(r, g, b, mode, speed);

        try
        {
            var device = StatusBar.GamepadStatuses.FirstOrDefault(d => d.IsLedSupported);
            if (device != null)
            {
                _controllerHidService.SetLightbarColor(device.VendorId, device.ProductId, r, g, b);
            }
        }
        catch { }
    }

    /// <summary>
    /// Full teardown for a real app exit (not a minimize-to-tray hide - see <c>MainWindow.OnClosing</c>):
    /// persists whatever the user's settings currently are, then live-disables everything that
    /// would otherwise keep affecting the rest of the system after the process is gone - virtual
    /// output devices (vJoy/ViGEm, via <see cref="_feeder"/>/<see cref="_wheelForceFeedback"/>'s
    /// own Dispose) and HidHide cloaking (<see cref="HidHideViewModel.DisableCloakingForShutdown"/>,
    /// which only flips the driver's live master switch, not the saved preference) so every
    /// physical device is visible and every virtual one is gone the moment ECCR exits. Settings
    /// are saved first so this live-disable step never gets persisted as the user's actual
    /// preference - both re-apply automatically via <see cref="LoadAppSettings"/> on next launch.
    /// </summary>
    private static readonly string ShutdownLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECCR", "shutdown-debug.log");

    /// <summary>
    /// Each <see cref="CleanupAndShutdown"/> step is independently guarded so a failure in one
    /// (e.g. settings serialization) can never skip the driver-teardown steps that follow it.
    /// </summary>
    private static void RunShutdownStep(string name, Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ShutdownLogPath)!);
                File.AppendAllText(ShutdownLogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {name} threw: {ex.GetType().Name}: {ex.Message}\n");
            }
            catch { }
        }
    }

    /// <summary>
    /// Ordered to match the requested teardown sequence: stop every background
    /// worker/poller/hook first (so nothing still calls into a handle being torn down below),
    /// then disconnect/dispose the virtual devices (ViGEm/vJoy/DS4, via <see cref="_feeder"/>),
    /// then unacquire the physical DirectInput handles (<see cref="_deviceManager"/>'s own
    /// <c>Dispose</c> already calls <c>StopPolling</c> too - calling it explicitly first here just
    /// makes the polling loop stop before virtual-device teardown runs instead of only stopping
    /// as a side effect of a later step), then restore HidHide last so physical devices become
    /// visible to other apps only once ECCR itself is done touching them. Each step stays
    /// independently guarded by <see cref="RunShutdownStep"/> so one failure can never skip the
    /// steps after it.
    /// </summary>
    public void CleanupAndShutdown()
    {
        RunShutdownStep(nameof(SaveAppSettings), SaveAppSettings);
        RunShutdownStep(nameof(AutoSaveCurrentProfile), AutoSaveCurrentProfile);

        // 1. Stop background workers/pollers/hooks.
        RunShutdownStep("DeviceManager.StopPolling", () => _deviceManager?.StopPolling());
        RunShutdownStep("GameProcessWatcher.Dispose", () => _gameProcessWatcher?.Dispose());
        RunShutdownStep("XInputStatusTimer.Stop", () => _xInputStatusTimer?.Stop());
        RunShutdownStep("ControllerHidService.ReleaseTriggerEffects", () => _controllerHidService.ReleaseTriggerEffects());
        RunShutdownStep("ControllerHidService.Dispose", () => _controllerHidService.Dispose());
        RunShutdownStep("ControllerLedViewModel.Dispose", () => _controllerLedViewModel?.Dispose());

        // 2. Disconnect virtual devices and release the ViGEm/vJoy/DS4 clients.
        RunShutdownStep("Feeder.Dispose", () => _feeder.Dispose());

        // 3. Unacquire/dispose physical DirectInput handles, including the wheel's exclusive FFB handle.
        RunShutdownStep("DeviceManager.Dispose", () => _deviceManager?.Dispose());
        RunShutdownStep("WheelForceFeedback.Dispose", () => _wheelForceFeedback.Dispose());

        // 4. Restore HidHide last - only once every native handle above is already gone.
        RunShutdownStep("HidHide.DisableCloakingForShutdown", () => HidHide.DisableCloakingForShutdown());

        RunShutdownStep("WinmmTimer release", () => _highResTimerHandle?.Dispose());
    }
}