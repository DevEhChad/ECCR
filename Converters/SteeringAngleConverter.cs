using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ECCR.Converters;

/// <summary>
/// Maps a calibrated 0..1 axis value to a wheel rotation angle, spanning the mapping's own
/// <see cref="ECCR.Models.MappingEntry.WheelRotationDegrees"/> lock-to-lock range (values[1])
/// rather than a fixed 900°, since direct-drive wheels commonly exceed 1080°.
/// </summary>
public class SteeringAngleConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        double pct = values.Count > 0 && values[0] is double d ? d : 0.5;
        double totalDegrees = values.Count > 1 && values[1] is int i ? i : 900;
        return (pct - 0.5) * totalDegrees;
    }
}
