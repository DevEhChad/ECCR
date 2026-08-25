using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using CommunityToolkit.Mvvm.ComponentModel;
using Nefarius.Drivers.HidHide;
using SharpDX.DirectInput;

namespace ECCR.Services;

public partial class HidDeviceItem : ObservableObject
{
    [ObservableProperty]
    private string _deviceInstanceId = string.Empty;

    [ObservableProperty]
    private string _friendlyName = string.Empty;

    [ObservableProperty]
    private bool _isHidden;
}

public class HidHideManager
{
    private readonly IHidHideControlService? _hidHide;

    public bool IsInstalled
    {
        get
        {
            try { return _hidHide?.IsInstalled ?? false; }
            catch { return false; }
        }
    }

    public bool IsGlobalActive
    {
        get
        {
            try { return _hidHide?.IsActive ?? false; }
            catch { return false; }
        }
    }

    public bool IsAppListInverted
    {
        get
        {
            try { return _hidHide?.IsAppListInverted ?? false; }
            catch { return false; }
        }
        set
        {
            try { if (_hidHide != null) _hidHide.IsAppListInverted = value; }
            catch { }
        }
    }

    public HidHideManager()
    {
        try
        {
            _hidHide = new HidHideControlService();
            if (_hidHide.IsInstalled)
            {
                _hidHide.IsAppListInverted = false;
                EnsureSelfWhitelisted();
                UnblockVirtualDevices();
            }
        }
        catch
        {
            _hidHide = null;
        }
    }

    public static bool IsVirtualDevice(string? name, string? deviceId, string? prodName = null, int vid = 0, int pid = 0)
    {
        if (vid == 0x1234) return true;
        if (vid == 0x045E && (pid == 0x028E || pid == 0x028F || pid == 0x02D1 || pid == 0x02DD || pid == 0x02E3 || pid == 0x0B12)) return true;

        string n = (name ?? string.Empty).ToLowerInvariant();
        string id = (deviceId ?? string.Empty).ToLowerInvariant();
        string p = (prodName ?? string.Empty).ToLowerInvariant();

        if (p.Contains("vjoy") || n.Contains("vjoy") || id.Contains("vjoy") ||
            id.Contains("vid_1234") || id.Contains("pid_bead") || id.Contains("pid_0be3") || id.Contains("root_vjoy"))
        {
            return true;
        }

        if (p.Contains("vigem") || n.Contains("vigem") || id.Contains("vigem") ||
            id.Contains("vid_045e&pid_028e") || n.Contains("virtual"))
        {
            return true;
        }

        if (n.Contains("hid-compliant game controller") || n.Contains("hid-compliant system controller"))
        {
            if (id.Contains("1234") || id.Contains("bead") || id.Contains("0be3") || 
                id.Contains("045e") || id.Contains("root") || id.Contains("vigem"))
            {
                return true;
            }
        }

        if (n == "controller (xbox 360 for windows)" && (id.Contains("045e") || id.Contains("xusb") || id.Contains("root") || id.Contains("vigem")))
        {
            return true;
        }

        return false;
    }

    public void UnblockVirtualDevices()
    {
        if (_hidHide == null || !IsInstalled) return;

        try
        {
            var blocked = _hidHide.BlockedInstanceIds.ToList();
            foreach (var id in blocked)
            {
                if (IsVirtualDevice(string.Empty, id))
                {
                    try { _hidHide.RemoveBlockedInstanceId(id); } catch { }
                }
            }
        }
        catch { }
    }

