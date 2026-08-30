using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    private Task? _pollingTask;

    public event Action<List<DeviceHardwareInfo>>? OnDevicesRefreshed;
    public event Action<RawDeviceInputState>? OnInputPolled;

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
                string name = d.InstanceName.ToLowerInvariant();
                if (name.Contains("vjoy") || name.Contains("vigem") || name.Contains("virtual")) continue;

                list.Add(new DeviceHardwareInfo
                {
                    InstanceGuid = d.InstanceGuid,
                    ProductGuid = d.ProductGuid,
                    InstanceName = d.InstanceName,
                    ProductName = d.ProductName,
                    ButtonCount = 32,
                    AxisCount = 8
                });
            }
        }
        catch { }
        return list;
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
                    }
                    catch { }
                }
            }
        }

        OnDevicesRefreshed?.Invoke(devices);
    }

    public void StartPolling(int pollIntervalMs = 4)
    {
        StopPolling();
        _pollingCts = new CancellationTokenSource();
        var token = _pollingCts.Token;

        _pollingTask = Task.Run(async () =>
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

                        if (state.PointOfViewControllers.Length > 0)
                        {
                            // DirectInput reports a POV hat as a single angle in hundredths of
                            // a degree (0 = up, clockwise), or -1 when centered/released. Only
                            // the 8 principal directions are handled here (diagonals like
                            // 4500 = up-right set both Up and Right), which is enough for a
                            // D-pad; a hat with finer resolution would need more angle checks.
                            int pov = state.PointOfViewControllers[0];
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

                        OnInputPolled?.Invoke(new RawDeviceInputState
                        {
                            InstanceGuid = kvp.Key,
                            DeviceName = joystick.Information.InstanceName,
                            Axes = axes,
                            Buttons = buttons
                        });
                    }
                    catch
                    {
                        try { kvp.Value.Acquire(); } catch { }
                    }
                }

                try { await Task.Delay(pollIntervalMs, token); }
                catch { break; }
            }
        }, token);
    }

    public void StopPolling()
    {
        var cts = _pollingCts;
        _pollingCts = null;
        if (cts != null)
        {
            try
            {
                cts.Cancel();
                _pollingTask?.Wait(100);
            }
            catch { }
            finally
            {
                cts.Dispose();
            }
        }
    }

    public void Dispose()
    {
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