using System;
using System.Globalization;
using Avalonia.Data.Converters;
using ECCR.Models;

namespace ECCR.Converters;

/// <summary>Header text for the Device Mapping Table's "Map Input" popup - "Map {Input Name}" plus the current Assigned Key, if any.</summary>
public class DeviceMappingTitleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MappingEntry entry) return "Map Input";

        string inputName = SemanticInputNameConverter.ResolveFor(entry);
        return entry.IsKeyboardTarget || entry.IsBound
            ? $"Map {inputName}: {entry.TargetOutput}"
            : $"Map {inputName}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
