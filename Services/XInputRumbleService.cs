using System;
using System.Runtime.InteropServices;

namespace ECCR.Services;

/// <summary>
/// Physical rumble sink for real Xbox-brand controllers - the counterpart to
/// <see cref="ControllerHidService.SetRumble"/>, which only reaches Sony pads (DS4/DualSense) via
/// a raw HID output report. A genuine XInput-compatible controller has no equivalent HID rumble
/// report; it must be driven through the separate XInput API (<c>XInputSetState</c>), which
/// addresses controllers by a 0-3 "user index" slot rather than the DirectInput instance GUID the
/// rest of this app uses, so there's no direct GUID-to-slot mapping to rely on.
/// <para>
/// To correlate a specific mapped device (identified here by VID/PID, same as
/// <see cref="ControllerHidService.GetVidPid"/> already resolves) to an XInput slot, this uses
/// <c>XInputGetCapabilitiesEx</c> (an undocumented but long-stable ordinal export, the same
/// technique projects like SDL2 use for exactly this) to read each connected slot's VID/PID.
/// If that call isn't available (very old xinput dll) or no slot's VID/PID matches, this falls
/// back to rumbling every currently-connected XInput slot - a best-effort fallback, consistent
/// with this codebase's existing "best-effort, document the limitation" approach elsewhere
/// (the Steam exe-resolution heuristic, the EA discovery provider). With more than one physical
/// Xbox-brand pad connected simultaneously, the fallback path may rumble the wrong one.
/// </para>
/// </summary>
public static class XInputRumbleService
{
    private const int MaxUserIndex = 4;
    private const int ErrorSuccess = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputVibration
    {
        public ushort LeftMotorSpeed;
        public ushort RightMotorSpeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputCapabilitiesEx
    {
        public byte Type;
        public byte SubType;
        public ushort Flags;
        public XInputGamepad Gamepad;
        public XInputVibration Vibration;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
        public ushort Unk1;
        public uint Unk2;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    private static extern int XInputSetState(uint dwUserIndex, ref XInputVibration pVibration);

    [DllImport("xinput1_4.dll", EntryPoint = "#108")]
    private static extern int XInputGetCapabilitiesEx(uint dwApiVersion, uint dwUserIndex, uint dwFlags, out XInputCapabilitiesEx pCapabilities);

    /// <summary>Sends motor speeds (0-255, rescaled to XInput's 0-65535 range) to whichever connected XInput slot's VID/PID matches, or every connected slot if no match can be determined. Never throws - a missing/outdated xinput1_4.dll or no connected controllers just results in a no-op.</summary>
    public static void SetRumble(int vendorId, int productId, byte largeMotor, byte smallMotor)
    {
        try
        {
            var vibration = new XInputVibration
            {
                LeftMotorSpeed = (ushort)(largeMotor * 257),  // 0-255 -> 0-65535
                RightMotorSpeed = (ushort)(smallMotor * 257)
            };

            bool anyMatched = false;

            for (uint i = 0; i < MaxUserIndex; i++)
            {
                int capsResult;
                XInputCapabilitiesEx caps;
                try
                {
                    capsResult = XInputGetCapabilitiesEx(1, i, 0, out caps);
                }
                catch (EntryPointNotFoundException)
                {
                    break; // no capabilities API at all - fall back below
                }

                if (capsResult != ErrorSuccess) continue; // slot not connected
                if (caps.VendorId != vendorId || caps.ProductId != productId) continue;

                XInputSetState(i, ref vibration);
                anyMatched = true;
            }

            if (anyMatched) return;

            // Either the capabilities API is unavailable, or none of the connected slots' VID/PID
            // matched - fall back to every connected slot (see this class's doc comment).
            for (uint i = 0; i < MaxUserIndex; i++)
            {
                XInputSetState(i, ref vibration);
            }
        }
        catch
        {
            // No XInput runtime, or a controller disconnected mid-call - treat as silently unavailable.
        }
    }
}
