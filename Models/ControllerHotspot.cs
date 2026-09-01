using Avalonia;

namespace ECCR.Models;

/// <summary>
/// One clickable region on a Visual Binder controller skin, named by physical position
/// (not by any one brand's button letters) so the same hotspot works across PlayStation,
/// Xbox, Nintendo, and generic pads via <see cref="ECCR.Services.ControllerHotspotService.GetSemanticButtonIndex"/>.
/// </summary>
public enum SemanticButton
{
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    FaceNorth,
    FaceEast,
    FaceSouth,
    FaceWest,
    L1,
    R1,
    L2,
    R2,
    L3,
    R3,
    Share,
    Options,
    Guide,
    TouchpadClick
}

/// <summary>A named clickable region on the controller skin image, in native 2816x1536 pixel space.</summary>
public class ControllerHotspot
{
    public SemanticButton Semantic { get; init; }
    public string Label { get; init; } = string.Empty;
    public Rect PixelBounds { get; init; }
}

/// <summary>Rest position and travel range of an analog stick graphic, in native pixel space.</summary>
public class StickWellPosition
{
    public Point CenterPixel { get; init; }
    public double TravelRadiusPixels { get; init; }
}
