using System.Collections.Generic;
using System.Linq;
using Avalonia;
using ECCR.Models;

namespace ECCR.Services;

/// <summary>
/// Hotspot coordinate table for the Visual Binder's controller-skin images
/// (Assets/ControllerSkins/*.png, chroma-keyed from Assets/Controller UI/*.jpg). All 6 skins
/// share the same native 2816x1536 canvas and button/stick layout (only shell color and
/// face-button glyphs differ), so one table covers every skin. Coordinates were measured
/// directly off the rendered artwork (connected-component analysis of the button/glyph
/// shapes), not guessed.
/// </summary>
public static class ControllerHotspotService
{
    public const double ImageNativeWidth = 2816;
    public const double ImageNativeHeight = 1536;

    public static StickWellPosition LeftStickWell { get; } = new() { CenterPixel = new Point(1117, 1118), TravelRadiusPixels = 85 };
    public static StickWellPosition RightStickWell { get; } = new() { CenterPixel = new Point(1700, 1118), TravelRadiusPixels = 85 };

    public static IReadOnlyList<ControllerHotspot> Hotspots { get; } = BuildHotspots();

    private static Rect Around(double cx, double cy, double width, double height) =>
        new(cx - width / 2, cy - height / 2, width, height);

    private static List<ControllerHotspot> BuildHotspots() => new()
    {
        new() { Semantic = SemanticButton.DPadUp,    Label = "D-Pad Up",               PixelBounds = Around(814, 786, 140, 140) },
        new() { Semantic = SemanticButton.DPadDown,  Label = "D-Pad Down",             PixelBounds = Around(831, 936, 140, 140) },
        new() { Semantic = SemanticButton.DPadLeft,  Label = "D-Pad Left",             PixelBounds = Around(726, 862, 140, 140) },
        new() { Semantic = SemanticButton.DPadRight, Label = "D-Pad Right",            PixelBounds = Around(918, 864, 140, 140) },

        new() { Semantic = SemanticButton.FaceNorth, Label = "Triangle / Y (North)",   PixelBounds = Around(2008, 752, 160, 160) },
        new() { Semantic = SemanticButton.FaceEast,  Label = "Circle / B (East)",      PixelBounds = Around(2142, 858, 160, 160) },
        new() { Semantic = SemanticButton.FaceSouth, Label = "Cross / A (South)",      PixelBounds = Around(1996, 968, 160, 160) },
        new() { Semantic = SemanticButton.FaceWest,  Label = "Square / X (West)",      PixelBounds = Around(1861, 862, 160, 160) },

        new() { Semantic = SemanticButton.L2, Label = "L2 / LT (Trigger)",             PixelBounds = Around(818, 308, 280, 200) },
        new() { Semantic = SemanticButton.R2, Label = "R2 / RT (Trigger)",             PixelBounds = Around(1998, 308, 280, 200) },
        new() { Semantic = SemanticButton.L1, Label = "L1 / LB (Bumper)",              PixelBounds = Around(800, 476, 280, 150) },
        new() { Semantic = SemanticButton.R1, Label = "R1 / RB (Bumper)",              PixelBounds = Around(2017, 476, 280, 150) },

        new() { Semantic = SemanticButton.Share,   Label = "Share / Create / View",    PixelBounds = Around(961, 694, 100, 100) },
        new() { Semantic = SemanticButton.Options, Label = "Options / Menu",           PixelBounds = Around(1856, 693, 100, 100) },
        new() { Semantic = SemanticButton.Guide,   Label = "PS / Guide / Home",        PixelBounds = Around(1408, 1038, 110, 110) },

        new() { Semantic = SemanticButton.L3, Label = "L3 (Left Stick Click)",         PixelBounds = Around(1117, 1118, 220, 220) },
        new() { Semantic = SemanticButton.R3, Label = "R3 (Right Stick Click)",        PixelBounds = Around(1700, 1118, 220, 220) },

        new() { Semantic = SemanticButton.TouchpadClick, Label = "Touchpad Click",     PixelBounds = new Rect(1012, 522, 793, 398) },
    };

