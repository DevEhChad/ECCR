using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ECCR.Models;

public enum InputType
{
    Axis,
    Button
}

public partial class MappingEntry : ObservableObject
{
    [ObservableProperty]
    private Guid _sourceDeviceGuid = Guid.Empty;

    [ObservableProperty]
    private string _sourceDeviceName = "Select Device...";

    [ObservableProperty]
    private string _sourceDisplayName = "Click to Bind";

    [ObservableProperty]
    private InputType _sourceType = InputType.Axis;

    [ObservableProperty]
    private int _sourceIndex = 0;

    [ObservableProperty]
    private uint _targetDeviceId = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    [NotifyPropertyChangedFor(nameof(Foreground))]
    private string _targetOutput = "VA-Throttle (RT - Right Trigger / Gas)";

    // --- Resolved Properties for Line 195 & 196 ---

    [JsonIgnore]
    public string Glyph => GetGlyphForOutput(TargetOutput);

    [JsonIgnore]
    public string Foreground => GetColorForOutput(TargetOutput);

    private static string GetGlyphForOutput(string output) => output switch
    {
        var s when s.Contains("Throttle") || s.Contains("Gas") || s.Contains("RT") => "⮝",
        var s when s.Contains("Brake") || s.Contains("LT") => "⮟",
        var s when s.Contains("Steering") || s.Contains("X-Axis") => "◎",
        var s when s.Contains("Clutch") => "⎊",
        var s when s.Contains("Handbrake") => "⧈",
        var s when s.Contains("Gear") => "⚙",
        var s when s.StartsWith("Button") || s.Contains("Btn") => "●",
        "A" or "Cross" => "Ⓐ",
        "B" or "Circle" => "Ⓑ",
        "X" or "Square" => "Ⓧ",
        "Y" or "Triangle" => "Ⓨ",
        _ => "•"
    };

    private static string GetColorForOutput(string output) => output switch
    {
        var s when s.Contains("Throttle") || s.Contains("Gas") || s.Contains("RT") => "#00E599", // Green
        var s when s.Contains("Brake") || s.Contains("LT") => "#FF4D6D",                         // Red
        var s when s.Contains("Steering") || s.Contains("X-Axis") => "#3E7BFA",                  // Blue
        var s when s.Contains("Clutch") => "#A855F7",                                            // Purple
        var s when s.Contains("Handbrake") => "#EC4899",                                         // Pink
        var s when s.Contains("Gear") => "#F59E0B",                                              // Amber
        "A" => "#00E599",
        "B" => "#FF4D6D",
        "X" => "#3E7BFA",
        "Y" => "#FFCC00",
        _ => "#8B95A5"                                                                           // Muted Gray
    };

    // --- Calibration Thresholds & Points ---

    [ObservableProperty]
    private int _rawMin = 0;

    [ObservableProperty]
    private int _rawMax = 65535;

    [ObservableProperty]
    private double _deadzone = 0.0;

    [ObservableProperty]
    private double _outerDeadzone = 0.0;

    [ObservableProperty]
    private bool _isInverted = false;

    // --- Live UI Visualizers ---

    [JsonIgnore]
    [ObservableProperty]
    private int _latestRawReading = 0;

    [JsonIgnore]
    [ObservableProperty]
    private double _liveRawPercentage = 0.0;

    [JsonIgnore]
    [ObservableProperty]
    private double _liveOutputPercentage = 0.0;

    [JsonIgnore]
    public bool IsAxis => SourceType == InputType.Axis;

    partial void OnSourceTypeChanged(InputType value)
    {
        OnPropertyChanged(nameof(IsAxis));
    }

    [RelayCommand]
    public void SetCurrentAsMin() => RawMin = LatestRawReading;

    [RelayCommand]
    public void SetCurrentAsMax() => RawMax = LatestRawReading;

    [RelayCommand]
    public void ResetCalibration()
    {
        RawMin = 0;
        RawMax = 65535;
        Deadzone = 0.0;
        OuterDeadzone = 0.0;
        IsInverted = false;
    }

    public double CalculateCalibratedValue(int rawReading)
    {
        LatestRawReading = rawReading;
        LiveRawPercentage = Math.Clamp((double)rawReading / 65535.0, 0.0, 1.0);

        int min = RawMin;
        int max = RawMax;

        double normalized;
        if (min == max)
        {
            normalized = rawReading >= min ? 1.0 : 0.0;
        }
        else
        {
            normalized = (double)(rawReading - min) / (max - min);
        }

        normalized = Math.Clamp(normalized, 0.0, 1.0);

        if (IsInverted)
        {
            normalized = 1.0 - normalized;
        }

        if (Deadzone > 0.0 && Deadzone < 0.9)
        {
            if (normalized <= Deadzone)
                normalized = 0.0;
            else
                normalized = (normalized - Deadzone) / (1.0 - Deadzone);
        }

        if (OuterDeadzone > 0.0 && OuterDeadzone < 0.9)
        {
            double maxThreshold = 1.0 - OuterDeadzone;
            if (normalized >= maxThreshold)
                normalized = 1.0;
            else
                normalized = normalized / maxThreshold;
        }

        double finalResult = Math.Clamp(normalized, 0.0, 1.0);
        LiveOutputPercentage = finalResult;
        return finalResult;
    }
}