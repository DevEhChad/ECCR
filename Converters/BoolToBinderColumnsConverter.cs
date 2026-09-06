using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace ECCR.Converters;

/// <summary>
/// Drives the Binder page's gamepad Bindings-tab grid width split. Takes the "is the diagram
/// visible" bool (callers pass <c>!IsVisualBinderHidden</c>, since the underlying per-device
/// setting is phrased as "hidden") - a plain <c>IsVisible=False</c> on the diagram alone isn't
/// enough to let the Rebinding Table fill the row: Avalonia's Grid still allocates a collapsed
/// element's column its full star-weight share of space, leaving a dead blank column. Zeroing out
/// the diagram column's and gutter's own <c>Width</c> (selected via a "Diagram"/"Gutter"
/// <c>ConverterParameter</c>) when not visible is what actually reclaims that space; rebinding the
/// whole <c>ColumnDefinitions</c> collection isn't supported by Avalonia's compiled bindings,
/// hence one converter applied to each column individually.
/// </summary>
public class BoolToBinderColumnsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value is true;
        bool isGutter = string.Equals(parameter as string, "Gutter", StringComparison.OrdinalIgnoreCase);

        if (!visible) return new GridLength(0);
        return isGutter ? new GridLength(16) : new GridLength(1.15, GridUnitType.Star);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
