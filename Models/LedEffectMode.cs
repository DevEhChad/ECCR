namespace ECCR.Models;

/// <summary>
/// Lightbar animation modes offered on the Controller LED page. <see cref="Static"/> just
/// holds the picked color; every other mode drives the lightbar from a running clock instead
/// of a fixed value - see <see cref="ECCR.ViewModels.ControllerLedViewModel"/>'s effect timer.
/// </summary>
public enum LedEffectMode
{
    Static,
    Breathing,
    Pulse,
    Rainbow,
    ColorCycle,
    Strobe
}
