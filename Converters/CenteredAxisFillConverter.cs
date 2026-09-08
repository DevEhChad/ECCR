using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ECCR.Converters;

/// <summary>
/// Turns an axis's <see cref="ECCR.ViewModels.InputMatrixAxisTileViewModel.CenteredOffset"/>
/// (-1..1, 0 = rest/center) into one half of a centered bidirectional fill bar's geometry: a
/// fixed-width track with a fill <c>Rectangle</c> that grows from the center line toward the left
/// (negative offset) or right (positive offset) - the Input Matrix's steering-style gauge, the
/// centered-axis counterpart of <see cref="PedalFillHeightConverter"/>'s unidirectional fill.
/// One converter with a "Left"/"Width" <c>ConverterParameter</c> (matching this codebase's
/// existing single-converter-multiple-parameter-modes convention, e.g. <c>BoolToDeviceMappingColumnsConverter</c>)
/// rather than two near-identical classes.
/// </summary>
public class CenteredAxisFillConverter : IValueConverter
{
    private const double TrackWidth = 120.0;
    private const double HalfTrack = TrackWidth / 2.0;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double raw = value is double d ? d : 0.0;

        if (parameter as string == "UnidirectionalWidth")
        {
            // Plain 0..1 normalized value (not a centered offset) -> full-track-width fill, for
            // the unidirectional pedal/slider gauge sharing this same fixed track width.
            return Math.Clamp(raw, 0.0, 1.0) * TrackWidth;
        }

        double offset = Math.Clamp(raw, -1.0, 1.0);
        double magnitude = Math.Abs(offset) * HalfTrack;

        return (parameter as string) switch
        {
            "Width" => magnitude,
            "Left" => offset >= 0 ? HalfTrack : HalfTrack - magnitude,
            _ => 0.0
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
