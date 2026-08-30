using System;
using System.Collections.Generic;
using System.Linq;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace ECCR.Services;

/// <summary>
/// ViGEm-backed half of the app's virtual output: emulates up to four independent virtual
/// Xbox 360 controllers via ViGEmBus, one per Player target ID, created lazily the first
/// time something maps to it. Only handles "[Xbox] ..." targets - see
/// <see cref="ECCR.Models.MappingEntry"/> for why routing is done by string prefix, and
/// <see cref="VJoyFeederService"/> for the sibling backend that handles "[Wheel] ..." targets.
/// Xbox 360 pads only expose 2 sticks, 2 triggers, and ~14 buttons, so this is the simpler
/// but more limited of the two backends - full wheel setups (H-pattern shifters, more than a
/// couple of dozen buttons) need the vJoy path instead.
/// </summary>
public class VirtualFeederService : IVirtualFeeder
{
    private readonly ViGEmClient? _viGEmClient;
    private readonly Dictionary<uint, IXbox360Controller> _xboxControllers = new();
    private readonly object _lock = new();
    private bool _isActive = true;

    public bool IsActive => _isActive;

    public VirtualFeederService()
    {
        try
        {
            _viGEmClient = new ViGEmClient();
        }
        catch
        {
            _viGEmClient = null;
        }
    }

    public void SetActive(bool active)
    {
        lock (_lock)
        {
            _isActive = active;
            if (!active)
            {
                foreach (var controller in _xboxControllers.Values)
                {
                    try
                    {
                        controller.Disconnect();
                    }
                    catch { }
                }
                _xboxControllers.Clear();
            }
        }
    }

    public List<uint> GetActivePlayerTargetIds()
    {
        lock (_lock)
        {
            return _xboxControllers.Keys.OrderBy(k => k).ToList();
        }
    }

