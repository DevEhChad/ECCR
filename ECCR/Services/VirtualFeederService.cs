using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ECCR.Models;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace ECCR.Services;

public class VirtualFeederService : IDisposable
{
    // --- ViGEm State (Xbox Controller Emulation) ---
    private ViGEmClient? _vigemClient;
    private readonly Dictionary<uint, IXbox360Controller> _xboxControllers = new();
    private bool _isViGEmAvailable = true;

    // --- vJoy Native P/Invoke (DirectInput Virtual Wheel) ---
    [DllImport("vJoyInterface.dll", EntryPoint = "vJoyEnabled", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool NativeVJoyEnabled();

    [DllImport("vJoyInterface.dll", EntryPoint = "AcquireVJD", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool NativeAcquireVJD(uint rID);

    [DllImport("vJoyInterface.dll", EntryPoint = "RelinquishVJD", CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeRelinquishVJD(uint rID);

    [DllImport("vJoyInterface.dll", EntryPoint = "SetAxis", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool NativeSetAxis(int value, uint rID, uint axis);

    [DllImport("vJoyInterface.dll", EntryPoint = "SetBtn", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool NativeSetBtn(bool value, uint rID, byte nBtn);

    [DllImport("vJoyInterface.dll", EntryPoint = "ResetVJD", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool NativeResetVJD(uint rID);

    // DirectInput HID Usages
    private const uint HID_USAGE_X = 0x30;   // Steering Wheel
    private const uint HID_USAGE_Y = 0x31;   // Throttle (Gas)
    private const uint HID_USAGE_Z = 0x32;   // Brake
    private const uint HID_USAGE_RX = 0x33;  // Clutch
    private const uint HID_USAGE_RY = 0x34;  // Handbrake
    private const uint HID_USAGE_RZ = 0x35;  // Auxiliary Axis
    private const uint HID_USAGE_SL0 = 0x36; // Slider 0
    private const uint HID_USAGE_SL1 = 0x37; // Slider 1

    private readonly HashSet<uint> _acquiredVJoyDevices = new();
    private bool _isVJoyAvailable = false;

    public bool IsViGEmAvailable => _isViGEmAvailable;
    public bool IsVJoyAvailable => _isVJoyAvailable;

    public VirtualFeederService()
    {
        // 1. Initialize ViGEm Xbox Bus
        try
        {
            _vigemClient = new ViGEmClient();
            GetOrCreateXboxController(1);
        }
        catch
        {
            _isViGEmAvailable = false;
        }

        // 2. Initialize vJoy DirectInput Bus
        try
        {
            if (NativeVJoyEnabled())
            {
                _isVJoyAvailable = true;
                AcquireVJoyDevice(1);
            }
        }
        catch
        {
            _isVJoyAvailable = false;
        }
    }

    public void AcquireVJoyDevice(uint deviceId)
    {
        if (!_isVJoyAvailable) return;
        if (!_acquiredVJoyDevices.Contains(deviceId))
        {
            try
            {
                if (NativeAcquireVJD(deviceId))
                {
                    NativeResetVJD(deviceId);
                    _acquiredVJoyDevices.Add(deviceId);
                }
            }
            catch { }
        }
    }

    private IXbox360Controller? GetOrCreateXboxController(uint targetId)
    {
        if (!_isViGEmAvailable || _vigemClient == null) return null;

        if (!_xboxControllers.TryGetValue(targetId, out var controller))
        {
            try
            {
                controller = _vigemClient.CreateXbox360Controller();
                controller.Connect();
                _xboxControllers[targetId] = controller;
            }
            catch
            {
                return null;
            }
        }

        return controller;
    }

    public void UpdateAxis(uint deviceId, string targetOutput, double normalizedFloat)
    {
        bool isXboxTarget = targetOutput.StartsWith("[Xbox") || targetOutput.Contains("Xbox") || targetOutput.Contains("Stick") || targetOutput.Contains("Trigger");

        if (isXboxTarget)
        {
            // === Route to ViGEm (Xbox Controller) ===
            var controller = GetOrCreateXboxController(deviceId);
            if (controller == null) return;

            short thumbValue = (short)Math.Round((normalizedFloat * 65535.0) - 32768.0);
            byte triggerValue = (byte)Math.Clamp((int)Math.Round(normalizedFloat * 255.0), 0, 255);

            try
            {
                if (targetOutput.Contains("RT") || targetOutput.Contains("Right Trigger") || targetOutput.Contains("Gas"))
                    controller.SetSliderValue(Xbox360Slider.RightTrigger, triggerValue);
                else if (targetOutput.Contains("LT") || targetOutput.Contains("Left Trigger") || targetOutput.Contains("Brake"))
                    controller.SetSliderValue(Xbox360Slider.LeftTrigger, triggerValue);
                else if (targetOutput.Contains("Left Stick X") || targetOutput.Contains("Steer"))
                    controller.SetAxisValue(Xbox360Axis.LeftThumbX, thumbValue);
                else if (targetOutput.Contains("Left Stick Y"))
                    controller.SetAxisValue(Xbox360Axis.LeftThumbY, thumbValue);
                else if (targetOutput.Contains("Right Stick X") || targetOutput.Contains("Handbrake"))
                    controller.SetAxisValue(Xbox360Axis.RightThumbX, thumbValue);
                else if (targetOutput.Contains("Right Stick Y") || targetOutput.Contains("Clutch"))
                    controller.SetAxisValue(Xbox360Axis.RightThumbY, thumbValue);

                controller.SubmitReport();
            }
            catch { }
        }
        else
        {
            // === Route to vJoy (DirectInput Virtual Wheel) ===
            AcquireVJoyDevice(deviceId);
            if (!_acquiredVJoyDevices.Contains(deviceId)) return;

            int vjoyVal = (int)Math.Round(1.0 + (normalizedFloat * 32767.0));
            vjoyVal = Math.Clamp(vjoyVal, 1, 32768);

            uint usage = ResolveVJoyAxis(targetOutput);
            NativeSetAxis(vjoyVal, deviceId, usage);
        }
    }

    public void UpdateButton(uint deviceId, string targetOutput, bool isPressed)
    {
        bool isXboxTarget = targetOutput.StartsWith("[Xbox") || targetOutput.Contains("Xbox");

        if (isXboxTarget)
        {
            // === Route to ViGEm (Xbox Controller) ===
            var controller = GetOrCreateXboxController(deviceId);
            if (controller == null) return;

            var btn = ResolveXboxButton(targetOutput);
            if (btn != null)
            {
                try
                {
                    controller.SetButtonState(btn.Value, isPressed);
                    controller.SubmitReport();
                }
                catch { }
            }
        }
        else
        {
            // === Route to vJoy (DirectInput Virtual Wheel Buttons & Shifter Gates) ===
            AcquireVJoyDevice(deviceId);
            if (!_acquiredVJoyDevices.Contains(deviceId)) return;

            byte btnIndex = ResolveVJoyButton(targetOutput);
            NativeSetBtn(isPressed, deviceId, btnIndex);
        }
    }

    private static uint ResolveVJoyAxis(string targetOutput)
    {
        if (targetOutput.Contains("Steer") || targetOutput.Contains("Axis X")) return HID_USAGE_X;
        if (targetOutput.Contains("Gas") || targetOutput.Contains("Throttle") || targetOutput.Contains("Axis Y")) return HID_USAGE_Y;
        if (targetOutput.Contains("Brake") || targetOutput.Contains("Axis Z")) return HID_USAGE_Z;
        if (targetOutput.Contains("Clutch") || targetOutput.Contains("Axis Rx")) return HID_USAGE_RX;
        if (targetOutput.Contains("Handbrake") || targetOutput.Contains("Axis Ry")) return HID_USAGE_RY;
        if (targetOutput.Contains("Slider 0")) return HID_USAGE_SL0;
        if (targetOutput.Contains("Slider 1")) return HID_USAGE_SL1;

        return HID_USAGE_RZ;
    }

    private static byte ResolveVJoyButton(string targetOutput)
    {
        if (targetOutput.Contains("1st Gear")) return 1;
        if (targetOutput.Contains("2nd Gear")) return 2;
        if (targetOutput.Contains("3rd Gear")) return 3;
        if (targetOutput.Contains("4th Gear")) return 4;
        if (targetOutput.Contains("5th Gear")) return 5;
        if (targetOutput.Contains("6th Gear")) return 6;
        if (targetOutput.Contains("7th Gear")) return 7;
        if (targetOutput.Contains("Reverse")) return 8;
        if (targetOutput.Contains("Paddle Down")) return 9;
        if (targetOutput.Contains("Paddle Up")) return 10;

        if (targetOutput.Contains("Button "))
        {
            string numStr = targetOutput.Substring(targetOutput.LastIndexOf("Button ") + 7).Trim();
            if (byte.TryParse(numStr, out byte num)) return Math.Clamp(num, (byte)1, (byte)128);
        }

        if (targetOutput.StartsWith("V") && byte.TryParse(targetOutput.Substring(1), out byte customIdx))
        {
            return Math.Clamp(customIdx, (byte)1, (byte)128);
        }

        return 1;
    }

    private static Xbox360Button? ResolveXboxButton(string targetOutput)
    {
        if (targetOutput.Contains("Xbox A") || targetOutput.Contains("(Cross)")) return Xbox360Button.A;
        if (targetOutput.Contains("Xbox B") || targetOutput.Contains("(Circle)")) return Xbox360Button.B;
        if (targetOutput.Contains("Xbox X") || targetOutput.Contains("(Square)")) return Xbox360Button.X;
        if (targetOutput.Contains("Xbox Y") || targetOutput.Contains("(Triangle)")) return Xbox360Button.Y;
        if (targetOutput.Contains("Xbox LB") || targetOutput.Contains("Left Bumper")) return Xbox360Button.LeftShoulder;
        if (targetOutput.Contains("Xbox RB") || targetOutput.Contains("Right Bumper")) return Xbox360Button.RightShoulder;
        if (targetOutput.Contains("Xbox View") || targetOutput.Contains("Back")) return Xbox360Button.Back;
        if (targetOutput.Contains("Xbox Menu") || targetOutput.Contains("Start")) return Xbox360Button.Start;
        if (targetOutput.Contains("Xbox LSB") || targetOutput.Contains("Left Stick Click")) return Xbox360Button.LeftThumb;
        if (targetOutput.Contains("Xbox RSB") || targetOutput.Contains("Right Stick Click")) return Xbox360Button.RightThumb;
        if (targetOutput.Contains("D-Pad Up")) return Xbox360Button.Up;
        if (targetOutput.Contains("D-Pad Down")) return Xbox360Button.Down;
        if (targetOutput.Contains("D-Pad Left")) return Xbox360Button.Left;
        if (targetOutput.Contains("D-Pad Right")) return Xbox360Button.Right;

        return null;
    }

    public void Dispose()
    {
        foreach (var devId in _acquiredVJoyDevices)
        {
            try { NativeRelinquishVJD(devId); } catch { }
        }
        _acquiredVJoyDevices.Clear();

        foreach (var c in _xboxControllers.Values)
        {
            try { c.Disconnect(); } catch { }
        }
        _xboxControllers.Clear();
        _vigemClient?.Dispose();
    }
}