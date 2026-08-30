using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ECCR.Converters;

/// <summary>Maps a calibrated 0..1 axis value to a bottom-anchored fill height in pixels.</summary>
public class PedalFillHeightConverter : IValueConverter
{
    private const double MaxHeight = 150.0;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double pct = value is double d ? d : 0.0;
        return Math.Clamp(pct, 0.0, 1.0) * MaxHeight;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
