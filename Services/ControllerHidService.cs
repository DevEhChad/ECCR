using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ECCR.Models;
using HidSharp;

namespace ECCR.Services;

/// <summary>
/// Reads battery/connection info and writes lightbar color for Sony gamepads (DualShock 4,
/// DualSense) via raw HID reports - something neither <see cref="InputDeviceManager"/>
/// (DirectInput only exposes translated axis/button state) nor <see cref="HidHideManager"/>
/// (WMI-based enumeration for cloaking, no report I/O) can do. Opens each device
/// non-exclusively via HidSharp so it coexists with DirectInput's own acquisition of the same
/// physical device (the same approach DS4Windows/DualSenseX use).
///
/// <para>
/// Report byte offsets below are reverse-engineered from community sources (DS4Windows,
/// pydualsense/dualsensectl-class open-source projects, the Linux hid-playstation driver) - not
/// vendor documentation - and have shown small drift across firmware/report-mode revisions.
/// They MUST be validated against real hardware before being trusted; the Bluetooth paths in
/// particular (extra header + CRC32 trailer, and some firmwares only start sending the extended
/// input report after the first output-report write) are the most failure-prone. A wrong offset
/// here is not destructive - the controller either ignores the write or a stale/garbage value is
/// read - but it needs a physical DualShock4/DualSense to confirm and correct.
/// </para>
/// </summary>
public class ControllerHidService : IDisposable
{
    public const int SonyVendorId = 0x054C;
    private const int Ds4ProductIdV1 = 0x05C4;
    private const int Ds4ProductIdV2 = 0x09CC;
    private const int DualSenseProductId = 0x0CE6;

    private static readonly int[] SupportedProductIds = [Ds4ProductIdV1, Ds4ProductIdV2, DualSenseProductId];

    private sealed class OpenDevice
    {
        public required HidDevice Device;
        public required HidStream Stream;

        // Output reports are full-state writes, not incremental - the lightbar (color/mode)
        // and rumble (motor intensities) writers below share a device's single output report,
        // so each remembers the other's last-written value and re-sends both together on every
        // write. Without this, calling SetRumble would silently reset the lightbar to black
        // (and vice versa) since whichever write happened second would zero out the bytes the
        // other one owns.
        public byte LastRed, LastGreen, LastBlue;
        public byte LastLargeMotor, LastSmallMotor;
    }

    private readonly Dictionary<string, OpenDevice> _openDevices = new();
    private readonly object _lock = new();
    private Timer? _pollTimer;
    private bool _disposed;

    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECCR", "hid-debug.log");

