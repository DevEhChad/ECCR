using System;
using System.Collections.Generic;
using ECCR.Models;

namespace ECCR.Services;

public static class DevicePresetService
{
    public static string GetButtonDisplayName(string deviceName, int index)
    {
        string dev = deviceName.ToLowerInvariant();
        if (dev.Contains("dualsense") || dev.Contains("dualshock") || dev.Contains("sony") || dev.Contains("wireless controller") || dev.Contains("ps5") || dev.Contains("ps4"))
        {
            return index switch
            {
                0 => "Square (□)",
                1 => "Cross (✕)",
                2 => "Circle (○)",
                3 => "Triangle (△)",
                4 => "L1 Bumper",
                5 => "R1 Bumper",
                6 => "L2 Trigger",
                7 => "R2 Trigger",
                8 => "Create / Share",
                9 => "Options / Menu",
                10 => "L3 Stick Click",
                11 => "R3 Stick Click",
                12 => "PS Guide Button",
                13 => "Touchpad Click",
                128 => "D-Pad Up",
                129 => "D-Pad Right",
                130 => "D-Pad Down",
                131 => "D-Pad Left",
                _ => $"Button {index + 1}"
            };
        }

        if (dev.Contains("gamepad") || dev.Contains("controller") || dev.Contains("xbox"))
        {
            return index switch
            {
                0 => "A Button",
                1 => "B Button",
                2 => "X Button",
                3 => "Y Button",
                4 => "LB Bumper",
                5 => "RB Bumper",
                6 => "View / Back",
                7 => "Menu / Start",
                8 => "Left Stick Click (LSB)",
                9 => "Right Stick Click (RSB)",
                128 => "D-Pad Up",
                129 => "D-Pad Right",
                130 => "D-Pad Down",
                131 => "D-Pad Left",
                _ => $"Button {index + 1}"
            };
        }

        return $"Button {index + 1}";
    }

    public static string GetAxisDisplayName(string deviceName, int index)
    {
        string dev = deviceName.ToLowerInvariant();
        if (dev.Contains("dualsense") || dev.Contains("dualshock") || dev.Contains("sony") || dev.Contains("wireless controller") || dev.Contains("ps5") || dev.Contains("ps4"))
        {
            return index switch
            {
                0 => "Left Stick Horizontal",
                1 => "Left Stick Vertical",
                2 => "Right Stick Horizontal",
                3 => "L2 Trigger Axis",
                4 => "R2 Trigger Axis",
                5 => "Right Stick Vertical",
                _ => $"Axis {index + 1}"
            };
        }

        if (dev.Contains("gamepad") || dev.Contains("controller") || dev.Contains("xbox"))
        {
            return index switch
            {
                0 => "Left Stick X",
                1 => "Left Stick Y",
                2 => "Left Trigger (LT)",
                3 => "Right Stick X",
                4 => "Right Stick Y",
                5 => "Right Trigger (RT)",
                _ => $"Axis {index + 1}"
            };
        }

        return index switch
        {
            0 => "Steering Wheel (Axis X)",
            1 => "Throttle Pedal (Axis Y)",
            2 => "Brake Pedal (Axis Z)",
            3 => "Clutch Pedal (Axis Rx)",
            4 => "Handbrake (Axis Ry)",
            5 => "Slider 0",
            6 => "Slider 1",
            _ => $"Axis {index + 1}"
        };
    }

