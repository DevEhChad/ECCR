using System;
using System.Collections.Generic;
using System.Linq;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace ECCR.Services;

/// <summary>
/// ViGEm-backed virtual DualShock4 output - the native-PlayStation-layout sibling of
/// <see cref="VirtualFeederService"/> (Xbox 360). Handles only "[DS4] ..." targets (see
/// <see cref="ECCR.Models.MappingEntry"/> for the string-prefix routing scheme); shares the
/// same one-controller-per-Player-target-id lazy-creation pattern. DS4 sticks/triggers are
/// unsigned bytes (0..255, centered at 128 for sticks / resting at 0 for triggers) rather than
/// Xbox 360's signed shorts, and the D-Pad is a single 8-direction enum rather than 4
/// independent buttons - <see cref="UpdateAxis"/>/<see cref="UpdateButton"/> account for both
/// differences.
/// </summary>
public class DualShock4FeederService : IVirtualFeeder
{
    private readonly ViGEmClient? _viGEmClient;
    private readonly Dictionary<uint, IDualShock4Controller> _controllers = new();

    // Independent per-controller D-Pad axis state, since ViGEm exposes the DS4 D-Pad as one
    // 8-way direction rather than 4 independent digital buttons like Xbox 360/most channels
    // here assume - each of the 4 D-Pad directions updates its own flag and the pair is
    // recombined into one DualShock4DPadDirection on every change.
    private readonly Dictionary<uint, (bool Up, bool Down, bool Left, bool Right)> _dpadState = new();

    private readonly object _lock = new();
    private bool _isActive = true;

    /// <summary>Raised whenever a game rumbles a virtual DS4 controller: (targetDeviceId, largeMotor, smallMotor).</summary>
    public event Action<uint, byte, byte>? RumbleReceived;

    public bool IsActive => _isActive;

