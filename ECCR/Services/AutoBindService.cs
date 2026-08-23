using System;
using System.Collections.Generic;
using ECCR.Models;

namespace ECCR.Services;

public static class AutoBindService
{
    public static List<MappingEntry> GenerateControllerPreset(Guid deviceGuid, string deviceName)
    {
        var binds = new List<MappingEntry>();
        bool isPlayStation = deviceName.Contains("DualSense", StringComparison.OrdinalIgnoreCase) ||
                             deviceName.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase) ||
                             deviceName.Contains("PS4", StringComparison.OrdinalIgnoreCase) ||
                             deviceName.Contains("PS5", StringComparison.OrdinalIgnoreCase);

        // Face Buttons: PS5 DirectInput Button Index -> Xbox 360 Target
        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Button,
            SourceIndex = isPlayStation ? 1 : 0, // Cross / A
            SourceDisplayName = isPlayStation ? "Cross (✕)" : "Button A",
            TargetOutput = "[Xbox] Xbox A (Cross / South)"
        });

        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Button,
            SourceIndex = isPlayStation ? 2 : 1, // Circle / B
            SourceDisplayName = isPlayStation ? "Circle (○)" : "Button B",
            TargetOutput = "[Xbox] Xbox B (Circle / East)"
        });

        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Button,
            SourceIndex = isPlayStation ? 0 : 2, // Square / X
            SourceDisplayName = isPlayStation ? "Square (□)" : "Button X",
            TargetOutput = "[Xbox] Xbox X (Square / West)"
        });

        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Button,
            SourceIndex = isPlayStation ? 3 : 3, // Triangle / Y
            SourceDisplayName = isPlayStation ? "Triangle (△)" : "Button Y",
            TargetOutput = "[Xbox] Xbox Y (Triangle / North)"
        });

        // Bumpers & Thumb Clicks
        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Button,
            SourceIndex = 4,
            SourceDisplayName = isPlayStation ? "L1 Bumper" : "Left Bumper (LB)",
            TargetOutput = "[Xbox] Xbox LB (Left Bumper / L1)"
        });

        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Button,
            SourceIndex = 5,
            SourceDisplayName = isPlayStation ? "R1 Bumper" : "Right Bumper (RB)",
            TargetOutput = "[Xbox] Xbox RB (Right Bumper / R1)"
        });

        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Button,
            SourceIndex = isPlayStation ? 10 : 8,
            SourceDisplayName = isPlayStation ? "L3 Stick Click" : "Left Stick Click (LS)",
            TargetOutput = "[Xbox] Xbox LSB (Left Stick Click / L3)"
        });

        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Button,
            SourceIndex = isPlayStation ? 11 : 9,
            SourceDisplayName = isPlayStation ? "R3 Stick Click" : "Right Stick Click (RS)",
            TargetOutput = "[Xbox] Xbox RSB (Right Stick Click / R3)"
        });

        // Start / Back / Guide
        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Button,
            SourceIndex = isPlayStation ? 9 : 7,
            SourceDisplayName = isPlayStation ? "Options / Menu" : "Menu / Start",
            TargetOutput = "[Xbox] Xbox Menu (Start / Options)"
        });

        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Button,
            SourceIndex = isPlayStation ? 8 : 6,
            SourceDisplayName = isPlayStation ? "Create / Share" : "View / Back",
            TargetOutput = "[Xbox] Xbox View (Back / Share)"
        });

        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Button,
            SourceIndex = isPlayStation ? 12 : 10,
            SourceDisplayName = isPlayStation ? "PS Guide Button" : "Guide / Home",
            TargetOutput = "[Xbox] Xbox Guide (Home / PS)"
        });

        // D-Pad
        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Button,
            SourceIndex = 128,
            SourceDisplayName = "D-Pad Up",
            TargetOutput = "[Xbox] D-Pad Up"
        });

        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Button,
            SourceIndex = 130,
            SourceDisplayName = "D-Pad Down",
            TargetOutput = "[Xbox] D-Pad Down"
        });

        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Button,
            SourceIndex = 131,
            SourceDisplayName = "D-Pad Left",
            TargetOutput = "[Xbox] D-Pad Left"
        });

        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Button,
            SourceIndex = 129,
            SourceDisplayName = "D-Pad Right",
            TargetOutput = "[Xbox] D-Pad Right"
        });

        // Analog Axes
        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Axis,
            SourceIndex = 0,
            SourceDisplayName = "Left Stick Horizontal",
            TargetOutput = "[Xbox] Left Stick X (Steer / Horizontal)",
            Deadzone = 0.08
        });

        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Axis,
            SourceIndex = 1,
            SourceDisplayName = "Left Stick Vertical",
            TargetOutput = "[Xbox] Left Stick Y (Vertical)",
            IsInverted = true,
            Deadzone = 0.08
        });

        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Axis,
            SourceIndex = isPlayStation ? 2 : 3,
            SourceDisplayName = "Right Stick Horizontal",
            TargetOutput = "[Xbox] Right Stick X (Camera Horizontal)",
            Deadzone = 0.08
        });

        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Axis,
            SourceIndex = isPlayStation ? 5 : 4,
            SourceDisplayName = "Right Stick Vertical",
            TargetOutput = "[Xbox] Right Stick Y (Camera Vertical)",
            IsInverted = true,
            Deadzone = 0.08
        });

        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Axis,
            SourceIndex = isPlayStation ? 3 : 2,
            SourceDisplayName = isPlayStation ? "L2 Trigger (Analog)" : "Left Trigger (LT)",
            TargetOutput = "[Xbox] Left Trigger (LT / L2 Axis)"
        });

        binds.Add(new MappingEntry
        {
            SourceDeviceGuid = deviceGuid,
            SourceDeviceName = deviceName,
            SourceType = InputType.Axis,
            SourceIndex = isPlayStation ? 4 : 5,
            SourceDisplayName = isPlayStation ? "R2 Trigger (Analog)" : "Right Trigger (RT)",
            TargetOutput = "[Xbox] Right Trigger (RT / R2 Axis)"
        });

        return binds;
    }
}