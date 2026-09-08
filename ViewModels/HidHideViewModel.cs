using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ECCR.Models;
using ECCR.Services;

namespace ECCR.ViewModels;

/// <summary>
/// Owns every piece of HidHide device-cloaking state and behaviour: the underlying
/// <see cref="HidHideManager"/>, the cloakable device list, the application whitelist and
/// the modal that surfaces them. Settings persistence is delegated back to the host view
/// model through <see cref="_settingsAccessor"/> / <see cref="_saveSettings"/> so that
/// settings.json remains a single source of truth.
/// </summary>
public partial class HidHideViewModel : ViewModelBase
{
    private readonly HidHideManager? _hidHideManager;
    private readonly Func<AppSettings> _settingsAccessor;
    private readonly Action _saveSettings;

    /// <summary>
    /// Suppresses the property-changed side effects while settings are being applied from
    /// disk, mirroring the old MainWindowViewModel._isLoadingSettings guard.
    /// </summary>
    private bool _isApplyingSettings;

    // HidHide Modal Controls
    [ObservableProperty]
    private bool _isHidHideMenuOpen = false;

    [ObservableProperty]
    private bool _isHidHideActive = false;

    /// <summary>
    /// When true (the default), <see cref="OnVirtualDevicesToggled"/> drives <see cref="IsHidHideActive"/>
    /// automatically: virtual devices off always forces cloaking off, and virtual devices back on
    /// restores whatever cloaking was set to right before that. When false, cloaking is fully
    /// manual and this hook does nothing.
    /// </summary>
    [ObservableProperty]
    private bool _linkCloakingToVirtualDevices = true;

    /// <summary>Cloaking state captured by <see cref="OnVirtualDevicesToggled"/> the moment virtual devices turn off, so turning them back on can restore it exactly.</summary>
    private bool _savedCloakingBeforeVirtualOff;

    [ObservableProperty]
    private bool _isAppListInverted = false;

    public ObservableCollection<HidDeviceItem> HidDevices { get; } = new();
    public ObservableCollection<string> WhitelistedApplications { get; } = new();

    /// <summary>True when the HidHide driver/service is present on this machine.</summary>
    public bool IsDriverInstalled => _hidHideManager?.IsInstalled ?? false;

    /// <summary>User-facing explanation of the most recent cloaking failure (driver not installed, access denied, ambiguous device, unreliable identifier), or null after a successful action - see <see cref="HidHideManager.LastError"/>.</summary>
    public string? LastHidHideError => _hidHideManager?.LastError;
    public bool HasHidHideError => !string.IsNullOrEmpty(LastHidHideError);

    /// <summary>Every method below that touches the driver calls this right after, so the HidHide page's warning banner reflects whatever <see cref="HidHideManager.LastError"/> just became (or was cleared to).</summary>
    private void RefreshErrorState()
    {
        OnPropertyChanged(nameof(LastHidHideError));
        OnPropertyChanged(nameof(HasHidHideError));
    }

    /// <summary>
    /// Fallback resolver for the hosting TopLevel's storage provider, supplied by
    /// <see cref="ECCR.Views.HidHideView"/>. The XAML passes the provider in as a command
    /// parameter; this covers the case where the visual tree cannot supply it yet.
    /// </summary>
    public Func<IStorageProvider?>? StorageProviderResolver { get; set; }

    /// <summary>Design-time constructor. Never touches the HidHide driver.</summary>
    public HidHideViewModel()
        : this(() => new AppSettings(), () => { }, designMode: true)
    {
    }

    public HidHideViewModel(Func<AppSettings> settingsAccessor, Action saveSettings, bool designMode = false)
    {
        _settingsAccessor = settingsAccessor;
        _saveSettings = saveSettings;
        _hidHideManager = designMode ? null : new HidHideManager();
    }

    // ---------------------------------------------------------------------
    // Settings bridge
    // ---------------------------------------------------------------------

