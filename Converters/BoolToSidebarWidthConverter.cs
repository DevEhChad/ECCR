using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ECCR.Converters;

/// <summary>
/// Drives the sidebar's own <c>Width</c> from <c>IsSidebarCollapsed</c> so the icon rail stays
/// laid out consistently, rather than toggling the whole panel's <c>IsVisible</c>.
/// </summary>
public class BoolToSidebarWidthConverter : IValueConverter
{
    public const double CollapsedWidth = 64;
    public const double ExpandedWidth = 220;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool collapsed = value is true;
        return collapsed ? CollapsedWidth : ExpandedWidth;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
