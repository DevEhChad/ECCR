using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SharpDX.DirectInput;

namespace ECCR.Services;

public class DeviceHardwareInfo
{
    public Guid InstanceGuid { get; set; }
    public Guid ProductGuid { get; set; }
    public string InstanceName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int ButtonCount { get; set; }
    public int AxisCount { get; set; }

    /// <summary>Whether the device reports a POV hat (D-Pad) - gates whether <see cref="DevicePresetService.GeneratePreset"/> emits the synthetic 128-131 D-Pad button indices for it.</summary>
    public bool HasPov { get; set; }

    /// <summary>
    /// USB vendor/product ID, decoded from <see cref="ProductGuid"/> (standard DirectInput
    /// convention: a product GUID's first 4 bytes are the VID/PID as two little-endian
    /// UInt16s - <c>{PPPPVVVV-0000-0000-0000-504944564944}</c>). 0 if decoding failed. Lets
    /// <see cref="DevicePresetService.DetectCategoryByVidPid"/> identify hardware more reliably
    /// than the name-substring fallback wherever a caller already has this struct in hand.
    /// </summary>
    public int VendorId { get; set; }
    public int ProductId { get; set; }
}

/// <summary>
/// Owns the DirectInput device enumeration and polling loop - the app's only source of raw
/// hardware input. <see cref="RefreshDevices"/> acquires every attached game controller and
/// keeps them acquired across calls (adding newly plugged-in devices, releasing unplugged
/// ones); <see cref="StartPolling"/> then reads all of them on a background loop and raises
/// <see cref="OnInputPolled"/> once per device per tick, which is what
/// <c>MainWindowViewModel.ProcessInputPolling</c> reacts to. vJoy/ViGEm virtual devices are
/// filtered out of <see cref="GetConnectedDevices"/> by name so the app never tries to read
/// its own virtual output back in as if it were a new physical controller.
/// </summary>
public class InputDeviceManager : IDisposable
{
    private readonly DirectInput _directInput;
    private readonly Dictionary<Guid, Joystick> _acquiredJoysticks = new();
    private CancellationTokenSource? _pollingCts;
    private Thread? _pollingThread;

    /// <summary>Guids already logged as a failed-to-acquire device, so a persistently-failing device (rather than a one-off transient race) logs once instead of once per 1.5s hotplug retry forever. Cleared on successful acquire or when the device disappears from enumeration.</summary>
    private readonly HashSet<Guid> _loggedAcquireFailures = new();

    /// <summary>Independent, much slower timer than the poll loop - re-runs <see cref="RefreshDevices"/> periodically so a hotplug (controller plugged/unplugged mid-session) is picked up without an app restart. Same pattern as <see cref="ControllerHidService"/>'s own device-refresh timer.</summary>
    private Timer? _hotplugTimer;
    private const int HotplugRefreshIntervalMs = 1500;

    public event Action<List<DeviceHardwareInfo>>? OnDevicesRefreshed;
    public event Action<RawDeviceInputState>? OnInputPolled;

    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECCR", "input-debug.log");
    private const long MaxLogSizeBytes = 5 * 1024 * 1024;

