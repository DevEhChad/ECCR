using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
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

    [ObservableProperty]
    private bool _isAppListInverted = false;

    public ObservableCollection<HidDeviceItem> HidDevices { get; } = new();
    public ObservableCollection<string> WhitelistedApplications { get; } = new();

    /// <summary>True when the HidHide driver/service is present on this machine.</summary>
    public bool IsDriverInstalled => _hidHideManager?.IsInstalled ?? false;

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
        settings.IsAppListInverted = IsAppListInverted;

        _saveSettings();
        IsHidHideMenuOpen = false;
    }

    // ---------------------------------------------------------------------
    // Device cloaking
    // ---------------------------------------------------------------------

    public void RefreshHidDevices()
    {
        if (_hidHideManager == null) return;

        HidDevices.Clear();
        var devices = _hidHideManager.GetConnectedHidDevices();
        var currentDriverBlocked = new HashSet<string>(_hidHideManager.GetBlockedInstanceIds(), StringComparer.OrdinalIgnoreCase);
        var savedBlocked = new HashSet<string>(_settingsAccessor().BlockedInstanceIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var item in devices)
        {
            if (currentDriverBlocked.Contains(item.DeviceInstanceId) || savedBlocked.Contains(item.DeviceInstanceId))
            {
                item.IsHidden = true;
                _hidHideManager.ToggleDeviceHiding(item, true);
            }
            HidDevices.Add(item);
        }
    }

    [RelayCommand]
    public void ToggleHidDevice(HidDeviceItem item)
    {
        if (_hidHideManager == null) return;
        _hidHideManager.ToggleDeviceHiding(item, item.IsHidden);
        _settingsAccessor().BlockedInstanceIds = _hidHideManager.GetBlockedInstanceIds();
        _saveSettings();
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
        _settingsAccessor().BlockedInstanceIds = _hidHideManager.GetBlockedInstanceIds();
        _saveSettings();
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
    }

    partial void OnIsHidHideActiveChanged(bool value)
    {
        if (_isApplyingSettings || _hidHideManager == null) return;
        _hidHideManager.SetGlobalHidingState(value);
        _saveSettings();
    }

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
