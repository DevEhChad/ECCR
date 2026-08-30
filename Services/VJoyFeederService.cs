using System;
using System.Collections.Generic;
using vJoyInterfaceWrap;

namespace ECCR.Services;

/// <summary>
/// Drives up to four vJoy virtual joysticks (device IDs 1-4, matching the app's
/// Player 1-4 <c>TargetDeviceId</c> convention) so that <c>[Wheel] ...</c> mapping
/// targets - previously accepted by the UI but silently dropped - actually reach
/// Windows as a combined DirectInput wheel device.
/// </summary>
public class VJoyFeederService : IVirtualFeeder
{
    private const uint ButtonPaddleUp = 33;
    private const uint ButtonPaddleDown = 34;
    private const uint ButtonGear1 = 35;
    private const uint ButtonGearReverse = 42;

    private readonly vJoy _vJoy = new();
    private readonly bool _driverEnabled;
    private readonly HashSet<uint> _acquiredDevices = new();
    private readonly Dictionary<(uint, HID_USAGES), (long Min, long Max)> _axisRanges = new();
    private readonly object _lock = new();
    private bool _isActive = true;

    public bool IsActive => _isActive;

    public VJoyFeederService()
    {
        try { _driverEnabled = _vJoy.vJoyEnabled(); }
        catch { _driverEnabled = false; }
    }

    public void SetActive(bool active)
    {
        lock (_lock)
        {
            _isActive = active;
            if (!active) ReleaseAllDevices();
        }
    }

    private bool EnsureAcquired(uint rID)
    {
        if (!_driverEnabled || !_isActive || rID == 0) return false;

        lock (_lock)
        {
            if (_acquiredDevices.Contains(rID)) return true;

            try
            {
                var status = _vJoy.GetVJDStatus(rID);
                if (status != VjdStat.VJD_STAT_OWN && status != VjdStat.VJD_STAT_FREE) return false;
                if (!_vJoy.AcquireVJD(rID)) return false;

                _vJoy.ResetVJD(rID);
                _acquiredDevices.Add(rID);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private (long Min, long Max) GetAxisRange(uint rID, HID_USAGES axis)
    {
        var key = (rID, axis);
        if (_axisRanges.TryGetValue(key, out var cached)) return cached;

        (long Min, long Max) range = (0, 32768);
        try
        {
            long min = 0, max = 0;
            if (_vJoy.GetVJDAxisExist(rID, axis) &&
                _vJoy.GetVJDAxisMin(rID, axis, ref min) &&
                _vJoy.GetVJDAxisMax(rID, axis, ref max) &&
                max > min)
            {
                range = (min, max);
            }
        }
        catch { }

        _axisRanges[key] = range;
        return range;
    }

    public void UpdateAxis(uint targetDeviceId, string targetOutput, double normalizedValue)
    {
        if (!_isActive || !targetOutput.StartsWith("[Wheel]", StringComparison.OrdinalIgnoreCase)) return;

        var axis = MapTargetToAxis(targetOutput);
        if (axis == null) return;
        if (!EnsureAcquired(targetDeviceId)) return;

        try
        {
            var (min, max) = GetAxisRange(targetDeviceId, axis.Value);
            double clamped = Math.Clamp(normalizedValue, 0.0, 1.0);
            int val = (int)Math.Round(min + clamped * (max - min));
            _vJoy.SetAxis(val, targetDeviceId, axis.Value);
        }
        catch { }
    }

    public void UpdateButton(uint targetDeviceId, string targetOutput, bool isPressed)
    {
        if (!_isActive || !targetOutput.StartsWith("[Wheel]", StringComparison.OrdinalIgnoreCase)) return;

        uint btn = MapTargetToButton(targetOutput);
        if (btn == 0) return;
        if (!EnsureAcquired(targetDeviceId)) return;

        try { _vJoy.SetBtn(isPressed, targetDeviceId, btn); }
        catch { }
    }

    private static HID_USAGES? MapTargetToAxis(string targetOutput)
    {
        if (targetOutput.Contains("Handbrake", StringComparison.OrdinalIgnoreCase)) return HID_USAGES.HID_USAGE_RY;
        if (targetOutput.Contains("Steering", StringComparison.OrdinalIgnoreCase) || targetOutput.Contains("Steer", StringComparison.OrdinalIgnoreCase)) return HID_USAGES.HID_USAGE_X;
        if (targetOutput.Contains("Clutch", StringComparison.OrdinalIgnoreCase)) return HID_USAGES.HID_USAGE_RX;
        if (targetOutput.Contains("Gas", StringComparison.OrdinalIgnoreCase) || targetOutput.Contains("Throttle", StringComparison.OrdinalIgnoreCase)) return HID_USAGES.HID_USAGE_Y;
        if (targetOutput.Contains("Brake", StringComparison.OrdinalIgnoreCase)) return HID_USAGES.HID_USAGE_Z;
        if (targetOutput.Contains("Combined Slider 0", StringComparison.OrdinalIgnoreCase)) return HID_USAGES.HID_USAGE_SL0;
        if (targetOutput.Contains("Dual Clutch Slider 1", StringComparison.OrdinalIgnoreCase)) return HID_USAGES.HID_USAGE_SL1;
        return null;
    }

    /// <summary>
    /// Generic "[Wheel] Button N" targets map 1:1 to vJoy buttons 1-32; paddles and
    /// the H-pattern gears (which vJoy has no native concept of) get dedicated buttons
    /// 33-42. A vJoy device needs to be configured (via the vJoy Configuration utility)
    /// with enough buttons to cover whichever of these a profile actually uses.
    /// </summary>
    private static uint MapTargetToButton(string targetOutput)
    {
        if (targetOutput.Contains("Paddle Up", StringComparison.OrdinalIgnoreCase)) return ButtonPaddleUp;
        if (targetOutput.Contains("Paddle Down", StringComparison.OrdinalIgnoreCase)) return ButtonPaddleDown;
        if (targetOutput.Contains("Reverse Gear", StringComparison.OrdinalIgnoreCase)) return ButtonGearReverse;

        foreach (var (label, offset) in GearLabels)
        {
            if (targetOutput.Contains(label, StringComparison.OrdinalIgnoreCase)) return ButtonGear1 + offset;
        }

        int idx = targetOutput.IndexOf("Button ", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            string numPart = targetOutput[(idx + "Button ".Length)..].Trim();
            if (int.TryParse(numPart, out int n) && n is > 0 and <= 32) return (uint)n;
        }

        return 0;
    }

    private static readonly (string Label, uint Offset)[] GearLabels =
    [
        ("1st Gear", 0), ("2nd Gear", 1), ("3rd Gear", 2), ("4th Gear", 3),
        ("5th Gear", 4), ("6th Gear", 5), ("7th Gear", 6)
    ];

    public void Reset()
    {
        lock (_lock)
        {
            foreach (var rID in _acquiredDevices)
            {
                try
                {
                    _vJoy.ResetVJD(rID);
                }
                catch { }
            }
        }
    }

    private void ReleaseAllDevices()
    {
        foreach (var rID in _acquiredDevices)
        {
            try
            {
                _vJoy.ResetVJD(rID);
                _vJoy.RelinquishVJD(rID);
            }
            catch { }
        }
        _acquiredDevices.Clear();
        _axisRanges.Clear();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            ReleaseAllDevices();
        }
    }
}
