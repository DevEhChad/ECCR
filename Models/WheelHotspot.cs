using Avalonia;

namespace ECCR.Models;

/// <summary>
/// One clickable region on the Binder page's generic wheel+pedal diagram, named by physical
/// position/function (not by any one brand's legend) so the same hotspot works across Moza,
/// Logitech, Fanatec, Thrustmaster, Simagic, and generic wheels via
/// <see cref="ECCR.Services.WheelHotspotService.GetWheelSemanticIndex"/>. Unlike
/// <see cref="SemanticButton"/> (gamepad, buttons only), this also names the wheel's analog
/// axes (steering, pedals, handbrake) since those - not digital buttons - are the primary
/// bindable elements on a wheel.
/// </summary>
public enum WheelSemanticInput
{
    FaceNorth,
    FaceEast,
    FaceSouth,
    FaceWest,
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    PaddleLeft,
    PaddleRight,
    BumperLeft,
    BumperRight,
    ShareView,
    OptionsMenu,
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
