using System;
using System.Collections.Generic;
using ECCR.Models;

namespace ECCR.Services;

public class PresetBindingItem
{
    public string PhysicalName { get; set; } = string.Empty;
    public int PhysicalIndex { get; set; }
    public InputType Type { get; set; } = InputType.Button;
    public string DefaultTargetOutput { get; set; } = "[Wheel] Button 1";
    public string Description { get; set; } = string.Empty;
}

public static class DevicePresetService
{
    public static List<PresetBindingItem> GeneratePreset(string deviceName, int detectedButtons = 32, int detectedAxes = 8)
    {
        string dev = deviceName.ToLowerInvariant();
        var list = new List<PresetBindingItem>();

        bool isMoza = dev.Contains("moza");
        bool isLogitech = dev.Contains("g920") || dev.Contains("g29") || dev.Contains("g27") || dev.Contains("g923") || dev.Contains("logitech");
        bool isPlayStation = dev.Contains("dualsense") || dev.Contains("dualshock") || dev.Contains("sony") || dev.Contains("wireless controller");
        bool isHandbrake = dev.Contains("handbrake") || dev.Contains("ebrake") || dev.Contains("手柄");

        // === 1. MOZA RACING WHEEL BASE & RIMS ===
        if (isMoza)
        {
            list.Add(new PresetBindingItem { PhysicalName = "Axis-X", PhysicalIndex = 0, Type = InputType.Axis, DefaultTargetOutput = "[Wheel] Steering (Axis X)", Description = "Direct Wheel Axis" });

            list.Add(new PresetBindingItem { PhysicalName = "B1", PhysicalIndex = 0, DefaultTargetOutput = "[Wheel] Button 1", Description = "A Button / Select" });
            list.Add(new PresetBindingItem { PhysicalName = "B2", PhysicalIndex = 1, DefaultTargetOutput = "[Wheel] Button 2", Description = "B Button / Cancel" });
            list.Add(new PresetBindingItem { PhysicalName = "B3", PhysicalIndex = 2, DefaultTargetOutput = "[Wheel] Button 3", Description = "X Button / Action" });
            list.Add(new PresetBindingItem { PhysicalName = "B4", PhysicalIndex = 3, DefaultTargetOutput = "[Wheel] Button 4", Description = "Y Button / Camera" });
            list.Add(new PresetBindingItem { PhysicalName = "B5", PhysicalIndex = 4, DefaultTargetOutput = "[Wheel] Paddle Down", Description = "Left Paddle Shift" });
            list.Add(new PresetBindingItem { PhysicalName = "B6", PhysicalIndex = 5, DefaultTargetOutput = "[Wheel] Paddle Up", Description = "Right Paddle Shift" });
            list.Add(new PresetBindingItem { PhysicalName = "B7", PhysicalIndex = 6, DefaultTargetOutput = "[Wheel] Button 7", Description = "Start / Menu" });
            list.Add(new PresetBindingItem { PhysicalName = "B8", PhysicalIndex = 7, DefaultTargetOutput = "[Wheel] Button 8", Description = "View / Telemetry" });

            for (int i = 8; i < Math.Min(detectedButtons, 24); i++)
            {
                list.Add(new PresetBindingItem
                {
                    PhysicalName = $"B{i + 1}",
                    PhysicalIndex = i,
                    Type = InputType.Button,
                    DefaultTargetOutput = $"[Wheel] Button {i + 1}",
                    Description = $"Aux Button {i + 1}"
                });
            }
            return list;
        }

        // === 2. LOGITECH WHEEL, PEDALS & GATED SHIFTER ===
        if (isLogitech)
        {
            list.Add(new PresetBindingItem { PhysicalName = "Axis-X", PhysicalIndex = 0, Type = InputType.Axis, DefaultTargetOutput = "[Wheel] Steering (Axis X)", Description = "Steering Axis" });
            list.Add(new PresetBindingItem { PhysicalName = "Axis-Y", PhysicalIndex = 1, Type = InputType.Axis, DefaultTargetOutput = "[Wheel] Gas / Throttle (Axis Y)", Description = "Throttle Pedal" });
            list.Add(new PresetBindingItem { PhysicalName = "Axis-Rx", PhysicalIndex = 3, Type = InputType.Axis, DefaultTargetOutput = "[Wheel] Brake (Axis Z)", Description = "Brake Pedal" });
            list.Add(new PresetBindingItem { PhysicalName = "Slider-1", PhysicalIndex = 6, Type = InputType.Axis, DefaultTargetOutput = "[Wheel] Clutch (Axis Rx)", Description = "Clutch Pedal" });

            list.Add(new PresetBindingItem { PhysicalName = "B1", PhysicalIndex = 0, DefaultTargetOutput = "[Wheel] Button 1", Description = "A Button" });
            list.Add(new PresetBindingItem { PhysicalName = "B2", PhysicalIndex = 1, DefaultTargetOutput = "[Wheel] Button 2", Description = "B Button" });
            list.Add(new PresetBindingItem { PhysicalName = "B3", PhysicalIndex = 2, DefaultTargetOutput = "[Wheel] Button 3", Description = "X Button" });
            list.Add(new PresetBindingItem { PhysicalName = "B4", PhysicalIndex = 3, DefaultTargetOutput = "[Wheel] Button 4", Description = "Y Button" });
            list.Add(new PresetBindingItem { PhysicalName = "B5", PhysicalIndex = 4, DefaultTargetOutput = "[Wheel] Paddle Down", Description = "Downshift (LB)" });
            list.Add(new PresetBindingItem { PhysicalName = "B6", PhysicalIndex = 5, DefaultTargetOutput = "[Wheel] Paddle Up", Description = "Upshift (RB)" });

            list.Add(new PresetBindingItem { PhysicalName = "B12", PhysicalIndex = 11, DefaultTargetOutput = "[Wheel] Reverse Gear", Description = "Reverse Gate" });
            list.Add(new PresetBindingItem { PhysicalName = "B13", PhysicalIndex = 12, DefaultTargetOutput = "[Wheel] 1st Gear", Description = "1st Gate" });
            list.Add(new PresetBindingItem { PhysicalName = "B14", PhysicalIndex = 13, DefaultTargetOutput = "[Wheel] 2nd Gear", Description = "2nd Gate" });
            list.Add(new PresetBindingItem { PhysicalName = "B15", PhysicalIndex = 14, DefaultTargetOutput = "[Wheel] 3rd Gear", Description = "3rd Gate" });
            list.Add(new PresetBindingItem { PhysicalName = "B16", PhysicalIndex = 15, DefaultTargetOutput = "[Wheel] 4th Gear", Description = "4th Gate" });
            list.Add(new PresetBindingItem { PhysicalName = "B17", PhysicalIndex = 16, DefaultTargetOutput = "[Wheel] 5th Gear", Description = "5th Gate" });
            list.Add(new PresetBindingItem { PhysicalName = "B18", PhysicalIndex = 17, DefaultTargetOutput = "[Wheel] 6th Gear", Description = "6th Gate" });
            return list;
        }

        // === 3. PLAYSTATION CONTROLLERS (DirectInput DualSense/DS4) ===
        if (isPlayStation)
        {
            list.Add(new PresetBindingItem { PhysicalName = "B2", PhysicalIndex = 1, DefaultTargetOutput = "[Xbox] Xbox A (Cross)", Description = "Cross (✕)" });
            list.Add(new PresetBindingItem { PhysicalName = "B3", PhysicalIndex = 2, DefaultTargetOutput = "[Xbox] Xbox B (Circle)", Description = "Circle (◯)" });
            list.Add(new PresetBindingItem { PhysicalName = "B1", PhysicalIndex = 0, DefaultTargetOutput = "[Xbox] Xbox X (Square)", Description = "Square (◻)" });
            list.Add(new PresetBindingItem { PhysicalName = "B4", PhysicalIndex = 3, DefaultTargetOutput = "[Xbox] Xbox Y (Triangle)", Description = "Triangle (△)" });
            list.Add(new PresetBindingItem { PhysicalName = "B5", PhysicalIndex = 4, DefaultTargetOutput = "[Xbox] Xbox LB (Left Bumper)", Description = "L1 Bumper" });
            list.Add(new PresetBindingItem { PhysicalName = "B6", PhysicalIndex = 5, DefaultTargetOutput = "[Xbox] Xbox RB (Right Bumper)", Description = "R1 Bumper" });
            list.Add(new PresetBindingItem { PhysicalName = "B9", PhysicalIndex = 8, DefaultTargetOutput = "[Xbox] Xbox View (Back)", Description = "Share / Create" });
            list.Add(new PresetBindingItem { PhysicalName = "B10", PhysicalIndex = 9, DefaultTargetOutput = "[Xbox] Xbox Menu (Start)", Description = "Options" });
            list.Add(new PresetBindingItem { PhysicalName = "B11", PhysicalIndex = 10, DefaultTargetOutput = "[Xbox] Xbox LSB (Left Stick Click)", Description = "L3 Click" });
            list.Add(new PresetBindingItem { PhysicalName = "B12", PhysicalIndex = 11, DefaultTargetOutput = "[Xbox] Xbox RSB (Right Stick Click)", Description = "R3 Click" });

            list.Add(new PresetBindingItem { PhysicalName = "Axis-X", PhysicalIndex = 0, Type = InputType.Axis, DefaultTargetOutput = "[Xbox] Left Stick X (Steer)", Description = "Left Stick X" });
            list.Add(new PresetBindingItem { PhysicalName = "Axis-Y", PhysicalIndex = 1, Type = InputType.Axis, DefaultTargetOutput = "[Xbox] Left Stick Y", Description = "Left Stick Y" });
            list.Add(new PresetBindingItem { PhysicalName = "Axis-Z", PhysicalIndex = 2, Type = InputType.Axis, DefaultTargetOutput = "[Xbox] Left Trigger (LT / Brake)", Description = "L2 Analog Trigger" });
            list.Add(new PresetBindingItem { PhysicalName = "Axis-Rz", PhysicalIndex = 5, Type = InputType.Axis, DefaultTargetOutput = "[Xbox] Right Trigger (RT / Gas)", Description = "R2 Analog Trigger" });

            list.Add(new PresetBindingItem { PhysicalName = "D-Pad Up", PhysicalIndex = 128, DefaultTargetOutput = "[Xbox] D-Pad Up", Description = "D-Pad Up" });
            list.Add(new PresetBindingItem { PhysicalName = "D-Pad Right", PhysicalIndex = 129, DefaultTargetOutput = "[Xbox] D-Pad Right", Description = "D-Pad Right" });
            list.Add(new PresetBindingItem { PhysicalName = "D-Pad Down", PhysicalIndex = 130, DefaultTargetOutput = "[Xbox] D-Pad Down", Description = "D-Pad Down" });
            list.Add(new PresetBindingItem { PhysicalName = "D-Pad Left", PhysicalIndex = 131, DefaultTargetOutput = "[Xbox] D-Pad Left", Description = "D-Pad Left" });
            return list;
        }

        // === 4. USB HANDBRAKES ===
        if (isHandbrake)
        {
            list.Add(new PresetBindingItem { PhysicalName = "Axis-Rz", PhysicalIndex = 5, Type = InputType.Axis, DefaultTargetOutput = "[Wheel] Handbrake (Axis Ry)", Description = "Handbrake Axis" });
            return list;
        }

        // === 5. HARDWARE-DISCOVERED DYNAMIC BINDINGS ===
        string prefix = dev.Contains("controller") || dev.Contains("pad") ? "[Xbox]" : "[Wheel]";
        
        for (int i = 0; i < Math.Min(detectedButtons, 32); i++)
        {
            string target = prefix == "[Xbox]" ? (i switch
            {
                0 => "[Xbox] Xbox A (Cross)",
                1 => "[Xbox] Xbox B (Circle)",
                2 => "[Xbox] Xbox X (Square)",
                3 => "[Xbox] Xbox Y (Triangle)",
                4 => "[Xbox] Xbox LB (Left Bumper)",
                5 => "[Xbox] Xbox RB (Right Bumper)",
                _ => $"[Xbox] Xbox A (Cross)"
            }) : $"[Wheel] Button {i + 1}";

            list.Add(new PresetBindingItem
            {
                PhysicalName = $"B{i + 1}",
                PhysicalIndex = i,
                Type = InputType.Button,
                DefaultTargetOutput = target,
                Description = $"Button #{i + 1}"
            });
        }

        return list;
    }
}