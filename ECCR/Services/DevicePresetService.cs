using System;
using System.Collections.Generic;
using ECCR.Models;

namespace ECCR.Services;

public static class DevicePresetService
{
    public static string GetButtonDisplayName(string deviceName, int index)
    {
        string dev = deviceName.ToLowerInvariant();

        // 1. PlayStation DualSense / PS5 / PS4
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
                6 => "L2 Trigger Btn",
                7 => "R2 Trigger Btn",
                8 => "Create / Share",
                9 => "Options / Menu",
                10 => "L3 Stick Click",
                11 => "R3 Stick Click",
                12 => "PS Guide Button",
                13 => "Touchpad Click",
                14 => "Mute Button",
                128 => "D-Pad Up",
                129 => "D-Pad Right",
                130 => "D-Pad Down",
                131 => "D-Pad Left",
                _ => $"PS Button {index + 1}"
            };
        }

        // 2. Sim-Rig Wheels & Pedals (Logitech, Moza, Fanatec, Thrustmaster, Simagic)
        bool isSimHardware = dev.Contains("logitech") || dev.Contains("moza") || dev.Contains("fanatec") || 
                             dev.Contains("thrustmaster") || dev.Contains("simagic") || dev.Contains("cammus") || 
                             dev.Contains("asetek") || dev.Contains("pedal") || dev.Contains("wheel") || dev.Contains("shifter");

        if (isSimHardware)
        {
            return index switch
            {
                4 => "Paddle Down (Left)",
                5 => "Paddle Up (Right)",
                11 => "H-Shifter Reverse",
                12 => "H-Shifter 1st Gear",
                13 => "H-Shifter 2nd Gear",
                14 => "H-Shifter 3rd Gear",
                15 => "H-Shifter 4th Gear",
                16 => "H-Shifter 5th Gear",
                17 => "H-Shifter 6th Gear",
                18 => "H-Shifter 7th Gear",
                128 => "D-Pad Up",
                129 => "D-Pad Right",
                130 => "D-Pad Down",
                131 => "D-Pad Left",
                _ => $"Wheel Button {index + 1}"
            };
        }

        // 3. Generic / Xbox Gamepads
        return index switch
        {
            0 => "Button A",
            1 => "Button B",
            2 => "Button X",
            3 => "Button Y",
            4 => "Left Bumper (LB)",
            5 => "Right Bumper (RB)",
            6 => "View / Back",
            7 => "Menu / Start",
            8 => "Left Stick Click (LS)",
            9 => "Right Stick Click (RS)",
            10 => "Guide / Home",
            128 => "D-Pad Up",
            129 => "D-Pad Right",
            130 => "D-Pad Down",
            131 => "D-Pad Left",
            _ => $"Button {index + 1}"
        };
    }

    public static string GetAxisDisplayName(string deviceName, int index)
    {
        string dev = deviceName.ToLowerInvariant();

        if (dev.Contains("dualsense") || dev.Contains("dualshock") || dev.Contains("sony") || dev.Contains("ps5") || dev.Contains("ps4"))
        {
            return index switch
            {
                0 => "Left Stick Horizontal",
                1 => "Left Stick Vertical",
                2 => "Right Stick Horizontal",
                3 => "L2 Trigger (Analog)",
                4 => "R2 Trigger (Analog)",
                5 => "Right Stick Vertical",
                _ => $"Analog Axis #{index + 1}"
            };
        }

        // Standalone Pedals
        if (dev.Contains("pedal") || dev.Contains("tlcm") || dev.Contains("crp") || dev.Contains("srp") || dev.Contains("v3"))
        {
            return index switch
            {
                0 => "Throttle Pedal",
                1 => "Brake Pedal",
                2 => "Clutch Pedal",
                _ => $"Pedal Axis #{index + 1}"
            };
        }

        // Sim Wheels
        bool isSimHardware = dev.Contains("logitech") || dev.Contains("moza") || dev.Contains("fanatec") || 
                             dev.Contains("thrustmaster") || dev.Contains("simagic") || dev.Contains("wheel");

        if (isSimHardware)
        {
            return index switch
            {
                0 => "Steering Wheel Axis",
                1 => "Throttle Pedal",
                2 => "Brake Pedal",
                3 => "Clutch Pedal",
                4 => "Handbrake Axis",
                _ => $"Axis #{index + 1}"
            };
        }

        return index switch
        {
            0 => "Left Stick X",
            1 => "Left Stick Y",
            2 => "Left Trigger (LT)",
            3 => "Right Stick X",
            4 => "Right Stick Y",
            5 => "Right Trigger (RT)",
            _ => $"Axis #{index + 1}"
        };
    }

    public static List<PresetBindingItem> GeneratePreset(string deviceName, int buttonCount, int axisCount, bool targetWheelDevice = true)
    {
        string dev = deviceName.ToLowerInvariant();
        bool isPlayStation = dev.Contains("dualsense") || dev.Contains("dualshock") || dev.Contains("sony") || dev.Contains("wireless controller") || dev.Contains("ps5") || dev.Contains("ps4");

        // 1. PlayStation Gamepads -> Default to Native Xbox 360 Device
        if (isPlayStation)
        {
            return new List<PresetBindingItem>
            {
                new() { PhysicalName = "Cross (✕)", Type = InputType.Button, PhysicalIndex = 1, DefaultTargetOutput = "[Xbox] Xbox A (Cross / South)", Description = "Xbox A (South / Confirm)" },
                new() { PhysicalName = "Circle (○)", Type = InputType.Button, PhysicalIndex = 2, DefaultTargetOutput = "[Xbox] Xbox B (Circle / East)", Description = "Xbox B (East / Cancel)" },
                new() { PhysicalName = "Square (□)", Type = InputType.Button, PhysicalIndex = 0, DefaultTargetOutput = "[Xbox] Xbox X (Square / West)", Description = "Xbox X (West / Reload)" },
                new() { PhysicalName = "Triangle (△)", Type = InputType.Button, PhysicalIndex = 3, DefaultTargetOutput = "[Xbox] Xbox Y (Triangle / North)", Description = "Xbox Y (North / Switch)" },
                new() { PhysicalName = "L1 Bumper", Type = InputType.Button, PhysicalIndex = 4, DefaultTargetOutput = "[Xbox] Xbox LB (Left Bumper / L1)", Description = "Xbox Left Bumper (LB)" },
                new() { PhysicalName = "R1 Bumper", Type = InputType.Button, PhysicalIndex = 5, DefaultTargetOutput = "[Xbox] Xbox RB (Right Bumper / R1)", Description = "Xbox Right Bumper (RB)" },
                new() { PhysicalName = "L3 Stick Click", Type = InputType.Button, PhysicalIndex = 10, DefaultTargetOutput = "[Xbox] Xbox LSB (Left Stick Click / L3)", Description = "Xbox Left Stick Click (LS)" },
                new() { PhysicalName = "R3 Stick Click", Type = InputType.Button, PhysicalIndex = 11, DefaultTargetOutput = "[Xbox] Xbox RSB (Right Stick Click / R3)", Description = "Xbox Right Stick Click (RS)" },
                new() { PhysicalName = "Create / Share", Type = InputType.Button, PhysicalIndex = 8, DefaultTargetOutput = "[Xbox] Xbox View (Back / Share)", Description = "Xbox View / Back" },
                new() { PhysicalName = "Options / Menu", Type = InputType.Button, PhysicalIndex = 9, DefaultTargetOutput = "[Xbox] Xbox Menu (Start / Options)", Description = "Xbox Menu / Start" },
                new() { PhysicalName = "PS Guide Button", Type = InputType.Button, PhysicalIndex = 12, DefaultTargetOutput = "[Xbox] Xbox Guide (Home / PS)", Description = "Xbox Home / Guide" },
                new() { PhysicalName = "D-Pad Up", Type = InputType.Button, PhysicalIndex = 128, DefaultTargetOutput = "[Xbox] D-Pad Up", Description = "Xbox D-Pad Up" },
                new() { PhysicalName = "D-Pad Down", Type = InputType.Button, PhysicalIndex = 130, DefaultTargetOutput = "[Xbox] D-Pad Down", Description = "Xbox D-Pad Down" },
                new() { PhysicalName = "D-Pad Left", Type = InputType.Button, PhysicalIndex = 131, DefaultTargetOutput = "[Xbox] D-Pad Left", Description = "Xbox D-Pad Left" },
                new() { PhysicalName = "D-Pad Right", Type = InputType.Button, PhysicalIndex = 129, DefaultTargetOutput = "[Xbox] D-Pad Right", Description = "Xbox D-Pad Right" },
                new() { PhysicalName = "Left Stick Horizontal", Type = InputType.Axis, PhysicalIndex = 0, DefaultTargetOutput = "[Xbox] Left Stick X (Steer / Horizontal)", Description = "Left Thumbstick X" },
                new() { PhysicalName = "Left Stick Vertical", Type = InputType.Axis, PhysicalIndex = 1, DefaultTargetOutput = "[Xbox] Left Stick Y (Vertical)", Description = "Left Thumbstick Y" },
                new() { PhysicalName = "Right Stick Horizontal", Type = InputType.Axis, PhysicalIndex = 2, DefaultTargetOutput = "[Xbox] Right Stick X (Camera Horizontal)", Description = "Right Thumbstick X" },
                new() { PhysicalName = "Right Stick Vertical", Type = InputType.Axis, PhysicalIndex = 5, DefaultTargetOutput = "[Xbox] Right Stick Y (Camera Vertical)", Description = "Right Thumbstick Y" },
                new() { PhysicalName = "L2 Trigger (Analog)", Type = InputType.Axis, PhysicalIndex = 3, DefaultTargetOutput = "[Xbox] Left Trigger (LT / L2 Axis)", Description = "Left Trigger (LT / Brake)" },
                new() { PhysicalName = "R2 Trigger (Analog)", Type = InputType.Axis, PhysicalIndex = 4, DefaultTargetOutput = "[Xbox] Right Trigger (RT / R2 Axis)", Description = "Right Trigger (RT / Gas)" }
            };
        }

        // 2. Sim-Rig Racing Wheels, Pedals & Shifters (Moza, Logitech, Fanatec, Thrustmaster, etc.)
        // When targetWheelDevice is true, all devices combine into vJoy Target #1 (Virtual Wheel)
        bool isSimHardware = dev.Contains("logitech") || dev.Contains("moza") || dev.Contains("fanatec") || 
                             dev.Contains("thrustmaster") || dev.Contains("simagic") || dev.Contains("cammus") || 
                             dev.Contains("asetek") || dev.Contains("wheel") || dev.Contains("pedal") || dev.Contains("shifter");

        if (isSimHardware)
        {
            var list = new List<PresetBindingItem>();

            // Steering Wheel Base
            if (dev.Contains("wheel") || dev.Contains("base") || dev.Contains("moza") || dev.Contains("fanatec") || dev.Contains("thrustmaster") || dev.Contains("logitech"))
            {
                list.Add(new PresetBindingItem { 
                    PhysicalName = "Steering Wheel Axis", 
                    Type = InputType.Axis, 
                    PhysicalIndex = 0, 
                    DefaultTargetOutput = targetWheelDevice ? "[Wheel] Steering (Axis X)" : "[Xbox] Left Stick X (Steer / Horizontal)", 
                    Description = "Primary Steering Control" 
                });
                list.Add(new PresetBindingItem { 
                    PhysicalName = "Paddle Down (Left)", 
                    Type = InputType.Button, 
                    PhysicalIndex = 4, 
                    DefaultTargetOutput = targetWheelDevice ? "[Wheel] Paddle Down" : "[Xbox] Xbox LB (Left Bumper / L1)", 
                    Description = "Downshift" 
                });
                list.Add(new PresetBindingItem { 
                    PhysicalName = "Paddle Up (Right)", 
                    Type = InputType.Button, 
                    PhysicalIndex = 5, 
                    DefaultTargetOutput = targetWheelDevice ? "[Wheel] Paddle Up" : "[Xbox] Xbox RB (Right Bumper / R1)", 
                    Description = "Upshift" 
                });
            }

            // Pedals (Discrete or Combined)
            if (dev.Contains("pedal") || dev.Contains("logitech") || dev.Contains("thrustmaster") || dev.Contains("fanatec") || dev.Contains("moza"))
            {
                list.Add(new PresetBindingItem { 
                    PhysicalName = "Throttle Pedal", 
                    Type = InputType.Axis, 
                    PhysicalIndex = 1, 
                    DefaultTargetOutput = targetWheelDevice ? "[Wheel] Gas / Throttle (Axis Y)" : "[Xbox] Right Trigger (RT / R2 Axis)", 
                    Description = "Accelerator" 
                });
                list.Add(new PresetBindingItem { 
                    PhysicalName = "Brake Pedal", 
                    Type = InputType.Axis, 
                    PhysicalIndex = 2, 
                    DefaultTargetOutput = targetWheelDevice ? "[Wheel] Brake (Axis Z)" : "[Xbox] Left Trigger (LT / L2 Axis)", 
                    Description = "Braking Axis" 
                });
                list.Add(new PresetBindingItem { 
                    PhysicalName = "Clutch Pedal", 
                    Type = InputType.Axis, 
                    PhysicalIndex = dev.Contains("logitech") ? 6 : 3, 
                    DefaultTargetOutput = targetWheelDevice ? "[Wheel] Clutch (Axis Rx)" : "[Xbox] Xbox LB (Left Bumper / L1)", 
                    Description = "Clutch Axis" 
                });
            }

            // Shifter & Face Buttons
            if (dev.Contains("shifter") || dev.Contains("logitech"))
            {
                list.Add(new PresetBindingItem { PhysicalName = "H-Shifter 1st Gear", Type = InputType.Button, PhysicalIndex = 12, DefaultTargetOutput = "[Wheel] 1st Gear", Description = "1st Gear" });
                list.Add(new PresetBindingItem { PhysicalName = "H-Shifter 2nd Gear", Type = InputType.Button, PhysicalIndex = 13, DefaultTargetOutput = "[Wheel] 2nd Gear", Description = "2nd Gear" });
                list.Add(new PresetBindingItem { PhysicalName = "H-Shifter 3rd Gear", Type = InputType.Button, PhysicalIndex = 14, DefaultTargetOutput = "[Wheel] 3rd Gear", Description = "3rd Gear" });
                list.Add(new PresetBindingItem { PhysicalName = "H-Shifter 4th Gear", Type = InputType.Button, PhysicalIndex = 15, DefaultTargetOutput = "[Wheel] 4th Gear", Description = "4th Gear" });
                list.Add(new PresetBindingItem { PhysicalName = "H-Shifter 5th Gear", Type = InputType.Button, PhysicalIndex = 16, DefaultTargetOutput = "[Wheel] 5th Gear", Description = "5th Gear" });
                list.Add(new PresetBindingItem { PhysicalName = "H-Shifter 6th Gear", Type = InputType.Button, PhysicalIndex = 17, DefaultTargetOutput = "[Wheel] 6th Gear", Description = "6th Gear" });
                list.Add(new PresetBindingItem { PhysicalName = "H-Shifter Reverse", Type = InputType.Button, PhysicalIndex = 11, DefaultTargetOutput = "[Wheel] Reverse Gear", Description = "Reverse Gear" });
            }

            return list;
        }

        // 3. Generic Controller Fallback
        return new List<PresetBindingItem>
        {
            new() { PhysicalName = "Button A", Type = InputType.Button, PhysicalIndex = 0, DefaultTargetOutput = "[Xbox] Xbox A (Cross / South)", Description = "Xbox A" },
            new() { PhysicalName = "Button B", Type = InputType.Button, PhysicalIndex = 1, DefaultTargetOutput = "[Xbox] Xbox B (Circle / East)", Description = "Xbox B" },
            new() { PhysicalName = "Button X", Type = InputType.Button, PhysicalIndex = 2, DefaultTargetOutput = "[Xbox] Xbox X (Square / West)", Description = "Xbox X" },
            new() { PhysicalName = "Button Y", Type = InputType.Button, PhysicalIndex = 3, DefaultTargetOutput = "[Xbox] Xbox Y (Triangle / North)", Description = "Xbox Y" },
            new() { PhysicalName = "Left Bumper (LB)", Type = InputType.Button, PhysicalIndex = 4, DefaultTargetOutput = "[Xbox] Xbox LB (Left Bumper / L1)", Description = "Left Bumper" },
            new() { PhysicalName = "Right Bumper (RB)", Type = InputType.Button, PhysicalIndex = 5, DefaultTargetOutput = "[Xbox] Xbox RB (Right Bumper / R1)", Description = "Right Bumper" },
            new() { PhysicalName = "Left Stick X", Type = InputType.Axis, PhysicalIndex = 0, DefaultTargetOutput = "[Xbox] Left Stick X (Steer / Horizontal)", Description = "Left Thumbstick X" },
            new() { PhysicalName = "Left Stick Y", Type = InputType.Axis, PhysicalIndex = 1, DefaultTargetOutput = "[Xbox] Left Stick Y (Vertical)", Description = "Left Thumbstick Y" },
            new() { PhysicalName = "Left Trigger (LT)", Type = InputType.Axis, PhysicalIndex = 2, DefaultTargetOutput = "[Xbox] Left Trigger (LT / L2 Axis)", Description = "Left Trigger (LT)" },
            new() { PhysicalName = "Right Stick X", Type = InputType.Axis, PhysicalIndex = 3, DefaultTargetOutput = "[Xbox] Right Stick X (Camera Horizontal)", Description = "Right Thumbstick X" },
            new() { PhysicalName = "Right Stick Y", Type = InputType.Axis, PhysicalIndex = 4, DefaultTargetOutput = "[Xbox] Right Stick Y (Camera Vertical)", Description = "Right Thumbstick Y" },
            new() { PhysicalName = "Right Trigger (RT)", Type = InputType.Axis, PhysicalIndex = 5, DefaultTargetOutput = "[Xbox] Right Trigger (RT / R2 Axis)", Description = "Right Trigger (RT)" }
        };
    }
}