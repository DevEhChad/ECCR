using Avalonia.Data.Converters;

namespace ECCR.Converters;

/// <summary>Small static converters for showing/hiding an empty-state message based on a collection's <c>Count</c>.</summary>
public static class IntToVisibilityConverters
{
    public static readonly IValueConverter IsZero = new FuncValueConverter<int, bool>(i => i == 0);
}
