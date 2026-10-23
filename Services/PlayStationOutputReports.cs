using System;

namespace ECCR.Services;

/// <summary>What a PlayStation pad's output report carries. Output reports are full-state writes, so every field is sent each time.</summary>
/// <param name="LeftTrigger">DualSense only: an 11-byte block from <see cref="DualSenseTriggerEffect"/>, or null to leave the trigger alone.</param>
internal readonly record struct PlayStationOutputState(
    byte Red, byte Green, byte Blue,
    byte LargeMotor, byte SmallMotor,
    byte[]? LeftTrigger = null, byte[]? RightTrigger = null);

/// <summary>
/// Builds the raw output reports for DualShock 4 and DualSense over USB and Bluetooth. Pure functions
/// (no device I/O) so the layouts can be checked without hardware; <see cref="ControllerHidService"/>
/// picks the builder by pad and transport and writes the result.
///
/// <para>
/// Layouts follow the Linux <c>hid-playstation</c> driver's structs. Both pads share one "common"
/// block whose position depends on the transport: USB sends report ID then the block (block at
/// offset 1); Bluetooth adds two header bytes first (block at offset 3) and ends with a CRC-32 over
/// the report prefixed by the byte 0xA2. A Bluetooth report with a missing or wrong CRC is silently
/// ignored by the pad. The DS4's block is [flags, flags, reserved, motorRight, motorLeft, R, G, B,
/// blinkOn, blinkOff]; the DualSense's is 47 bytes, see <see cref="WriteDualSenseCommon"/>.
/// </para>
/// </summary>
internal static class PlayStationOutputReports
{
    /// <summary>Both pads' Bluetooth output reports (DS4 ID 0x11, DualSense ID 0x31) are 78 bytes, the last four being the CRC.</summary>
    public const int BluetoothReportLength = 78;

    private const byte BluetoothCrcSeed = 0xA2;

    public static byte[] DualSenseUsb(int length, in PlayStationOutputState s)
    {
        var report = new byte[length];
        report[0] = 0x02;
        WriteDualSenseCommon(report, 1, s);
        return report;
    }

    /// <param name="sequence">0-15, incremented by the caller for each report sent to the same pad.</param>
    public static byte[] DualSenseBluetooth(byte sequence, in PlayStationOutputState s)
    {
        var report = new byte[BluetoothReportLength];
        report[0] = 0x31;
        report[1] = (byte)((sequence & 0x0F) << 4);
        report[2] = 0x10; // output tag the driver always sends
        WriteDualSenseCommon(report, 3, s);
        AppendCrc(report);
        return report;
    }

    public static byte[] DualShock4Usb(int length, in PlayStationOutputState s)
    {
        var report = new byte[length];
        report[0] = 0x05;
        WriteDualShock4Common(report, 1, 0xFF, s);
        return report;
    }

    public static byte[] DualShock4Bluetooth(in PlayStationOutputState s)
    {
        var report = new byte[BluetoothReportLength];
        report[0] = 0x11;
        report[1] = 0xC4; // 0x80 = HID report, 0x40 = CRC present, low bits = 4 ms input-report interval
        WriteDualShock4Common(report, 3, 0x07, s); // 0x07 = motors + lightbar + blink
        AppendCrc(report);
        return report;
    }

    /// <summary>
    /// The DualSense common block, starting at <paramref name="c"/>: [0] valid_flag0, [1] valid_flag1,
    /// [2] right (weak) motor, [3] left (strong) motor, [10..20] right trigger effect, [21..31] left
    /// trigger effect, [44..46] lightbar RGB. valid_flag0 bit 0 selects classic rumble, bits 2/3
    /// enable the right/left trigger effect; valid_flag1 bit 2 enables lightbar control.
    /// </summary>
    private static void WriteDualSenseCommon(byte[] report, int c, in PlayStationOutputState s)
    {
        byte flags0 = 0x01;
        if (s.RightTrigger != null && c + 10 + DualSenseTriggerEffect.Length <= report.Length)
        {
            flags0 |= 0x04;
            Array.Copy(s.RightTrigger, 0, report, c + 10, DualSenseTriggerEffect.Length);
        }
        if (s.LeftTrigger != null && c + 21 + DualSenseTriggerEffect.Length <= report.Length)
        {
            flags0 |= 0x08;
            Array.Copy(s.LeftTrigger, 0, report, c + 21, DualSenseTriggerEffect.Length);
        }

        report[c] = flags0;
        report[c + 1] = 0x04;
        report[c + 2] = s.SmallMotor;
        report[c + 3] = s.LargeMotor;
        if (c + 46 < report.Length)
        {
            report[c + 44] = s.Red;
            report[c + 45] = s.Green;
            report[c + 46] = s.Blue;
        }
    }

    private static void WriteDualShock4Common(byte[] report, int c, byte flags, in PlayStationOutputState s)
    {
        report[c] = flags;
        report[c + 3] = s.SmallMotor; // right, weak
        report[c + 4] = s.LargeMotor; // left, strong
        if (c + 7 < report.Length)
        {
            report[c + 5] = s.Red;
            report[c + 6] = s.Green;
            report[c + 7] = s.Blue;
        }
    }

    /// <summary>Stores the CRC of the report's first 74 bytes, prefixed by the Bluetooth output seed byte, little-endian in the last four.</summary>
    private static void AppendCrc(byte[] report)
    {
        int crcOffset = report.Length - 4;
        uint crc = Crc32(BluetoothCrcSeed, report.AsSpan(0, crcOffset));
        report[crcOffset] = (byte)crc;
        report[crcOffset + 1] = (byte)(crc >> 8);
        report[crcOffset + 2] = (byte)(crc >> 16);
        report[crcOffset + 3] = (byte)(crc >> 24);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (value >> 1) ^ 0xEDB88320u : value >> 1;
            }
            table[i] = value;
        }
        return table;
    }

    /// <summary>Standard CRC-32 (zlib) of <paramref name="seed"/> followed by <paramref name="data"/>.</summary>
    internal static uint Crc32(byte seed, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        crc = CrcTable[(crc ^ seed) & 0xFF] ^ (crc >> 8);
        foreach (byte b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return ~crc;
    }
}
