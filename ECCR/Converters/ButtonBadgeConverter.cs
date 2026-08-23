using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ECCR.Converters;

public class BadgeInfo
{
    public string Glyph { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public IBrush Background { get; set; } = new SolidColorBrush(Color.Parse("#1F283C"));
    public IBrush Foreground { get; set; } = new SolidColorBrush(Colors.White);
    public IBrush Border { get; set; } = new SolidColorBrush(Color.Parse("#3D4B6E"));
    public bool HasBadge => !string.IsNullOrEmpty(Glyph);
}

public class ButtonBadgeConverter : IValueConverter
{
    public static readonly ButtonBadgeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string text = value?.ToString() ?? string.Empty;
        string lower = text.ToLowerInvariant();
        var badge = new BadgeInfo { Label = text };

        // === PLAYSTATION FACE BUTTONS ===
        if (lower.Contains("cross") || lower.Contains("✕") || lower.Contains("ps-cross"))
        {
            badge.Glyph = "✕";
            badge.Background = new SolidColorBrush(Color.Parse("#1A2B4C"));
            badge.Foreground = new SolidColorBrush(Color.Parse("#00A2FF")); // PlayStation Blue Cross
            badge.Border = new SolidColorBrush(Color.Parse("#0066CC"));
            return badge;
        }
        if (lower.Contains("circle") || lower.Contains("◯") || lower.Contains("ps-circle"))
        {
            badge.Glyph = "◯";
            badge.Background = new SolidColorBrush(Color.Parse("#3D1D24"));
            badge.Foreground = new SolidColorBrush(Color.Parse("#FF4D6D")); // PlayStation Red Circle
            badge.Border = new SolidColorBrush(Color.Parse("#B31938"));
            return badge;
        }
        if (lower.Contains("square") || lower.Contains("◻") || lower.Contains("ps-square"))
        {
            badge.Glyph = "◻";
            badge.Background = new SolidColorBrush(Color.Parse("#381D35"));
            badge.Foreground = new SolidColorBrush(Color.Parse("#FF66CC")); // PlayStation Pink Square
            badge.Border = new SolidColorBrush(Color.Parse("#B32D80"));
            return badge;
        }
        if (lower.Contains("triangle") || lower.Contains("△") || lower.Contains("ps-triangle"))
        {
            badge.Glyph = "△";
            badge.Background = new SolidColorBrush(Color.Parse("#143529"));
            badge.Foreground = new SolidColorBrush(Color.Parse("#00E599")); // PlayStation Green Triangle
            badge.Border = new SolidColorBrush(Color.Parse("#00995E"));
            return badge;
        }

        // === XBOX BUTTONS ===
        if (lower.StartsWith("xbox a") || lower.Contains("(a)"))
        {
            badge.Glyph = "A";
            badge.Background = new SolidColorBrush(Color.Parse("#133624"));
            badge.Foreground = new SolidColorBrush(Color.Parse("#107C10")); // Xbox Green A
            badge.Border = new SolidColorBrush(Color.Parse("#0E5E0E"));
            return badge;
        }
        if (lower.StartsWith("xbox b") || lower.Contains("(b)"))
        {
            badge.Glyph = "B";
            badge.Background = new SolidColorBrush(Color.Parse("#3D1A1F"));
            badge.Foreground = new SolidColorBrush(Color.Parse("#E81123")); // Xbox Red B
            badge.Border = new SolidColorBrush(Color.Parse("#A80010"));
            return badge;
        }
        if (lower.StartsWith("xbox x") || lower.Contains("(x)"))
        {
            badge.Glyph = "X";
            badge.Background = new SolidColorBrush(Color.Parse("#142B47"));
            badge.Foreground = new SolidColorBrush(Color.Parse("#0078D7")); // Xbox Blue X
            badge.Border = new SolidColorBrush(Color.Parse("#00509E"));
            return badge;
        }
        if (lower.StartsWith("xbox y") || lower.Contains("(y)"))
        {
            badge.Glyph = "Y";
            badge.Background = new SolidColorBrush(Color.Parse("#3D3314"));
            badge.Foreground = new SolidColorBrush(Color.Parse("#FFB900")); // Xbox Yellow Y
            badge.Border = new SolidColorBrush(Color.Parse("#B38200"));
            return badge;
        }

        // === TRANSMISSION & SIM RACING BADGES ===
        if (lower.Contains("shifter"))
        {
            badge.Glyph = "⚙";
            badge.Background = new SolidColorBrush(Color.Parse("#2C223D"));
            badge.Foreground = new SolidColorBrush(Color.Parse("#B68CFF"));
            badge.Border = new SolidColorBrush(Color.Parse("#6842A6"));
            return badge;
        }
        if (lower.Contains("paddle"))
        {
            badge.Glyph = "⚡";
            badge.Background = new SolidColorBrush(Color.Parse("#20343D"));
            badge.Foreground = new SolidColorBrush(Color.Parse("#38BDF8"));
            badge.Border = new SolidColorBrush(Color.Parse("#0284C7"));
            return badge;
        }
        if (lower.Contains("throttle") || lower.Contains("gas"))
        {
            badge.Glyph = "▲";
            badge.Background = new SolidColorBrush(Color.Parse("#163A2D"));
            badge.Foreground = new SolidColorBrush(Color.Parse("#00E599"));
            badge.Border = new SolidColorBrush(Color.Parse("#00995E"));
            return badge;
        }
        if (lower.Contains("brake"))
        {
            badge.Glyph = "▼";
            badge.Background = new SolidColorBrush(Color.Parse("#3D1A1F"));
            badge.Foreground = new SolidColorBrush(Color.Parse("#FF5C5C"));
            badge.Border = new SolidColorBrush(Color.Parse("#A80010"));
            return badge;
        }
        if (lower.Contains("handbrake") || lower.Contains("ebrake"))
        {
            badge.Glyph = "⛔";
            badge.Background = new SolidColorBrush(Color.Parse("#3D2914"));
            badge.Foreground = new SolidColorBrush(Color.Parse("#FB923C"));
            badge.Border = new SolidColorBrush(Color.Parse("#C2410C"));
            return badge;
        }

        return badge;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}