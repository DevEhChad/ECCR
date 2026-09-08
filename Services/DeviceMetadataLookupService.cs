using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ECCR.Models;

namespace ECCR.Services;

/// <summary>
/// Tier 1 of the hardware VID/PID lookup chain <see cref="DevicePresetService.ResolveCategoryAndProfile"/>
/// walks: an online, community-maintained device registry, consulted before <see cref="WheelProfileService"/>'s
/// bundled/user-local JSON (Tier 2) and <see cref="DevicePresetService"/>'s hardcoded per-category
/// tables (Tier 3 - the final generic-name fallback, already unconditional and crash-proof, so
/// nothing downstream of this class needs to change to stay safe if every tier above it misses).
///
/// <para>
/// Disabled by design until <see cref="RemoteRegistryUrlTemplate"/> is given a real hosted
/// endpoint: with it null, <see cref="PrefetchAsync"/> returns immediately and <see cref="TryResolve"/>
/// always defers to <see cref="WheelProfileService.TryResolve"/>, i.e. today's Tier 2/3 behavior is
/// completely unchanged. The plumbing (2-second timeout, per-VID/PID caching and de-duplication,
/// same <see cref="WheelProfileEntry"/> wire shape as the bundled file) is real and ready to go the
/// moment a registry URL is set - nothing else in the app needs to change.
/// </para>
/// </summary>
public static class DeviceMetadataLookupService
{
    /// <summary>
    /// {0:X4}/{1:X4} are the vendor/product IDs, upper-hex, no "0x" prefix - matching how every
    /// other VID/PID display string in this app is formatted (see <see cref="DeviceHardwareInfo"/>
    /// call sites). Null disables Tier 1 outright - see the class doc comment.
    /// </summary>
    private static readonly string? RemoteRegistryUrlTemplate = null;

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(2) };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Successful remote lookups for this app run - never persisted, since the whole point of Tier 1 is to reflect whatever the community registry currently says.</summary>
    private static readonly ConcurrentDictionary<(int VendorId, int ProductId), WheelProfileEntry> RemoteCache = new();

    /// <summary>Every VID/PID pair a fetch has already been attempted for this run (success, failure, or disabled) - keeps a device that hotplugs repeatedly from re-hitting the network every time.</summary>
    private static readonly ConcurrentDictionary<(int VendorId, int ProductId), byte> Attempted = new();

    /// <summary>
    /// Preferred entry point for every caller that used to go straight to <see cref="WheelProfileService.TryResolve"/>:
    /// a successful Tier 1 fetch for this exact VID/PID wins, otherwise falls through to Tier 2
    /// unchanged. Synchronous and instant either way - this never blocks on the network itself,
    /// only ever reads whatever <see cref="PrefetchAsync"/> has already cached.
    /// </summary>
    public static bool TryResolve(int vendorId, int productId, out WheelProfileEntry? entry)
    {
        if (RemoteCache.TryGetValue((vendorId, productId), out entry)) return true;
        return WheelProfileService.TryResolve(vendorId, productId, out entry);
    }

    /// <summary>
    /// Fire-and-forget: call once per connected device (safe to call repeatedly - a no-op after
    /// the first attempt per VID/PID, and an immediate no-op whenever <see cref="RemoteRegistryUrlTemplate"/>
    /// is unset). On success, later <see cref="TryResolve"/> calls immediately pick up the new
    /// entry; on any failure - offline, timeout past the 2-second ceiling, 404 for an
    /// unrecognized device, malformed JSON - this silently leaves Tier 2/3 as the answer, exactly
    /// per the "must never crash on a lookup miss" requirement.
    /// </summary>
    public static async Task PrefetchAsync(int vendorId, int productId)
    {
        if (vendorId == 0) return;
        if (string.IsNullOrEmpty(RemoteRegistryUrlTemplate)) return;
        if (!Attempted.TryAdd((vendorId, productId), 0)) return;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            string url = string.Format(RemoteRegistryUrlTemplate, vendorId, productId);
            using var stream = await HttpClient.GetStreamAsync(url, cts.Token).ConfigureAwait(false);
            var entry = await JsonSerializer.DeserializeAsync<WheelProfileEntry>(stream, JsonOptions, cts.Token).ConfigureAwait(false);
            if (entry != null) RemoteCache[(vendorId, productId)] = entry;
        }
        catch
        {
            // Offline, timeout, 404/5xx, malformed JSON - all equally fall through to Tier 2/3.
        }
    }
}
