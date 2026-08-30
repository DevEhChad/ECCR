using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ECCR.Converters;

public class PlayerBadgeInfo
{
    public string BadgeText { get; set; } = "P1";
    public string FullName { get; set; } = "Player 1 (Rig #1)";
    public IBrush Background { get; set; } = Brushes.Transparent;
    public IBrush Border { get; set; } = Brushes.Transparent;
    public IBrush Foreground { get; set; } = Brushes.White;
}

/// <summary>
/// Maps a raw <c>TargetDeviceId</c> (1-4) to the same themed "P1"/"P2"/... badge info as
/// <c>MainWindowViewModel.PlayerTargets</c> (see <see cref="ECCR.Models.PlayerTargetOption"/>).
/// Not currently referenced from any XAML - the main grid's player badges are bound directly
/// to a matching <see cref="ECCR.Models.PlayerTargetOption"/> from that list instead, via its
/// own color properties, so this converter is redundant with it. Safe to remove, or to use
/// in a spot where only the bare numeric ID is available and the full option list isn't.
/// </summary>
public class PlayerTargetConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        uint id = 1;
        if (value is uint u) id = u;
        else if (value is int i) id = (uint)i;

        return id switch
        {
            1 => new PlayerBadgeInfo
            {
                BadgeText = "P1",
                FullName = "Player 1 (Primary / Rig)",
                Background = Brush.Parse("#122A1E"),
                Border = Brush.Parse("#00E599"),
                Foreground = Brush.Parse("#5CFFBE")
            },
            2 => new PlayerBadgeInfo
            {
                BadgeText = "P2",
                FullName = "Player 2 (Split-Screen)",
                Background = Brush.Parse("#2B1D12"),
                Border = Brush.Parse("#FA8B3E"),
                Foreground = Brush.Parse("#FFAF70")
            },
            3 => new PlayerBadgeInfo
            {
                BadgeText = "P3",
                FullName = "Player 3",
                Background = Brush.Parse("#26142E"),
                Border = Brush.Parse("#C33EFA"),
                Foreground = Brush.Parse("#DB70FF")
            },
            4 => new PlayerBadgeInfo
            {
                BadgeText = "P4",
                FullName = "Player 4",
                Background = Brush.Parse("#2A141A"),
                Border = Brush.Parse("#FA3E5E"),
                Foreground = Brush.Parse("#FF7088")
            },
            _ => new PlayerBadgeInfo
            {
                BadgeText = $"P{id}",
                FullName = $"Player {id}",
                Background = Brush.Parse("#1A202E"),
                Border = Brush.Parse("#3E7BFA"),
                Foreground = Brush.Parse("#70A0FF")
            }
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}