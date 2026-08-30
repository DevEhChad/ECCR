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

/// <summary>
/// Thin wrapper around Nefarius' HidHide driver control service. HidHide is a Windows
/// filter driver that can hide a physical HID device from every application except the ones
/// on its whitelist - this is what lets a game see only the app's combined virtual wheel
/// instead of also seeing (and double-reading, or force-feedback-fighting over) the raw
/// physical wheel/pedals underneath it. Two invariants this class exists to protect:
/// (1) ECCR itself, and a handful of system executables it depends on, must always stay
/// whitelisted or the app would lock itself out of its own devices (see
/// <see cref="EnsureSelfWhitelisted"/>/<see cref="GetPermanentAppPaths"/>); (2) the app's own
/// virtual devices (vJoy/ViGEm) must never end up in HidHide's blocklist, or the very output
/// the app creates would be hidden from every game (see <see cref="IsVirtualDevice"/> and
/// its use throughout this file).
/// </summary>
public class HidHideManager
{
    private readonly IHidHideControlService? _hidHide;

    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECCR", "hid-debug.log");

    /// <summary>Temporary diagnostic log (shared file/format with <see cref="ControllerHidService"/>) for troubleshooting which devices get included/excluded and why.</summary>
    private static void LogDebug(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
            File.AppendAllText(LogFilePath, $"[{DateTime.Now:HH:mm:ss.fff}] [HidHide] {message}\n");
        }
        catch { }
    }

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

    /// <summary>
    /// Identifies the app's own virtual output devices (vJoy: VID 0x1234; ViGEm's emulated
    /// Xbox 360 pad: VID 0x045E PID 0x028E specifically - ViGEm's own emulation target, not
    /// any other Microsoft PID) so they're never accidentally cloaked. Checks VID/PID first
    /// when available, then falls back to substring matching on device name/instance ID/
    /// product name for callers (like the device-instance-ID-only overloads used throughout
    /// this file) that don't have VID/PID parsed out. Over-matching here is the safer failure
    /// mode than under-matching, since the consequence of a false positive is just "one real
    /// device stays visible to HidHide's picker", while a false negative could hide the app's
    /// own virtual device from every game - see the 045E:028E handling below for why that PID
    /// in particular stays an unconditional match rather than trying to disambiguate a genuine
    /// wired Xbox 360 pad from ViGEm's impersonation of one.
    /// </summary>
    public static bool IsVirtualDevice(string? name, string? deviceId, string? prodName = null, int vid = 0, int pid = 0)
    {
        string n = (name ?? string.Empty).ToLowerInvariant();
        string id = (deviceId ?? string.Empty).ToLowerInvariant();
        string p = (prodName ?? string.Empty).ToLowerInvariant();

        // vJoy's default VID (0x1234) is vJoy-specific and not shared with any real hardware
        // vendor, so this stays a bare VID match.
        if (vid == 0x1234) return true;
        if (p.Contains("vjoy") || n.Contains("vjoy") || id.Contains("vjoy") ||
            id.Contains("vid_1234") || id.Contains("pid_bead") || id.Contains("pid_0be3") || id.Contains("root_vjoy"))
        {
            return true;
        }

        // ViGEm's virtual Xbox 360 pad always identifies as exactly 045E:028E (a real Microsoft
        // Xbox 360 controller's own VID/PID, since ViGEm deliberately impersonates one for game
        // compatibility) - PID alone is therefore ambiguous with a genuine wired Xbox 360 pad.
        // Verified live against a real machine running ECCR with virtual output active: a
        // ViGEm-created 045E:028E pad does NOT reliably carry a "root" instance-id token (it
        // showed up on real USB-tree-shaped instance IDs, indistinguishable from a genuine wired
        // pad by instance ID alone) - so unlike the true ROOT-enumerated vJoy case, there is no
        // reliable disambiguator available here. Given a false negative (accidentally exposing
        // ECCR's own virtual output to being hidden, which breaks every game's input) is far
        // worse than a false positive (one genuine wired Xbox 360 pad, a rare case among this
        // app's users, not appearing in the HidHide picker), 028E stays an unconditional match.
        // Other 045E PIDs (028F/02D1/02DD/02E3/0B12 etc.) belong to real Microsoft Xbox One/
        // Series hardware that ViGEm does not emulate, and correctly do NOT match here.
        bool isVigemVidPid = (vid == 0x045E && pid == 0x028E) || id.Contains("vid_045e&pid_028e");
        bool isVigemNameHit = p.Contains("vigem") || n.Contains("vigem") || id.Contains("vigem");

        if (isVigemNameHit || isVigemVidPid) return true;

        // isVigemVidPid already covers 045E:028E unconditionally above; this catches the
        // remaining vJoy-token cases for a generically-named "HID-compliant ..." device whose
        // vid/pid weren't parsed out by the caller.
        if (n.Contains("hid-compliant game controller") || n.Contains("hid-compliant system controller"))
        {
            if (id.Contains("1234") || id.Contains("bead") || id.Contains("0be3") || id.Contains("vigem"))
            {
                return true;
            }
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

    /// <summary>
    /// Builds the device list shown in the HidHide dialog. Two enumeration passes are
    /// combined because neither alone is complete: DirectInput only sees devices exposing a
    /// game-controller interface (and gives a clean instance path HidHide can key on), while
    /// the WMI PnP query below also catches HID devices some drivers don't surface through
    /// DirectInput at all - at the cost of noisier IDs and needing the keyword/PNPClass
    /// filtering in <see cref="IsNonGamingPeripheral"/> to keep keyboards/mice/webcams out.
    /// <c>seenIds</c> dedupes across both passes by instance ID.
    /// </summary>
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
                // DirectInput packs USB VID/PID into the first 4 bytes of ProductGuid
                // (a legacy convention, not a real GUID) - PID low/high byte then VID
                // low/high byte, both little-endian.
                byte[] bytes = d.ProductGuid.ToByteArray();
                int pid = bytes[0] | (bytes[1] << 8);
                int vid = bytes[2] | (bytes[3] << 8);

                string devName = d.InstanceName;
                string prodName = d.ProductName;

                if (IsVirtualDevice(devName, null, prodName, vid, pid))
                {
                    LogDebug($"DirectInput pass: excluded '{devName}' (VID={vid:X4} PID={pid:X4}) - matched IsVirtualDevice (pre-instance-id)");
                    continue;
                }

                string instanceId = string.Empty;

                // HidHide's blocklist is keyed by Windows device instance path
                // (VID_xxxx&PID_xxxx\...), not DirectInput's InstanceGuid, so it has to be
                // recovered from the device's raw interface path. A typical interface path
                // looks like "\\?\HID#VID_1234&PID_5678#7&abc123&0&0000#{guid}" - strip the
                // leading "\\?\" / "." noise and the trailing "#{class guid}" suffix, then
                // swap the remaining "#" separators back to "\" to get the instance ID form
                // HidHide expects.
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
                    LogDebug($"DirectInput pass: excluded '{devName}' ({instanceId}) - matched IsVirtualDevice (post-instance-id)");
                    continue;
                }

                if (seenIds.Add(instanceId))
                {
                    string friendlyName = BuildBrandedFriendlyName(
                        !string.IsNullOrWhiteSpace(prodName) ? prodName : devName, vid);

                    LogDebug($"DirectInput pass: added '{friendlyName}' ({instanceId})");
                    list.Add(new HidDeviceItem
                    {
                        DeviceInstanceId = instanceId,
                        FriendlyName = friendlyName,
                        IsHidden = blockedInstances.Contains(instanceId) ||
                                   blockedInstances.Any(b => string.Equals(b, instanceId, StringComparison.OrdinalIgnoreCase))
                    });
                }
            }

            // Only PNPClass = 'XBOXGameDevice' is included here - real Xbox controllers that
            // enumerate via XUSB rather than a DirectInput-visible HID joystick interface.
            // The former broader 'HIDClass' query pulled in every HID device on the system
            // (keyboards, mice, touchpads, webcams, "HID-compliant vendor-defined device", the
            // laptop lid switch, etc.) that IsNonGamingPeripheral's keyword list couldn't fully
            // keep out - narrowing to just XBOXGameDevice keeps this pass limited to real game
            // controllers instead of "every device with a HID descriptor".
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceID, Name, Caption, Description, Service, PNPClass FROM Win32_PnPEntity WHERE PNPClass = 'XBOXGameDevice'");
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

                    if (IsVirtualDevice(name, deviceId))
                    {
                        LogDebug($"WMI pass: excluded '{name}' ({deviceId}) - matched IsVirtualDevice");
                        continue;
                    }
                    if (IsNonGamingPeripheral(name, deviceId))
                    {
                        LogDebug($"WMI pass: excluded '{name}' ({deviceId}) - matched IsNonGamingPeripheral");
                        continue;
                    }

                    // The PNPClass filter (XBOXGameDevice) plus the two exclusions above are
                    // already sufficient to isolate real gaming HID devices - a device that
                    // makes it this far no longer needs to also match a hardcoded brand/keyword
                    // whitelist (that used to silently drop any real controller whose
                    // Windows-assigned name didn't happen to contain a listed brand word).
                    if (seenIds.Add(deviceId))
                    {
                        int vid = ParseVidFromInstanceId(deviceId);
                        string friendlyName = BuildBrandedFriendlyName(name, vid);

                        list.Add(new HidDeviceItem
                        {
                            DeviceInstanceId = deviceId,
                            FriendlyName = friendlyName,
                            IsHidden = blockedInstances.Contains(deviceId) ||
                                       blockedInstances.Any(b => string.Equals(b, deviceId, StringComparison.OrdinalIgnoreCase))
                        });
                        LogDebug($"WMI pass: added '{name}' ({deviceId})");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"WMI pass threw: {ex.GetType().Name}: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            LogDebug($"GetConnectedHidDevices threw: {ex.GetType().Name}: {ex.Message}");
        }

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

    /// <summary>
    /// USB vendor IDs for controller/wheel/joystick brands whose devices sometimes enumerate
    /// under a generic Windows-assigned name (e.g. "HID-compliant game controller") instead of
    /// their real product string - used by <see cref="BuildBrandedFriendlyName"/> to recover a
    /// recognizable brand name for the HidHide device list instead of showing the generic label.
    /// </summary>
    private static readonly Dictionary<int, string> KnownControllerVendors = new()
    {
        { 0x054C, "Sony" },
        { 0x045E, "Microsoft" },
        { 0x046D, "Logitech" },
        { 0x044F, "Thrustmaster" },
        { 0x0EB7, "Fanatec" },
        { 0x057E, "Nintendo" },
        { 0x28DE, "Valve" },
        { 0x2DC8, "8BitDo" },
        { 0x1532, "Razer" },
        { 0x24C6, "PowerA" },
        { 0x20D6, "PowerA" },
        { 0x0F0D, "HORI" },
        { 0x1038, "SteelSeries" },
        { 0x3537, "GameSir" },
        { 0x0079, "DragonRise" },
        { 0x1D79, "Simagic" },
        { 0x16D0, "Cammus" },
    };

    private static bool LooksGenericHidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        string lower = name.ToLowerInvariant();
        return lower.Contains("hid-compliant") ||
               lower.Contains("usb input device") ||
               lower.Contains("usb game controller") ||
               lower == "generic usb joystick";
    }

    /// <summary>
    /// Picks the best display name for a real controller/wheel/joystick: the raw DirectInput
    /// product/instance name when it already looks like a real brand string, otherwise a
    /// "{Brand} Controller" fallback built from the device's USB VID (see
    /// <see cref="KnownControllerVendors"/>) so the HidHide list never shows a bare
    /// "HID-compliant game controller" entry the user can't identify.
    /// </summary>
    private static string BuildBrandedFriendlyName(string rawName, int vid)
    {
        if (!LooksGenericHidName(rawName)) return rawName;

        if (KnownControllerVendors.TryGetValue(vid, out var brand))
        {
            return $"{brand} Controller";
        }

        return string.IsNullOrWhiteSpace(rawName) ? "Game Controller" : rawName;
    }

    /// <summary>Extracts the USB VID from a Windows device instance ID like "USB\VID_054C&amp;PID_0CE6\..." (0 if absent/unparseable).</summary>
    private static int ParseVidFromInstanceId(string instanceId)
    {
        int idx = instanceId.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
        if (idx < 0 || idx + 8 > instanceId.Length) return 0;

        string hex = instanceId.Substring(idx + 4, 4);
        return int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int vid) ? vid : 0;
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

    /// <summary>
    /// Reconciles the driver's actual blocklist to exactly match <paramref name="targetBlockedIds"/>
    /// (adds what's missing, removes what shouldn't be there) instead of only ever adding.
    /// Called when the HidHide dialog closes and when settings are loaded at startup - a
    /// one-directional "add only" sync would leave devices the user unchecked still hidden
    /// after their next session, since HidHide's own blocklist persists independently of
    /// this app's settings.json.
    /// </summary>
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