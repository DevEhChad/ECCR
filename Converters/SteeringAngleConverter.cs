using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ECCR.Converters;

/// <summary>Maps a calibrated 0..1 axis value to a −450..450 degree wheel rotation.</summary>
public class SteeringAngleConverter : IValueConverter
{
    private const double MaxDegrees = 450.0;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double pct = value is double d ? d : 0.5;
        return (pct - 0.5) * 2.0 * MaxDegrees;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
