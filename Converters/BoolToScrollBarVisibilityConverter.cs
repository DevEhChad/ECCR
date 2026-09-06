using System;
using System.Globalization;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;

namespace ECCR.Converters;

/// <summary>
/// Drives the Binder page's gamepad Bindings-tab outer <see cref="ScrollViewer"/>'s
/// <c>HorizontalScrollBarVisibility</c> from <c>IsVisualBinderHidden</c>. A ScrollViewer set up
/// for horizontal scrolling (<c>Auto</c>) measures its content with unconstrained width and
/// arranges it at its own natural size rather than stretching to the viewport - which is exactly
/// why the Rebinding Table's <c>*</c> column previously couldn't claim the space freed up when
/// the diagram column collapsed to zero (the Grid never stretched to the larger viewport in the
/// first place). Disabling horizontal scrolling once the diagram is hidden (the table alone
/// always comfortably fits its own MinWidth) makes the ScrollViewer measure/arrange against the
/// real, finite viewport width instead, letting the Grid's star column actually stretch.
/// </summary>
public class BoolToScrollBarVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
