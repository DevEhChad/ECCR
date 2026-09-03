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
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ECCR.Models;
using ECCR.Services;
using ECCR.Views;
using Velopack;

namespace ECCR.ViewModels;

/// <summary>
/// The app's central view model and the hub everything else plugs into. Owns the live
/// <see cref="Mappings"/> list (the single source of truth for every axis/button binding),
/// drives the DirectInput poll loop via <see cref="InputDeviceManager"/> and feeds calibrated
/// values to <see cref="IVirtualFeeder"/>, and hosts <see cref="HidHide"/> /
/// <see cref="AutoBind"/> as sub-view-models that share this instance's device list and
/// mapping collection rather than owning their own. Persistence is split two ways: app-wide
/// preferences live in <c>%AppData%/ECCR/settings.json</c> (<see cref="SaveAppSettings"/> /
/// <see cref="LoadAppSettings"/>), while the actual bindings for the active profile live in
/// <c>%AppData%/ECCR/Profiles/{name}.json</c> (<see cref="SaveProfileToDisk"/> /
/// <see cref="LoadSelectedProfile"/>) and are auto-saved on essentially every edit via
/// <see cref="AutoSaveCurrentProfile"/>.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly InputDeviceManager? _deviceManager;
    private readonly DependencyManager _dependencyManager = new();
    private readonly IVirtualFeeder _feeder = new CompositeFeederService();
    private readonly WheelForceFeedbackService _wheelForceFeedback = new();
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

    public ObservableCollection<string> Profiles { get; } = new();
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

    /// <summary>Auto-Bind wizard state and preset generation.</summary>
    public AutoBindWizardViewModel AutoBind { get; }

    /// <summary>Persistent top device-status bar state (battery/connection for gamepad-class devices), visible on every page.</summary>
    public DeviceStatusBarViewModel StatusBar { get; } = new();

    private readonly ControllerHidService _controllerHidService = new();
    private readonly Dictionary<AppPage, ViewModelBase> _pageViewModels = new();
    private ControllerLedViewModel? _controllerLedViewModel;
    private ControllerMappingViewModel? _controllerMappingViewModel;
    private DeviceMappingViewModel? _deviceMappingViewModel;

    // Shell navigation
    [ObservableProperty]
    private AppPage _currentPage = AppPage.Home;

    [ObservableProperty]
    private ViewModelBase? _currentPageViewModel;

    [ObservableProperty]
    private bool _isSidebarCollapsed = false;

    public ObservableCollection<NavItem> PrimaryNavItems { get; } = new();
    public ObservableCollection<NavItem> UtilityNavItems { get; } = new();

    private MappingEntry? _listeningEntry;

    /// <summary>
    /// Fired once per physical device per poll tick with a live, calibration-independent
    /// snapshot of its raw button/stick state - purely for UI animation (the Visual Binder's
    /// moving stick graphics and pressed-button highlights), not consumed by the feeder path.
    /// </summary>
    public event Action<LiveDeviceInputState>? OnLiveDeviceStateUpdated;
    private string _preListenDisplayName = "Click to Bind";
    private DispatcherTimer? _listenTimeoutTimer;
    private readonly Dictionary<Guid, int[]> _axisBaselines = new();

    /// <summary>
    /// Rotation/force-feedback tuning per wheel device name, keyed and persisted the same way
    /// as <see cref="UserProfile.WheelSettings"/> (round-tripped in <see cref="SaveProfileToDisk"/>/
    /// <see cref="LoadSelectedProfile"/>). Entries are created lazily via
    /// <see cref="GetOrCreateWheelSettings"/> - the Wheel Settings tab (<see cref="DeviceMappingViewModel"/>)
    /// is the only consumer today.
    /// </summary>
    public Dictionary<string, WheelDeviceSettings> WheelSettings { get; private set; } = new();

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
        SyncWheelForceFeedbackMappings();
        AutoSaveCurrentProfile();
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
            AutoBind = new AutoBindWizardViewModel();
            return;
        }

        bool isFirstRun = !File.Exists(_settingsFilePath);

        Directory.CreateDirectory(_profilesDirectory);

        ResolveAppVersion();
        Mappings.CollectionChanged += OnMappingsCollectionChanged;

        HidHide = new HidHideViewModel(() => _currentSettings, SaveAppSettings);
        IsHidHideServiceInstalled = HidHide.IsDriverInstalled;

        AutoBind = new AutoBindWizardViewModel(
            ConnectedDevices,
            () => _deviceManager?.GetConnectedDevices() ?? new List<DeviceHardwareInfo>(),
            Mappings,
            PlayerTargets,
            WheelSlots,
            () =>
            {
                RebuildGroupedMappings();
                UpdateVirtualDeviceSummary();
                AutoSaveCurrentProfile();
            });

        InitializeShellNavigation();

        LoadProfileListFromDisk();
        LoadAppSettings();
        CheckPostUpdateStatus();

        _deviceManager = new InputDeviceManager();
        _deviceManager.OnDevicesRefreshed += devices =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                // Diff instead of unconditional Clear()+re-add: a name that's still present
                // must never be removed-then-re-added within the same tick, since
                // ConnectedDevices is two-way bound to AutoBind.WizardSelectedDevice (and other
                // pages) - a momentary empty collection would otherwise snap the user's
                // selection back to whatever ends up first in the rebuilt list.
                var newNames = devices.Select(d => d.InstanceName).ToList();
                for (int i = ConnectedDevices.Count - 1; i >= 0; i--)
                {
                    if (!newNames.Contains(ConnectedDevices[i])) ConnectedDevices.RemoveAt(i);
                }
                foreach (var name in newNames)
                {
                    if (!ConnectedDevices.Contains(name)) ConnectedDevices.Add(name);
                }

                if (ConnectedDevices.Count > 0 && string.IsNullOrEmpty(AutoBind.WizardSelectedDevice))
                {
                    AutoBind.WizardSelectedDevice = ConnectedDevices[0];
                }

                StatusBar.SyncConnectedDevices(devices);
            });
        };

        _deviceManager.OnInputPolled += ProcessInputPolling;

        _deviceManager.RefreshDevices();
        _pollStopwatch.Start();
        _deviceManager.StartPolling(pollIntervalMs: 4);

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

        // Forwards a game's rumble feedback (via either virtual pad type) to whichever
        // physical Sony device(s) are mapped into that player slot - the reverse direction of
        // the axis/button feed above.
        _feeder.RumbleReceived += (targetDeviceId, largeMotor, smallMotor) =>
        {
            var guids = Mappings
                .Where(m => m.TargetDeviceId == targetDeviceId && m.SourceDeviceGuid != Guid.Empty)
                .Select(m => m.SourceDeviceGuid)
                .Distinct();

            foreach (var guid in guids)
            {
                var (vendorId, productId) = ControllerHidService.GetVidPid(guid);
                _controllerHidService.SetRumble(vendorId, productId, largeMotor, smallMotor);
            }
        };

        LoadSelectedProfile();
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
    /// as <see cref="HidHide"/>/<see cref="AutoBind"/> themselves), the primary/utility nav
    /// item lists, and selects Home as the initial page.
    /// </summary>
    private void InitializeShellNavigation()
    {
        var homeIcon = LoadIcon("Full-HomeLogo.png");
        var devicesIcon = LoadIcon("Full-DevicesLogo.png");
        var controllerIcon = LoadIcon("Full-ControllerLogo.png");
        var wheelIcon = LoadIcon("Full-WheelLogo.png");
        var ledIcon = LoadIcon("Full-LEDLogo.png");
        var hidHideIcon = LoadIcon("Full-HidHide.png");

        _pageViewModels[AppPage.Home] = new HomeViewModel(StatusBar, BuildQuickActionCards(devicesIcon, hidHideIcon), NavigateToCommand);
        _pageViewModels[AppPage.ConnectedDevices] = new ConnectedDevicesPageViewModel(this, StatusBar.AllDeviceStatuses);
        _controllerMappingViewModel = new ControllerMappingViewModel(this, GamepadGroupedMappings);
        _pageViewModels[AppPage.ControllerMapping] = _controllerMappingViewModel;
        _deviceMappingViewModel = new DeviceMappingViewModel(this, WheelGroupedMappings);
        _pageViewModels[AppPage.DeviceMapping] = _deviceMappingViewModel;
        _controllerLedViewModel = new ControllerLedViewModel(StatusBar.GamepadStatuses, _controllerHidService, AutoSaveCurrentProfile);
        _pageViewModels[AppPage.ControllerLed] = _controllerLedViewModel;
        _pageViewModels[AppPage.Profiles] = new ProfilesViewModel(this);
        _pageViewModels[AppPage.HidHide] = HidHide;
        _pageViewModels[AppPage.AutoBind] = AutoBind;
        _pageViewModels[AppPage.About] = new AboutViewModel(AppVersion);
        _pageViewModels[AppPage.Settings] = new SettingsViewModel(this);

        PrimaryNavItems.Add(new NavItem(AppPage.Home, "Home", "🏠", iconImage: homeIcon));
        PrimaryNavItems.Add(new NavItem(AppPage.ConnectedDevices, "Connected Devices", "🕹️", iconImage: devicesIcon));
        PrimaryNavItems.Add(new NavItem(AppPage.ControllerMapping, "Controller Mapping", "🎯", iconImage: controllerIcon));
        PrimaryNavItems.Add(new NavItem(AppPage.DeviceMapping, "Device Mapping", "🗺️", iconImage: wheelIcon));
        PrimaryNavItems.Add(new NavItem(AppPage.ControllerLed, "Controller LED", "💡", iconImage: ledIcon));
        PrimaryNavItems.Add(new NavItem(AppPage.Profiles, "Profiles", "📄"));

        UtilityNavItems.Add(new NavItem(AppPage.HidHide, "HidHide", "🛡", iconImage: hidHideIcon));
        UtilityNavItems.Add(new NavItem(AppPage.AutoBind, "Auto Bind", "⚡"));
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
        new NavItem(AppPage.HidHide, "HidHide", "🛡", "Hide physical devices from games.", hidHideIcon),
        new NavItem(AppPage.AutoBind, "Auto Bind", "⚡", "Generate a starting set of bindings."),
        new NavItem(AppPage.Settings, "Settings", "⚙️", "Manage app settings."),
        new NavItem(AppPage.About, "About", "ℹ️", "Changelog and links.")
    };

    [RelayCommand]
    public void NavigateTo(AppPage page)
    {
        CurrentPage = page;
        CurrentPageViewModel = _pageViewModels[page];

        foreach (var item in PrimaryNavItems) item.IsSelected = item.Page == page;
        foreach (var item in UtilityNavItems) item.IsSelected = item.Page == page;

        // Replaces the old Open*Menu()-triggered refresh/preselect behavior now that HidHide
        // and Auto-Bind are always-present pages instead of explicitly opened dialogs.
        if (page == AppPage.HidHide)
        {
            HidHide.RefreshHidDevices();
            HidHide.RefreshHidApplications();
        }
        else if (page == AppPage.AutoBind)
        {
            AutoBind.PrepareForNavigation();
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

        var category = DevicePresetService.DetectCategory(deviceName);
        if (DevicePresetService.IsWheelOrPedalCategory(category))
        {
            if (_deviceMappingViewModel != null) _deviceMappingViewModel.SelectedDeviceName = deviceName;
            NavigateTo(AppPage.DeviceMapping);
        }
        else
        {
            if (_controllerMappingViewModel != null) _controllerMappingViewModel.SelectedDeviceName = deviceName;
            NavigateTo(AppPage.ControllerMapping);
        }
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
        foreach (var bind in xboxBinds) outputs.Add(bind);

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
        foreach (var bind in wheelBinds) outputs.Add(bind);

        for (int i = 1; i <= 32; i++) outputs.Add($"[Wheel] Button {i}");

        string[] ds4Binds = [
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
        foreach (var bind in ds4Binds) outputs.Add(bind);

        return outputs;
    }

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
    /// Fired once per physical device per DirectInput poll tick (every ~4ms, see
    /// <c>InputDeviceManager.StartPolling</c>). Runs in one of two mutually exclusive modes
    /// depending on <see cref="_listeningEntry"/>: normal feed-through (find every mapping
    /// bound to this device and push calibrated values to <see cref="_feeder"/>), or capture
    /// mode while the "Click to Bind" listener is armed (watch for the next button press or
    /// large axis movement on any device and bind that to the listening entry). The two
    /// never run in the same tick so a listening session can't also be feeding stale output.
    /// </summary>
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
            if (OnLiveDeviceStateUpdated != null)
            {
                var (lx, ly, rx, ry) = DevicePresetService.GetStickAxisIndices(state.DeviceName);
                var live = new LiveDeviceInputState
                {
                    DeviceName = state.DeviceName,
                    InstanceGuid = state.InstanceGuid,
                    Buttons = (bool[])state.Buttons.Clone(),
                    NormalizedAxes = state.Axes.Select(a => a / 65535.0).ToArray(),
                    HasSticks = lx >= 0,
                    LeftStickX = lx >= 0 ? state.Axes[lx] / 65535.0 : 0.5,
                    LeftStickY = ly >= 0 ? state.Axes[ly] / 65535.0 : 0.5,
                    RightStickX = rx >= 0 ? state.Axes[rx] / 65535.0 : 0.5,
                    RightStickY = ry >= 0 ? state.Axes[ry] / 65535.0 : 0.5,
                };
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
                    if (IsVirtualOutputActive && mapping.SourceIndex >= 0 && mapping.SourceIndex < state.Buttons.Length)
                    {
                        _feeder.UpdateButton(mapping.TargetDeviceId, mapping.TargetOutput, state.Buttons[mapping.SourceIndex]);
                    }
                }
            }
            return;
        }

        // Capture mode: the first poll after StartListening just records each axis's
        // current resting value as a baseline (rather than binding on it), since without a
        // baseline any already-off-zero axis would immediately "trigger" a bind the instant
        // listening starts.
        if (!_axisBaselines.TryGetValue(state.InstanceGuid, out var baselines))
        {
            _axisBaselines[state.InstanceGuid] = (int[])state.Axes.Clone();
            return;
        }

        // Buttons are checked before axes: a firm button press is an unambiguous signal,
        // while a wheel/pedal can drift enough on its own to cross the axis threshold below,
        // so preferring buttons avoids accidentally capturing axis noise when the user meant
        // to press a button that happens to share a poll tick with it.
        for (int i = 0; i < state.Buttons.Length; i++)
        {
            if (state.Buttons[i])
            {
                string btnLabel = DevicePresetService.GetEffectiveDisplayName(state.DeviceName, InputType.Button, i, GetOrCreateWheelSettings(state.DeviceName));
                AssignDetectedInput(_listeningEntry, state.InstanceGuid, state.DeviceName, InputType.Button, i, btnLabel);
                return;
            }
        }

        // 10000 out of a 0-65535 raw range (~15%) is the movement threshold for "the user is
        // actually moving this axis" vs. normal jitter on an unmoved one.
        for (int i = 0; i < state.Axes.Length; i++)
        {
            if (Math.Abs(state.Axes[i] - baselines[i]) > 10000)
            {
                string axisLabel = DevicePresetService.GetEffectiveDisplayName(state.DeviceName, InputType.Axis, i, GetOrCreateWheelSettings(state.DeviceName));
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
            UpdateVirtualDeviceSummary();
            StopListening();
            AutoSaveCurrentProfile();
        });
    }

    /// <summary>
    /// Sets (or, if <paramref name="nickname"/> is null/whitespace, clears) the user-editable
    /// nickname for one physical input, then refreshes <see cref="MappingEntry.SourceDisplayName"/>
    /// on every mapping entry bound to that same physical device+index - not just the one the
    /// user renamed from - so the new name shows up immediately everywhere that input appears
    /// (e.g. the same wheel button mapped to two different targets in two different mapping
    /// rows). Called from the rename Flyout in <see cref="ECCR.Views.Shared.DeviceMappingGroupsPanel"/>.
    /// </summary>
    public void RenameBoundInput(MappingEntry entry, string? nickname)
    {
        var settings = GetOrCreateWheelSettings(entry.SourceDeviceName);
        settings.SetNickname(entry.SourceType, entry.SourceIndex, nickname);

        string effectiveName = DevicePresetService.GetEffectiveDisplayName(entry.SourceDeviceName, entry.SourceType, entry.SourceIndex, settings);
        foreach (var m in Mappings.Where(m =>
                     string.Equals(m.SourceDeviceName, entry.SourceDeviceName, StringComparison.OrdinalIgnoreCase) &&
                     m.SourceType == entry.SourceType &&
                     m.SourceIndex == entry.SourceIndex))
        {
            m.SourceDisplayName = effectiveName;
        }
    }

    /// <summary>
    /// Picks a default target channel the instant an axis/button is captured via
    /// "Click to Bind" (see <see cref="AssignDetectedInput"/>), so a new binding is never
    /// left pointed at nothing before the user gets a chance to change it. This is a
    /// separate, simpler per-index guess from the full <see cref="DevicePresetService.GeneratePreset"/>
    /// table the Auto-Bind Wizard uses - it only needs one reasonable answer per index
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

        bool isSimHardware = DevicePresetService.IsWheelOrPedalCategory(category);

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

    /// <summary>True while a "Click to Bind" capture is armed - drives Esc/Backspace routing in <c>MainWindow.OnKeyDown</c> and the hint banner's active state.</summary>
    public bool IsListening => _listeningEntry != null;

    /// <summary>
    /// Arms (or, if already armed for this entry, cancels) capture mode - see
    /// <see cref="ProcessInputPolling"/> for what happens on the polling thread while armed.
    /// Bound to each mapping row's physical-input button in the main grid. Auto-cancels after
    /// 7 seconds of no input (<see cref="_listenTimeoutTimer"/>); Esc (<see cref="CancelListening"/>)
    /// and Backspace (<see cref="ClearListeningBinding"/>) are routed in from <c>MainWindow.OnKeyDown</c>
    /// while <see cref="IsListening"/> is true.
    /// </summary>
    [RelayCommand]
    public void StartListening(MappingEntry entry)
    {
        if (_listeningEntry == entry)
        {
            CancelListening();
            return;
        }

        if (_listeningEntry != null) CancelListening();

        _preListenDisplayName = entry.SourceDisplayName;
        _axisBaselines.Clear();
        entry.SourceDisplayName = "Listening... (Esc to cancel, Backspace to clear, 7s timeout)";
        MappingStatus = $"Listening ({entry.TargetOutput})";
        _listeningEntry = entry;
        OnPropertyChanged(nameof(IsListening));

        _listenTimeoutTimer?.Stop();
        _listenTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _listenTimeoutTimer.Tick += (_, _) => CancelListening();
        _listenTimeoutTimer.Start();
    }

    /// <summary>Aborts an in-progress capture without touching the entry's existing binding - restores whatever label it showed before listening started.</summary>
    public void CancelListening()
    {
        if (_listeningEntry == null) return;
        _listeningEntry.SourceDisplayName = _preListenDisplayName;
        StopListening();
    }

    /// <summary>Wipes the currently-listening entry back to a fully unbound state.</summary>
    public void ClearListeningBinding()
    {
        if (_listeningEntry == null) return;
        var entry = _listeningEntry;

        // Falls back into whichever "Uncategorized ..." group matches the section the entry
        // was already in (by its old device name's category) rather than always landing in the
        // gamepad section - clearing a wheel binding should keep it visible on the wheel page.
        bool wasWheel = DevicePresetService.IsWheelOrPedalCategory(DevicePresetService.DetectCategory(entry.SourceDeviceName));
        entry.SourceDeviceName = wasWheel ? UncategorizedWheelDeviceName : UncategorizedGamepadDeviceName;
        entry.SourceDeviceGuid = Guid.Empty;
        entry.SourceIndex = 0;
        entry.SourceDisplayName = "Click to Bind";
        StopListening();
        RebuildGroupedMappings();
        AutoSaveCurrentProfile();
    }

    private void StopListening()
    {
        _listenTimeoutTimer?.Stop();
        _listenTimeoutTimer = null;
        _listeningEntry = null;
        _axisBaselines.Clear();
        MappingStatus = IsVirtualOutputActive ? "Active" : "Standby (Disabled)";
        OnPropertyChanged(nameof(IsListening));
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
            if (_listeningEntry == item) StopListening();
            Mappings.Remove(item);
        }

        RebuildGroupedMappings();
        UpdateVirtualDeviceSummary();
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
            if (_listeningEntry == item) StopListening();
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
        var targetDevice = _deviceManager?.GetConnectedDevices().FirstOrDefault(d => d.InstanceName == deviceName);
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
        UpdateVirtualDeviceSummary();
        AutoSaveCurrentProfile();
    }

    /// <summary>
    /// Placeholder "device name" for a mapping that hasn't been bound to a real physical device
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
    /// Adds a brand-new, not-yet-bound mapping to whichever section (<see cref="AddGamepadMapping"/>
    /// vs <see cref="AddWheelMapping"/>) its "+ New Binding" button was clicked from - it shows
    /// up under an "Uncategorized ..." group in that section only, never guessing a real
    /// connected device the way this used to (which is what caused a brand-new binding to land
    /// under an arbitrary already-mapped device's group, sometimes in the wrong section
    /// entirely). Click-to-Bind on the resulting row still works exactly as before.
    /// </summary>
    private void AddUncategorizedMapping(bool isWheel)
    {
        Mappings.Add(new MappingEntry
        {
            SourceDeviceName = isWheel ? UncategorizedWheelDeviceName : UncategorizedGamepadDeviceName,
            SourceDisplayName = "Click to Bind",
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
        if (_listeningEntry == entry) StopListening();
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

        Profiles.Clear();
        LoadProfileListFromDisk();
        SelectedProfile = Profiles.FirstOrDefault() ?? "Default";
        LoadSelectedProfile();

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
                // initializer (AddMappingToDevice, the Auto-Bind wizard) - that never raises
                // PropertyChanged, so OnMappingEntryPropertyChanged's cloak hook alone would
                // miss it. Skipped during profile load: SyncHidHideCloakingForAllMappings()
                // already covers that in one deduplicated pass after the whole profile is in.
                if (!_isLoadingProfile && item.SourceDeviceGuid != Guid.Empty)
                {
                    HidHide.EnsureDeviceCloaked(item.SourceDeviceGuid, item.SourceDeviceName);
                }
            }
        }
        if (e.OldItems != null) foreach (MappingEntry item in e.OldItems) item.PropertyChanged -= OnMappingEntryPropertyChanged;
        SyncWheelForceFeedbackMappings();
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

                    GyroAimEnabled = settings.GyroAimEnabled;
                    GyroAimSourceDeviceName = settings.GyroAimSourceDeviceName;
                    GyroAimTargetPlayer = settings.GyroAimTargetPlayer;
                    TouchAimEnabled = settings.TouchAimEnabled;
                    TouchAimSourceDeviceName = settings.TouchAimSourceDeviceName;
                    TouchAimTargetPlayer = settings.TouchAimTargetPlayer;

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
            _currentSettings.ShowChangelogOnUpdate = ShowChangelogOnUpdate;
            _currentSettings.IsVirtualOutputActive = IsVirtualOutputActive;
            _currentSettings.IsSidebarCollapsed = IsSidebarCollapsed;
            _currentSettings.ControllerSkin = ControllerSkin;
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
                Mappings = Mappings.ToList(),
                LedRed = (byte)(_controllerLedViewModel?.Red ?? 0),
                LedGreen = (byte)(_controllerLedViewModel?.Green ?? 122),
                LedBlue = (byte)(_controllerLedViewModel?.Blue ?? 255),
                LedMode = _controllerLedViewModel?.EffectMode ?? LedEffectMode.Static,
                LedSpeed = _controllerLedViewModel?.EffectSpeed ?? 1.0,
                WheelSettings = WheelSettings
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
                    UpdateVirtualDeviceSummary();
                    ApplyProfileLedColor(profile.LedRed, profile.LedGreen, profile.LedBlue, profile.LedMode, profile.LedSpeed);
                    LoadWheelSettings(profile.WheelSettings);
                    MigrateLegacyWheelRotationDegrees();
                    SyncHidHideCloakingForAllMappings();
                    return;
                }
            }

            Mappings.Clear();
            AddUncategorizedMapping(isWheel: false);
            RebuildGroupedMappings();
            UpdateVirtualDeviceSummary();
            ApplyProfileLedColor(0, 122, 255, LedEffectMode.Static, 1.0);
            LoadWheelSettings(new Dictionary<string, WheelDeviceSettings>());
        }
        finally
        {
            _isLoadingProfile = false;
        }
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

    public void CleanupAndShutdown()
    {
        RunShutdownStep(nameof(SaveAppSettings), SaveAppSettings);
        RunShutdownStep(nameof(AutoSaveCurrentProfile), AutoSaveCurrentProfile);

        RunShutdownStep("HidHide.DisableCloakingForShutdown", () => HidHide.DisableCloakingForShutdown());

        RunShutdownStep("DeviceManager teardown", () =>
        {
            _deviceManager?.StopPolling();
            _deviceManager?.Dispose();
        });
        RunShutdownStep("ControllerHidService.Dispose", () => _controllerHidService.Dispose());
        RunShutdownStep("ControllerLedViewModel.Dispose", () => _controllerLedViewModel?.Dispose());
        RunShutdownStep("Feeder.Dispose", () => _feeder.Dispose());
        RunShutdownStep("WheelForceFeedback.Dispose", () => _wheelForceFeedback.Dispose());
    }
}