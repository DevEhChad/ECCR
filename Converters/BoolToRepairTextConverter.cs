using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ECCR.Converters;

public class BoolToRepairTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isInstalled && isInstalled)
        {
            return "Reinstall / Repair";
        }
        return "Install";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}