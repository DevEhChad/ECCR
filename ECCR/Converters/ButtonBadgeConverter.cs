using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ECCR.Converters;

public class BadgeInfo
{
    public bool HasBadge { get; set; } = true;
    public string Glyph { get; set; } = string.Empty;
    public IBrush Foreground { get; set; } = Brushes.White;
    public IBrush Background { get; set; } = new SolidColorBrush(Color.Parse("#161B26"));
    public IBrush Border { get; set; } = new SolidColorBrush(Color.Parse("#252F44"));
}

public class ButtonBadgeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name || string.IsNullOrWhiteSpace(name))
            return new BadgeInfo { HasBadge = false };

        string upper = name.ToUpperInvariant().Trim();

        // 1. VIRTUAL OUTPUT CHANNELS (Channel Column / Dropdowns)
        if (upper.StartsWith("[XBOX]") || upper.StartsWith("XBOX-"))
        {
            if (upper.Contains("XBOX A") || upper.Contains("XBOX-A"))
                return new BadgeInfo { Glyph = "Ⓐ", Foreground = new SolidColorBrush(Color.Parse("#00E599")), Background = new SolidColorBrush(Color.Parse("#122A22")), Border = new SolidColorBrush(Color.Parse("#237A55")) };

            if (upper.Contains("XBOX B") || upper.Contains("XBOX-B"))
                return new BadgeInfo { Glyph = "Ⓑ", Foreground = new SolidColorBrush(Color.Parse("#FF4D6D")), Background = new SolidColorBrush(Color.Parse("#2E151B")), Border = new SolidColorBrush(Color.Parse("#7A2332")) };

            if (upper.Contains("XBOX X") || upper.Contains("XBOX-X"))
                return new BadgeInfo { Glyph = "Ⓧ", Foreground = new SolidColorBrush(Color.Parse("#3E7BFA")), Background = new SolidColorBrush(Color.Parse("#14223E")), Border = new SolidColorBrush(Color.Parse("#234D7A")) };

            if (upper.Contains("XBOX Y") || upper.Contains("XBOX-Y"))
                return new BadgeInfo { Glyph = "Ⓨ", Foreground = new SolidColorBrush(Color.Parse("#FFCC00")), Background = new SolidColorBrush(Color.Parse("#2E2711")), Border = new SolidColorBrush(Color.Parse("#7A6923")) };

            if (upper.Contains("LB") || upper.Contains("LEFT BUMPER"))
                return new BadgeInfo { Glyph = "LB", Foreground = new SolidColorBrush(Color.Parse("#BAC2DE")) };

            if (upper.Contains("RB") || upper.Contains("RIGHT BUMPER"))
                return new BadgeInfo { Glyph = "RB", Foreground = new SolidColorBrush(Color.Parse("#BAC2DE")) };

            if (upper.Contains("LT") || upper.Contains("LEFT TRIGGER"))
                return new BadgeInfo { Glyph = "LT", Foreground = new SolidColorBrush(Color.Parse("#BAC2DE")) };

            if (upper.Contains("RT") || upper.Contains("RIGHT TRIGGER"))
                return new BadgeInfo { Glyph = "RT", Foreground = new SolidColorBrush(Color.Parse("#BAC2DE")) };

            if (upper.Contains("LSB") || upper.Contains("LEFT STICK CLICK"))
                return new BadgeInfo { Glyph = "LS", Foreground = new SolidColorBrush(Color.Parse("#BAC2DE")) };

            if (upper.Contains("RSB") || upper.Contains("RIGHT STICK CLICK"))
                return new BadgeInfo { Glyph = "RS", Foreground = new SolidColorBrush(Color.Parse("#BAC2DE")) };

            if (upper.Contains("LEFT STICK"))
                return new BadgeInfo { Glyph = "🕹️", Foreground = new SolidColorBrush(Color.Parse("#70A0FF")) };

            if (upper.Contains("RIGHT STICK"))
                return new BadgeInfo { Glyph = "🕹️", Foreground = new SolidColorBrush(Color.Parse("#A78BFA")) };

            if (upper.Contains("D-PAD") || upper.Contains("DPAD"))
            {
                string glyph = upper switch
                {
                    var s when s.Contains("UP") => "▲",
                    var s when s.Contains("DOWN") => "▼",
                    var s when s.Contains("LEFT") => "◄",
                    var s when s.Contains("RIGHT") => "►",
                    _ => "❖"
                };
                return new BadgeInfo { Glyph = glyph, Foreground = new SolidColorBrush(Color.Parse("#BAC2DE")) };
            }

            return new BadgeInfo { Glyph = "🎮", Foreground = new SolidColorBrush(Color.Parse("#3E7BFA")) };
        }

        if (upper.StartsWith("[WHEEL]") || upper.StartsWith("VJOY-"))
        {
            if (upper.Contains("STEERING") || upper.Contains("AXIS X"))
                return new BadgeInfo { Glyph = "◎", Foreground = new SolidColorBrush(Color.Parse("#3E7BFA")), Background = new SolidColorBrush(Color.Parse("#14223E")), Border = new SolidColorBrush(Color.Parse("#3E7BFA")) };

            if (upper.Contains("GAS") || upper.Contains("THROTTLE") || upper.Contains("AXIS Y"))
                return new BadgeInfo { Glyph = "⮝", Foreground = new SolidColorBrush(Color.Parse("#00E599")), Background = new SolidColorBrush(Color.Parse("#122A22")), Border = new SolidColorBrush(Color.Parse("#00E599")) };

            if (upper.Contains("BRAKE") || upper.Contains("AXIS Z"))
                return new BadgeInfo { Glyph = "⮟", Foreground = new SolidColorBrush(Color.Parse("#FF4D6D")), Background = new SolidColorBrush(Color.Parse("#2E151B")), Border = new SolidColorBrush(Color.Parse("#FF4D6D")) };

            if (upper.Contains("CLUTCH") || upper.Contains("AXIS RX"))
                return new BadgeInfo { Glyph = "⎊", Foreground = new SolidColorBrush(Color.Parse("#A855F7")), Background = new SolidColorBrush(Color.Parse("#241638")), Border = new SolidColorBrush(Color.Parse("#A855F7")) };

            if (upper.Contains("HANDBRAKE") || upper.Contains("AXIS RY"))
                return new BadgeInfo { Glyph = "⧈", Foreground = new SolidColorBrush(Color.Parse("#EC4899")), Background = new SolidColorBrush(Color.Parse("#2E1624")), Border = new SolidColorBrush(Color.Parse("#EC4899")) };

            if (upper.Contains("GEAR") || upper.Contains("REVERSE"))
            {
                string gear = upper switch
                {
                    var s when s.Contains("REVERSE") => "R",
                    var s when s.Contains("1ST") || s.Contains("GEAR 1") => "1",
                    var s when s.Contains("2ND") || s.Contains("GEAR 2") => "2",
                    var s when s.Contains("3RD") || s.Contains("GEAR 3") => "3",
                    var s when s.Contains("4TH") || s.Contains("GEAR 4") => "4",
                    var s when s.Contains("5TH") || s.Contains("GEAR 5") => "5",
                    var s when s.Contains("6TH") || s.Contains("GEAR 6") => "6",
                    var s when s.Contains("7TH") || s.Contains("GEAR 7") => "7",
                    _ => "⚙"
                };
                return new BadgeInfo { Glyph = gear, Foreground = new SolidColorBrush(Color.Parse("#F59E0B")), Background = new SolidColorBrush(Color.Parse("#2D2111")), Border = new SolidColorBrush(Color.Parse("#F59E0B")) };
            }

            if (upper.Contains("PADDLE UP"))
                return new BadgeInfo { Glyph = "▲", Foreground = new SolidColorBrush(Color.Parse("#3E7BFA")) };

            if (upper.Contains("PADDLE DOWN"))
                return new BadgeInfo { Glyph = "▼", Foreground = new SolidColorBrush(Color.Parse("#3E7BFA")) };

            return new BadgeInfo { Glyph = "◎", Foreground = new SolidColorBrush(Color.Parse("#3E7BFA")) };
        }

        // 2. PHYSICAL HARDWARE INPUTS (Physical Key / Axis Column)
        if (upper.StartsWith("CROSS") || upper.Contains("(✕)"))
            return new BadgeInfo { Glyph = "✕", Foreground = new SolidColorBrush(Color.Parse("#58A6FF")), Background = new SolidColorBrush(Color.Parse("#122438")), Border = new SolidColorBrush(Color.Parse("#234D7A")) };

        if (upper.StartsWith("CIRCLE") || upper.Contains("(○)"))
            return new BadgeInfo { Glyph = "○", Foreground = new SolidColorBrush(Color.Parse("#FF4D6D")), Background = new SolidColorBrush(Color.Parse("#2E151B")), Border = new SolidColorBrush(Color.Parse("#7A2332")) };

        if (upper.StartsWith("SQUARE") || upper.Contains("(□)"))
            return new BadgeInfo { Glyph = "□", Foreground = new SolidColorBrush(Color.Parse("#FF70A6")), Background = new SolidColorBrush(Color.Parse("#2E1528")), Border = new SolidColorBrush(Color.Parse("#7A2362")) };

        if (upper.StartsWith("TRIANGLE") || upper.Contains("(△)"))
            return new BadgeInfo { Glyph = "△", Foreground = new SolidColorBrush(Color.Parse("#00E599")), Background = new SolidColorBrush(Color.Parse("#122A22")), Border = new SolidColorBrush(Color.Parse("#237A55")) };

        if (upper.Contains("STEERING"))
            return new BadgeInfo { Glyph = "◎", Foreground = new SolidColorBrush(Color.Parse("#3E7BFA")), Background = new SolidColorBrush(Color.Parse("#14223E")), Border = new SolidColorBrush(Color.Parse("#3E7BFA")) };

        if (upper.Contains("THROTTLE") || upper.Contains("ACCELERATOR") || upper.Contains("GAS"))
            return new BadgeInfo { Glyph = "⮝", Foreground = new SolidColorBrush(Color.Parse("#00E599")), Background = new SolidColorBrush(Color.Parse("#122A22")), Border = new SolidColorBrush(Color.Parse("#00E599")) };

        if (upper.Contains("BRAKE") && !upper.Contains("HANDBRAKE"))
            return new BadgeInfo { Glyph = "⮟", Foreground = new SolidColorBrush(Color.Parse("#FF4D6D")), Background = new SolidColorBrush(Color.Parse("#2E151B")), Border = new SolidColorBrush(Color.Parse("#FF4D6D")) };

        if (upper.Contains("CLUTCH"))
            return new BadgeInfo { Glyph = "⎊", Foreground = new SolidColorBrush(Color.Parse("#A855F7")), Background = new SolidColorBrush(Color.Parse("#241638")), Border = new SolidColorBrush(Color.Parse("#A855F7")) };

        if (upper.Contains("HANDBRAKE") || upper.Contains("EBRAKE"))
            return new BadgeInfo { Glyph = "⧈", Foreground = new SolidColorBrush(Color.Parse("#EC4899")), Background = new SolidColorBrush(Color.Parse("#2E1624")), Border = new SolidColorBrush(Color.Parse("#EC4899")) };

        if (upper.Contains("SHIFTER") || upper.Contains("GEAR"))
        {
            string gear = upper switch
            {
                var s when s.Contains("REVERSE") => "R",
                var s when s.Contains("1ST") || s.Contains("GEAR 1") => "1",
                var s when s.Contains("2ND") || s.Contains("GEAR 2") => "2",
                var s when s.Contains("3RD") || s.Contains("GEAR 3") => "3",
                var s when s.Contains("4TH") || s.Contains("GEAR 4") => "4",
                var s when s.Contains("5TH") || s.Contains("GEAR 5") => "5",
                var s when s.Contains("6TH") || s.Contains("GEAR 6") => "6",
                var s when s.Contains("7TH") || s.Contains("GEAR 7") => "7",
                _ => "⚙"
            };
            return new BadgeInfo { Glyph = gear, Foreground = new SolidColorBrush(Color.Parse("#F59E0B")), Background = new SolidColorBrush(Color.Parse("#2D2111")), Border = new SolidColorBrush(Color.Parse("#F59E0B")) };
        }

        if (upper.Contains("PADDLE DOWN"))
            return new BadgeInfo { Glyph = "▼", Foreground = new SolidColorBrush(Color.Parse("#3E7BFA")) };

        if (upper.Contains("PADDLE UP"))
            return new BadgeInfo { Glyph = "▲", Foreground = new SolidColorBrush(Color.Parse("#3E7BFA")) };

        if (upper.StartsWith("BUTTON A"))
            return new BadgeInfo { Glyph = "Ⓐ", Foreground = new SolidColorBrush(Color.Parse("#00E599")), Background = new SolidColorBrush(Color.Parse("#122A22")), Border = new SolidColorBrush(Color.Parse("#237A55")) };

        if (upper.StartsWith("BUTTON B"))
            return new BadgeInfo { Glyph = "Ⓑ", Foreground = new SolidColorBrush(Color.Parse("#FF4D6D")), Background = new SolidColorBrush(Color.Parse("#2E151B")), Border = new SolidColorBrush(Color.Parse("#7A2332")) };

        if (upper.StartsWith("BUTTON X"))
            return new BadgeInfo { Glyph = "Ⓧ", Foreground = new SolidColorBrush(Color.Parse("#3E7BFA")), Background = new SolidColorBrush(Color.Parse("#14223E")), Border = new SolidColorBrush(Color.Parse("#234D7A")) };

        if (upper.StartsWith("BUTTON Y"))
            return new BadgeInfo { Glyph = "Ⓨ", Foreground = new SolidColorBrush(Color.Parse("#FFCC00")), Background = new SolidColorBrush(Color.Parse("#2E2711")), Border = new SolidColorBrush(Color.Parse("#7A6923")) };

        if (upper.Contains("L1") || upper.Contains("LB"))
            return new BadgeInfo { Glyph = "LB", Foreground = new SolidColorBrush(Color.Parse("#BAC2DE")) };

        if (upper.Contains("R1") || upper.Contains("RB"))
            return new BadgeInfo { Glyph = "RB", Foreground = new SolidColorBrush(Color.Parse("#BAC2DE")) };

        if (upper.Contains("L2") || upper.Contains("LT"))
            return new BadgeInfo { Glyph = "LT", Foreground = new SolidColorBrush(Color.Parse("#BAC2DE")) };

        if (upper.Contains("R2") || upper.Contains("RT"))
            return new BadgeInfo { Glyph = "RT", Foreground = new SolidColorBrush(Color.Parse("#BAC2DE")) };

        if (upper.Contains("D-PAD") || upper.Contains("DPAD"))
        {
            string dpad = upper switch
            {
                var s when s.Contains("UP") => "▲",
                var s when s.Contains("DOWN") => "▼",
                var s when s.Contains("LEFT") => "◄",
                var s when s.Contains("RIGHT") => "►",
                _ => "❖"
            };
            return new BadgeInfo { Glyph = dpad, Foreground = new SolidColorBrush(Color.Parse("#BAC2DE")) };
        }

        if (upper.Contains("STICK"))
            return new BadgeInfo { Glyph = "🕹️", Foreground = new SolidColorBrush(Color.Parse("#70A0FF")) };

        return new BadgeInfo { HasBadge = false };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}