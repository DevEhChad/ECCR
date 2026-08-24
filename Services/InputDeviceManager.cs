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
                            int pov = state.PointOfViewControllers[0];
                            bool up = pov >= 0 && (pov == 0 || pov == 4500 || pov == 31500);
                            bool right = pov >= 0 && (pov == 4500 || pov == 9000 || pov == 13500);
                            bool down = pov >= 0 && (pov == 13500 || pov == 18000 || pov == 22500);
                            bool left = pov >= 0 && (pov == 22500 || pov == 27000 || pov == 31500);

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