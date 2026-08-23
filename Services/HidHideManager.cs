using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using Nefarius.Drivers.HidHide;

namespace ECCR.Services;

public partial class HidDeviceItem : ObservableObject
{
    private bool _isHidden;

    public string FriendlyName { get; set; } = string.Empty;
    public string Name 
    { 
        get => FriendlyName; 
        set => FriendlyName = value; 
    }

    public string Manufacturer { get; set; } = "Controller";

    public string DeviceInstanceId { get; set; } = string.Empty;
    public string InstanceId 
    { 
        get => DeviceInstanceId; 
        set => DeviceInstanceId = value; 
    }

    public List<string> AllInstanceIds { get; set; } = new();

    public string Description { get; set; } = string.Empty;

    public bool IsHidden
    {
        get => _isHidden;
        set => SetProperty(ref _isHidden, value);
    }
}

public class HidHideManager
{
    private readonly IHidHideControlService? _hidHideService;
    private readonly bool _isInstalled;

    public bool IsInstalled => _isInstalled;

    public bool IsGlobalActive
    {
        get => _hidHideService?.IsActive ?? false;
        set
        {
            if (_hidHideService != null && _isInstalled)
            {
                try
                {
                    _hidHideService.IsActive = value;
                }
                catch { }
            }
        }
    }

    public bool IsAppListInverted
    {
        get => _hidHideService?.IsAppListInverted ?? false;
        set
        {
            if (_hidHideService != null && _isInstalled)
            {
                try
                {
                    _hidHideService.IsAppListInverted = value;
                }
                catch { }
            }
        }
    }

    public HidHideManager()
    {
        try
        {
            _hidHideService = new HidHideControlService();
            _isInstalled = _hidHideService.IsInstalled;

            if (_isInstalled)
            {
                // Always whitelist ECCR so it can read cloaked hardware
                string currentAppPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (!string.IsNullOrEmpty(currentAppPath))
                {
                    AddApplicationExemption(currentAppPath);
                }
            }
        }
        catch
        {
            _isInstalled = false;
        }
    }

    public void SetGlobalHidingState(bool active)
    {
        IsGlobalActive = active;
    }

    public List<HidDeviceItem> GetConnectedHidDevices()
    {
        var resultList = new List<HidDeviceItem>();
        if (!_isInstalled || _hidHideService == null) return resultList;

        var blockedIds = new HashSet<string>(_hidHideService.BlockedInstanceIds, StringComparer.OrdinalIgnoreCase);

        // 1. Build a USB / Parent Hardware Name Lookup
        var usbNameLookup = new Dictionary<string, (string Name, string Manufacturer)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var usbSearcher = new ManagementObjectSearcher(
                @"SELECT DeviceID, Name, Manufacturer, Description, Caption FROM Win32_PnPEntity WHERE PNPClass = 'USB' OR PNPClass = 'Media' OR PNPClass = 'Bluetooth' OR PNPClass = 'XboxComposite'");

            foreach (ManagementObject obj in usbSearcher.Get())
            {
                string devId = obj["DeviceID"]?.ToString() ?? string.Empty;
                string name = obj["Name"]?.ToString() ?? obj["Caption"]?.ToString() ?? string.Empty;
                string mfg = obj["Manufacturer"]?.ToString() ?? string.Empty;

                var match = Regex.Match(devId, @"VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4}", RegexOptions.IgnoreCase);
                if (match.Success && !string.IsNullOrWhiteSpace(name))
                {
                    string vidPid = match.Value.ToUpperInvariant();
                    if (!usbNameLookup.ContainsKey(vidPid) || usbNameLookup[vidPid].Name.Contains("USB", StringComparison.OrdinalIgnoreCase))
                    {
                        usbNameLookup[vidPid] = (name, mfg);
                    }
                }
            }
        }
        catch { }

