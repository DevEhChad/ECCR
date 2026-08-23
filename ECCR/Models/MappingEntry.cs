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
    private string _targetOutput = "VA-Throttle (RT - Right Trigger / Gas)";

    // --- Calibration Thresholds & Points ---

    [ObservableProperty]
    private int _rawMin = 0;

    [ObservableProperty]
    private int _rawMax = 65535;

    [ObservableProperty]
    private double _deadzone = 0.0; // Inner Deadzone (0.0 to 0.4)

    [ObservableProperty]
    private double _outerDeadzone = 0.0; // Outer / Upper Deadzone (0.0 to 0.4)

    [ObservableProperty]
    private bool _isInverted = false;

    // --- Live UI Visualizers (Ignored by JSON persistence) ---

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
    public void SetCurrentAsMin()
    {
        RawMin = LatestRawReading;
    }

    [RelayCommand]
    public void SetCurrentAsMax()
    {
        RawMax = LatestRawReading;
    }

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
            // Handles both normal (Min < Max) and hardware-reversed potentiometer curves
            normalized = (double)(rawReading - min) / (max - min);
        }

        normalized = Math.Clamp(normalized, 0.0, 1.0);

        if (IsInverted)
        {
            normalized = 1.0 - normalized;
        }

        // Apply Inner Deadzone
        if (Deadzone > 0.0 && Deadzone < 0.9)
        {
            if (normalized <= Deadzone)
            {
                normalized = 0.0;
            }
            else
            {
                normalized = (normalized - Deadzone) / (1.0 - Deadzone);
            }
        }

        // Apply Outer Deadzone (ensures 100% reach without bottoming out physical sensor)
        if (OuterDeadzone > 0.0 && OuterDeadzone < 0.9)
        {
            double maxThreshold = 1.0 - OuterDeadzone;
            if (normalized >= maxThreshold)
            {
                normalized = 1.0;
            }
            else
            {
                normalized = normalized / maxThreshold;
            }
        }

        double finalResult = Math.Clamp(normalized, 0.0, 1.0);
        LiveOutputPercentage = finalResult;
        return finalResult;
    }
}