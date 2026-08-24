using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ECCR.Models;

public partial class MappingEntry : ObservableObject
{
    [JsonIgnore]
    [ObservableProperty]
    private bool _isSelected = false;

    [ObservableProperty]
    private string _sourceDeviceName = string.Empty;

    [ObservableProperty]
    private Guid _sourceDeviceGuid = Guid.Empty;

    [ObservableProperty]
    private InputType _sourceType = InputType.Button;

    [ObservableProperty]
    private int _sourceIndex = 0;

    [ObservableProperty]
    private string _sourceDisplayName = "Click to Bind";

    [ObservableProperty]
    private uint _targetDeviceId = 1;

    [ObservableProperty]
    private string _targetOutput = "[Xbox] Xbox A (Cross / South / Handbrake)";

    [ObservableProperty]
    private double _deadzone = 0.0;

    [ObservableProperty]
    private bool _isInverted = false;

    [ObservableProperty]
    private int _rawMin = 0;

    [ObservableProperty]
    private int _rawMax = 65535;

    [ObservableProperty]
    private int _rawCenter = 32767;

    [JsonIgnore]
    [ObservableProperty]
    private double _liveRawPercentage = 0.0;

    [JsonIgnore]
    [ObservableProperty]
    private double _liveOutputPercentage = 0.0;

    [JsonIgnore]
    [ObservableProperty]
    private int _latestRawReading = 0;

    [JsonIgnore]
    public bool IsAxis => SourceType == InputType.Axis;

    partial void OnSourceTypeChanged(InputType value)
    {
        OnPropertyChanged(nameof(IsAxis));
    }

    public double CalculateCalibratedValue(int rawVal)
    {
        LatestRawReading = rawVal;
        LiveRawPercentage = Math.Clamp(rawVal / 65535.0, 0.0, 1.0);

        int min = Math.Min(RawMin, RawMax);
        int max = Math.Max(RawMin, RawMax);
        if (max - min <= 0) max = min + 1;

        double clamped = Math.Clamp(rawVal, min, max);
        double normalized = (clamped - min) / (double)(max - min);

        bool isCenteredAxis = TargetOutput.Contains("Stick") || TargetOutput.Contains("Steer") || TargetOutput.Contains("Steering");

        if (isCenteredAxis)
        {
            double offset = normalized - 0.5;
            double halfDz = Deadzone * 0.5;

            if (Math.Abs(offset) < halfDz)
            {
                offset = 0.0;
            }
            else
            {
                offset = Math.Sign(offset) * ((Math.Abs(offset) - halfDz) / (0.5 - halfDz)) * 0.5;
            }

            normalized = 0.5 + offset;
        }
        else
        {
            if (Deadzone > 0.0)
            {
                if (normalized < Deadzone)
                    normalized = 0.0;
                else
                    normalized = (normalized - Deadzone) / (1.0 - Deadzone);
            }
        }

        if (IsInverted)
        {
            normalized = 1.0 - normalized;
        }

        LiveOutputPercentage = Math.Clamp(normalized, 0.0, 1.0);
        return LiveOutputPercentage;
    }
}