    /// <summary>
    /// Restores HidHide state from persisted settings. Side effects on the observable
    /// properties are suppressed and applied explicitly, exactly as the original
    /// LoadAppSettings block did.
    /// </summary>
    public void ApplySettings(AppSettings settings)
    {
        _isApplyingSettings = true;
        try
        {
            IsHidHideActive = settings.IsHidHideActive;
            _hidHideManager?.SetGlobalHidingState(IsHidHideActive);
            LinkCloakingToVirtualDevices = settings.LinkCloakingToVirtualDevices;

            IsAppListInverted = settings.IsAppListInverted;
            if (_hidHideManager != null) _hidHideManager.IsAppListInverted = IsAppListInverted;

            if (settings.BlockedInstanceIds != null && settings.BlockedInstanceIds.Count > 0)
            {
                _hidHideManager?.SyncBlockedInstances(settings.BlockedInstanceIds);
            }

            if (settings.WhitelistedApplications != null && settings.WhitelistedApplications.Count > 0)
            {
                foreach (var app in settings.WhitelistedApplications)
                {
                    _hidHideManager?.AddApplicationExemption(app);
                }
            }
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    /// <summary>Writes the live HidHide state into the settings object about to be serialized.</summary>
    public void WriteSettings(AppSettings settings)
    {
        settings.IsHidHideActive = IsHidHideActive;
        settings.LinkCloakingToVirtualDevices = LinkCloakingToVirtualDevices;
        settings.IsAppListInverted = IsAppListInverted;
        settings.BlockedInstanceIds = _hidHideManager?.GetBlockedInstanceIds() ?? new List<string>();
        settings.WhitelistedApplications = _hidHideManager?.GetApplicationExemptions() ?? new List<string>();
    }

    // ---------------------------------------------------------------------
    // Modal lifecycle
    // ---------------------------------------------------------------------

    [RelayCommand]
    public void OpenHidHideMenu()
    {
        RefreshHidDevices();
        RefreshHidApplications();
        IsHidHideMenuOpen = true;
    }

    /// <summary>
    /// Commits the dialog's checkbox state to both the live HidHide driver and settings.json
    /// in one place, rather than writing through on every individual checkbox toggle - the
    /// driver call in <see cref="HidHideManager.SyncBlockedInstances"/> is relatively expensive (it walks
    /// and rewrites the whole blocklist), so batching it to dialog-close avoids doing that
    /// once per click while the user is still working through the device list.
    /// </summary>
    [RelayCommand]
    public void CloseHidHideMenu()
    {
        if (_hidHideManager == null) return;

        var blockedIds = HidDevices
            .Where(d => d.IsHidden && !HidHideManager.IsVirtualDevice(d.FriendlyName, d.DeviceInstanceId))
            .Select(d => d.DeviceInstanceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _hidHideManager.SyncBlockedInstances(blockedIds);

        var settings = _settingsAccessor();
        settings.BlockedInstanceIds = blockedIds;
        settings.WhitelistedApplications = _hidHideManager.GetApplicationExemptions();
        settings.IsHidHideActive = IsHidHideActive;
        settings.LinkCloakingToVirtualDevices = LinkCloakingToVirtualDevices;
        settings.IsAppListInverted = IsAppListInverted;

        _saveSettings();
        IsHidHideMenuOpen = false;
    }

    // ---------------------------------------------------------------------
    // Device cloaking
    // ---------------------------------------------------------------------

    /// <summary>
    /// A device counts as hidden if either the driver already has it blocked (source of
    /// truth after a fresh install/external change) or it's in this app's own saved settings
    /// (source of truth for what the user asked for) - the union of the two, checked in the
    /// merge loop below, is what the checkbox state reflects on open. That handles the
    /// driver's blocklist and settings.json drifting out of sync (e.g. the driver was reset
    /// externally, or settings were restored from an old backup) without silently unblocking
    /// something the user previously chose to hide.
    ///
    /// <para>
    /// <see cref="HidHideManager.GetConnectedHidDevices"/> does a synchronous WMI query plus a
    /// DirectInput probe per attached device - the WMI call alone commonly takes several hundred
    /// ms to a couple of seconds (COM/WMI service spin-up on first use). This used to run
    /// straight on the UI thread every time the HidHide page was opened (see
    /// <c>MainWindowViewModel.NavigateTo</c>), which is exactly what made navigating there freeze
    /// the whole app for a few seconds. The scan (and the per-device driver sync it drives) now
    /// runs on a background thread; only the final <see cref="HidDevices"/> population - which
    /// must run on the UI thread since it's bound - hops back via the implicit continuation.
    /// </para>
    /// </summary>
    public async void RefreshHidDevices()
    {
        var manager = _hidHideManager;
        if (manager == null) return;

        try
        {
            var savedBlocked = new HashSet<string>(_settingsAccessor().BlockedInstanceIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            var devices = await Task.Run(() =>
            {
                var list = manager.GetConnectedHidDevices();
                var currentDriverBlocked = new HashSet<string>(manager.GetBlockedInstanceIds(), StringComparer.OrdinalIgnoreCase);

                foreach (var item in list)
                {
                    if (currentDriverBlocked.Contains(item.DeviceInstanceId) || savedBlocked.Contains(item.DeviceInstanceId))
                    {
                        item.IsHidden = true;
                        manager.ToggleDeviceHiding(item, true);
                    }
                }

                return list;
            });

            HidDevices.Clear();
            foreach (var item in devices) HidDevices.Add(item);
            RefreshErrorState();
        }
        catch
        {
            // Swallowed, same tolerance every other best-effort HidHide scan in this app has -
            // the page just shows whatever devices were already in HidDevices (or none).
        }
    }

    [RelayCommand]
    public void ToggleHidDevice(HidDeviceItem item)
    {
        if (_hidHideManager == null) return;
        _hidHideManager.ToggleDeviceHiding(item, item.IsHidden);
        _hidHideManager.SyncLiveNow();
        _settingsAccessor().BlockedInstanceIds = _hidHideManager.GetBlockedInstanceIds();
        _saveSettings();
        RefreshErrorState();
    }

    [RelayCommand]
    public void SelectAllHidDevices()
    {
        if (_hidHideManager == null) return;
        foreach (var device in HidDevices)
        {
            if (!HidHideManager.IsVirtualDevice(device.FriendlyName, device.DeviceInstanceId))
            {
                device.IsHidden = true;
                _hidHideManager.ToggleDeviceHiding(device, true);
            }
        }
        _hidHideManager.SyncLiveNow();
        _settingsAccessor().BlockedInstanceIds = _hidHideManager.GetBlockedInstanceIds();
        _saveSettings();
        RefreshErrorState();
    }

    [RelayCommand]
    public void DeselectAllHidDevices()
    {
        if (_hidHideManager == null) return;
        foreach (var device in HidDevices)
        {
            device.IsHidden = false;
            _hidHideManager.ToggleDeviceHiding(device, false);
        }
        _settingsAccessor().BlockedInstanceIds = new List<string>();
        _hidHideManager.SyncBlockedInstances(new List<string>());
        _saveSettings();
        RefreshErrorState();
    }

    partial void OnIsHidHideActiveChanged(bool value)
    {
        if (_isApplyingSettings || _hidHideManager == null) return;
        _hidHideManager.SetGlobalHidingState(value);
        _saveSettings();
        RefreshErrorState();
    }

    partial void OnLinkCloakingToVirtualDevicesChanged(bool value)
    {
        if (_isApplyingSettings) return;
        _saveSettings();
    }

    /// <summary>
    /// Called from <c>MainWindowViewModel.OnIsVirtualOutputActiveChanged</c> whenever Virtual
    /// Devices is toggled. When <see cref="LinkCloakingToVirtualDevices"/> is off, this is a
    /// no-op - cloaking stays exactly as the user last set it regardless of Virtual Devices
    /// state. When on: turning Virtual Devices off remembers the current cloaking state and
    /// forces cloaking off (nothing to cloak for while there's no virtual output to avoid
    /// double-input on); turning Virtual Devices back on restores cloaking to whatever it was
    /// remembered as - so a device that was never cloaked stays uncloaked, and one that was
    /// cloaked comes back cloaked, without the user having to re-toggle it themselves.
    /// </summary>
    public void OnVirtualDevicesToggled(bool virtualActive)
    {
        if (!LinkCloakingToVirtualDevices) return;

        if (!virtualActive)
        {
            _savedCloakingBeforeVirtualOff = IsHidHideActive;
            IsHidHideActive = false;
        }
        else
        {
            IsHidHideActive = _savedCloakingBeforeVirtualOff;
        }
    }

    /// <summary>
    /// Live-cloaks a physical device the instant it becomes a mapping source - see
    /// <see cref="HidHideManager.EnsureDeviceCloaked"/> for why (prevents a game from seeing
    /// both the raw device and ECCR's virtual output at once). Called from
    /// <c>MainWindowViewModel.OnMappingEntryPropertyChanged</c> whenever a mapping's
    /// <c>SourceDeviceGuid</c> resolves to a real device - i.e. on every "Add Input" against an
    /// already-connected device, and the instant a "Click to Map" capture resolves.
    ///
    /// <para>
    /// This used to call straight into <see cref="HidHideManager.EnsureDeviceCloaked"/> on the UI
    /// thread: that constructs a fresh DirectInput/Joystick COM object and makes several blocking
    /// driver IOCTL calls (interface-path resolution, blocklist read, blocklist write) - real
    /// device I/O, not something that belongs on the UI thread. That was the actual cause of the
    /// app freezing on "Add Input" and on the button press that completes a capture (both paths
    /// funnel through this method). The driver work now runs on a background thread
    /// (<see cref="HidHideManager"/>'s own <c>_driverLock</c> serializes it against any other
    /// concurrent call so two near-simultaneous captures can't race each other); only the final
    /// <see cref="HidDevices"/>/settings update - which needs the UI thread since the collection
    /// is bound - hops back.
    /// </para>
    /// </summary>
    public void EnsureDeviceCloaked(Guid instanceGuid, string? deviceName)
    {
        var manager = _hidHideManager;
        if (manager == null) return;

        Task.Run(() =>
        {
            string? instanceId = manager.EnsureDeviceCloaked(instanceGuid, deviceName);
            List<string>? blockedIds = string.IsNullOrWhiteSpace(instanceId) ? null : manager.GetBlockedInstanceIds();
            return (instanceId, blockedIds);
        }).ContinueWith(t =>
        {
            if (t.IsFaulted) return;
            var (instanceId, blockedIds) = t.Result;

            Dispatcher.UIThread.Post(() =>
            {
                RefreshErrorState();
                if (string.IsNullOrWhiteSpace(instanceId) || blockedIds == null) return;

                var openDialogEntry = HidDevices.FirstOrDefault(d => string.Equals(d.DeviceInstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
                if (openDialogEntry != null) openDialogEntry.IsHidden = true;

                _settingsAccessor().BlockedInstanceIds = blockedIds;
                _saveSettings();
            });
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Turns cloaking off at the driver level without touching <see cref="IsHidHideActive"/> or
    /// persisting anything - called from <c>MainWindowViewModel.CleanupAndShutdown</c> so every
    /// physical device becomes visible to other apps again the moment ECCR exits, while the
    /// user's actual preference (re-applied via <see cref="ApplySettings"/> on next launch)
    /// stays saved as whatever it was.
    /// </summary>
    public void DisableCloakingForShutdown() => _hidHideManager?.SetGlobalHidingState(false);

    partial void OnIsAppListInvertedChanged(bool value)
    {
        if (_isApplyingSettings || _hidHideManager == null) return;
        _hidHideManager.IsAppListInverted = value;
        _saveSettings();
    }

    // ---------------------------------------------------------------------
    // Application whitelist
    // ---------------------------------------------------------------------

    public void RefreshHidApplications()
    {
        if (_hidHideManager == null) return;

        WhitelistedApplications.Clear();
        foreach (var app in _hidHideManager.GetApplicationExemptions()) WhitelistedApplications.Add(app);
        IsAppListInverted = _hidHideManager.IsAppListInverted;
    }

    [RelayCommand]
    public async Task AddApplicationToHidHide(IStorageProvider? storageProvider)
    {
        storageProvider ??= StorageProviderResolver?.Invoke();
        if (storageProvider == null || _hidHideManager == null) return;
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
            _saveSettings();
        }
    }

    [RelayCommand]
    public async Task AddDirectoryToHidHide(IStorageProvider? storageProvider)
    {
        storageProvider ??= StorageProviderResolver?.Invoke();
        if (storageProvider == null || _hidHideManager == null) return;
        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Game Folder" });
        if (folders.Count > 0)
        {
            _hidHideManager.AddDirectoryExemptions(folders[0].Path.LocalPath);
            RefreshHidApplications();
            _saveSettings();
        }
    }

    [RelayCommand]
    public void RemoveApplicationFromHidHide(string appPath)
    {
        if (_hidHideManager == null) return;
        _hidHideManager.RemoveApplicationExemption(appPath);
        RefreshHidApplications();
        _saveSettings();
    }

    [RelayCommand]
    public void ClearAllApplicationsFromHidHide()
    {
        if (_hidHideManager == null) return;
        _hidHideManager.ClearAllApplicationExemptions();
        RefreshHidApplications();
        _saveSettings();
    }
}
