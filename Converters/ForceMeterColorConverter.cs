using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ECCR.Converters;

/// <summary>
/// Colors the Wheel Settings tab's FFB output meter's <c>ProgressBar.Foreground</c> by how close
/// <see cref="ECCR.Models.WheelDeviceSettings.LiveOutputForcePercent"/> (0-1.5, where 1.0 = 100%
/// of DirectInput's own force range) is to clipping: green under 70%, yellow from 70% up to
/// 100%, red at/above 100% (matching <see cref="ECCR.Models.WheelDeviceSettings.IsClipping"/>'s
/// own threshold). Same palette as the rest of the app's live meters (see
/// <see cref="ButtonBadgeConverter"/>'s green/red hex values, "AccentYellowBrush" in
/// Styles/Colors.axaml).
/// </summary>
public class ForceMeterColorConverter : IValueConverter
{
    private static readonly IBrush Green = Brush.Parse("#00E599");
    private static readonly IBrush Yellow = Brush.Parse("#FFB900");
    private static readonly IBrush Red = Brush.Parse("#FA3E5E");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double percent = value is double d ? d : 0.0;
        if (percent >= 1.0) return Red;
        if (percent >= 0.7) return Yellow;
        return Green;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
