using System;
using System.Runtime.InteropServices;

namespace ECCR.Services;

public class VJoyFeederService : IDisposable
{
    [DllImport("vJoyInterface.dll", EntryPoint = "vJoyEnabled", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool NativeVJoyEnabled();

    [DllImport("vJoyInterface.dll", EntryPoint = "AcquireVJD", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool NativeAcquireVJD(uint rID);

    [DllImport("vJoyInterface.dll", EntryPoint = "RelinquishVJD", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool NativeRelinquishVJD(uint rID);

    [DllImport("vJoyInterface.dll", EntryPoint = "SetBtn", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool NativeSetBtn(bool value, uint rID, byte nBtn);

    [DllImport("vJoyInterface.dll", EntryPoint = "SetAxis", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool NativeSetAxis(long value, uint rID, uint whichAxis);

    [DllImport("vJoyInterface.dll", EntryPoint = "ResetAll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeResetAll();

    private const uint HID_USAGE_X = 0x30;
    private const uint HID_USAGE_Y = 0x31;
    private const uint HID_USAGE_Z = 0x32;
    private const uint HID_USAGE_RX = 0x33;
    private const uint HID_USAGE_RY = 0x34;
    private const uint HID_USAGE_RZ = 0x35;
    private const uint HID_USAGE_SL0 = 0x36;
    private const uint HID_USAGE_SL1 = 0x37;

    private bool _isVJoyAvailable = false;
    private readonly bool[] _acquiredDevices = new bool[17];

    public bool Initialize()
    {
        try
        {
            _isVJoyAvailable = NativeVJoyEnabled();
            return _isVJoyAvailable;
        }
        catch
        {
            _isVJoyAvailable = false;
            return false;
        }
    }

    private bool EnsureAcquired(uint deviceId)
    {
        if (!_isVJoyAvailable || deviceId < 1 || deviceId > 16) return false;

        if (_acquiredDevices[deviceId]) return true;

        try
        {
            if (NativeAcquireVJD(deviceId))
            {
                _acquiredDevices[deviceId] = true;
                return true;
            }
        }
        catch { }

        return false;
    }

    public void UpdateButton(uint deviceId, string targetOutput, bool isPressed)
    {
        if (!EnsureAcquired(deviceId)) return;

        byte buttonNumber = ParseButtonNumber(targetOutput);
        if (buttonNumber > 0 && buttonNumber <= 128)
        {
            try
            {
                NativeSetBtn(isPressed, deviceId, buttonNumber);
            }
            catch { }
        }
    }

    public void UpdateAxis(uint deviceId, string targetOutput, double normalizedValue)
    {
        if (!EnsureAcquired(deviceId)) return;

        uint axisUsage = ParseAxisUsage(targetOutput);
        if (axisUsage == 0) return;

        long vJoyAxisValue = (long)Math.Clamp(normalizedValue * 32767.0 + 1.0, 1.0, 32768.0);

        try
        {
            NativeSetAxis(vJoyAxisValue, deviceId, axisUsage);
        }
        catch { }
    }

    private static byte ParseButtonNumber(string targetOutput)
    {
        string upper = targetOutput.ToUpperInvariant();

        if (upper.Contains("1ST GEAR") || upper.Contains("GEAR 1")) return 1;
        if (upper.Contains("2ND GEAR") || upper.Contains("GEAR 2")) return 2;
        if (upper.Contains("3RD GEAR") || upper.Contains("GEAR 3")) return 3;
        if (upper.Contains("4TH GEAR") || upper.Contains("GEAR 4")) return 4;
        if (upper.Contains("5TH GEAR") || upper.Contains("GEAR 5")) return 5;
        if (upper.Contains("6TH GEAR") || upper.Contains("GEAR 6")) return 6;
        if (upper.Contains("7TH GEAR") || upper.Contains("GEAR 7")) return 7;
        if (upper.Contains("REVERSE")) return 8;

        if (upper.Contains("PADDLE DOWN")) return 9;
        if (upper.Contains("PADDLE UP")) return 10;

        int btnIdx = upper.IndexOf("BUTTON", StringComparison.Ordinal);
        if (btnIdx >= 0)
        {
            string numPart = upper[(btnIdx + 6)..].Trim();
            string digits = string.Empty;
            foreach (char c in numPart)
            {
                if (char.IsDigit(c)) digits += c;
                else if (digits.Length > 0) break;
            }

            if (byte.TryParse(digits, out byte b))
                return b;
        }

        return 0;
    }

    private static uint ParseAxisUsage(string targetOutput)
    {
        string upper = targetOutput.ToUpperInvariant();

        if (upper.Contains("STEERING") || upper.Contains("AXIS X")) return HID_USAGE_X;
        if (upper.Contains("GAS") || upper.Contains("THROTTLE") || upper.Contains("AXIS Y")) return HID_USAGE_Y;
        if (upper.Contains("BRAKE") || upper.Contains("AXIS Z")) return HID_USAGE_Z;
        if (upper.Contains("CLUTCH") || upper.Contains("AXIS RX")) return HID_USAGE_RX;
        if (upper.Contains("HANDBRAKE") || upper.Contains("AXIS RY")) return HID_USAGE_RY;
        if (upper.Contains("AXIS RZ")) return HID_USAGE_RZ;
        if (upper.Contains("SLIDER 0") || upper.Contains("SLIDER 1")) return HID_USAGE_SL0;
        if (upper.Contains("SLIDER 2")) return HID_USAGE_SL1;

        return 0;
    }

    public void Shutdown()
    {
        if (!_isVJoyAvailable) return;

        try
        {
            for (uint i = 1; i <= 16; i++)
            {
                if (_acquiredDevices[i])
                {
                    NativeRelinquishVJD(i);
                    _acquiredDevices[i] = false;
                }
            }
            NativeResetAll();
        }
        catch { }
    }

    public void Dispose()
    {
        Shutdown();
    }
}