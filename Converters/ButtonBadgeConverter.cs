using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ECCR.Converters;

public class BadgeInfo
{
    public bool HasBadge { get; set; } = false;
    public string Glyph { get; set; } = string.Empty;
    public IBrush Background { get; set; } = Brushes.Transparent;
    public IBrush Border { get; set; } = Brushes.Transparent;
    public IBrush Foreground { get; set; } = Brushes.White;
}

public class ButtonBadgeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name || string.IsNullOrWhiteSpace(name))
            return new BadgeInfo();

        string lower = name.ToLowerInvariant();

        // ==========================================
        // 1. EMULATED VIRTUAL CHANNELS: [Xbox]
        // ==========================================
        if (name.StartsWith("[Xbox]", StringComparison.OrdinalIgnoreCase))
        {
            if (name.Contains("Xbox A")) return CreateBadge("Ⓐ", "#193627", "#00E599", "#5CFFBE"); // Green
            if (name.Contains("Xbox B")) return CreateBadge("Ⓑ", "#3D1D24", "#FA3E5E", "#FF7088"); // Red
            if (name.Contains("Xbox X")) return CreateBadge("Ⓧ", "#1A2E4C", "#3E7BFA", "#70A0FF"); // Blue
            if (name.Contains("Xbox Y")) return CreateBadge("Ⓨ", "#3D3418", "#FFB900", "#FFD254"); // Yellow

            if (name.Contains("Xbox LB")) return CreateBadge("LB", "#232A3B", "#4A587A", "#BAC2DE");
            if (name.Contains("Xbox RB")) return CreateBadge("RB", "#232A3B", "#4A587A", "#BAC2DE");
            if (name.Contains("Left Trigger")) return CreateBadge("LT", "#232A3B", "#4A587A", "#BAC2DE");
            if (name.Contains("Right Trigger")) return CreateBadge("RT", "#232A3B", "#4A587A", "#BAC2DE");

            if (name.Contains("Left Stick X")) return CreateBadge("LX", "#1A2E4C", "#3E7BFA", "#70A0FF");
            if (name.Contains("Left Stick Y")) return CreateBadge("LY", "#1A2E4C", "#3E7BFA", "#70A0FF");
            if (name.Contains("Right Stick X")) return CreateBadge("RX", "#1A2E4C", "#3E7BFA", "#70A0FF");
            if (name.Contains("Right Stick Y")) return CreateBadge("RY", "#1A2E4C", "#3E7BFA", "#70A0FF");
            if (name.Contains("Xbox LSB")) return CreateBadge("LS", "#232A3B", "#4A587A", "#BAC2DE");
            if (name.Contains("Xbox RSB")) return CreateBadge("RS", "#232A3B", "#4A587A", "#BAC2DE");

            if (name.Contains("D-Pad Up")) return CreateBadge("▲", "#161B26", "#3D4B6E", "#FFFFFF");
            if (name.Contains("D-Pad Down")) return CreateBadge("▼", "#161B26", "#3D4B6E", "#FFFFFF");
            if (name.Contains("D-Pad Left")) return CreateBadge("◄", "#161B26", "#3D4B6E", "#FFFFFF");
            if (name.Contains("D-Pad Right")) return CreateBadge("►", "#161B26", "#3D4B6E", "#FFFFFF");
            if (name.Contains("Xbox View")) return CreateBadge("⧉", "#1E2B45", "#3E7BFA", "#70A0FF");
            if (name.Contains("Xbox Menu")) return CreateBadge("☰", "#1E2B45", "#3E7BFA", "#70A0FF");
            if (name.Contains("Xbox Guide")) return CreateBadge("⨂", "#193627", "#00E599", "#5CFFBE");

            return new BadgeInfo();
        }

        // ==========================================
        // 2. EMULATED VIRTUAL CHANNELS: [Wheel]
        // ==========================================
        if (name.StartsWith("[Wheel]", StringComparison.OrdinalIgnoreCase))
        {
            if (lower.Contains("steering")) return CreateBadge("⎈", "#1E2B45", "#3E7BFA", "#70A0FF");
            if (lower.Contains("gas") || lower.Contains("throttle")) return CreateBadge("🗲", "#193627", "#00E599", "#5CFFBE");
            if (lower.Contains("brake") && !lower.Contains("handbrake")) return CreateBadge("⛔", "#3D1D24", "#FA3E5E", "#FF7088");
            if (lower.Contains("clutch") && !lower.Contains("slider")) return CreateBadge("⚙", "#36291C", "#FA8B3E", "#FFAF70");
            if (lower.Contains("handbrake")) return CreateBadge("⎊", "#361D38", "#C33EFA", "#DB70FF");
            if (lower.Contains("paddle up")) return CreateBadge("▶", "#1F283C", "#5A6B8C", "#B0C0E0");
            if (lower.Contains("paddle down")) return CreateBadge("◀", "#1F283C", "#5A6B8C", "#B0C0E0");

            if (lower.Contains("1st gear")) return CreateBadge("1", "#252B3B", "#4D5B7A", "#C2CEE8");
            if (lower.Contains("2nd gear")) return CreateBadge("2", "#252B3B", "#4D5B7A", "#C2CEE8");
            if (lower.Contains("3rd gear")) return CreateBadge("3", "#252B3B", "#4D5B7A", "#C2CEE8");
            if (lower.Contains("4th gear")) return CreateBadge("4", "#252B3B", "#4D5B7A", "#C2CEE8");
            if (lower.Contains("5th gear")) return CreateBadge("5", "#252B3B", "#4D5B7A", "#C2CEE8");
            if (lower.Contains("6th gear")) return CreateBadge("6", "#252B3B", "#4D5B7A", "#C2CEE8");
            if (lower.Contains("7th gear")) return CreateBadge("7", "#252B3B", "#4D5B7A", "#C2CEE8");
            if (lower.Contains("reverse gear")) return CreateBadge("R", "#3D1D24", "#FA3E5E", "#FF7088");

            return new BadgeInfo();
        }

        // ==========================================
        // 3. PHYSICAL INPUTS (Detected Hardware)
        // ==========================================

        // --- Standard Xbox & Moza Wheels (Xbox Scheme) ---
        if (name.Contains("Moza A Button") || name.StartsWith("A Button") || name.Contains("Cross / A") || lower.Contains("(a / cross)"))
            return CreateBadge("Ⓐ", "#193627", "#00E599", "#5CFFBE"); // Green

        if (name.Contains("Moza B Button") || name.StartsWith("B Button") || name.Contains("Circle / B") || lower.Contains("(b / circle)"))
            return CreateBadge("Ⓑ", "#3D1D24", "#FA3E5E", "#FF7088"); // Red

        if (name.Contains("Moza X Button") || name.StartsWith("X Button") || name.Contains("Square / X") || lower.Contains("(x / square)"))
            return CreateBadge("Ⓧ", "#1A2E4C", "#3E7BFA", "#70A0FF"); // Blue

        if (name.Contains("Moza Y Button") || name.StartsWith("Y Button") || name.Contains("Triangle / Y") || lower.Contains("(y / triangle)"))
            return CreateBadge("Ⓨ", "#3D3418", "#FFB900", "#FFD254"); // Yellow

        if (name.Contains("Moza Xbox Guide Button") || name.Contains("Xbox Guide Button"))
            return CreateBadge("⨂", "#193627", "#00E599", "#5CFFBE");

        // --- PlayStation Face Buttons ---
        if (name.Contains("Cross (✕)") || name.Contains("✕")) return CreateBadge("✕", "#1A2E4C", "#3E7BFA", "#70A0FF");
        if (name.Contains("Circle (○)") || name.Contains("○")) return CreateBadge("○", "#3D1D24", "#FA3E5E", "#FF7088");
        if (name.Contains("Square (□)") || name.Contains("□")) return CreateBadge("□", "#381E3D", "#D43EFA", "#EA70FF");
        if (name.Contains("Triangle (△)") || name.Contains("△")) return CreateBadge("△", "#193627", "#00E599", "#5CFFBE");

        // --- PlayStation Triggers & Bumpers ---
        if (name.Contains("L1 Bumper")) return CreateBadge("L1", "#232A3B", "#4A587A", "#BAC2DE");
        if (name.Contains("R1 Bumper")) return CreateBadge("R1", "#232A3B", "#4A587A", "#BAC2DE");
        if (name.Contains("L2 Trigger")) return CreateBadge("L2", "#232A3B", "#4A587A", "#BAC2DE");
        if (name.Contains("R2 Trigger")) return CreateBadge("R2", "#232A3B", "#4A587A", "#BAC2DE");
        if (name.Contains("L3 Stick Click")) return CreateBadge("L3", "#232A3B", "#4A587A", "#BAC2DE");
        if (name.Contains("R3 Stick Click")) return CreateBadge("R3", "#232A3B", "#4A587A", "#BAC2DE");
        if (name.Contains("PS Guide Button")) return CreateBadge("PS", "#1A2E4C", "#3E7BFA", "#70A0FF");

        // --- Sim Wheel Hardware & Pedals ---
        if (lower.Contains("steering")) return CreateBadge("⎈", "#1E2B45", "#3E7BFA", "#70A0FF");
        if (lower.Contains("throttle") || lower.Contains("gas")) return CreateBadge("🗲", "#193627", "#00E599", "#5CFFBE");
        if (lower.Contains("brake") && !lower.Contains("handbrake")) return CreateBadge("⛔", "#3D1D24", "#FA3E5E", "#FF7088");
        if (lower.Contains("clutch")) return CreateBadge("⚙", "#36291C", "#FA8B3E", "#FFAF70");
        if (lower.Contains("handbrake") || lower.Contains("ebrake")) return CreateBadge("⎊", "#361D38", "#C33EFA", "#DB70FF");
        if (lower.Contains("right paddle")) return CreateBadge("▶", "#1F283C", "#5A6B8C", "#B0C0E0");
        if (lower.Contains("left paddle")) return CreateBadge("◀", "#1F283C", "#5A6B8C", "#B0C0E0");
        if (lower.Contains("shifter 1st gear")) return CreateBadge("1", "#252B3B", "#4D5B7A", "#C2CEE8");
        if (lower.Contains("shifter 2nd gear")) return CreateBadge("2", "#252B3B", "#4D5B7A", "#C2CEE8");
        if (lower.Contains("shifter 3rd gear")) return CreateBadge("3", "#252B3B", "#4D5B7A", "#C2CEE8");
        if (lower.Contains("shifter 4th gear")) return CreateBadge("4", "#252B3B", "#4D5B7A", "#C2CEE8");
        if (lower.Contains("shifter 5th gear")) return CreateBadge("5", "#252B3B", "#4D5B7A", "#C2CEE8");
        if (lower.Contains("shifter 6th gear")) return CreateBadge("6", "#252B3B", "#4D5B7A", "#C2CEE8");
        if (lower.Contains("shifter reverse gear")) return CreateBadge("R", "#3D1D24", "#FA3E5E", "#FF7088");

        return new BadgeInfo();
    }

    private static BadgeInfo CreateBadge(string glyph, string bgHex, string borderHex, string fgHex)
    {
        return new BadgeInfo
        {
            HasBadge = true,
            Glyph = glyph,
            Background = Brush.Parse(bgHex),
            Border = Brush.Parse(borderHex),
            Foreground = Brush.Parse(fgHex)
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}