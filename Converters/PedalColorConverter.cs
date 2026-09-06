using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ECCR.Converters;

/// <summary>
/// Resolves a pedal/lever's accent color from its <see cref="Models.MappingEntry.TargetOutput"/>
/// string - shared by <see cref="Views.CalibrationDialog"/>'s pedal-bar visual and the Binder
/// page's Pedal Settings tab, so both surfaces agree on the same color table instead of each
/// keeping its own copy.
/// </summary>
public class PedalColorConverter : IValueConverter
{
    public static IBrush Resolve(string? targetOutput)
    {
        if (targetOutput == null) return Brush.Parse("#00E599");
        if (targetOutput.Contains("Handbrake", StringComparison.OrdinalIgnoreCase)) return Brush.Parse("#FFAF70");
        if (targetOutput.Contains("Brake", StringComparison.OrdinalIgnoreCase)) return Brush.Parse("#FF4D6A");
        if (targetOutput.Contains("Clutch", StringComparison.OrdinalIgnoreCase)) return Brush.Parse("#3E7BFA");
        return Brush.Parse("#00E599"); // Throttle/Gas, and the generic fallback
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Resolve(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
