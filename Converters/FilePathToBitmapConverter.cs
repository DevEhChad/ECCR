using System;
using System.Collections.Concurrent;
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
///
/// <para>
/// Decoded bitmaps are cached per path for the process lifetime - the Games/Profiles pages'
/// <c>ItemsControl</c>s aren't virtualized (a plain <c>WrapPanel</c>), so every card's image
/// binding re-runs this converter on the UI thread every time the page's item source is
/// rebuilt (navigating back to the page, a library rescan, etc.). Without caching that meant a
/// full synchronous disk-read-and-decode per game, per visit - the app-wide "freeze when
/// clicking through pages" this was one cause of. Game art files don't change on disk during a
/// session, so a plain never-evicted cache is safe.
/// </para>
/// </summary>
public class FilePathToBitmapConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;

        if (Cache.TryGetValue(path, out var cached)) return cached;

        Bitmap? bitmap = null;
        if (File.Exists(path))
        {
            try { bitmap = new Bitmap(path); }
            catch { bitmap = null; }
        }

        Cache[path] = bitmap;
        return bitmap;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