    /// <summary>Diagnostic log for device acquisition failures and background-loop exceptions - previously these were bare `catch { }` swallows with no trace, making a real "this specific device never responds" report indistinguishable from a transient hotplug race.</summary>
    private static void LogDebug(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
            if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MaxLogSizeBytes)
                File.Delete(LogFilePath);
            File.AppendAllText(LogFilePath, $"[{DateTime.Now:HH:mm:ss.fff}] [InputDevice] {message}\n");
        }
        catch { }
    }

    public InputDeviceManager()
    {
        _directInput = new DirectInput();
    }

    public List<DeviceHardwareInfo> GetConnectedDevices()
    {
        var list = new List<DeviceHardwareInfo>();
        try
        {
            var devices = _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
            foreach (var d in devices)
            {
                var (vendorId, productId) = ParseVidPidFromProductGuid(d.ProductGuid);

                // VID/PID-aware check (not a bare name substring) - necessary because ViGEm's
                // emulated Xbox 360 pad deliberately impersonates a real Microsoft controller and
                // its DirectInput name ("Controller (XBOX 360 For Windows)") contains none of
                // "vjoy"/"vigem"/"virtual". See HidHideManager.IsVirtualDevice's doc comment for
                // why VID/PID 045E:028E has to be matched unconditionally rather than by name.
                if (HidHideManager.IsVirtualDevice(d.InstanceName, null, d.ProductName, vendorId, productId)) continue;

                // Real per-device capabilities rather than a one-size-fits-all guess, so
                // DevicePresetService.GeneratePreset can trim its presets to what this specific
                // device actually has instead of assuming every device is a 32-button/8-axis
                // superset. Queried via a short-lived Joystick that's never Acquire()'d - reading
                // Capabilities doesn't require acquisition, and RefreshDevices (below) owns the
                // long-lived acquired instance for actual polling.
                int buttonCount = 32;
                int axisCount = 8;
                bool hasPov = true;
                try
                {
                    using var probe = new Joystick(_directInput, d.InstanceGuid);
                    var caps = probe.Capabilities;
                    buttonCount = caps.ButtonCount;
                    axisCount = Math.Min(caps.AxeCount, 8); // RawDeviceInputState.Axes is a fixed 8-wide array
                    hasPov = caps.PovCount > 0;
                }
                catch { /* keep the 32/8/true fallback */ }

                list.Add(new DeviceHardwareInfo
                {
                    InstanceGuid = d.InstanceGuid,
                    ProductGuid = d.ProductGuid,
                    InstanceName = d.InstanceName,
                    ProductName = d.ProductName,
                    ButtonCount = buttonCount,
                    AxisCount = axisCount,
                    HasPov = hasPov,
                    VendorId = vendorId,
                    ProductId = productId
                });
            }
        }
        catch { }
        return list;
    }

    /// <summary>Decodes a DirectInput product GUID's embedded USB VID/PID (see <see cref="DeviceHardwareInfo.VendorId"/>'s doc comment). Returns (0, 0) if the GUID doesn't follow the standard layout.</summary>
    private static (int VendorId, int ProductId) ParseVidPidFromProductGuid(Guid productGuid)
    {
        try
        {
            var bytes = productGuid.ToByteArray();
            int vendorId = bytes[0] | (bytes[1] << 8);
            int productId = bytes[2] | (bytes[3] << 8);
            return (vendorId, productId);
        }
        catch
        {
            return (0, 0);
        }
    }

    public void RefreshDevices()
    {
        var devices = GetConnectedDevices();
        lock (_acquiredJoysticks)
        {
            var currentGuids = devices.Select(d => d.InstanceGuid).ToHashSet();
            var toRemove = _acquiredJoysticks.Keys.Where(g => !currentGuids.Contains(g)).ToList();

            foreach (var g in toRemove)
            {
                try
                {
                    _acquiredJoysticks[g].Unacquire();
                    _acquiredJoysticks[g].Dispose();
                }
                catch { }
                _acquiredJoysticks.Remove(g);
                _loggedAcquireFailures.Remove(g);
            }

            foreach (var d in devices)
            {
                if (!_acquiredJoysticks.ContainsKey(d.InstanceGuid))
                {
                    try
                    {
                        var joystick = new Joystick(_directInput, d.InstanceGuid);
                        joystick.Properties.BufferSize = 128;
                        joystick.Acquire();
                        _acquiredJoysticks[d.InstanceGuid] = joystick;
                        _loggedAcquireFailures.Remove(d.InstanceGuid);
                    }
                    catch (Exception ex)
                    {
                        // Logged once per persistent failure (not every 1.5s hotplug retry) - see
                        // _loggedAcquireFailures's doc comment. This is what used to be a bare
                        // catch { } with zero trace of why a specific device never came online.
                        if (_loggedAcquireFailures.Add(d.InstanceGuid))
                        {
                            LogDebug($"Acquire failed for '{d.InstanceName}' (VID={d.VendorId:X4} PID={d.ProductId:X4}): {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
            }
        }

        OnDevicesRefreshed?.Invoke(devices);
    }

    /// <summary>Starts the periodic re-enumeration timer that detects hotplug events. Call once, alongside <see cref="StartPolling"/>.</summary>
    public void StartHotplugWatcher()
    {
        _hotplugTimer?.Dispose();
        // An unhandled exception inside a System.Threading.Timer callback is fatal to the whole
        // process - RefreshDevices() has no top-level guard of its own, so one is needed here.
        _hotplugTimer = new Timer(_ =>
        {
            try { RefreshDevices(); }
            catch (Exception ex) { LogDebug($"Hotplug refresh threw: {ex.GetType().Name}: {ex.Message}"); }
        }, null, dueTime: HotplugRefreshIntervalMs, period: HotplugRefreshIntervalMs);
    }

    /// <summary>
    /// Runs on a dedicated background <see cref="Thread"/> (not a thread-pool <c>Task.Run</c>) -
    /// this loop spins continuously at <paramref name="pollIntervalMs"/> cadence for the entire
    /// app lifetime, which is exactly what a dedicated thread is for rather than permanently
    /// occupying a thread-pool worker slot.
    /// </summary>
    public void StartPolling(int pollIntervalMs = 4)
    {
        StopPolling();
        _pollingCts = new CancellationTokenSource();
        var token = _pollingCts.Token;

        _pollingThread = new Thread(() =>
        {
            while (!token.IsCancellationRequested)
            {
                List<KeyValuePair<Guid, Joystick>> joysticksCopy;
                lock (_acquiredJoysticks)
                {
                    joysticksCopy = _acquiredJoysticks.ToList();
                }

                foreach (var kvp in joysticksCopy)
                {
                    if (token.IsCancellationRequested) break;

                    RawDeviceInputState? polledState = null;

                    try
                    {
                        var joystick = kvp.Value;
                        joystick.Poll();
                        var state = joystick.GetCurrentState();

                        // Fixed axis order every mapping/preset/display-name table in the app
                        // assumes: X, Y, Z, RotationX, RotationY, RotationZ, Slider0, Slider1.
                        int[] axes = [
                            state.X,
                            state.Y,
                            state.Z,
                            state.RotationX,
                            state.RotationY,
                            state.RotationZ,
                            state.Sliders.Length > 0 ? state.Sliders[0] : 0,
                            state.Sliders.Length > 1 ? state.Sliders[1] : 0
                        ];

                        bool[] buttons = state.Buttons;

                        // Hoisted out of the `if` below so it's available for RawDeviceInputState
                        // even when this device has no POV hat at all (stays at the "centered/none"
                        // sentinel -1 in that case) - the diagnostics panel wants the raw angle
                        // regardless of whether a POV is present.
                        int pov = -1;

                        if (state.PointOfViewControllers.Length > 0)
                        {
                            // DirectInput reports a POV hat as a single angle in hundredths of
                            // a degree (0 = up, clockwise), or -1 when centered/released. Only
                            // the 8 principal directions are handled here (diagonals like
                            // 4500 = up-right set both Up and Right), which is enough for a
                            // D-pad; a hat with finer resolution would need more angle checks.
                            pov = state.PointOfViewControllers[0];
                            bool up = pov >= 0 && (pov == 0 || pov == 4500 || pov == 31500);
                            bool right = pov >= 0 && (pov == 4500 || pov == 9000 || pov == 13500);
                            bool down = pov >= 0 && (pov == 13500 || pov == 18000 || pov == 22500);
                            bool left = pov >= 0 && (pov == 22500 || pov == 27000 || pov == 31500);

                            // Fold the hat's 4 directions into synthetic button indices
                            // 128-131 so the rest of the app (mapping capture, display names,
                            // presets) can treat "D-Pad Up" as just another button rather than
                            // needing a whole separate POV concept.
                            var expandedButtons = new bool[132];
                            Array.Copy(buttons, expandedButtons, Math.Min(buttons.Length, 128));
                            expandedButtons[128] = up;
                            expandedButtons[129] = right;
                            expandedButtons[130] = down;
                            expandedButtons[131] = left;
                            buttons = expandedButtons;
                        }

                        polledState = new RawDeviceInputState
                        {
                            InstanceGuid = kvp.Key,
                            DeviceName = joystick.Information.InstanceName,
                            Axes = axes,
                            Buttons = buttons,
                            PovAngleHundredths = pov
                        };
                    }
                    catch
                    {
                        try { kvp.Value.Acquire(); } catch { }
                    }

                    // Deliberately outside the catch above: a throw from a subscriber (e.g.
                    // MainWindowViewModel.ProcessInputPolling) must never be misattributed as a
                    // device read failure and trigger a spurious Acquire() retry on a device that
                    // was actually read just fine this tick.
                    if (polledState != null)
                    {
                        try { OnInputPolled?.Invoke(polledState); }
                        catch (Exception ex) { LogDebug($"OnInputPolled subscriber threw: {ex.GetType().Name}: {ex.Message}"); }
                    }
                }

                try { Thread.Sleep(pollIntervalMs); }
                catch { break; }
            }
        })
        { IsBackground = true, Name = "InputPolling", Priority = ThreadPriority.AboveNormal };
        _pollingThread.Start();
    }

    public void StopPolling()
    {
        _hotplugTimer?.Dispose();
        _hotplugTimer = null;

        var cts = _pollingCts;
        _pollingCts = null;
        if (cts != null)
        {
            try
            {
                cts.Cancel();
                // Give the loop a real chance to observe cancellation and return before Dispose()
                // tears down the same Joystick/DirectInput objects it may still be touching - a
                // loop that can be mid-Poll()/GetCurrentState() rather than just sitting in a sleep.
                _pollingThread?.Join(500);
            }
            catch { }
            finally
            {
                cts.Dispose();
                _pollingThread = null;
            }
        }
    }

    public void Dispose()
    {
        _hotplugTimer?.Dispose();
        _hotplugTimer = null;
        StopPolling();
        lock (_acquiredJoysticks)
        {
            foreach (var j in _acquiredJoysticks.Values)
            {
                try
                {
                    j.Unacquire();
                    j.Dispose();
                }
                catch { }
            }
            _acquiredJoysticks.Clear();
        }
        try { _directInput.Dispose(); } catch { }
    }
}