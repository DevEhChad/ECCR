using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ECCR.Models;
using ECCR.ViewModels;

namespace ECCR.Services;

/// <summary>
/// Global, profile-independent per-device store (<c>%AppData%\ECCR\hardware_profiles.json</c>)
/// for Input Matrix metadata (nicknames, hidden/shown inputs) - see <see cref="HardwareDeviceProfile"/>.
/// Keyed by a VID/PID+cleaned-instance-name composite (<see cref="BuildCompositeKey"/>) rather
/// than the raw DirectInput instance name that every other per-device dictionary in this app uses
/// (<c>MainWindowViewModel.GetOrCreateWheelSettings</c>/<c>GetOrCreateGamepadSettings</c>,
/// <see cref="Models.UserProfile.WheelSettings"/>/<c>GamepadSettings</c> - those stay exactly as
/// they are, keyed by instance name, since they serve live per-tick polling match). This store is
/// additive and parallel to those, not a replacement: it exists purely so a device's custom
/// nicknames survive a game-profile switch/import/reset and a replug into a different USB port
/// (VID/PID is a model identity, stable across ports - see <see cref="InputDeviceManager.ParseVidPidFromProductGuid"/>
/// equivalent decode already done by <see cref="DeviceHardwareInfo"/>). Two identical physical
/// units of the same device model will collide on this key (no serial number is exposed anywhere
/// by DirectInput) - an accepted, inherent limitation of VID/PID-based identity, not a bug.
/// Static and lazily-loaded like <see cref="WheelProfileService"/>, but read-write (unlike that
/// service): every mutation is written back to disk immediately (matching
/// <c>MainWindowViewModel.AutoSaveCurrentProfile</c>'s immediate-write policy) since nickname
/// edits are low-frequency explicit user actions, not something needing debounce.
/// </summary>
public static class HardwareProfileService
{
    private class RootDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public List<string> MigratedLegacyProfiles { get; set; } = new();
        public Dictionary<string, HardwareDeviceProfile> Devices { get; set; } = new();
    }

    private static readonly object LoadLock = new();
    private static RootDocument? _root;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECCR", "hardware_profiles.json");

    private static readonly Regex TrailingInstanceSuffix = new(@"\s*(#\d+|\(\d+\))\s*$", RegexOptions.Compiled);

    /// <summary>
    /// <c>VID_XXXX_PID_YYYY_CleanedInstanceName</c> - the persistent hardware key. "Cleaning" the
    /// instance name strips a trailing enumeration-order suffix some drivers append for multiple
    /// identical units ("... Wheel #2", "... Wheel (2)") and collapses/strips punctuation so the
    /// key stays a stable, JSON-key-safe string across minor driver string variance.
    /// </summary>
    public static string BuildCompositeKey(int vendorId, int productId, string instanceName)
    {
        string vidPid = $"VID_{vendorId:X4}_PID_{productId:X4}";
        return $"{vidPid}_{CleanDeviceName(instanceName)}";
    }

    private static string CleanDeviceName(string name)
    {
        string s = TrailingInstanceSuffix.Replace(name.Trim(), string.Empty).Trim();
        s = Regex.Replace(s, @"\s+", " ");
        return Regex.Replace(s, @"[^\w\s-]", string.Empty);
    }

    /// <summary>Resolves the composite key for a live/known device (falling back to (0,0) VID/PID if the device isn't currently enumerated) and returns its global entry, creating one if needed.</summary>
    public static HardwareDeviceProfile GetOrCreate(MainWindowViewModel host, string deviceName)
    {
        var info = host.GetDeviceHardwareInfo(deviceName);
        int vendorId = info?.VendorId ?? 0;
        int productId = info?.ProductId ?? 0;
        return GetOrCreate(vendorId, productId, deviceName);
    }

    public static HardwareDeviceProfile GetOrCreate(int vendorId, int productId, string instanceName)
    {
        string key = BuildCompositeKey(vendorId, productId, instanceName);
        var root = GetRoot();

        lock (LoadLock)
        {
            if (root.Devices.TryGetValue(key, out var existing))
            {
                existing.DisplayName = instanceName; // keep the last-seen name fresh for export/diagnostics
                return existing;
            }

            var created = new HardwareDeviceProfile
            {
                CompositeKey = key,
                DisplayName = instanceName,
                VendorId = vendorId,
                ProductId = productId
            };
            root.Devices[key] = created;
            return created;
        }
    }

    /// <summary>Overwrites (or inserts) one device's global entry wholesale - the Import Device Config action.</summary>
    public static void ReplaceEntry(MainWindowViewModel host, string deviceName, HardwareDeviceProfile imported)
    {
        var info = host.GetDeviceHardwareInfo(deviceName);
        int vendorId = info?.VendorId ?? 0;
        int productId = info?.ProductId ?? 0;
        string key = BuildCompositeKey(vendorId, productId, deviceName);

        imported.CompositeKey = key;
        imported.VendorId = vendorId;
        imported.ProductId = productId;
        imported.DisplayName = deviceName;

        var root = GetRoot();
        lock (LoadLock) { root.Devices[key] = imported; }
        Save();
    }

    /// <summary>Every mutating call on a <see cref="HardwareDeviceProfile"/> obtained here should be followed by this - no autosave hook exists on the plain POCO the way <see cref="ECCR.Models.WheelDeviceSettings"/>'s ObservableObject has one.</summary>
    public static void Save()
    {
        var root = GetRoot();
        try
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            lock (LoadLock)
            {
                File.WriteAllText(path, JsonSerializer.Serialize(root, JsonOptions));
            }
        }
        catch { /* best-effort, same as every other auto-save path in this app */ }
    }

    /// <summary>
    /// One-time gap-fill from a just-loaded profile's legacy per-profile <see cref="WheelDeviceSettings"/>
    /// nickname/hidden-input state into this global store, for any device that's currently
    /// connected (an unconnected device's legacy data migrates the next time it's plugged in and
    /// any profile loads). Never overwrites an already-migrated/edited global entry - only fills
    /// keys the global entry doesn't already have. Runs at most once per profile name (tracked in
    /// <see cref="RootDocument.MigratedLegacyProfiles"/>) so reloading an already-migrated profile
    /// is a no-op.
    /// </summary>
    public static void MigrateFromLegacyIfNeeded(
        string profileName,
        IReadOnlyDictionary<string, WheelDeviceSettings> legacyWheelSettings,
        Func<string, DeviceHardwareInfo?> resolveHardwareInfo)
    {
        var root = GetRoot();
        if (root.MigratedLegacyProfiles.Contains(profileName)) return;

        foreach (var (deviceName, legacy) in legacyWheelSettings)
        {
            var info = resolveHardwareInfo(deviceName);
            if (info == null) continue; // not currently connected - migrates next time it's plugged in and a profile loads

            var target = GetOrCreate(info.VendorId, info.ProductId, deviceName);

            foreach (var (key, nickname) in legacy.ButtonNicknames)
            {
                if (!target.ButtonNicknames.ContainsKey(key)) target.ButtonNicknames[key] = nickname;
            }
            foreach (var key in legacy.HiddenPlaceholderInputs)
            {
                target.HiddenPlaceholderInputs.Add(key);
            }
            if (legacy.ShowAllPhysicalInputs) target.ShowAllPhysicalInputs = true;
        }

        root.MigratedLegacyProfiles.Add(profileName);
        Save(); // persist the migrated-profiles marker even if this profile's device set had nothing to migrate, so it isn't re-scanned every load
    }

    private static RootDocument GetRoot()
    {
        if (_root != null) return _root;
        lock (LoadLock)
        {
            if (_root != null) return _root;
            _root = Load();
            return _root;
        }
    }

    private static RootDocument Load()
    {
        try
        {
            string path = FilePath;
            if (!File.Exists(path)) return new RootDocument();
            return JsonSerializer.Deserialize<RootDocument>(File.ReadAllText(path)) ?? new RootDocument();
        }
        catch
        {
            return new RootDocument(); // malformed file should never crash the app - starts fresh
        }
    }
}
