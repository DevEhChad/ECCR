using System;
using System.Globalization;
using Avalonia.Data.Converters;
using ECCR.Models;

namespace ECCR.Converters;

/// <summary>Header text for the Rebinding Table's "Rebind" popup - "Rebind {Input Name}" plus the current Assigned Key, if any.</summary>
public class RebindTitleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MappingEntry entry) return "Rebind";

        string inputName = SemanticInputNameConverter.ResolveFor(entry);
        return entry.IsKeyboardTarget || entry.IsBound
            ? $"Rebind {inputName}: {entry.TargetOutput}"
            : $"Rebind {inputName}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
