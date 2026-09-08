using System.Collections.Generic;
using System.Linq;
using Avalonia;
using ECCR.Models;

namespace ECCR.Services;

/// <summary>
/// Hotspot coordinate table for the Device Mappings page's generic wheel+pedal diagram - the wheel
/// equivalent of <see cref="ControllerHotspotService"/>. Unlike the gamepad Visual Device Mapping,
/// there is no product photo to overlay: the diagram itself is hand-drawn vector shapes (see
/// <c>Views/DeviceMappingDiagramView.axaml</c>'s wheel branch), authored together with these hotspot
/// bounds on one shared 1400x900 canvas rather than measured off pre-existing artwork.
/// Only the brand-independent inputs get a hotspot here (steering, the 4 pedal/handbrake axes,
/// D-Pad) - see <see cref="WheelSemanticInput"/>'s doc comment for why every face-button/paddle/
/// bumper/menu hotspot was removed rather than positioned per brand. The Device Mapping Table (with
/// "Show All Inputs" on) remains the complete, always-accurate listing of every physical input;
/// this diagram is a quick-click convenience for the handful of controls it can place correctly.
/// </summary>
public static class WheelHotspotService
{
    public const double CanvasWidth = 1400;
    public const double CanvasHeight = 900;

    public static IReadOnlyList<WheelHotspot> Hotspots { get; } = BuildHotspots();

    private static Rect Around(double cx, double cy, double width, double height) =>
        new(cx - width / 2, cy - height / 2, width, height);

    private static List<WheelHotspot> BuildHotspots() => new()
    {
        // The wheel rim/hub itself - a large region behind every other hotspot below, so
        // clicking blank rim area still arms the steering axis.
        new() { Semantic = WheelSemanticInput.SteeringAxis, Label = "Steering", IsAxis = true, PixelBounds = Around(500, 420, 560, 560) },

        // No face-button/paddle/bumper/menu hotspots: which raw button index those land on
        // shifts with the attached rim and can't be positioned generically - see
        // WheelSemanticInput's doc comment. D-Pad stays since its 4-direction numbering is a
        // structural fact of the POV hat, not a functional guess.
        new() { Semantic = WheelSemanticInput.DPadUp,    Label = "D-Pad Up",    PixelBounds = Around(320, 350, 70, 70) },
        new() { Semantic = WheelSemanticInput.DPadRight, Label = "D-Pad Right", PixelBounds = Around(390, 420, 70, 70) },
        new() { Semantic = WheelSemanticInput.DPadDown,  Label = "D-Pad Down", PixelBounds = Around(320, 490, 70, 70) },
        new() { Semantic = WheelSemanticInput.DPadLeft,  Label = "D-Pad Left", PixelBounds = Around(250, 420, 70, 70) },

        new() { Semantic = WheelSemanticInput.ClutchPedal,   Label = "Clutch Pedal",   IsAxis = true, PixelBounds = Around(1090, 700, 80, 300) },
        new() { Semantic = WheelSemanticInput.BrakePedal,    Label = "Brake Pedal",    IsAxis = true, PixelBounds = Around(1190, 700, 80, 300) },
        new() { Semantic = WheelSemanticInput.ThrottlePedal, Label = "Throttle Pedal", IsAxis = true, PixelBounds = Around(1290, 700, 80, 300) },
        new() { Semantic = WheelSemanticInput.HandbrakeAxis, Label = "Handbrake",      IsAxis = true, PixelBounds = Around(1370, 700, 60, 220) },
    };

    public static string GetSemanticLabel(WheelSemanticInput semantic) =>
        Hotspots.First(h => h.Semantic == semantic).Label;

    /// <summary>
    /// Maps a semantic hotspot to its physical index. Every remaining <see cref="WheelSemanticInput"/>
    /// member (see its doc comment) is brand-independent by construction - the pedal/steering
    /// axis order and the POV hat's synthetic D-Pad indices are the same on every wheel/pedal
    /// category - so this is one flat table rather than a per-brand lookup. Returns null for a
    /// non-wheel category.
    /// </summary>
    public static int? GetWheelSemanticIndex(DeviceHardwareCategory category, WheelSemanticInput semantic)
    {
        if (!DevicePresetService.IsWheelOrPedalCategory(category)) return null;

        return semantic switch
        {
            WheelSemanticInput.SteeringAxis => 0,
            WheelSemanticInput.ThrottlePedal => 1,
            WheelSemanticInput.BrakePedal => 2,
            WheelSemanticInput.ClutchPedal => 3,
            WheelSemanticInput.HandbrakeAxis => 4,
            WheelSemanticInput.DPadUp => 128,
            WheelSemanticInput.DPadRight => 129,
            WheelSemanticInput.DPadDown => 130,
            WheelSemanticInput.DPadLeft => 131,
            _ => null
        };
    }
}
