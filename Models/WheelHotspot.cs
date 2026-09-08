using Avalonia;

namespace ECCR.Models;

/// <summary>
/// One clickable region on the Device Mappings page's generic wheel+pedal diagram, limited to the
/// handful of inputs whose raw index is actually brand-independent (see
/// <see cref="ECCR.Services.WheelHotspotService.GetWheelSemanticIndex"/>): the analog
/// axes (steering, pedals, handbrake) - not digital buttons - are the primary bindable
/// elements on a wheel, plus the D-Pad, whose 4-direction numbering is a structural fact of
/// the POV hat rather than a functional guess. Deliberately has no face-button/paddle/bumper/
/// menu entries - which raw button index is a paddle vs. a rotary click vs. a menu button
/// shifts every time a rim is swapped, so this diagram no longer guesses a position for them;
/// the Device Mapping Table's Identify Input flow (press the physical control, watch which row
/// lights up) is how a user finds those instead.
/// </summary>
public enum WheelSemanticInput
{
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    SteeringAxis,
    ThrottlePedal,
    BrakePedal,
    ClutchPedal,
    HandbrakeAxis
}

/// <summary>A named clickable region on the generic wheel diagram, in native pixel space (see <see cref="ECCR.Services.WheelHotspotService.CanvasWidth"/>/<see cref="ECCR.Services.WheelHotspotService.CanvasHeight"/>).</summary>
public class WheelHotspot
{
    public WheelSemanticInput Semantic { get; init; }
    public string Label { get; init; } = string.Empty;
    public Rect PixelBounds { get; init; }
    public bool IsAxis { get; init; }
}
