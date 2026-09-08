using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ECCR.Converters;

/// <summary>
/// Highlights the Device Mappings page's currently-selected device row. A <see cref="IMultiValueConverter"/>
/// (values[0] = the row's own device name, values[1] = the page's selected device name) rather
/// than a "deviceRowSelected" style class toggled via <c>Classes.X="{Binding ...}"</c>: that
/// attached-property syntax only accepts a single simple binding, not a <c>MultiBinding</c>
/// (confirmed by an AVLN2000 "unable to resolve" build error), so equality-highlighting is
/// applied directly to <see cref="Avalonia.Controls.TemplatedControl.Background"/>/
/// <c>BorderBrush</c> instead. <paramref name="parameter"/> selects which of the two brushes to
/// return: "Border" for the border, anything else for the fill.
/// </summary>
public class DeviceRowStateConverter : IMultiValueConverter
{
    private static readonly IBrush SelectedBackground = Brush.Parse("#1A2E4C");
    private static readonly IBrush SelectedBorder = Brush.Parse("#00B074");
    private static readonly IBrush DefaultBackground = Brush.Parse("#121622");
    private static readonly IBrush DefaultBorder = Brush.Parse("#20283C");

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isSelected = values.Count >= 2 &&
            values[0] is string rowDeviceName &&
            values[1] is string selectedDeviceName &&
            string.Equals(rowDeviceName, selectedDeviceName, StringComparison.Ordinal);

        bool wantsBorder = string.Equals(parameter as string, "Border", StringComparison.OrdinalIgnoreCase);
        if (wantsBorder) return isSelected ? SelectedBorder : DefaultBorder;
        return isSelected ? SelectedBackground : DefaultBackground;
    }
}