    public static HashSet<string> GetPermanentAppPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            string? currentExe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(currentExe) && File.Exists(currentExe))
                paths.Add(currentExe);

            string? mainModule = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(mainModule) && File.Exists(mainModule))
                paths.Add(mainModule);

            string baseDir = AppContext.BaseDirectory;
            if (Directory.Exists(baseDir))
            {
                foreach (var exe in Directory.GetFiles(baseDir, "*.exe", SearchOption.TopDirectoryOnly))
                    paths.Add(exe);
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string velopackCurrent = Path.Combine(localAppData, "ECCR", "current", "ECCR.exe");
            if (File.Exists(velopackCurrent))
                paths.Add(velopackCurrent);

            string velopackRoot = Path.Combine(localAppData, "ECCR", "ECCR.exe");
            if (File.Exists(velopackRoot))
                paths.Add(velopackRoot);

            string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrWhiteSpace(dotnetRoot))
            {
                string hostExe = Path.Combine(dotnetRoot, "dotnet.exe");
                if (File.Exists(hostExe)) paths.Add(hostExe);
            }
            string progFilesDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
            if (File.Exists(progFilesDotnet)) paths.Add(progFilesDotnet);

            string hidHideCli = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe");
            if (File.Exists(hidHideCli)) paths.Add(hidHideCli);
        }
        catch { }

        return paths;
    }

    public void EnsureSelfWhitelisted()
    {
        if (_hidHide == null || !IsInstalled) return;

        try
        {
            var permanent = GetPermanentAppPaths();
            var current = new HashSet<string>(_hidHide.ApplicationPaths, StringComparer.OrdinalIgnoreCase);

            foreach (var path in permanent)
            {
                if (!current.Contains(path))
                {
                    try { _hidHide.AddApplicationPath(path); } catch { }
                }
            }
        }
        catch { }
    }

    public void SetGlobalHidingState(bool active)
    {
        try
        {
            if (_hidHide != null && IsInstalled)
            {
                UnblockVirtualDevices();
                _hidHide.IsActive = active;
                if (active)
                {
                    EnsureSelfWhitelisted();
                }
            }
        }
        catch { }
    }

    public List<string> GetBlockedInstanceIds()
    {
        if (_hidHide == null || !IsInstalled) return new List<string>();
        try
        {
            return _hidHide.BlockedInstanceIds
                .Where(id => !IsVirtualDevice(string.Empty, id))
                .ToList();
        }
        catch { return new List<string>(); }
    }

    public List<HidDeviceItem> GetConnectedHidDevices()
    {
        var list = new List<HidDeviceItem>();
        if (_hidHide == null || !IsInstalled) return list;

        try
        {
            UnblockVirtualDevices();

            var blockedInstances = new HashSet<string>(_hidHide.BlockedInstanceIds, StringComparer.OrdinalIgnoreCase);
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var directInput = new DirectInput();
            var gameDevices = directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);

            foreach (var d in gameDevices)
            {
                byte[] bytes = d.ProductGuid.ToByteArray();
                int pid = bytes[0] | (bytes[1] << 8);
                int vid = bytes[2] | (bytes[3] << 8);

                string devName = d.InstanceName;
                string prodName = d.ProductName;

                if (IsVirtualDevice(devName, null, prodName, vid, pid))
                {
                    continue;
                }

                string instanceId = string.Empty;

                try
                {
                    using var joystick = new Joystick(directInput, d.InstanceGuid);
                    string? interfacePath = joystick.Properties.InterfacePath;

                    if (!string.IsNullOrWhiteSpace(interfacePath))
                    {
                        string clean = interfacePath.TrimStart('\\', '?', '.');
                        int guidIndex = clean.LastIndexOf('{');
                        if (guidIndex > 0)
                        {
                            clean = clean.Substring(0, guidIndex).TrimEnd('#');
                        }
                        instanceId = clean.Replace('#', '\\');
                    }
                }
                catch { }

                if (string.IsNullOrWhiteSpace(instanceId))
                {
                    instanceId = $"HID\\VID_{vid:X4}&PID_{pid:X4}";
                }

                if (IsVirtualDevice(devName, instanceId, prodName, vid, pid))
                {
                    continue;
                }

                if (seenIds.Add(instanceId))
                {
                    list.Add(new HidDeviceItem
                    {
                        DeviceInstanceId = instanceId,
                        FriendlyName = devName,
                        IsHidden = blockedInstances.Contains(instanceId) || 
                                   blockedInstances.Any(b => string.Equals(b, instanceId, StringComparison.OrdinalIgnoreCase))
                    });
                }
            }

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceID, Name, Caption, Description, Service, PNPClass FROM Win32_PnPEntity WHERE PNPClass = 'HIDClass' OR PNPClass = 'XBOXGameDevice'");
                using var collection = searcher.Get();

                foreach (ManagementObject obj in collection)
                {
                    string? deviceId = obj["DeviceID"]?.ToString();
                    string? name = obj["Name"]?.ToString() ?? obj["Caption"]?.ToString() ?? obj["Description"]?.ToString();
                    string? service = obj["Service"]?.ToString();

                    if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(name)) continue;

                    if (string.Equals(service, "vjoy", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(service, "ViGEmBus", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (IsVirtualDevice(name, deviceId)) continue;
                    if (IsNonGamingPeripheral(name, deviceId)) continue;

                    string lower = name.ToLowerInvariant();
                    string idLower = deviceId.ToLowerInvariant();

                    bool isGaming = idLower.Contains("ig_") ||
                                    lower.Contains("controller") || lower.Contains("gamepad") || lower.Contains("joystick") ||
                                    lower.Contains("wheel") || lower.Contains("pedal") || lower.Contains("shifter") ||
                                    lower.Contains("handbrake") || lower.Contains("moza") || lower.Contains("logitech") ||
                                    lower.Contains("thrustmaster") || lower.Contains("fanatec") || lower.Contains("simagic") ||
                                    lower.Contains("dualsense") || lower.Contains("dualshock");

                    if (isGaming && seenIds.Add(deviceId))
                    {
                        list.Add(new HidDeviceItem
                        {
                            DeviceInstanceId = deviceId,
                            FriendlyName = name,
                            IsHidden = blockedInstances.Contains(deviceId) ||
                                       blockedInstances.Any(b => string.Equals(b, deviceId, StringComparison.OrdinalIgnoreCase))
                        });
                    }
                }
            }
            catch { }
        }
        catch { }

        return list.OrderBy(d => d.FriendlyName).ToList();
    }

    private static bool IsNonGamingPeripheral(string name, string deviceId)
    {
        string lower = name.ToLowerInvariant();

        string[] nonGamingKeywords = [
            "keyboard", "mouse", "pointing device", "touchpad", "touch pad", "touch screen",
            "headset", "headphone", "microphone", "audio", "speaker", "sound", "realtek",
            "camera", "webcam", "bluetooth radio", "bluetooth adapter", "composite device",
            "consumer control", "system controller", "vendor-defined", "virtual mouse", "virtual keyboard"
        ];

        foreach (var keyword in nonGamingKeywords)
        {
            if (lower.Contains(keyword)) return true;
        }

        return false;
    }

    public void ToggleDeviceHiding(HidDeviceItem device, bool hide)
    {
        if (_hidHide == null || !IsInstalled) return;
        if (IsVirtualDevice(device.FriendlyName, device.DeviceInstanceId)) return;

        try
        {
            var blocked = new HashSet<string>(_hidHide.BlockedInstanceIds, StringComparer.OrdinalIgnoreCase);

            if (hide)
            {
                if (!blocked.Contains(device.DeviceInstanceId))
                {
                    _hidHide.AddBlockedInstanceId(device.DeviceInstanceId);
                }
            }
            else
            {
                if (blocked.Contains(device.DeviceInstanceId))
                {
                    _hidHide.RemoveBlockedInstanceId(device.DeviceInstanceId);
                }
            }
            device.IsHidden = hide;
        }
        catch { }
    }

    public void SyncBlockedInstances(IEnumerable<string> targetBlockedIds)
    {
        if (_hidHide == null || !IsInstalled) return;

        try
        {
            UnblockVirtualDevices();

            var current = new HashSet<string>(_hidHide.BlockedInstanceIds, StringComparer.OrdinalIgnoreCase);
            var desired = new HashSet<string>(
                targetBlockedIds.Where(id => !string.IsNullOrWhiteSpace(id) && !IsVirtualDevice(string.Empty, id)),
                StringComparer.OrdinalIgnoreCase
            );

            foreach (var id in desired)
            {
                if (!current.Contains(id))
                {
                    try { _hidHide.AddBlockedInstanceId(id); } catch { }
                }
            }

            foreach (var id in current)
            {
                if (!desired.Contains(id) || IsVirtualDevice(string.Empty, id))
                {
                    try { _hidHide.RemoveBlockedInstanceId(id); } catch { }
                }
            }
        }
        catch { }
    }

    public List<string> GetApplicationExemptions()
    {
        if (_hidHide == null || !IsInstalled) return new List<string>();
        try
        {
            var permanent = GetPermanentAppPaths();
            return _hidHide.ApplicationPaths
                .Where(p => !permanent.Contains(p))
                .ToList();
        }
        catch { return new List<string>(); }
    }

    public void AddApplicationExemption(string fullExePath)
    {
        if (_hidHide == null || !IsInstalled || string.IsNullOrWhiteSpace(fullExePath)) return;
        try
        {
            var current = new HashSet<string>(_hidHide.ApplicationPaths, StringComparer.OrdinalIgnoreCase);
            if (!current.Contains(fullExePath))
            {
                _hidHide.AddApplicationPath(fullExePath);
            }
        }
        catch { }
    }

    public void AddDirectoryExemptions(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return;
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

    public void RemoveApplicationExemption(string fullExePath)
    {
        if (_hidHide == null || !IsInstalled) return;
        try
        {
            var permanent = GetPermanentAppPaths();
            if (permanent.Contains(fullExePath)) return;

            _hidHide.RemoveApplicationPath(fullExePath);
        }
        catch { }
    }

    public void ClearAllApplicationExemptions()
    {
        if (_hidHide == null || !IsInstalled) return;
        try
        {
            var permanent = GetPermanentAppPaths();
            foreach (var path in _hidHide.ApplicationPaths.ToList())
            {
                if (!permanent.Contains(path))
                {
                    _hidHide.RemoveApplicationPath(path);
                }
            }
            EnsureSelfWhitelisted();
        }
        catch { }
    }
}