    public static List<PresetBindingItem> GeneratePreset(string deviceName, int buttonCount, int axisCount, bool targetIsWheel)
    {
        var list = new List<PresetBindingItem>();
        string dev = deviceName.ToLowerInvariant();
        bool isPlayStation = dev.Contains("dualsense") || dev.Contains("dualshock") || dev.Contains("sony") || dev.Contains("wireless controller") || dev.Contains("ps5") || dev.Contains("ps4");

        if (isPlayStation)
        {
            list.Add(new PresetBindingItem { PhysicalName = "Left Stick Horizontal", Type = InputType.Axis, PhysicalIndex = 0, DefaultTargetOutput = "[Xbox] Left Stick X (Steer / Horizontal)" });
            list.Add(new PresetBindingItem { PhysicalName = "Left Stick Vertical", Type = InputType.Axis, PhysicalIndex = 1, DefaultTargetOutput = "[Xbox] Left Stick Y (Vertical)" });
            list.Add(new PresetBindingItem { PhysicalName = "Right Stick Horizontal", Type = InputType.Axis, PhysicalIndex = 2, DefaultTargetOutput = "[Xbox] Right Stick X (Camera Horizontal)" });
            list.Add(new PresetBindingItem { PhysicalName = "L2 Trigger Axis", Type = InputType.Axis, PhysicalIndex = 3, DefaultTargetOutput = "[Xbox] Left Trigger (LT / L2 Axis)" });
            list.Add(new PresetBindingItem { PhysicalName = "R2 Trigger Axis", Type = InputType.Axis, PhysicalIndex = 4, DefaultTargetOutput = "[Xbox] Right Trigger (RT / R2 Axis)" });
            list.Add(new PresetBindingItem { PhysicalName = "Right Stick Vertical", Type = InputType.Axis, PhysicalIndex = 5, DefaultTargetOutput = "[Xbox] Right Stick Y (Camera Vertical)" });

            list.Add(new PresetBindingItem { PhysicalName = "Cross (✕)", Type = InputType.Button, PhysicalIndex = 1, DefaultTargetOutput = "[Xbox] Xbox A (Cross / South)" });
            list.Add(new PresetBindingItem { PhysicalName = "Circle (○)", Type = InputType.Button, PhysicalIndex = 2, DefaultTargetOutput = "[Xbox] Xbox B (Circle / East)" });
            list.Add(new PresetBindingItem { PhysicalName = "Square (□)", Type = InputType.Button, PhysicalIndex = 0, DefaultTargetOutput = "[Xbox] Xbox X (Square / West)" });
            list.Add(new PresetBindingItem { PhysicalName = "Triangle (△)", Type = InputType.Button, PhysicalIndex = 3, DefaultTargetOutput = "[Xbox] Xbox Y (Triangle / North)" });
            list.Add(new PresetBindingItem { PhysicalName = "L1 Bumper", Type = InputType.Button, PhysicalIndex = 4, DefaultTargetOutput = "[Xbox] Xbox LB (Left Bumper / L1)" });
            list.Add(new PresetBindingItem { PhysicalName = "R1 Bumper", Type = InputType.Button, PhysicalIndex = 5, DefaultTargetOutput = "[Xbox] Xbox RB (Right Bumper / R1)" });
            list.Add(new PresetBindingItem { PhysicalName = "L3 Stick Click", Type = InputType.Button, PhysicalIndex = 10, DefaultTargetOutput = "[Xbox] Xbox LSB (Left Stick Click / L3)" });
            list.Add(new PresetBindingItem { PhysicalName = "R3 Stick Click", Type = InputType.Button, PhysicalIndex = 11, DefaultTargetOutput = "[Xbox] Xbox RSB (Right Stick Click / R3)" });
            list.Add(new PresetBindingItem { PhysicalName = "Create / Share", Type = InputType.Button, PhysicalIndex = 8, DefaultTargetOutput = "[Xbox] Xbox View (Back / Share)" });
            list.Add(new PresetBindingItem { PhysicalName = "Options / Menu", Type = InputType.Button, PhysicalIndex = 9, DefaultTargetOutput = "[Xbox] Xbox Menu (Start / Options)" });
            list.Add(new PresetBindingItem { PhysicalName = "PS Guide Button", Type = InputType.Button, PhysicalIndex = 12, DefaultTargetOutput = "[Xbox] Xbox Guide (Home / PS)" });

            list.Add(new PresetBindingItem { PhysicalName = "D-Pad Up", Type = InputType.Button, PhysicalIndex = 128, DefaultTargetOutput = "[Xbox] D-Pad Up" });
            list.Add(new PresetBindingItem { PhysicalName = "D-Pad Down", Type = InputType.Button, PhysicalIndex = 130, DefaultTargetOutput = "[Xbox] D-Pad Down" });
            list.Add(new PresetBindingItem { PhysicalName = "D-Pad Left", Type = InputType.Button, PhysicalIndex = 131, DefaultTargetOutput = "[Xbox] D-Pad Left" });
            list.Add(new PresetBindingItem { PhysicalName = "D-Pad Right", Type = InputType.Button, PhysicalIndex = 129, DefaultTargetOutput = "[Xbox] D-Pad Right" });
        }
        else
        {
            for (int i = 0; i < Math.Min(axisCount, 6); i++)
            {
                string axisName = GetAxisDisplayName(deviceName, i);
                string target = targetIsWheel ? (i == 0 ? "[Wheel] Steering (Axis X)" : (i == 1 ? "[Wheel] Gas / Throttle (Axis Y)" : "[Wheel] Brake (Axis Z)")) : "[Xbox] Left Stick X (Steer / Horizontal)";
                list.Add(new PresetBindingItem { PhysicalName = axisName, Type = InputType.Axis, PhysicalIndex = i, DefaultTargetOutput = target });
            }

            for (int i = 0; i < Math.Min(buttonCount, 16); i++)
            {
                string btnName = GetButtonDisplayName(deviceName, i);
                string target = targetIsWheel ? $"[Wheel] Button {i + 1}" : "[Xbox] Xbox A (Cross / South)";
                list.Add(new PresetBindingItem { PhysicalName = btnName, Type = InputType.Button, PhysicalIndex = i, DefaultTargetOutput = target });
            }
        }

        return list;
    }
}
