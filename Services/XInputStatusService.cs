using System;
using System.Runtime.InteropServices;
using ECCR.Models;

namespace ECCR.Services;

/// <summary>
/// Connection type and battery for Xbox-style pads, read through XInput - the counterpart to
/// <see cref="ControllerHidService"/>, which only reads Sony pads' raw HID reports. XInput reports a
/// battery <i>type</i> that tells a wired pad from a wireless one, plus a coarse four-step level
/// (empty/low/medium/full) rather than a percentage, so the percentage returned here is an
/// approximation of the step. A pad is matched to its XInput slot by VID/PID (see
/// <see cref="XInputRumbleService.FindSlot"/>).
/// </summary>
public static class XInputStatusService
{
    private const byte BatteryDevTypeGamepad = 0;
    private const byte BatteryTypeDisconnected = 0;
    private const byte BatteryTypeWired = 1;
    private const byte BatteryTypeUnknown = 0xFF;
    private const int ErrorSuccess = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputBatteryInformation
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")]
    private static extern int XInputGetBatteryInformation(uint dwUserIndex, byte devType, out XInputBatteryInformation pBatteryInformation);

    /// <summary>
    /// Xbox pads that reach Windows over Bluetooth rather than the Xbox Wireless Adapter. XInput can
    /// tell wired from wireless but not which wireless link, so a wireless pad outside this list is
    /// reported as plain <see cref="ControllerConnectionType.Wireless"/>.
    /// </summary>
    private static readonly int[] BluetoothXboxProductIds =
    [
        0x02E0, // Xbox One S controller
        0x02FD, // Xbox One S controller (newer firmware)
        0x0B13, // Xbox Series X|S controller
        0x0B05, // Xbox Elite Series 2
        0x0B22  // Xbox Elite Series 2 Core
    ];

    public readonly record struct PadStatus(ControllerConnectionType Connection, int? BatteryPercent);

    /// <summary>False when the pad has no XInput slot (so it is not an XInput device, or XInput is unavailable) or reports no usable battery type. Never throws.</summary>
    public static bool TryGetStatus(int vendorId, int productId, out PadStatus status)
    {
        status = default;

        int slot = XInputRumbleService.FindSlot(vendorId, productId);
        if (slot < 0) return false;

        try
        {
            if (XInputGetBatteryInformation((uint)slot, BatteryDevTypeGamepad, out var battery) != ErrorSuccess) return false;

            switch (battery.BatteryType)
            {
                case BatteryTypeDisconnected:
                    return false;

                case BatteryTypeWired:
                    status = new PadStatus(ControllerConnectionType.Usb, null);
                    return true;

                case BatteryTypeUnknown:
                    status = new PadStatus(WirelessType(productId), null);
                    return true;

                default:
                    status = new PadStatus(WirelessType(productId), LevelToPercent(battery.BatteryLevel));
                    return true;
            }
        }
        catch
        {
            return false; // no XInput runtime
        }
    }

    private static ControllerConnectionType WirelessType(int productId) =>
        Array.IndexOf(BluetoothXboxProductIds, productId) >= 0 ? ControllerConnectionType.Bluetooth : ControllerConnectionType.Wireless;

    /// <summary>XInput's empty/low/medium/full steps as a percentage near the middle of each step.</summary>
    private static int LevelToPercent(byte level) => level switch
    {
        0 => 5,
        1 => 25,
        2 => 60,
        _ => 100
    };
}