    public DualShock4FeederService()
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
                foreach (var controller in _controllers.Values)
                {
                    try { controller.Disconnect(); } catch { }
                }
                _controllers.Clear();
                _dpadState.Clear();
            }
        }
    }

    private IDualShock4Controller? GetOrCreateController(uint targetDeviceId)
    {
        if (!_isActive || _viGEmClient == null) return null;

        lock (_lock)
        {
            if (!_isActive) return null;

            if (_controllers.TryGetValue(targetDeviceId, out var existing))
            {
                return existing;
            }

            try
            {
                var controller = _viGEmClient.CreateDualShock4Controller();
#pragma warning disable CS0618 // ViGEm's DS4 FeedbackReceived is flagged obsolete in favor of AwaitRawOutputReport(), but is still the simplest working rumble signal for this backend.
                controller.FeedbackReceived += (_, e) =>
                    RumbleReceived?.Invoke(targetDeviceId, e.LargeMotor, e.SmallMotor);
#pragma warning restore CS0618
                controller.Connect();
                _controllers[targetDeviceId] = controller;
                _dpadState[targetDeviceId] = (false, false, false, false);
                return controller;
            }
            catch
            {
                return null;
            }
        }
    }

    public void UpdateAxis(uint targetDeviceId, string targetOutput, double normalizedValue)
    {
        if (!_isActive) return;
        if (!targetOutput.StartsWith("[DS4]", StringComparison.OrdinalIgnoreCase)) return;

        var controller = GetOrCreateController(targetDeviceId);
        if (controller == null) return;

        try
        {
            if (targetOutput.Contains("Left Stick X") || targetOutput.Contains("Steer / Horizontal"))
            {
                controller.SetAxisValue(DualShock4Axis.LeftThumbX, ToDs4StickByte(normalizedValue));
            }
            else if (targetOutput.Contains("Left Stick Y") || targetOutput.Contains("Steer / Vertical"))
            {
                controller.SetAxisValue(DualShock4Axis.LeftThumbY, ToDs4StickByte(normalizedValue));
            }
            else if (targetOutput.Contains("Right Stick X") || targetOutput.Contains("Camera / Look Horizontal"))
            {
                controller.SetAxisValue(DualShock4Axis.RightThumbX, ToDs4StickByte(normalizedValue));
            }
            else if (targetOutput.Contains("Right Stick Y") || targetOutput.Contains("Camera / Look Vertical"))
            {
                controller.SetAxisValue(DualShock4Axis.RightThumbY, ToDs4StickByte(normalizedValue));
            }
            else if (targetOutput.Contains("L2 Trigger") || targetOutput.Contains("Brake Axis"))
            {
                byte val = (byte)(Math.Clamp(normalizedValue, 0.0, 1.0) * 255.0);
                controller.SetSliderValue(DualShock4Slider.LeftTrigger, val);
            }
            else if (targetOutput.Contains("R2 Trigger") || targetOutput.Contains("Gas Axis"))
            {
                byte val = (byte)(Math.Clamp(normalizedValue, 0.0, 1.0) * 255.0);
                controller.SetSliderValue(DualShock4Slider.RightTrigger, val);
            }
            else
            {
                // No free analog input left (mirrors VirtualFeederService.UpdateAxis's same
                // fallback for the Xbox 360 backend) - threshold into the closest digital button.
                var btn = MapTargetToDs4Button(targetOutput);
                if (btn != null)
                {
                    controller.SetButtonState(btn, normalizedValue > 0.5);
                }
            }

            controller.SubmitReport();
        }
        catch { }
    }

    public void UpdateButton(uint targetDeviceId, string targetOutput, bool isPressed)
    {
        if (!_isActive) return;
        if (!targetOutput.StartsWith("[DS4]", StringComparison.OrdinalIgnoreCase)) return;

        var controller = GetOrCreateController(targetDeviceId);
        if (controller == null) return;

        try
        {
            if (targetOutput.Contains("L2 Trigger"))
            {
                controller.SetSliderValue(DualShock4Slider.LeftTrigger, isPressed ? (byte)255 : (byte)0);
                controller.SubmitReport();
                return;
            }

            if (targetOutput.Contains("R2 Trigger"))
            {
                controller.SetSliderValue(DualShock4Slider.RightTrigger, isPressed ? (byte)255 : (byte)0);
                controller.SubmitReport();
                return;
            }

            if (targetOutput.Contains("PS (Guide)"))
            {
                controller.SetButtonState(DualShock4SpecialButton.Ps, isPressed);
                controller.SubmitReport();
                return;
            }

            if (targetOutput.Contains("Touchpad Click"))
            {
                controller.SetButtonState(DualShock4SpecialButton.Touchpad, isPressed);
                controller.SubmitReport();
                return;
            }

            if (targetOutput.Contains("D-Pad"))
            {
                UpdateDPad(targetDeviceId, controller, targetOutput, isPressed);
                controller.SubmitReport();
                return;
            }

            var btn = MapTargetToDs4Button(targetOutput);
            if (btn != null)
            {
                controller.SetButtonState(btn, isPressed);
                controller.SubmitReport();
            }
        }
        catch { }
    }

    private void UpdateDPad(uint targetDeviceId, IDualShock4Controller controller, string targetOutput, bool isPressed)
    {
        (bool Up, bool Down, bool Left, bool Right) state = _dpadState.TryGetValue(targetDeviceId, out var s)
            ? s
            : (Up: false, Down: false, Left: false, Right: false);

        if (targetOutput.Contains("Up")) state.Up = isPressed;
        else if (targetOutput.Contains("Down")) state.Down = isPressed;
        else if (targetOutput.Contains("Left")) state.Left = isPressed;
        else if (targetOutput.Contains("Right")) state.Right = isPressed;

        _dpadState[targetDeviceId] = state;

        var direction = (state.Up, state.Down, state.Left, state.Right) switch
        {
            (true, false, false, true) => DualShock4DPadDirection.Northeast,
            (true, false, true, false) => DualShock4DPadDirection.Northwest,
            (false, true, false, true) => DualShock4DPadDirection.Southeast,
            (false, true, true, false) => DualShock4DPadDirection.Southwest,
            (true, false, false, false) => DualShock4DPadDirection.North,
            (false, true, false, false) => DualShock4DPadDirection.South,
            (false, false, true, false) => DualShock4DPadDirection.West,
            (false, false, false, true) => DualShock4DPadDirection.East,
            _ => DualShock4DPadDirection.None
        };

        controller.SetDPadDirection(direction);
    }

    private static byte ToDs4StickByte(double normalizedValue) =>
        (byte)Math.Clamp(normalizedValue * 255.0, 0.0, 255.0);

    private static DualShock4Button? MapTargetToDs4Button(string targetOutput)
    {
        if (targetOutput.Contains("Cross")) return DualShock4Button.Cross;
        if (targetOutput.Contains("Circle")) return DualShock4Button.Circle;
        if (targetOutput.Contains("Square")) return DualShock4Button.Square;
        if (targetOutput.Contains("Triangle")) return DualShock4Button.Triangle;
        if (targetOutput.Contains("L1")) return DualShock4Button.ShoulderLeft;
        if (targetOutput.Contains("R1")) return DualShock4Button.ShoulderRight;
        if (targetOutput.Contains("L3")) return DualShock4Button.ThumbLeft;
        if (targetOutput.Contains("R3")) return DualShock4Button.ThumbRight;
        if (targetOutput.Contains("Options")) return DualShock4Button.Options;
        if (targetOutput.Contains("Share")) return DualShock4Button.Share;

        return null;
    }

    public List<uint> GetActivePlayerTargetIds()
    {
        lock (_lock) { return _controllers.Keys.OrderBy(k => k).ToList(); }
    }

    public void Reset()
    {
        lock (_lock)
        {
            foreach (var controller in _controllers.Values)
            {
                try
                {
                    controller.SetAxisValue(DualShock4Axis.LeftThumbX, 128);
                    controller.SetAxisValue(DualShock4Axis.LeftThumbY, 128);
                    controller.SetAxisValue(DualShock4Axis.RightThumbX, 128);
                    controller.SetAxisValue(DualShock4Axis.RightThumbY, 128);
                    controller.SetSliderValue(DualShock4Slider.LeftTrigger, 0);
                    controller.SetSliderValue(DualShock4Slider.RightTrigger, 0);
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
                foreach (var controller in _controllers.Values)
                {
                    try { controller.Disconnect(); } catch { }
                }
                _controllers.Clear();
                _dpadState.Clear();
            }
        }
        catch { }
        try { _viGEmClient?.Dispose(); } catch { }
    }
}