    /// <summary>
    /// Temporary diagnostic log for troubleshooting LED/battery HID I/O against real hardware -
    /// this is a WinExe with no visible console, so file logging is the only way to see what a
    /// write/enumeration actually did. Every call is wrapped so logging itself can never throw.
    /// </summary>
    private static void LogDebug(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
            File.AppendAllText(LogFilePath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    /// <summary>Raised on a background thread every poll tick with the latest status for every open Sony device.</summary>
    public event Action<List<ControllerStatus>>? OnStatusUpdated;

    // Battery/lightbar only needed occasional polling, but gyro/touch (added alongside those)
    // need to be read often enough to be usable for live aim/touch mapping - ~30Hz is a
    // reasonable middle ground that stays cheap (a few hundred bytes per read) without the
    // pileup risk of trying to hit a full 125Hz-class polling rate on a background Timer.
    private const int PollIntervalMs = 33;
    private int _pollInProgress;

    public void Start()
    {
        RefreshDevices();
        _pollTimer = new Timer(_ => PollOnce(), null, dueTime: 0, period: PollIntervalMs);
    }

    public void Stop()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;

        lock (_lock)
        {
            foreach (var d in _openDevices.Values)
            {
                try { d.Stream.Dispose(); } catch { }
            }
            _openDevices.Clear();
        }
    }

    /// <summary>
    /// Extracts VID/PID from a DirectInput product GUID, whose first 4 bytes are the raw USB
    /// VID (bytes 0-1, little-endian) and PID (bytes 2-3, little-endian) - the same convention
    /// DirectInput has followed since DirectX. Used by consumers (Controllers page, device
    /// status bar) to correlate a <see cref="ControllerStatus"/> back to the DirectInput device
    /// name shown everywhere else in the app, since this service only knows the raw HID side.
    /// </summary>
    public static (int VendorId, int ProductId) GetVidPid(Guid productGuid)
    {
        var bytes = productGuid.ToByteArray();
        int vendorId = bytes[0] | (bytes[1] << 8);
        int productId = bytes[2] | (bytes[3] << 8);
        return (vendorId, productId);
    }

    private void RefreshDevices()
    {
        try
        {
            var devices = DeviceList.Local.GetHidDevices(SonyVendorId, null, null, null)
                .Where(d => SupportedProductIds.Contains(d.ProductID))
                .ToList();

            lock (_lock)
            {
                var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var dev in devices)
                {
                    seenPaths.Add(dev.DevicePath);
                    if (_openDevices.ContainsKey(dev.DevicePath)) continue;

                    try
                    {
                        var config = new OpenConfiguration();
                        config.SetOption(OpenOption.Exclusive, false);

                        if (dev.TryOpen(config, out var stream))
                        {
                            // Shorter than PollIntervalMs so a quiet/disconnected device's read
                            // times out well before the next tick fires instead of piling up.
                            try { stream.ReadTimeout = 25; } catch { }
                            _openDevices[dev.DevicePath] = new OpenDevice { Device = dev, Stream = stream };
                            LogDebug($"Opened device VID={dev.VendorID:X4} PID={dev.ProductID:X4} " +
                                     $"MaxInputReportLength={dev.GetMaxInputReportLength()} MaxOutputReportLength={dev.GetMaxOutputReportLength()} " +
                                     $"Path={dev.DevicePath}");
                        }
                        else
                        {
                            LogDebug($"TryOpen returned false for VID={dev.VendorID:X4} PID={dev.ProductID:X4} Path={dev.DevicePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Exception opening VID={dev.VendorID:X4} PID={dev.ProductID:X4} Path={dev.DevicePath}: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                if (devices.Count > 1)
                {
                    var byPid = devices.GroupBy(d => d.ProductID);
                    foreach (var grp in byPid)
                    {
                        if (grp.Count() > 1)
                        {
                            LogDebug($"NOTE: {grp.Count()} HidDevice entries found for PID={grp.Key:X4} - multiple top-level collections/interfaces for the same controller.");
                        }
                    }
                }

                var stale = _openDevices.Keys.Where(p => !seenPaths.Contains(p)).ToList();
                foreach (var path in stale)
                {
                    try { _openDevices[path].Stream.Dispose(); } catch { }
                    _openDevices.Remove(path);
                }
            }
        }
        catch { }
    }

    private int _ticksSinceRefresh;

    private void PollOnce()
    {
        if (_disposed) return;

        // Re-entrancy guard: a Timer callback can fire again before the previous one finishes
        // if a Read() ever takes longer than PollIntervalMs - skip this tick rather than pile
        // up overlapping reads on the same HidStream.
        if (Interlocked.Exchange(ref _pollInProgress, 1) != 0) return;
        try
        {
            PollOnceCore();
        }
        finally
        {
            Interlocked.Exchange(ref _pollInProgress, 0);
        }
    }

    private void PollOnceCore()
    {
        // Device enumeration (WMI-backed) is far heavier than a report read - only do it once
        // per second's worth of ticks, not on every ~33ms tick.
        if (_ticksSinceRefresh++ % Math.Max(1, 1000 / PollIntervalMs) == 0)
        {
            RefreshDevices();
        }

        List<OpenDevice> snapshot;
        lock (_lock) { snapshot = _openDevices.Values.ToList(); }

        var results = new List<ControllerStatus>();
        foreach (var od in snapshot)
        {
            try
            {
                int reportLen = od.Device.GetMaxInputReportLength();
                if (reportLen <= 0) continue;

                var buffer = new byte[reportLen];
                int read = od.Stream.Read(buffer);
                if (read <= 0) continue;

                var status = ParseInputReport(od.Device, buffer, read);
                if (status != null) results.Add(status);
            }
            catch
            {
                // A read timeout or transient disconnect - the device will be dropped/re-opened
                // on the next RefreshDevices() if it's actually gone; otherwise just skip this tick.
            }
        }

        if (results.Count > 0) OnStatusUpdated?.Invoke(results);
    }

    private static ControllerStatus ParseInputReport(HidDevice device, byte[] buffer, int length)
    {
        bool isDualSense = device.ProductID == DualSenseProductId;
        byte reportId = buffer[0];

        var status = new ControllerStatus
        {
            VendorId = device.VendorID,
            ProductId = device.ProductID,
            Category = DeviceHardwareCategory.PlayStationController,
            IsLedSupported = true
        };

        // USB reports use ID 0x01 for both DS4 and DualSense at the full 64-byte length; a
        // shorter buffer at the same ID means the OS handed back a generic/minimal HID report
        // instead (e.g. before BT extended reporting kicks in), so battery isn't trustworthy.
        if (reportId == 0x01 && length >= 64)
        {
            status.ConnectionType = ControllerConnectionType.Usb;
            status.IsBatterySupported = true;

            if (isDualSense)
            {
                // pydualsense/hid-playstation-derived offsets for the USB input report.
                int battByte = buffer[53];
                int level = battByte & 0x0F;
                bool charging = (buffer[54] & 0x08) != 0;
                bool full = (buffer[54] & 0x20) != 0;
                status.BatteryPercent = full ? 100 : Math.Clamp(level * 10 + 5, 0, 100);
                status.IsCharging = charging;
                // Confirmed against a live DualSense (2026-08-30 diagnostic capture): offset 12
                // is actually the sensor timestamp (steadily increasing every sample), 14 is a
                // constant/reserved value - the real gyro triple sits 4 bytes later than the
                // community table this was first derived from assumed (that table's button
                // block is 3 bytes; this hardware's is 4).
                ParseMotionAndTouch(status, buffer, length, gyroOffset: 16, touchOffset: 33);
            }
            else
            {
                // DS4Windows-derived offset for the USB input report.
                int battByte = buffer[30];
                bool cableConnected = (battByte & 0x10) != 0;
                int level = battByte & 0x0F;
                status.IsCharging = cableConnected && level < 10;
                status.BatteryPercent = cableConnected
                    ? Math.Clamp((level + 1) * 10, 0, 100)
                    : Math.Clamp(level * 10 + 5, 0, 100);
                ParseMotionAndTouch(status, buffer, length, gyroOffset: 13, touchOffset: 35);
            }

            return status;
        }

        // Bluetooth extended reports: DS4 uses ID 0x11 (2-byte header before the same payload
        // shape as the USB report, plus a 4-byte CRC32 trailer), DualSense uses ID 0x31 (1-byte
        // header). Some firmwares only emit these after the app has written an output report
        // first - if that hasn't happened yet, this branch simply won't match and the device
        // reports as connected with no battery data until the next poll.
        if (!isDualSense && reportId == 0x11 && length >= 34)
        {
            status.ConnectionType = ControllerConnectionType.Bluetooth;
            status.IsBatterySupported = true;

            int battByte = buffer[32];
            bool cableConnected = (battByte & 0x10) != 0;
            int level = battByte & 0x0F;
            status.IsCharging = cableConnected && level < 10;
            status.BatteryPercent = cableConnected
                ? Math.Clamp((level + 1) * 10, 0, 100)
                : Math.Clamp(level * 10 + 5, 0, 100);
            // Same +2 byte shift observed between this BT battery offset (32) and the USB one
            // (30) applied to the USB gyro/touch offsets too.
            ParseMotionAndTouch(status, buffer, length, gyroOffset: 15, touchOffset: 37);

            return status;
        }

        if (isDualSense && reportId == 0x31 && length >= 57)
        {
            status.ConnectionType = ControllerConnectionType.Bluetooth;
            status.IsBatterySupported = true;

            int level = buffer[55] & 0x0F;
            bool charging = (buffer[56] & 0x08) != 0;
            status.BatteryPercent = Math.Clamp(level * 10 + 5, 0, 100);
            status.IsCharging = charging;
            // Same +2 byte shift observed between this BT battery offset (55) and the USB one
            // (53) applied to the corrected USB gyro/touch offsets (16/33) above.
            ParseMotionAndTouch(status, buffer, length, gyroOffset: 18, touchOffset: 35);

            return status;
        }

        // Unrecognized report shape for this poll tick - still report the device as present
        // (name/connection pill shows up) with no battery reading rather than dropping it.
        status.ConnectionType = ControllerConnectionType.Unknown;
        status.IsBatterySupported = false;
        return status;
    }

    /// <summary>
    /// Fills in <see cref="ControllerStatus.GyroPitch"/>/<see cref="ControllerStatus.GyroYaw"/>/
    /// <see cref="ControllerStatus.GyroRoll"/> and <see cref="ControllerStatus.TouchActive"/>/
    /// <see cref="ControllerStatus.TouchX"/>/<see cref="ControllerStatus.TouchY"/> from a
    /// recognized input report. <paramref name="gyroOffset"/> is the buffer index of the first
    /// of three little-endian int16 gyro axes (6 bytes total); <paramref name="touchOffset"/>
    /// is the index of the first touch point's 4-byte packed record (byte0 bit7 clear = actively
    /// touching, bytes1-3 = 12-bit X + 12-bit Y packed the same way on both device types). The
    /// USB DualSense offsets (16/33) were confirmed against a live controller (raw report
    /// capture: bytes 16-21 sit near zero with only single-digit LSB jitter at rest, exactly
    /// where a still gyro triple should read, and byte 33 held constant at a fixed "not
    /// touching" bit pattern with no finger on the pad) - the community report-layout table
    /// this was first derived from turned out to assume a 3-byte button block where this
    /// hardware actually uses 4, which is why the confirmed offsets land 4 bytes later than
    /// that table implied. DS4's offsets and the Bluetooth variants of both device types are
    /// still only offset-by-analogy from that same correction and remain unverified - same
    /// "needs real-hardware validation" caveat as the rest of this file.
    /// </summary>
    private static void ParseMotionAndTouch(ControllerStatus status, byte[] buffer, int length, int gyroOffset, int touchOffset)
    {
        if (length > gyroOffset + 5)
        {
            status.IsMotionSupported = true;

            short gx = (short)(buffer[gyroOffset] | (buffer[gyroOffset + 1] << 8));
            short gy = (short)(buffer[gyroOffset + 2] | (buffer[gyroOffset + 3] << 8));
            short gz = (short)(buffer[gyroOffset + 4] | (buffer[gyroOffset + 5] << 8));

            // Raw gyro units aren't physically calibrated here (no vendor-documented
            // deg/s-per-LSB scale factor was verified) - divided down to a roughly -1..1 "aim
            // sensitivity" range instead of a physical unit, since bindable gyro-aim is the
            // only consumer today.
            const double gyroScale = 8192.0;
            status.GyroPitch = Math.Clamp(gx / gyroScale, -1.0, 1.0);
            status.GyroYaw = Math.Clamp(gy / gyroScale, -1.0, 1.0);
            status.GyroRoll = Math.Clamp(gz / gyroScale, -1.0, 1.0);
        }

        if (length > touchOffset + 3)
        {
            byte b0 = buffer[touchOffset];
            byte b1 = buffer[touchOffset + 1];
            byte b2 = buffer[touchOffset + 2];
            byte b3 = buffer[touchOffset + 3];

            bool active = (b0 & 0x80) == 0; // bit7 clear = finger down
            status.TouchActive = active;

            if (active)
            {
                int rawX = b1 | ((b2 & 0x0F) << 8);
                int rawY = (b2 >> 4) | (b3 << 4);
                // DS4/DualSense touchpad surface is documented at roughly 1920x943/1942
                // depending on model - close enough that a fixed 1920x943 normalization is
                // "in bounds" for either without needing per-model detection.
                status.TouchX = Math.Clamp(rawX / 1920.0, 0.0, 1.0);
                status.TouchY = Math.Clamp(rawY / 943.0, 0.0, 1.0);
            }
        }
    }

    /// <summary>
    /// Sets the lightbar color for a Sony device already tracked by this service (matched by
    /// VID/PID via <see cref="GetVidPid"/>). No-ops (returns false) for anything else - LED
    /// control is strictly scoped to DualShock4/DualSense.
    /// </summary>
    public bool SetLightbarColor(int vendorId, int productId, byte r, byte g, byte b)
    {
        if (vendorId != SonyVendorId)
        {
            LogDebug($"SetLightbarColor: rejected, VID={vendorId:X4} is not Sony.");
            return false;
        }

        var target = FindOutputTarget(productId, out int candidateCount);
        if (target == null)
        {
            LogDebug($"SetLightbarColor: no open device found for PID={productId:X4} ({candidateCount} candidates).");
            return false;
        }

        target.LastRed = r;
        target.LastGreen = g;
        target.LastBlue = b;
        return WriteOutputReport(target, productId, "SetLightbarColor");
    }

    /// <summary>
    /// Sets rumble motor intensity for a Sony device already tracked by this service - the
    /// physical-side counterpart of a game rumbling a virtual Xbox 360/DualShock4 pad (see
    /// <see cref="Services.CompositeFeederService.RumbleReceived"/>, forwarded here by
    /// <c>MainWindowViewModel</c>). <paramref name="largeMotor"/> is the low-frequency/strong
    /// motor, <paramref name="smallMotor"/> the high-frequency/weak one - same convention ViGEm
    /// uses for both virtual pad types, so callers can forward the values through unchanged.
    /// </summary>
    public bool SetRumble(int vendorId, int productId, byte largeMotor, byte smallMotor)
    {
        if (vendorId != SonyVendorId) return false;

        var target = FindOutputTarget(productId, out _);
        if (target == null) return false;

        target.LastLargeMotor = largeMotor;
        target.LastSmallMotor = smallMotor;
        return WriteOutputReport(target, productId, "SetRumble");
    }

    private OpenDevice? FindOutputTarget(int productId, out int candidateCount)
    {
        lock (_lock)
        {
            var candidates = _openDevices.Values.Where(d => d.Device.ProductID == productId).ToList();
            candidateCount = candidates.Count;
            // Prefer the interface with the largest output report - a DualSense/DS4 exposing more
            // than one HidDevice entry for the same PID (secondary/auxiliary interface, or a
            // dongle) is far more likely to accept the full lightbar/rumble-control report on
            // whichever interface actually supports the biggest output report.
            return candidates.OrderByDescending(d => d.Device.GetMaxOutputReportLength()).FirstOrDefault();
        }
    }

    /// <summary>
    /// Output reports are full-state writes, so lightbar and rumble share one write combining
    /// both the device's last-known color and last-known motor intensities - see the
    /// <see cref="OpenDevice"/> fields this reads from.
    /// </summary>
    private bool WriteOutputReport(OpenDevice target, int productId, string callerForLog)
    {
        try
        {
            bool isDualSense = productId == DualSenseProductId;
            int outLen = target.Device.GetMaxOutputReportLength();
            if (outLen <= 0)
            {
                LogDebug($"{callerForLog}: PID={productId:X4} MaxOutputReportLength is {outLen}, aborting.");
                return false;
            }

            var buffer = new byte[outLen];

            if (isDualSense)
            {
                buffer[0] = 0x02;
                buffer[1] = 0x01; // valid_flag0: DS_OUTPUT_VALID_FLAG0_COMPATIBLE_VIBRATION (bit 0x01), per hid-playstation.c
                buffer[2] = 0x04; // valid_flag1: DS_OUTPUT_VALID_FLAG1_LIGHTBAR_CONTROL_ENABLE (bit 0x04), verified against hid-playstation.c
                if (outLen > 4)
                {
                    buffer[3] = target.LastSmallMotor; // motor_right (weak/high-freq)
                    buffer[4] = target.LastLargeMotor; // motor_left (strong/low-freq)
                }
                // RGB lightbar bytes - verified against Linux hid-playstation.c, dualsensectl, and pydualsense (all three agree).
                if (outLen > 47)
                {
                    buffer[45] = target.LastRed;
                    buffer[46] = target.LastGreen;
                    buffer[47] = target.LastBlue;
                }
                else
                {
                    LogDebug($"{callerForLog}: DualSense outLen={outLen} is too short for the RGB offsets (need >47) - report format may differ on this interface.");
                }
            }
            else
            {
                buffer[0] = 0x05;
                buffer[1] = 0xFF; // flags: enable rumble + LED + flash
                if (outLen > 4)
                {
                    buffer[3] = target.LastSmallMotor; // right/weak motor - best-effort offset, matches the RGB-at-6/7/8 layout already in use here
                    buffer[4] = target.LastLargeMotor; // left/strong motor
                }
                if (outLen > 8)
                {
                    buffer[6] = target.LastRed;
                    buffer[7] = target.LastGreen;
                    buffer[8] = target.LastBlue;
                }
            }

            LogDebug($"{callerForLog}: writing PID={productId:X4} outLen={outLen} " +
                     $"RGB=({target.LastRed},{target.LastGreen},{target.LastBlue}) motors=(L{target.LastLargeMotor},S{target.LastSmallMotor}) " +
                     $"buffer[0..2]=({buffer[0]:X2},{buffer[1]:X2},{buffer[2]:X2})");
            target.Stream.Write(buffer);
            LogDebug($"{callerForLog}: Write() returned without throwing.");
            return true;
        }
        catch (Exception ex)
        {
            LogDebug($"{callerForLog}: Write() threw for PID={productId:X4}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
