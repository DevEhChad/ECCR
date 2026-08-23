using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SharpDX.DirectInput;

namespace ECCR.Services;

public class RawDeviceInputState
{
    public Guid InstanceGuid { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public int[] Axes { get; set; } = new int[8];
    public bool[] Buttons { get; set; } = new bool[132];
}

public class ConnectedDeviceInfo
{
    public Guid InstanceGuid { get; set; }
    public string InstanceName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int ButtonCount { get; set; }
    public int AxisCount { get; set; }
}

public class InputDeviceManager : IDisposable
{
    private readonly DirectInput _directInput;
    private readonly List<Joystick> _activeJoysticks = new();
    private readonly List<ConnectedDeviceInfo> _connectedDevices = new();
    private readonly object _lock = new();

    private Thread? _pollingThread;
    private bool _isPolling;

    public event Action<List<ConnectedDeviceInfo>>? OnDevicesRefreshed;
    public event Action<RawDeviceInputState>? OnInputPolled;

    public InputDeviceManager()
    {
        _directInput = new DirectInput();
    }

    public void RefreshDevices()
    {
        lock (_lock)
        {
            foreach (var j in _activeJoysticks)
            {
                try
                {
                    j.Unacquire();
                    j.Dispose();
                }
                catch { }
            }
            _activeJoysticks.Clear();
            _connectedDevices.Clear();

            var devices = _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);

            foreach (var dev in devices)
            {
                if (IsVirtualDevice(dev))
                {
                    continue;
                }

                try
                {
                    var joystick = new Joystick(_directInput, dev.InstanceGuid);
                    joystick.Acquire();

                    int buttonCount = joystick.Capabilities.ButtonCount;
                    int axisCount = joystick.Capabilities.AxeCount;

                    _activeJoysticks.Add(joystick);
                    _connectedDevices.Add(new ConnectedDeviceInfo
                    {
                        InstanceGuid = dev.InstanceGuid,
                        InstanceName = dev.InstanceName ?? string.Empty,
                        ProductName = dev.ProductName ?? string.Empty,
                        ButtonCount = Math.Max(buttonCount, 16),
                        AxisCount = Math.Max(axisCount, 6)
                    });
                }
                catch { }
            }
        }

        OnDevicesRefreshed?.Invoke(GetConnectedDevices());
    }

    private static bool IsVirtualDevice(DeviceInstance dev)
    {
        string name = (dev.InstanceName ?? string.Empty).ToLowerInvariant();
        string prod = (dev.ProductName ?? string.Empty).ToLowerInvariant();
        string guidStr = dev.ProductGuid.ToString().ToLowerInvariant();

        if (name.Contains("vjoy") || prod.Contains("vjoy") ||
            name.Contains("vigem") || prod.Contains("vigem") ||
            name.Contains("nefarius") || prod.Contains("nefarius") ||
            name.Contains("virtual") || prod.Contains("virtual") ||
            name.Contains("root#") || prod.Contains("root#") ||
            name.Contains("software device"))
        {
            return true;
        }

        if (guidStr.StartsWith("028e045e") || prod.Contains("xbox 360 for windows"))
        {
            return true;
        }

        if (guidStr.StartsWith("0be31234"))
        {
            return true;
        }

        return false;
    }

    public List<ConnectedDeviceInfo> GetConnectedDevices()
    {
        lock (_lock)
        {
            return _connectedDevices.ToList();
        }
    }

    public void StartPolling(int pollIntervalMs = 4)
    {
        if (_isPolling) return;
        _isPolling = true;

        _pollingThread = new Thread(() =>
        {
            while (_isPolling)
            {
                PollAllDevices();
                Thread.Sleep(pollIntervalMs);
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };

        _pollingThread.Start();
    }

    private void PollAllDevices()
    {
        lock (_lock)
        {
            for (int i = 0; i < _activeJoysticks.Count; i++)
            {
                var joystick = _activeJoysticks[i];
                try
                {
                    joystick.Poll();
                    var state = joystick.GetCurrentState();

                    var inputState = new RawDeviceInputState
                    {
                        InstanceGuid = joystick.Information.InstanceGuid,
                        DeviceName = joystick.Information.InstanceName ?? string.Empty,
                        Axes = new int[]
                        {
                            state.X,
                            state.Y,
                            state.Z,
                            state.RotationX,
                            state.RotationY,
                            state.RotationZ,
                            state.Sliders.Length > 0 ? state.Sliders[0] : 0,
                            state.Sliders.Length > 1 ? state.Sliders[1] : 0
                        }
                    };

                    bool[] rawButtons = state.Buttons;
                    for (int b = 0; b < Math.Min(rawButtons.Length, 128); b++)
                    {
                        inputState.Buttons[b] = rawButtons[b];
                    }

                    if (state.PointOfViewControllers.Length > 0)
                    {
                        int pov = state.PointOfViewControllers[0];
                        if (pov >= 0)
                        {
                            inputState.Buttons[128] = (pov >= 31500 || pov <= 4500);
                            inputState.Buttons[129] = (pov >= 4500 && pov <= 13500);
                            inputState.Buttons[130] = (pov >= 13500 && pov <= 22500);
                            inputState.Buttons[131] = (pov >= 22500 && pov <= 31500);
                        }
                    }

                    OnInputPolled?.Invoke(inputState);
                }
                catch
                {
                    try { joystick.Acquire(); } catch { }
                }
            }
        }
    }

    public void StopPolling()
    {
        _isPolling = false;
        _pollingThread?.Join(200);
        _pollingThread = null;
    }

    public void Dispose()
    {
        StopPolling();
        lock (_lock)
        {
            foreach (var j in _activeJoysticks)
            {
                try
                {
                    j.Unacquire();
                    j.Dispose();
                }
                catch { }
            }
            _activeJoysticks.Clear();
        }
        _directInput.Dispose();
    }
}