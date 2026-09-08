using System.Collections.Generic;

namespace ECCR.Models;

/// <summary>
/// Per-device metadata in the global hardware store (<see cref="ECCR.Services.HardwareProfileService"/>,
/// <c>%AppData%\ECCR\hardware_profiles.json</c>), keyed by a VID/PID+cleaned-name composite key
/// rather than the raw DirectInput instance name - independent of any game profile, so switching/
/// importing/resetting profiles never touches a device's custom nicknames. Mirrors
/// <see cref="WheelDeviceSettings"/>'s nickname/hidden-input shape exactly (same "{InputType}:{index}"
/// key format), since that's the field set moving here; <see cref="WheelDeviceSettings"/>'s FFB/
/// rotation fields stay per-profile and are untouched by this class.
/// </summary>
public class HardwareDeviceProfile
{
    public string CompositeKey { get; set; } = string.Empty;

    /// <summary>Last-seen DirectInput instance name - for export files and diagnostics only, not part of the lookup key.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public int VendorId { get; set; }
    public int ProductId { get; set; }

    /// <summary>User-assigned friendly names for physical buttons/POV directions, keyed "Button:{index}".</summary>
    public Dictionary<string, string> ButtonNicknames { get; set; } = new();

    /// <summary>Physical inputs dismissed from the Device Mapping Table's "Show All Inputs" placeholder listing, keyed "{InputType}:{index}".</summary>
    public HashSet<string> HiddenPlaceholderInputs { get; set; } = new();

    public bool ShowAllPhysicalInputs { get; set; } = false;

    private static string InputKey(InputType type, int index) => $"{type}:{index}";

    public string? GetButtonNickname(InputType type, int index) =>
        ButtonNicknames.TryGetValue(InputKey(type, index), out var nick) && !string.IsNullOrWhiteSpace(nick) ? nick : null;

    public void SetButtonNickname(InputType type, int index, string? nickname)
    {
        string key = InputKey(type, index);
        if (string.IsNullOrWhiteSpace(nickname)) ButtonNicknames.Remove(key);
        else ButtonNicknames[key] = nickname.Trim();
    }

    public bool IsInputHidden(InputType type, int index) => HiddenPlaceholderInputs.Contains(InputKey(type, index));

    public void SetInputHidden(InputType type, int index, bool hidden)
    {
        string key = InputKey(type, index);
        if (hidden) HiddenPlaceholderInputs.Add(key);
        else HiddenPlaceholderInputs.Remove(key);
    }

    public void ClearHiddenPlaceholderInputs() => HiddenPlaceholderInputs.Clear();
}
