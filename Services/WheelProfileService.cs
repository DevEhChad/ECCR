using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia.Platform;
using ECCR.Models;

namespace ECCR.Services;

/// <summary>
/// Optional VID/PID-keyed overlay on top of <see cref="DevicePresetService"/>'s hardcoded
/// per-brand naming tables, loaded from two layered JSON files so a user can add or correct a
/// wheel's category/button/axis labels without recompiling:
/// <list type="bullet">
/// <item>the bundled default, <c>avares://ECCR/Assets/wheel_profiles.json</c> - ships with the
/// app, documents the schema, and seeds a couple of example entries;</item>
/// <item>an optional user override, <c>%AppData%/ECCR/wheel_profiles.json</c> (same folder
/// <see cref="ProfileService"/> already uses) - entries here win over the bundled ones for the
/// same VID/PID.</item>
/// </list>
/// Both files are optional; if neither exists (or neither has an entry for a given VID/PID),
/// <see cref="TryResolve"/> just returns false and every caller falls through to today's
/// hardcoded-table behavior unchanged. Loaded once, lazily, on first use.
/// </summary>
public static class WheelProfileService
{
    private static readonly Uri BundledAssetUri = new("avares://ECCR/Assets/wheel_profiles.json");
    private static readonly object LoadLock = new();
    private static Dictionary<(int VendorId, int ProductId), WheelProfileEntry>? _entries;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Sentinel used as the dictionary's ProductId slot for a vendor-wide entry (<see cref="WheelProfileEntry.ProductId"/> == null).</summary>
    private const int VendorWideProductId = -1;

    public static bool TryResolve(int vendorId, int productId, out WheelProfileEntry? entry)
    {
        var entries = GetEntries();
        if (entries.TryGetValue((vendorId, productId), out entry)) return true;
        if (entries.TryGetValue((vendorId, VendorWideProductId), out entry)) return true;
        entry = null;
        return false;
    }

    private static Dictionary<(int, int), WheelProfileEntry> GetEntries()
    {
        if (_entries != null) return _entries;
        lock (LoadLock)
        {
            if (_entries != null) return _entries;

            var merged = new Dictionary<(int, int), WheelProfileEntry>();
            MergeFrom(LoadBundled(), merged);
            MergeFrom(LoadUserOverride(), merged);
            _entries = merged;
            return _entries;
        }
    }

    private static void MergeFrom(List<WheelProfileEntry>? entries, Dictionary<(int, int), WheelProfileEntry> target)
    {
        if (entries == null) return;
        foreach (var entry in entries)
        {
            var key = (entry.VendorId, entry.ProductId ?? VendorWideProductId);
            target[key] = entry; // later source (user override) intentionally overwrites the bundled default for the same key
        }
    }

    private static List<WheelProfileEntry>? LoadBundled()
    {
        try
        {
            if (!AssetLoader.Exists(BundledAssetUri)) return null;
            using var stream = AssetLoader.Open(BundledAssetUri);
            return JsonSerializer.Deserialize<List<WheelProfileEntry>>(stream, JsonOptions);
        }
        catch
        {
            return null; // malformed bundled file should never crash the app - just yields no overrides
        }
    }

    private static List<WheelProfileEntry>? LoadUserOverride()
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECCR", "wheel_profiles.json");
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<List<WheelProfileEntry>>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null; // malformed user file should never crash the app - just yields no overrides
        }
    }
}
