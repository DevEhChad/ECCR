using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ECCR.Models;
using ECCR.Services;

namespace ECCR.Converters;

/// <summary>
/// Maps a device's hardware category to the same logo art the sidebar/app already uses for
/// that device kind, so the Connected Devices row icons match the rest of the app instead of
/// falling back to an emoji glyph. Icons are loaded once and cached (not per-row) since they're
/// shared, immutable bitmaps.
/// </summary>
public class CategoryToGlyphConverter : IValueConverter
{
    private static readonly Bitmap ControllerIcon = LoadIcon("Full-ControllerLogo.png");
    private static readonly Bitmap DevicesIcon = LoadIcon("Full-DevicesLogo.png");

    private static Bitmap LoadIcon(string fileName) =>
        new(AssetLoader.Open(new Uri($"avares://ECCR/Assets/UILogos/{fileName}")));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DeviceHardwareCategory category && DevicePresetService.IsWheelOrPedalCategory(category)
            ? DevicesIcon
            : ControllerIcon;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
