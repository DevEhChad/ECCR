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
    public bool[] Buttons { get; set; } = Array.Empty<bool>();
    public int[] PointOfViewControllers { get; set; } = Array.Empty<int>();
}

public class ConnectedDeviceInfo
{
    public Guid InstanceGuid { get; set; }
    public string InstanceName { get; set; } = string.Empty;
    public DeviceType Type { get; set; }
    public int ButtonCount { get; set; } = 32;
    public int AxisCount { get; set; } = 8;
}

public class InputDeviceManager : IDisposable
{
    private readonly DirectInput _directInput = new();
    private readonly List<Joystick> _activeJoysticks = new();
    private readonly List<ConnectedDeviceInfo> _connectedDevices = new();
    private Thread? _pollThread;
    private bool _isPolling;

    public event Action<List<ConnectedDeviceInfo>>? OnDevicesRefreshed;
    public event Action<RawDeviceInputState>? OnInputPolled;

    public List<ConnectedDeviceInfo> GetConnectedDevices()
    {
        lock (_connectedDevices)
        {
            return _connectedDevices.ToList();
        }
    }

    public void RefreshDevices()
    {
        lock (_activeJoysticks)
        {
            foreach (var js in _activeJoysticks)
            {
                try { js.Unacquire(); js.Dispose(); } catch { }
            }
            _activeJoysticks.Clear();

            lock (_connectedDevices)
            {
                _connectedDevices.Clear();

                var devices = _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AllDevices);

                foreach (var deviceInstance in devices)
                {
                    try
                    {
                        var joystick = new Joystick(_directInput, deviceInstance.InstanceGuid);
                        joystick.Acquire();
                        _activeJoysticks.Add(joystick);

                        var caps = joystick.Capabilities;

                        _connectedDevices.Add(new ConnectedDeviceInfo
                        {
                            InstanceGuid = deviceInstance.InstanceGuid,
                            InstanceName = deviceInstance.InstanceName,
                            Type = deviceInstance.Type,
                            ButtonCount = Math.Max(caps.ButtonCount, 32),
                            AxisCount = Math.Max(caps.AxeCount, 8)
                        });
                    }
                    catch { }
                }

                OnDevicesRefreshed?.Invoke(_connectedDevices.ToList());
            }
        }
    }

    public void StartPolling(int pollIntervalMs = 4)
    {
        if (_isPolling) return;
        _isPolling = true;

        _pollThread = new Thread(() =>
        {
            while (_isPolling)
            {
                lock (_activeJoysticks)
                {
                    for (int i = 0; i < _activeJoysticks.Count; i++)
                    {
                        var joystick = _activeJoysticks[i];
                        try
                        {
                            joystick.Poll();
                            var state = joystick.GetCurrentState();
                            var info = _connectedDevices[i];

                            bool[] rawButtons = state.Buttons;
                            bool[] totalButtons = new bool[rawButtons.Length + 4];
                            Array.Copy(rawButtons, totalButtons, rawButtons.Length);

                            if (state.PointOfViewControllers.Length > 0)
                            {
                                int pov = state.PointOfViewControllers[0];
                                if (pov >= 0)
                                {
                                    totalButtons[rawButtons.Length + 0] = (pov >= 31500 || pov <= 4500);  // POV Up
                                    totalButtons[rawButtons.Length + 1] = (pov >= 4500 && pov <= 13500);  // POV Right
                                    totalButtons[rawButtons.Length + 2] = (pov >= 13500 && pov <= 22500); // POV Down
                                    totalButtons[rawButtons.Length + 3] = (pov >= 22500 && pov <= 31500); // POV Left
                                }
                            }

                            var inputState = new RawDeviceInputState
                            {
                                InstanceGuid = info.InstanceGuid,
                                DeviceName = info.InstanceName,
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
                                },
                                Buttons = totalButtons,
                                PointOfViewControllers = state.PointOfViewControllers
                            };

                            OnInputPolled?.Invoke(inputState);
                        }
                        catch { }
                    }
                }

                Thread.Sleep(pollIntervalMs);
            }
        })
        {
            IsBackground = true
        };

        _pollThread.Start();
    }

    public void StopPolling()
    {
        _isPolling = false;
    }

    public void Dispose()
    {
        StopPolling();
        lock (_activeJoysticks)
        {
            foreach (var js in _activeJoysticks)
            {
                try { js.Unacquire(); js.Dispose(); } catch { }
            }
            _activeJoysticks.Clear();
        }
        _directInput.Dispose();
    }
}