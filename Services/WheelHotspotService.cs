using System.Collections.Generic;
using System.Linq;
using Avalonia;
using ECCR.Models;

namespace ECCR.Services;

/// <summary>
/// Hotspot coordinate table for the Binder page's generic wheel+pedal diagram - the wheel
/// equivalent of <see cref="ControllerHotspotService"/>. Unlike the gamepad Visual Binder,
/// there is no product photo to overlay: the diagram itself is hand-drawn vector shapes (see
/// <c>Views/BinderDiagramView.axaml</c>'s wheel branch), authored together with these hotspot
/// bounds on one shared 1400x900 canvas rather than measured off pre-existing artwork.
///
/// <para>
/// NOTE: as of the Bindings tab now hiding the visual diagram entirely for wheel devices
/// (<c>Views/BinderPageView.axaml</c>'s Bindings tab - wheels get the Rebinding Table only,
/// which already lists every physical input via <c>BinderPageViewModel.RebuildDisplayRows</c>),
/// this service and the wheel-hotspot click-to-bind flow it feeds
/// (<see cref="ECCR.ViewModels.BinderDiagramViewModel.ArmWheelHotspot"/>,
/// <see cref="ECCR.ViewModels.WheelHotspotViewModel"/>) are unreachable from the UI. Left in
/// place rather than deleted since <see cref="ECCR.ViewModels.BinderDiagramViewModel"/> still
/// computes the same live wheel state (steering angle, pedal fills) the Calibration tab reuses,
/// and gamepad hotspot code shares that class - deleting just the wheel half would be a bigger,
/// separate cleanup than this pass's scope.
/// </para>
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

        new() { Semantic = WheelSemanticInput.PaddleLeft,  Label = "Left Paddle (Shift Down)",  PixelBounds = Around(260, 180, 160, 70) },
        new() { Semantic = WheelSemanticInput.PaddleRight, Label = "Right Paddle (Shift Up)",    PixelBounds = Around(740, 180, 160, 70) },
        new() { Semantic = WheelSemanticInput.BumperLeft,  Label = "Left Bumper (L1 / LB)",      PixelBounds = Around(340, 230, 120, 50) },
        new() { Semantic = WheelSemanticInput.BumperRight, Label = "Right Bumper (R1 / RB)",     PixelBounds = Around(660, 230, 120, 50) },

        new() { Semantic = WheelSemanticInput.FaceNorth, Label = "Triangle / Y (North)", PixelBounds = Around(680, 350, 70, 70) },
        new() { Semantic = WheelSemanticInput.FaceEast,  Label = "Circle / B (East)",    PixelBounds = Around(750, 420, 70, 70) },
        new() { Semantic = WheelSemanticInput.FaceSouth, Label = "Cross / A (South)",    PixelBounds = Around(680, 490, 70, 70) },
        new() { Semantic = WheelSemanticInput.FaceWest,  Label = "Square / X (West)",    PixelBounds = Around(610, 420, 70, 70) },

        new() { Semantic = WheelSemanticInput.DPadUp,    Label = "D-Pad Up",    PixelBounds = Around(320, 350, 70, 70) },
        new() { Semantic = WheelSemanticInput.DPadRight, Label = "D-Pad Right", PixelBounds = Around(390, 420, 70, 70) },
        new() { Semantic = WheelSemanticInput.DPadDown,  Label = "D-Pad Down", PixelBounds = Around(320, 490, 70, 70) },
        new() { Semantic = WheelSemanticInput.DPadLeft,  Label = "D-Pad Left", PixelBounds = Around(250, 420, 70, 70) },

        new() { Semantic = WheelSemanticInput.ShareView,   Label = "Share / View",   PixelBounds = Around(440, 340, 60, 60) },
        new() { Semantic = WheelSemanticInput.OptionsMenu, Label = "Options / Menu", PixelBounds = Around(560, 340, 60, 60) },

        new() { Semantic = WheelSemanticInput.ClutchPedal,   Label = "Clutch Pedal",   IsAxis = true, PixelBounds = Around(1090, 700, 80, 300) },
        new() { Semantic = WheelSemanticInput.BrakePedal,    Label = "Brake Pedal",    IsAxis = true, PixelBounds = Around(1190, 700, 80, 300) },
        new() { Semantic = WheelSemanticInput.ThrottlePedal, Label = "Throttle Pedal", IsAxis = true, PixelBounds = Around(1290, 700, 80, 300) },
        new() { Semantic = WheelSemanticInput.HandbrakeAxis, Label = "Handbrake",      IsAxis = true, PixelBounds = Around(1370, 700, 60, 220) },
    };

    public static string GetSemanticLabel(WheelSemanticInput semantic) =>
        Hotspots.First(h => h.Semantic == semantic).Label;

    /// <summary>
    /// Maps a semantic hotspot to the physical index this wheel category reports it on, using
    /// the exact index numbering already established in <see cref="DevicePresetService.GetButtonDisplayName"/>/
    /// <see cref="DevicePresetService.GetAxisDisplayName"/>/<c>MainWindowViewModel.GuessBestTargetChannel</c>'s
    /// wheel axis table (0=steering, 1=throttle, 2=brake, 3=clutch, 4=handbrake). Returns null
    /// for a non-wheel category, or a button semantic this category has no discrete input for
    /// (e.g. Moza has no separate bumper buttons - 8/9 are stick/rotary clicks there) - the
    /// hotspot then simply never lights up, though it can still be bound via the ordinary
    /// physical-press capture flow.
    /// </summary>
    public static int? GetWheelSemanticIndex(DeviceHardwareCategory category, WheelSemanticInput semantic)
    {
        if (!DevicePresetService.IsWheelOrPedalCategory(category)) return null;

        switch (semantic)
        {
            case WheelSemanticInput.SteeringAxis: return 0;
            case WheelSemanticInput.ThrottlePedal: return 1;
            case WheelSemanticInput.BrakePedal: return 2;
            case WheelSemanticInput.ClutchPedal: return 3;
            case WheelSemanticInput.HandbrakeAxis: return 4;
        }

        bool isMoza = category is DeviceHardwareCategory.MozaWheel;
        if (isMoza)
        {
            return semantic switch
            {
                WheelSemanticInput.FaceSouth => 0,
                WheelSemanticInput.FaceEast => 1,
                WheelSemanticInput.FaceWest => 2,
                WheelSemanticInput.FaceNorth => 3,
                WheelSemanticInput.ShareView => 4,
                WheelSemanticInput.OptionsMenu => 5,
                WheelSemanticInput.PaddleLeft => 6,
                WheelSemanticInput.PaddleRight => 7,
                WheelSemanticInput.DPadUp => 128,
                WheelSemanticInput.DPadRight => 129,
                WheelSemanticInput.DPadDown => 130,
                WheelSemanticInput.DPadLeft => 131,
                _ => null // no discrete bumper buttons - 8/9 are stick/rotary clicks on Moza
            };
        }

        if (category == DeviceHardwareCategory.LogitechRig)
        {
            return semantic switch
            {
                WheelSemanticInput.FaceSouth => 0,
                WheelSemanticInput.FaceWest => 1,
                WheelSemanticInput.FaceEast => 2,
                WheelSemanticInput.FaceNorth => 3,
                WheelSemanticInput.PaddleRight => 4,
                WheelSemanticInput.PaddleLeft => 5,
                WheelSemanticInput.BumperRight => 6,
                WheelSemanticInput.BumperLeft => 7,
                WheelSemanticInput.ShareView => 8,
                WheelSemanticInput.OptionsMenu => 9,
                WheelSemanticInput.DPadUp => 128,
                WheelSemanticInput.DPadRight => 129,
                WheelSemanticInput.DPadDown => 130,
                WheelSemanticInput.DPadLeft => 131,
                _ => null
            };
        }

        // FanatecRig / ThrustmasterRig / SimagicRig / GenericWheelOrPedals
        return semantic switch
        {
            WheelSemanticInput.FaceSouth => 0,
            WheelSemanticInput.FaceEast => 1,
            WheelSemanticInput.FaceWest => 2,
            WheelSemanticInput.FaceNorth => 3,
            WheelSemanticInput.ShareView => 4,
            WheelSemanticInput.OptionsMenu => 5,
            WheelSemanticInput.PaddleLeft => 6,
            WheelSemanticInput.PaddleRight => 7,
            WheelSemanticInput.BumperLeft => 8,
            WheelSemanticInput.BumperRight => 9,
            WheelSemanticInput.DPadUp => 128,
            WheelSemanticInput.DPadRight => 129,
            WheelSemanticInput.DPadDown => 130,
            WheelSemanticInput.DPadLeft => 131,
            _ => null
        };
    }
}