    public static string GetSemanticLabel(SemanticButton semantic) =>
        Hotspots.First(h => h.Semantic == semantic).Label;

    /// <summary>
    /// Maps a semantic hotspot to the physical button index this hardware category reports it
    /// on, using the exact index numbering already established in
    /// <see cref="DevicePresetService.GetButtonDisplayName"/>/<see cref="DevicePresetService.GeneratePreset"/>.
    /// Returns null when the category has no discrete button for that semantic (e.g. Xbox pads
    /// have no L2/R2 *button*, only an axis, and no touchpad) - the hotspot then simply never
    /// lights up for that category, though it can still be bound via the physical-press capture
    /// flow if the user pulls the trigger far enough to register as an axis instead.
    /// </summary>
    public static int? GetSemanticButtonIndex(DeviceHardwareCategory category, SemanticButton semantic)
    {
        switch (category)
        {
            case DeviceHardwareCategory.PlayStationController:
                return semantic switch
                {
                    SemanticButton.FaceWest => 0,
                    SemanticButton.FaceSouth => 1,
                    SemanticButton.FaceEast => 2,
                    SemanticButton.FaceNorth => 3,
                    SemanticButton.L1 => 4,
                    SemanticButton.R1 => 5,
                    SemanticButton.L2 => 6,
                    SemanticButton.R2 => 7,
                    SemanticButton.Share => 8,
                    SemanticButton.Options => 9,
                    SemanticButton.L3 => 10,
                    SemanticButton.R3 => 11,
                    SemanticButton.Guide => 12,
                    SemanticButton.TouchpadClick => 13,
                    SemanticButton.DPadUp => 128,
                    SemanticButton.DPadRight => 129,
                    SemanticButton.DPadDown => 130,
                    SemanticButton.DPadLeft => 131,
                    _ => null
                };

            case DeviceHardwareCategory.NintendoController:
                // Nintendo's diamond is physically rotated relative to Xbox/PlayStation - map by
                // on-controller position, not by the raw index's own letter.
                return semantic switch
                {
                    SemanticButton.FaceNorth => 3, // X
                    SemanticButton.FaceEast => 1,  // A
                    SemanticButton.FaceSouth => 0, // B
                    SemanticButton.FaceWest => 2,  // Y
                    SemanticButton.L1 => 4,
                    SemanticButton.R1 => 5,
                    SemanticButton.L2 => 6, // ZL
                    SemanticButton.R2 => 7, // ZR
                    SemanticButton.Share => 8,   // Minus
                    SemanticButton.Options => 9, // Plus
                    SemanticButton.L3 => 10,
                    SemanticButton.R3 => 11,
                    SemanticButton.Guide => 12, // Home
                    SemanticButton.DPadUp => 128,
                    SemanticButton.DPadRight => 129,
                    SemanticButton.DPadDown => 130,
                    SemanticButton.DPadLeft => 131,
                    _ => null // no touchpad
                };

            case DeviceHardwareCategory.XboxGamepad:
            case DeviceHardwareCategory.GenericGamepad:
                return semantic switch
                {
                    SemanticButton.FaceSouth => 0, // A
                    SemanticButton.FaceEast => 1,  // B
                    SemanticButton.FaceWest => 2,  // X
                    SemanticButton.FaceNorth => 3, // Y
                    SemanticButton.L1 => 4,
                    SemanticButton.R1 => 5,
                    SemanticButton.Share => 6,   // View/Back
                    SemanticButton.Options => 7, // Menu/Start
                    SemanticButton.L3 => 8,
                    SemanticButton.R3 => 9,
                    SemanticButton.Guide => 10,
                    SemanticButton.DPadUp => 128,
                    SemanticButton.DPadRight => 129,
                    SemanticButton.DPadDown => 130,
                    SemanticButton.DPadLeft => 131,
                    _ => null // no L2/R2 button, no touchpad
                };

            default:
                return null; // wheel/pedal categories and anything unrecognized: no digital-button highlight
        }
    }
}