        // 2. Query all active HID endpoints and group by physical hardware ID (VID/PID)
        var groupedControllers = new Dictionary<string, HidDeviceItem>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var hidSearcher = new ManagementObjectSearcher(
                @"SELECT DeviceID, Name, Manufacturer, Description, Caption FROM Win32_PnPEntity WHERE PNPClass = 'HIDClass'");

            foreach (ManagementObject obj in hidSearcher.Get())
            {
                string instanceId = obj["DeviceID"]?.ToString() ?? string.Empty;
                string rawName = obj["Name"]?.ToString() ?? obj["Caption"]?.ToString() ?? string.Empty;
                string rawMfg = obj["Manufacturer"]?.ToString() ?? string.Empty;
                string description = obj["Description"]?.ToString() ?? string.Empty;

                if (string.IsNullOrEmpty(instanceId)) continue;

                // Eliminate mousepads, headsets, keyboards, mice, and virtual devices
                if (IsExcludedPeripheral(instanceId, rawName, description, rawMfg)) continue;

                var vidPidMatch = Regex.Match(instanceId, @"VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4}", RegexOptions.IgnoreCase);
                string groupKey = vidPidMatch.Success ? vidPidMatch.Value.ToUpperInvariant() : instanceId;

                // A. Try Windows Registry OEM Joystick Name (The exact name joy.cpl uses)
                string bestName = GetOemJoystickName(groupKey);

                // B. Try USB Parent Lookup
                if (string.IsNullOrWhiteSpace(bestName) && vidPidMatch.Success && usbNameLookup.TryGetValue(vidPidMatch.Value, out var parentInfo))
                {
                    if (!string.IsNullOrWhiteSpace(parentInfo.Name) && !parentInfo.Name.StartsWith("USB Input", StringComparison.OrdinalIgnoreCase))
                    {
                        bestName = parentInfo.Name;
                    }
                }

                // C. Fallback to Raw PnP Name
                if (string.IsNullOrWhiteSpace(bestName))
                {
                    bestName = rawName;
                }

                // Clean and normalize hardware names & manufacturers
                bestName = CleanHardwareName(bestName, instanceId, groupKey);
                string bestMfg = ResolveManufacturer(groupKey, rawMfg, bestName);

                // Only keep gaming peripherals (Wheels, Pedals, Shifters, Handbrakes, Joysticks, Gamepads)
                if (!IsGameController(bestName, rawName, description, instanceId)) continue;

                if (!groupedControllers.TryGetValue(groupKey, out var item))
                {
                    item = new HidDeviceItem
                    {
                        FriendlyName = bestName,
                        Manufacturer = bestMfg,
                        DeviceInstanceId = instanceId,
                        Description = description,
                        AllInstanceIds = new List<string>()
                    };
                    groupedControllers[groupKey] = item;
                }

                item.AllInstanceIds.Add(instanceId);

                // If any endpoint of this physical device is cloaked, flag the card as hidden
                if (blockedIds.Contains(instanceId))
                {
                    item.IsHidden = true;
                }
            }
        }
        catch { }

        return groupedControllers.Values.OrderBy(c => c.FriendlyName).ToList();
    }

    private static string GetOemJoystickName(string vidPid)
    {
        if (string.IsNullOrWhiteSpace(vidPid) || !vidPid.Contains("VID_")) return string.Empty;

        try
        {
            // Check Current User Joystick Registry
            using var keyCu = Registry.CurrentUser.OpenSubKey($@"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\{vidPid}");
            if (keyCu?.GetValue("OEMName") is string nameCu && !string.IsNullOrWhiteSpace(nameCu))
            {
                return nameCu.Trim();
            }

            // Check Local Machine Joystick Registry
            using var keyLm = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\{vidPid}");
            if (keyLm?.GetValue("OEMName") is string nameLm && !string.IsNullOrWhiteSpace(nameLm))
            {
                return nameLm.Trim();
            }
        }
        catch { }

        return string.Empty;
    }

    private static bool IsExcludedPeripheral(string instanceId, string name, string desc, string mfg)
    {
        string combined = $"{instanceId} {name} {desc} {mfg}".ToLowerInvariant();

        string[] excludedKeywords = [
            "goliathus", "razer inc", "chroma", "mouse", "keyboard", 
            "touchpad", "touch pad", "digitizer", "vhf device", "virtual hid framework",
            "vjoy device", "audio", "headset", "consumer control", "system control", 
            "vendor-defined", "light", "led", "rgb", "bluetooth low energy", "composite device"
        ];

        return excludedKeywords.Any(k => combined.Contains(k));
    }

    private static bool IsGameController(string cleanName, string rawName, string desc, string instanceId)
    {
        string combined = $"{cleanName} {rawName} {desc} {instanceId}".ToLowerInvariant();

        string[] simKeywords = [
            "wheel", "g920", "g29", "g27", "g923", "moza", "thrustmaster", "fanatec", "simagic", 
            "pedal", "shifter", "handbrake", "ebrake", "手柄", "game controller", "gamepad", 
            "joystick", "flight", "hotas", "rudder", "yoke", "throttle", "dualsense", 
            "dualshock", "wireless controller", "xbox", "directinput", "vkb", "virpil"
        ];

        return simKeywords.Any(k => combined.Contains(k));
    }

    private static string CleanHardwareName(string name, string instanceId, string vidPid)
    {
        string lower = $"{name} {instanceId} {vidPid}".ToLowerInvariant();

        // Specific hardware patterns
        if (lower.Contains("g920")) return "Logitech G920 Driving Force Racing Wheel";
        if (lower.Contains("g29")) return "Logitech G29 Driving Force Racing Wheel";
        if (lower.Contains("g27")) return "Logitech G27 Racing Wheel";
        if (lower.Contains("g923")) return "Logitech G923 Racing Wheel";
        if (lower.Contains("moza") && lower.Contains("r5")) return "MOZA R5 Direct Drive Base";
        if (lower.Contains("moza")) return "MOZA Racing Wheelbase";
        if (lower.Contains("dualsense")) return "Sony PlayStation 5 DualSense Controller";
        if (lower.Contains("dualshock")) return "Sony PlayStation 4 DualShock Controller";
        if (lower.Contains("手柄") || lower.Contains("handbrake") || lower.Contains("ebrake")) return "USB Sim Handbrake";

        // Fallback for generic strings using VID signatures
        if (name.StartsWith("Generic USB", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("HID-compliant", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("USB Input", StringComparison.OrdinalIgnoreCase))
        {
            if (vidPid.Contains("VID_3416", StringComparison.OrdinalIgnoreCase)) return "MOZA R5 Direct Drive Base";
            if (vidPid.Contains("VID_046D", StringComparison.OrdinalIgnoreCase)) return "Logitech Racing Controller";
            if (vidPid.Contains("VID_054C", StringComparison.OrdinalIgnoreCase)) return "Sony DualSense Controller";
            if (vidPid.Contains("VID_044F", StringComparison.OrdinalIgnoreCase)) return "Thrustmaster Racing Wheel";
            if (vidPid.Contains("VID_0EB7", StringComparison.OrdinalIgnoreCase)) return "Fanatec Wheelbase";
            if (vidPid.Contains("VID_231D", StringComparison.OrdinalIgnoreCase)) return "VKB Sim Flight Controller";
            if (vidPid.Contains("VID_3344", StringComparison.OrdinalIgnoreCase)) return "VIRPIL Flight Controller";
            if (vidPid.Contains("VID_16C0", StringComparison.OrdinalIgnoreCase)) return "USB Sim Handbrake / Button Box";

            return "USB Game Controller";
        }

        return name.Trim();
    }

    private static string ResolveManufacturer(string vidPid, string rawMfg, string cleanName)
    {
        string upperVid = vidPid.ToUpperInvariant();

        if (upperVid.Contains("VID_046D") || cleanName.Contains("Logitech", StringComparison.OrdinalIgnoreCase)) return "Logitech";
        if (upperVid.Contains("VID_3416") || cleanName.Contains("MOZA", StringComparison.OrdinalIgnoreCase)) return "MOZA";
        if (upperVid.Contains("VID_054C") || cleanName.Contains("Sony", StringComparison.OrdinalIgnoreCase) || cleanName.Contains("DualSense", StringComparison.OrdinalIgnoreCase)) return "Sony";
        if (upperVid.Contains("VID_045E") || cleanName.Contains("Xbox", StringComparison.OrdinalIgnoreCase)) return "Microsoft";
        if (upperVid.Contains("VID_044F") || cleanName.Contains("Thrustmaster", StringComparison.OrdinalIgnoreCase)) return "Thrustmaster";
        if (upperVid.Contains("VID_0EB7") || cleanName.Contains("Fanatec", StringComparison.OrdinalIgnoreCase)) return "Fanatec";
        if (upperVid.Contains("VID_231D")) return "VKB";
        if (upperVid.Contains("VID_3344")) return "VIRPIL";
        if (cleanName.Contains("Handbrake", StringComparison.OrdinalIgnoreCase) || cleanName.Contains("手柄")) return "USB Sim";

        if (!string.IsNullOrWhiteSpace(rawMfg) && !rawMfg.Contains("Standard", StringComparison.OrdinalIgnoreCase) && !rawMfg.Contains("Generic", StringComparison.OrdinalIgnoreCase))
        {
            return rawMfg.Trim();
        }

        return "Controller";
    }

    public void ToggleDeviceHiding(HidDeviceItem device, bool hide)
    {
        if (!_isInstalled || _hidHideService == null) return;

        try
        {
            foreach (var instanceId in device.AllInstanceIds)
            {
                if (hide)
                {
                    _hidHideService.AddBlockedInstanceId(instanceId);
                }
                else
                {
                    _hidHideService.RemoveBlockedInstanceId(instanceId);
                }
            }
            device.IsHidden = hide;
        }
        catch { }
    }

    public IReadOnlyList<string> GetApplicationExemptions()
    {
        if (!_isInstalled || _hidHideService == null) return Array.Empty<string>();

        try
        {
            return _hidHideService.ApplicationPaths;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void AddApplicationExemption(string fullExecutablePath)
    {
        if (!_isInstalled || _hidHideService == null || !File.Exists(fullExecutablePath)) return;

        try
        {
            _hidHideService.AddApplicationPath(fullExecutablePath);
        }
        catch { }
    }

    public void AddDirectoryExemptions(string directoryPath)
    {
        if (!_isInstalled || _hidHideService == null || !Directory.Exists(directoryPath)) return;

        try
        {
            var exeFiles = Directory.GetFiles(directoryPath, "*.exe", SearchOption.AllDirectories);
            foreach (var exe in exeFiles)
            {
                AddApplicationExemption(exe);
            }
        }
        catch { }
    }

    public void RemoveApplicationExemption(string fullExecutablePath)
    {
        if (!_isInstalled || _hidHideService == null) return;

        try
        {
            _hidHideService.RemoveApplicationPath(fullExecutablePath);
        }
        catch { }
    }

    public void ClearAllApplicationExemptions()
    {
        if (!_isInstalled || _hidHideService == null) return;

        try
        {
            var existingPaths = _hidHideService.ApplicationPaths.ToList();
            foreach (var path in existingPaths)
            {
                try
                {
                    _hidHideService.RemoveApplicationPath(path);
                }
                catch { }
            }

            string currentAppPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (!string.IsNullOrEmpty(currentAppPath))
            {
                AddApplicationExemption(currentAppPath);
            }
        }
        catch { }
    }
}