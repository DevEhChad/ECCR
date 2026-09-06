using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace ECCR.Converters;

/// <summary>
/// Loads an arbitrary on-disk file path (a game's banner/icon) into a <see cref="Bitmap"/> - the
/// first place in the app that loads images from outside the bundled <c>avares://</c> assets.
/// Returns null on a missing file or a decode failure, paired in XAML with the existing
/// <c>ObjectConverters.IsNotNull</c>/<c>IsNull</c> fallback-pair pattern to show a placeholder
/// glyph instead.
/// </summary>
public class FilePathToBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
