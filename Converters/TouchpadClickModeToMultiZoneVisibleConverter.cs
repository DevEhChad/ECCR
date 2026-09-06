using System;
using System.Globalization;
using Avalonia.Data.Converters;
using ECCR.Models;

namespace ECCR.Converters;

/// <summary>Shows the "Top Band Height" slider only when Multi-Zone mode is selected - the other two modes never read <see cref="GamepadDeviceSettings.TouchpadTopSplitFraction"/>.</summary>
public class TouchpadClickModeToMultiZoneVisibleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TouchpadClickMode.MultiZone;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
