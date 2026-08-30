using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ECCR.Models;
using ECCR.Services;

namespace ECCR.Views;

public partial class CalibrationDialog : Window
{
    public CalibrationDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdateVisualizerMode();
    }

    /// <summary>
    /// Wheel/pedal-category devices get a dedicated steering dial or pedal bar visual;
    /// everything else (gamepads, and any wheel axis without a dedicated visual) keeps
    /// the original generic progress bar.
    /// </summary>
    private void UpdateVisualizerMode()
    {
        if (DataContext is not MappingEntry entry || entry.SourceType != InputType.Axis)
        {
            ShowGenericBar();
            return;
        }

        var category = DevicePresetService.DetectCategory(entry.SourceDeviceName);
        if (!DevicePresetService.IsWheelOrPedalCategory(category))
        {
            ShowGenericBar();
            return;
        }

        string target = entry.TargetOutput;
        bool isSteering = target.Contains("Steering", StringComparison.OrdinalIgnoreCase) ||
                           target.Contains("Steer", StringComparison.OrdinalIgnoreCase);
        bool isPedal = target.Contains("Throttle", StringComparison.OrdinalIgnoreCase) ||
                       target.Contains("Gas", StringComparison.OrdinalIgnoreCase) ||
                       target.Contains("Brake", StringComparison.OrdinalIgnoreCase) ||
                       target.Contains("Clutch", StringComparison.OrdinalIgnoreCase) ||
                       target.Contains("Handbrake", StringComparison.OrdinalIgnoreCase);

        if (isSteering)
        {
            GenericOutputBar.IsVisible = false;
            SteeringWheelPanel.IsVisible = true;
            PedalBarPanel.IsVisible = false;
        }
        else if (isPedal)
        {
            GenericOutputBar.IsVisible = false;
            SteeringWheelPanel.IsVisible = false;
            PedalBarPanel.IsVisible = true;
            PedalFill.Background = ResolvePedalBrush(target);
        }
        else
        {
            ShowGenericBar();
        }
    }

    private void ShowGenericBar()
    {
        GenericOutputBar.IsVisible = true;
        SteeringWheelPanel.IsVisible = false;
        PedalBarPanel.IsVisible = false;
    }

    private static IBrush ResolvePedalBrush(string targetOutput)
    {
        if (targetOutput.Contains("Handbrake", StringComparison.OrdinalIgnoreCase)) return Brush.Parse("#FFAF70");
        if (targetOutput.Contains("Brake", StringComparison.OrdinalIgnoreCase)) return Brush.Parse("#FF4D6A");
        if (targetOutput.Contains("Clutch", StringComparison.OrdinalIgnoreCase)) return Brush.Parse("#3E7BFA");
        return Brush.Parse("#00E599");
    }

    private void OnSetMinClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MappingEntry entry)
        {
            entry.RawMin = entry.LatestRawReading;
        }
    }

    private void OnSetCenterClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MappingEntry entry)
        {
            entry.RawCenter = entry.LatestRawReading;
        }
    }

    private void OnSetMaxClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MappingEntry entry)
        {
            entry.RawMax = entry.LatestRawReading;
        }
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MappingEntry entry)
        {
            entry.RawMin = 0;
            entry.RawMax = 65535;
            entry.RawCenter = 32767;
            entry.Deadzone = 0.0;
            entry.IsInverted = false;
        }
    }

    private void OnDoneClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}