    private IXbox360Controller? GetOrCreateXboxController(uint targetDeviceId)
    {
        if (!_isActive || _viGEmClient == null) return null;

        lock (_lock)
        {
            if (!_isActive) return null;

            if (_xboxControllers.TryGetValue(targetDeviceId, out var existing))
            {
                return existing;
            }

            try
            {
                var controller = _viGEmClient.CreateXbox360Controller();
                controller.Connect();
                _xboxControllers[targetDeviceId] = controller;
                return controller;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Routes one calibrated axis value to the matching Xbox 360 report field, matched by
    /// substring against <paramref name="targetOutput"/>. Thumbstick axes are ViGEm
    /// <c>short</c> (-32768..32767, centered at 0) so the incoming 0..1 value is rescaled by
    /// <c>(value * 65535) - 32768</c>; triggers are ViGEm <c>byte</c> sliders (0..255, resting
    /// at 0) so they're just scaled by 255 directly - see <see cref="UpdateButton"/> for the
    /// button-side scheme.
    /// </summary>
    public void UpdateAxis(uint targetDeviceId, string targetOutput, double normalizedValue)
    {
        if (!_isActive) return;

        if (!targetOutput.StartsWith("[Xbox]", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var controller = GetOrCreateXboxController(targetDeviceId);
        if (controller == null) return;

        try
        {
            if (targetOutput.Contains("Left Stick X") || targetOutput.Contains("Steer / Horizontal"))
            {
                short val = (short)Math.Clamp((normalizedValue * 65535.0) - 32768.0, -32768.0, 32767.0);
                controller.SetAxisValue(Xbox360Axis.LeftThumbX, val);
            }
            else if (targetOutput.Contains("Left Stick Y") || targetOutput.Contains("Steer / Vertical"))
            {
                short val = (short)Math.Clamp((normalizedValue * 65535.0) - 32768.0, -32768.0, 32767.0);
                controller.SetAxisValue(Xbox360Axis.LeftThumbY, val);
            }
            else if (targetOutput.Contains("Right Stick X") || targetOutput.Contains("Camera / Look Horizontal"))
            {
                short val = (short)Math.Clamp((normalizedValue * 65535.0) - 32768.0, -32768.0, 32767.0);
                controller.SetAxisValue(Xbox360Axis.RightThumbX, val);
            }
            else if (targetOutput.Contains("Right Stick Y") || targetOutput.Contains("Camera / Look Vertical"))
            {
                short val = (short)Math.Clamp((normalizedValue * 65535.0) - 32768.0, -32768.0, 32767.0);
                controller.SetAxisValue(Xbox360Axis.RightThumbY, val);
            }
            else if (targetOutput.Contains("Left Trigger") || targetOutput.Contains("Brake Axis") || targetOutput.Contains("LT"))
            {
                byte val = (byte)(Math.Clamp(normalizedValue, 0.0, 1.0) * 255.0);
                controller.SetSliderValue(Xbox360Slider.LeftTrigger, val);
            }
            else if (targetOutput.Contains("Right Trigger") || targetOutput.Contains("Gas Axis") || targetOutput.Contains("RT"))
            {
                byte val = (byte)(Math.Clamp(normalizedValue, 0.0, 1.0) * 255.0);
                controller.SetSliderValue(Xbox360Slider.RightTrigger, val);
            }

            controller.SubmitReport();
        }
        catch { }
    }

    public void UpdateButton(uint targetDeviceId, string targetOutput, bool isPressed)
    {
        if (!_isActive) return;

        if (!targetOutput.StartsWith("[Xbox]", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var controller = GetOrCreateXboxController(targetDeviceId);
        if (controller == null) return;

        try
        {
            if (targetOutput.Contains("Left Trigger") || targetOutput.Contains("(LT"))
            {
                controller.SetSliderValue(Xbox360Slider.LeftTrigger, isPressed ? (byte)255 : (byte)0);
                controller.SubmitReport();
                return;
            }

            if (targetOutput.Contains("Right Trigger") || targetOutput.Contains("(RT"))
            {
                controller.SetSliderValue(Xbox360Slider.RightTrigger, isPressed ? (byte)255 : (byte)0);
                controller.SubmitReport();
                return;
            }

            var btn = MapTargetToXboxButton(targetOutput);
            if (btn != null)
            {
                controller.SetButtonState(btn, isPressed);
                controller.SubmitReport();
            }
        }
        catch { }
    }

    private static Xbox360Button? MapTargetToXboxButton(string targetOutput)
    {
        if (targetOutput.Contains("Xbox A") || targetOutput.Contains("Cross / South")) return Xbox360Button.A;
        if (targetOutput.Contains("Xbox B") || targetOutput.Contains("Circle / East")) return Xbox360Button.B;
        if (targetOutput.Contains("Xbox X") || targetOutput.Contains("Square / West")) return Xbox360Button.X;
        if (targetOutput.Contains("Xbox Y") || targetOutput.Contains("Triangle / North")) return Xbox360Button.Y;
        if (targetOutput.Contains("Xbox LB") || targetOutput.Contains("Left Bumper") || targetOutput.Contains("Paddle Down")) return Xbox360Button.LeftShoulder;
        if (targetOutput.Contains("Xbox RB") || targetOutput.Contains("Right Bumper") || targetOutput.Contains("Paddle Up")) return Xbox360Button.RightShoulder;
        if (targetOutput.Contains("Xbox LSB") || targetOutput.Contains("Left Stick Click") || targetOutput.Contains("/ L3")) return Xbox360Button.LeftThumb;
        if (targetOutput.Contains("Xbox RSB") || targetOutput.Contains("Right Stick Click") || targetOutput.Contains("/ R3")) return Xbox360Button.RightThumb;
        if (targetOutput.Contains("D-Pad Up")) return Xbox360Button.Up;
        if (targetOutput.Contains("D-Pad Down")) return Xbox360Button.Down;
        if (targetOutput.Contains("D-Pad Left")) return Xbox360Button.Left;
        if (targetOutput.Contains("D-Pad Right")) return Xbox360Button.Right;
        if (targetOutput.Contains("Xbox Menu") || targetOutput.Contains("Start / Options")) return Xbox360Button.Start;
        if (targetOutput.Contains("Xbox View") || targetOutput.Contains("Back / Map") || targetOutput.Contains("Share")) return Xbox360Button.Back;
        if (targetOutput.Contains("Xbox Guide") || targetOutput.Contains("Home / Guide")) return Xbox360Button.Guide;

        return null;
    }

    public void Reset()
    {
        lock (_lock)
        {
            foreach (var controller in _xboxControllers.Values)
            {
                try
                {
                    controller.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
                    controller.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
                    controller.SetAxisValue(Xbox360Axis.RightThumbX, 0);
                    controller.SetAxisValue(Xbox360Axis.RightThumbY, 0);
                    controller.SetSliderValue(Xbox360Slider.LeftTrigger, 0);
                    controller.SetSliderValue(Xbox360Slider.RightTrigger, 0);
                    controller.SubmitReport();
                }
                catch { }
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Reset();
            lock (_lock)
            {
                foreach (var controller in _xboxControllers.Values)
                {
                    try { controller.Disconnect(); } catch { }
                }
                _xboxControllers.Clear();
            }
        }
        catch { }
        try
        {
            _viGEmClient?.Dispose();
        }
        catch { }
    }
}