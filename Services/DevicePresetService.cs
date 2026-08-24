using System;
using System.Collections.Generic;
using ECCR.Models;

namespace ECCR.Services;

public enum DeviceHardwareCategory
{
    MozaEsxWheel,
    MozaWheel,
    LogitechRig,
    FanatecRig,
    ThrustmasterRig,
    SimagicRig,
    GenericWheelOrPedals,
    PlayStationController,
    NintendoController,
    XboxGamepad,
    GenericGamepad
}

public static class DevicePresetService
{
    public static DeviceHardwareCategory DetectCategory(string deviceName)
    {
        string dev = deviceName.ToLowerInvariant();

        if (dev.Contains("dualsense") || dev.Contains("dualshock") || dev.Contains("sony") || dev.Contains("wireless controller") || dev.Contains("ps5") || dev.Contains("ps4") || dev.Contains("ps3"))
            return DeviceHardwareCategory.PlayStationController;

        if (dev.Contains("esx") || (dev.Contains("moza") && dev.Contains("xbox")))
            return DeviceHardwareCategory.MozaEsxWheel;

        if (dev.Contains("moza") || dev.Contains("gudsen") || dev.Contains("r3") || dev.Contains("r5") || 
            dev.Contains("r9") || dev.Contains("r12") || dev.Contains("r16") || dev.Contains("r21") || 
            dev.Contains("es steering") || dev.Contains("ks steering") || dev.Contains("fsr") || dev.Contains("cs steering") || dev.Contains("gs steering"))
            return DeviceHardwareCategory.MozaWheel;

        if (dev.Contains("g29") || dev.Contains("g920") || dev.Contains("g923") || dev.Contains("g27") || 
            dev.Contains("g25") || dev.Contains("logitech") || dev.Contains("driving force") || dev.Contains("pro racing wheel"))
            return DeviceHardwareCategory.LogitechRig;

        if (dev.Contains("fanatec") || dev.Contains("csl") || dev.Contains("podium") || dev.Contains("clubsport") || dev.Contains("gran turismo dd"))
            return DeviceHardwareCategory.FanatecRig;

        if (dev.Contains("thrustmaster") || dev.Contains("t300") || dev.Contains("t248") || dev.Contains("t150") || 
            dev.Contains("t500") || dev.Contains("tx") || dev.Contains("t-gt") || dev.Contains("ts-pc") || dev.Contains("t818") || dev.Contains("t128"))
            return DeviceHardwareCategory.ThrustmasterRig;

        if (dev.Contains("simagic") || dev.Contains("alpha") || dev.Contains("p1000") || dev.Contains("p2000") || dev.Contains("gt neo"))
            return DeviceHardwareCategory.SimagicRig;

        if (dev.Contains("wheel") || dev.Contains("pedal") || dev.Contains("shifter") || dev.Contains("handbrake") || 
            dev.Contains("simucube") || dev.Contains("cammus") || dev.Contains("asetek") || dev.Contains("heusinkveld") || dev.Contains("vrs"))
            return DeviceHardwareCategory.GenericWheelOrPedals;

        if (dev.Contains("switch") || dev.Contains("pro controller") || dev.Contains("joy-con") || dev.Contains("nintendo"))
            return DeviceHardwareCategory.NintendoController;

        if (dev.Contains("xbox") || dev.Contains("x-box") || dev.Contains("microsoft") || dev.Contains("xinput") || dev.Contains("8bitdo"))
            return DeviceHardwareCategory.XboxGamepad;

        return DeviceHardwareCategory.GenericGamepad;
    }

    public static string GetButtonDisplayName(string deviceName, int index)
    {
        var category = DetectCategory(deviceName);

        switch (category)
        {
            case DeviceHardwareCategory.PlayStationController:
                return index switch
                {
                    0 => "Square (□)",
                    1 => "Cross (✕)",
                    2 => "Circle (○)",
                    3 => "Triangle (△)",
                    4 => "L1 Bumper",
                    5 => "R1 Bumper",
                    6 => "L2 Trigger Button",
                    7 => "R2 Trigger Button",
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

            case DeviceHardwareCategory.MozaEsxWheel:
            case DeviceHardwareCategory.MozaWheel:
                return index switch
                {
                    0 => "Moza A Button (Xbox Ⓐ)",
                    1 => "Moza B Button (Xbox Ⓑ)",
                    2 => "Moza X Button (Xbox Ⓧ)",
                    3 => "Moza Y Button (Xbox Ⓨ)",
                    4 => "Moza View Button (⧉ Back)",
                    5 => "Moza Menu Button (☰ Start)",
                    6 => "Left Paddle (Paddle Down / Shift Down)",
                    7 => "Right Paddle (Paddle Up / Shift Up)",
                    8 => "Left Stick Click (LSB)",
                    9 => "Right Stick Click (RSB)",
                    10 => "Moza Xbox Guide Button (⨂ Home)",
                    11 => "Moza Share Button / S1",
                    12 => "Moza S2 / Rotary Press",
                    128 => "Moza D-Pad Up",
                    129 => "Moza D-Pad Right",
                    130 => "Moza D-Pad Down",
                    131 => "Moza D-Pad Left",
                    _ => $"Moza Button {index + 1}"
                };

            case DeviceHardwareCategory.LogitechRig:
                return index switch
                {
                    0 => "Logitech Cross / A",
                    1 => "Logitech Square / X",
                    2 => "Logitech Circle / B",
                    3 => "Logitech Triangle / Y",
                    4 => "Right Paddle (Paddle Up)",
                    5 => "Left Paddle (Paddle Down)",
                    6 => "R2 / RT Button",
                    7 => "L2 / LT Button",
                    8 => "Share / View",
                    9 => "Options / Menu",
                    10 => "R3 / RSB Button",
                    11 => "L3 / LSB Button",
                    12 => "Shifter 1st Gear",
                    13 => "Shifter 2nd Gear",
                    14 => "Shifter 3rd Gear",
                    15 => "Shifter 4th Gear",
                    16 => "Shifter 5th Gear",
                    17 => "Shifter 6th Gear",
                    18 => "Shifter Reverse Gear",
                    19 => "Dial Clockwise (+)",
                    20 => "Dial Counter-Clockwise (-)",
                    21 => "Enter / Return Button",
                    128 => "D-Pad Up",
                    129 => "D-Pad Right",
                    130 => "D-Pad Down",
                    131 => "D-Pad Left",
                    _ => $"Logitech Button {index + 1}"
                };

            case DeviceHardwareCategory.NintendoController:
                return index switch
                {
                    0 => "B Button",
                    1 => "A Button",
                    2 => "Y Button",
                    3 => "X Button",
                    4 => "L Bumper",
                    5 => "R Bumper",
                    6 => "ZL Trigger",
                    7 => "ZR Trigger",
                    8 => "Minus (-) Button",
                    9 => "Plus (+) Button",
                    10 => "Left Stick Click",
                    11 => "Right Stick Click",
                    12 => "Home Button",
                    13 => "Capture Button",
                    128 => "D-Pad Up",
                    129 => "D-Pad Right",
                    130 => "D-Pad Down",
                    131 => "D-Pad Left",
                    _ => $"Button {index + 1}"
                };

            case DeviceHardwareCategory.FanatecRig:
            case DeviceHardwareCategory.ThrustmasterRig:
            case DeviceHardwareCategory.SimagicRig:
            case DeviceHardwareCategory.GenericWheelOrPedals:
                return index switch
                {
                    0 => "Wheel Button 1 (A / Cross)",
                    1 => "Wheel Button 2 (B / Circle)",
                    2 => "Wheel Button 3 (X / Square)",
                    3 => "Wheel Button 4 (Y / Triangle)",
                    4 => "Wheel View / Back",
                    5 => "Wheel Menu / Start",
                    6 => "Left Paddle (Paddle Down)",
                    7 => "Right Paddle (Paddle Up)",
                    8 => "Wheel Button 7 (LB / L1)",
                    9 => "Wheel Button 8 (RB / R1)",
                    10 => "Left Stick / Rotary Click",
                    11 => "Right Stick / Rotary Click",
                    12 => "Shifter 1st Gear",
                    13 => "Shifter 2nd Gear",
                    14 => "Shifter 3rd Gear",
                    15 => "Shifter 4th Gear",
                    16 => "Shifter 5th Gear",
                    17 => "Shifter 6th Gear",
                    18 => "Shifter 7th / Reverse Gear",
                    128 => "D-Pad Up",
                    129 => "D-Pad Right",
                    130 => "D-Pad Down",
                    131 => "D-Pad Left",
                    _ => $"Wheel Button {index + 1}"
                };

            case DeviceHardwareCategory.XboxGamepad:
            case DeviceHardwareCategory.GenericGamepad:
            default:
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
                    10 => "Xbox Guide Button",
                    128 => "D-Pad Up",
                    129 => "D-Pad Right",
                    130 => "D-Pad Down",
                    131 => "D-Pad Left",
                    _ => $"Button {index + 1}"
                };
        }
    }

    public static string GetAxisDisplayName(string deviceName, int index)
    {
        var category = DetectCategory(deviceName);

        switch (category)
        {
            case DeviceHardwareCategory.PlayStationController:
            case DeviceHardwareCategory.NintendoController:
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

            case DeviceHardwareCategory.MozaEsxWheel:
            case DeviceHardwareCategory.MozaWheel:
            case DeviceHardwareCategory.LogitechRig:
            case DeviceHardwareCategory.FanatecRig:
            case DeviceHardwareCategory.ThrustmasterRig:
            case DeviceHardwareCategory.SimagicRig:
            case DeviceHardwareCategory.GenericWheelOrPedals:
                return index switch
                {
                    0 => "Steering Wheel (Axis X)",
                    1 => "Throttle Pedal (Axis Y)",
                    2 => "Brake Pedal (Axis Z)",
                    3 => "Clutch Pedal (Axis Rx)",
                    4 => "Handbrake (Axis Ry)",
                    5 => "Combined Slider 0",
                    6 => "Dual Clutch Slider 1",
                    _ => $"Wheel Axis {index + 1}"
                };

            case DeviceHardwareCategory.XboxGamepad:
            case DeviceHardwareCategory.GenericGamepad:
            default:
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
    }

    public static List<PresetBindingItem> GeneratePreset(string deviceName, int buttonCount, int axisCount, bool targetIsWheel)
    {
        var list = new List<PresetBindingItem>();
        var category = DetectCategory(deviceName);

        if (category == DeviceHardwareCategory.PlayStationController)
        {
            // DirectInput Axes for DualSense (PS5) & DualShock 4 (PS4)
            list.Add(new PresetBindingItem { PhysicalName = "Left Stick Horizontal", Type = InputType.Axis, PhysicalIndex = 0, DefaultTargetOutput = targetIsWheel ? "[Wheel] Steering (Axis X)" : "[Xbox] Left Stick X (Steer / Horizontal)" });
            list.Add(new PresetBindingItem { PhysicalName = "Left Stick Vertical", Type = InputType.Axis, PhysicalIndex = 1, DefaultTargetOutput = targetIsWheel ? "[Wheel] Gas / Throttle (Axis Y)" : "[Xbox] Left Stick Y (Steer / Vertical)" });
            list.Add(new PresetBindingItem { PhysicalName = "Right Stick Horizontal", Type = InputType.Axis, PhysicalIndex = 2, DefaultTargetOutput = targetIsWheel ? "[Wheel] Handbrake (Axis Ry)" : "[Xbox] Right Stick X (Camera / Look Horizontal)" });
            list.Add(new PresetBindingItem { PhysicalName = "L2 Trigger Axis", Type = InputType.Axis, PhysicalIndex = 3, DefaultTargetOutput = targetIsWheel ? "[Wheel] Brake (Axis Z)" : "[Xbox] Left Trigger (LT / Brake Axis)" });
            list.Add(new PresetBindingItem { PhysicalName = "R2 Trigger Axis", Type = InputType.Axis, PhysicalIndex = 4, DefaultTargetOutput = targetIsWheel ? "[Wheel] Gas / Throttle (Axis Y)" : "[Xbox] Right Trigger (RT / Gas Axis)" });
            list.Add(new PresetBindingItem { PhysicalName = "Right Stick Vertical", Type = InputType.Axis, PhysicalIndex = 5, DefaultTargetOutput = targetIsWheel ? "[Wheel] Combined Slider 0" : "[Xbox] Right Stick Y (Camera / Look Vertical)" });

            // DirectInput Face Buttons & Controls
            list.Add(new PresetBindingItem { PhysicalName = "Cross (✕)", Type = InputType.Button, PhysicalIndex = 1, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 1" : "[Xbox] Xbox A (Cross / South / Handbrake)" });
            list.Add(new PresetBindingItem { PhysicalName = "Circle (○)", Type = InputType.Button, PhysicalIndex = 2, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 2" : "[Xbox] Xbox B (Circle / East)" });
            list.Add(new PresetBindingItem { PhysicalName = "Square (□)", Type = InputType.Button, PhysicalIndex = 0, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 3" : "[Xbox] Xbox X (Square / West / Shift Down)" });
            list.Add(new PresetBindingItem { PhysicalName = "Triangle (△)", Type = InputType.Button, PhysicalIndex = 3, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 4" : "[Xbox] Xbox Y (Triangle / North / Shift Up)" });
            list.Add(new PresetBindingItem { PhysicalName = "L1 Bumper", Type = InputType.Button, PhysicalIndex = 4, DefaultTargetOutput = targetIsWheel ? "[Wheel] Paddle Down (Left Shift)" : "[Xbox] Xbox LB (Left Bumper / L1 / Clutch)" });
            list.Add(new PresetBindingItem { PhysicalName = "R1 Bumper", Type = InputType.Button, PhysicalIndex = 5, DefaultTargetOutput = targetIsWheel ? "[Wheel] Paddle Up (Right Shift)" : "[Xbox] Xbox RB (Right Bumper / R1)" });
            list.Add(new PresetBindingItem { PhysicalName = "Create / Share", Type = InputType.Button, PhysicalIndex = 8, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 11" : "[Xbox] Xbox View (Back / Map / Share)" });
            list.Add(new PresetBindingItem { PhysicalName = "Options / Menu", Type = InputType.Button, PhysicalIndex = 9, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 12" : "[Xbox] Xbox Menu (Start / Options)" });
            list.Add(new PresetBindingItem { PhysicalName = "L3 Stick Click", Type = InputType.Button, PhysicalIndex = 10, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 9" : "[Xbox] Xbox LSB (Left Stick Click / L3)" });
            list.Add(new PresetBindingItem { PhysicalName = "R3 Stick Click", Type = InputType.Button, PhysicalIndex = 11, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 10" : "[Xbox] Xbox RSB (Right Stick Click / R3)" });
            list.Add(new PresetBindingItem { PhysicalName = "PS Guide Button", Type = InputType.Button, PhysicalIndex = 12, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 13" : "[Xbox] Xbox Guide (Home / Guide)" });

            list.Add(new PresetBindingItem { PhysicalName = "D-Pad Up", Type = InputType.Button, PhysicalIndex = 128, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 14" : "[Xbox] D-Pad Up" });
            list.Add(new PresetBindingItem { PhysicalName = "D-Pad Down", Type = InputType.Button, PhysicalIndex = 130, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 15" : "[Xbox] D-Pad Down" });
            list.Add(new PresetBindingItem { PhysicalName = "D-Pad Left", Type = InputType.Button, PhysicalIndex = 131, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 16" : "[Xbox] D-Pad Left" });
            list.Add(new PresetBindingItem { PhysicalName = "D-Pad Right", Type = InputType.Button, PhysicalIndex = 129, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 17" : "[Xbox] D-Pad Right" });
            return list;
        }

        bool isMoza = category == DeviceHardwareCategory.MozaEsxWheel || category == DeviceHardwareCategory.MozaWheel;

        if (isMoza)
        {
            list.Add(new PresetBindingItem { PhysicalName = "Steering Wheel (Axis X)", Type = InputType.Axis, PhysicalIndex = 0, DefaultTargetOutput = targetIsWheel ? "[Wheel] Steering (Axis X)" : "[Xbox] Left Stick X (Steer / Horizontal)" });
            list.Add(new PresetBindingItem { PhysicalName = "Moza A Button (Xbox Ⓐ)", Type = InputType.Button, PhysicalIndex = 0, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 1" : "[Xbox] Xbox A (Cross / South / Handbrake)" });
            list.Add(new PresetBindingItem { PhysicalName = "Moza B Button (Xbox Ⓑ)", Type = InputType.Button, PhysicalIndex = 1, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 2" : "[Xbox] Xbox B (Circle / East)" });
            list.Add(new PresetBindingItem { PhysicalName = "Moza X Button (Xbox Ⓧ)", Type = InputType.Button, PhysicalIndex = 2, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 3" : "[Xbox] Xbox X (Square / West / Shift Down)" });
            list.Add(new PresetBindingItem { PhysicalName = "Moza Y Button (Xbox Ⓨ)", Type = InputType.Button, PhysicalIndex = 3, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 4" : "[Xbox] Xbox Y (Triangle / North / Shift Up)" });
            list.Add(new PresetBindingItem { PhysicalName = "Moza View Button (⧉ Back)", Type = InputType.Button, PhysicalIndex = 4, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 11" : "[Xbox] Xbox View (Back / Map / Share)" });
            list.Add(new PresetBindingItem { PhysicalName = "Moza Menu Button (☰ Start)", Type = InputType.Button, PhysicalIndex = 5, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 12" : "[Xbox] Xbox Menu (Start / Options)" });
            list.Add(new PresetBindingItem { PhysicalName = "Left Paddle (Paddle Down / Shift Down)", Type = InputType.Button, PhysicalIndex = 6, DefaultTargetOutput = targetIsWheel ? "[Wheel] Paddle Down (Left Shift)" : "[Xbox] Xbox LB (Left Bumper / L1 / Clutch)" });
            list.Add(new PresetBindingItem { PhysicalName = "Right Paddle (Paddle Up / Shift Up)", Type = InputType.Button, PhysicalIndex = 7, DefaultTargetOutput = targetIsWheel ? "[Wheel] Paddle Up (Right Shift)" : "[Xbox] Xbox RB (Right Bumper / R1)" });
            list.Add(new PresetBindingItem { PhysicalName = "Left Stick Click (LSB)", Type = InputType.Button, PhysicalIndex = 8, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 9" : "[Xbox] Xbox LSB (Left Stick Click / L3)" });
            list.Add(new PresetBindingItem { PhysicalName = "Right Stick Click (RSB)", Type = InputType.Button, PhysicalIndex = 9, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 10" : "[Xbox] Xbox RSB (Right Stick Click / R3)" });
            list.Add(new PresetBindingItem { PhysicalName = "Moza Xbox Guide Button (⨂ Home)", Type = InputType.Button, PhysicalIndex = 10, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 13" : "[Xbox] Xbox Guide (Home / Guide)" });

            list.Add(new PresetBindingItem { PhysicalName = "Moza D-Pad Up", Type = InputType.Button, PhysicalIndex = 128, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 14" : "[Xbox] D-Pad Up" });
            list.Add(new PresetBindingItem { PhysicalName = "Moza D-Pad Down", Type = InputType.Button, PhysicalIndex = 130, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 15" : "[Xbox] D-Pad Down" });
            list.Add(new PresetBindingItem { PhysicalName = "Moza D-Pad Left", Type = InputType.Button, PhysicalIndex = 131, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 16" : "[Xbox] D-Pad Left" });
            list.Add(new PresetBindingItem { PhysicalName = "Moza D-Pad Right", Type = InputType.Button, PhysicalIndex = 129, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 17" : "[Xbox] D-Pad Right" });
            return list;
        }

        bool isSimHardware = category == DeviceHardwareCategory.LogitechRig || 
                             category == DeviceHardwareCategory.FanatecRig || 
                             category == DeviceHardwareCategory.ThrustmasterRig || 
                             category == DeviceHardwareCategory.SimagicRig || 
                             category == DeviceHardwareCategory.GenericWheelOrPedals;

        if (isSimHardware)
        {
            list.Add(new PresetBindingItem { PhysicalName = "Steering Wheel (Axis X)", Type = InputType.Axis, PhysicalIndex = 0, DefaultTargetOutput = targetIsWheel ? "[Wheel] Steering (Axis X)" : "[Xbox] Left Stick X (Steer / Horizontal)" });
            list.Add(new PresetBindingItem { PhysicalName = "Throttle Pedal (Axis Y)", Type = InputType.Axis, PhysicalIndex = 1, DefaultTargetOutput = targetIsWheel ? "[Wheel] Gas / Throttle (Axis Y)" : "[Xbox] Right Trigger (RT / Gas Axis)" });
            list.Add(new PresetBindingItem { PhysicalName = "Brake Pedal (Axis Z)", Type = InputType.Axis, PhysicalIndex = 2, DefaultTargetOutput = targetIsWheel ? "[Wheel] Brake (Axis Z)" : "[Xbox] Left Trigger (LT / Brake Axis)" });
            list.Add(new PresetBindingItem { PhysicalName = "Clutch Pedal (Axis Rx)", Type = InputType.Axis, PhysicalIndex = 3, DefaultTargetOutput = targetIsWheel ? "[Wheel] Clutch (Axis Rx)" : "[Xbox] Xbox LB (Left Bumper / L1 / Clutch)" });
            list.Add(new PresetBindingItem { PhysicalName = "Handbrake (Axis Ry)", Type = InputType.Axis, PhysicalIndex = 4, DefaultTargetOutput = targetIsWheel ? "[Wheel] Handbrake (Axis Ry)" : "[Xbox] Xbox A (Cross / South / Handbrake)" });

            list.Add(new PresetBindingItem { PhysicalName = "Right Paddle (Paddle Up)", Type = InputType.Button, PhysicalIndex = 5, DefaultTargetOutput = targetIsWheel ? "[Wheel] Paddle Up (Right Shift)" : "[Xbox] Xbox RB (Right Bumper / R1)" });
            list.Add(new PresetBindingItem { PhysicalName = "Left Paddle (Paddle Down)", Type = InputType.Button, PhysicalIndex = 4, DefaultTargetOutput = targetIsWheel ? "[Wheel] Paddle Down (Left Shift)" : "[Xbox] Xbox LB (Left Bumper / L1 / Clutch)" });

            list.Add(new PresetBindingItem { PhysicalName = GetButtonDisplayName(deviceName, 0), Type = InputType.Button, PhysicalIndex = 0, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 1" : "[Xbox] Xbox A (Cross / South / Handbrake)" });
            list.Add(new PresetBindingItem { PhysicalName = GetButtonDisplayName(deviceName, 1), Type = InputType.Button, PhysicalIndex = 1, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 2" : "[Xbox] Xbox B (Circle / East)" });
            list.Add(new PresetBindingItem { PhysicalName = GetButtonDisplayName(deviceName, 2), Type = InputType.Button, PhysicalIndex = 2, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 3" : "[Xbox] Xbox X (Square / West / Shift Down)" });
            list.Add(new PresetBindingItem { PhysicalName = GetButtonDisplayName(deviceName, 3), Type = InputType.Button, PhysicalIndex = 3, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 4" : "[Xbox] Xbox Y (Triangle / North / Shift Up)" });

            for (int i = 12; i <= 18; i++)
            {
                string gearTarget = i switch
                {
                    12 => "[Wheel] 1st Gear",
                    13 => "[Wheel] 2nd Gear",
                    14 => "[Wheel] 3rd Gear",
                    15 => "[Wheel] 4th Gear",
                    16 => "[Wheel] 5th Gear",
                    17 => "[Wheel] 6th Gear",
                    18 => "[Wheel] Reverse Gear",
                    _ => $"[Wheel] Button {i + 1}"
                };
                list.Add(new PresetBindingItem { PhysicalName = GetButtonDisplayName(deviceName, i), Type = InputType.Button, PhysicalIndex = i, DefaultTargetOutput = targetIsWheel ? gearTarget : $"[Xbox] Xbox A (Cross / South / Handbrake)" });
            }
        }
        else
        {
            list.Add(new PresetBindingItem { PhysicalName = "Left Stick X", Type = InputType.Axis, PhysicalIndex = 0, DefaultTargetOutput = targetIsWheel ? "[Wheel] Steering (Axis X)" : "[Xbox] Left Stick X (Steer / Horizontal)" });
            list.Add(new PresetBindingItem { PhysicalName = "Left Stick Y", Type = InputType.Axis, PhysicalIndex = 1, DefaultTargetOutput = targetIsWheel ? "[Wheel] Gas / Throttle (Axis Y)" : "[Xbox] Left Stick Y (Steer / Vertical)" });
            list.Add(new PresetBindingItem { PhysicalName = "Left Trigger (LT)", Type = InputType.Axis, PhysicalIndex = 2, DefaultTargetOutput = targetIsWheel ? "[Wheel] Brake (Axis Z)" : "[Xbox] Left Trigger (LT / Brake Axis)" });
            list.Add(new PresetBindingItem { PhysicalName = "Right Stick X", Type = InputType.Axis, PhysicalIndex = 3, DefaultTargetOutput = targetIsWheel ? "[Wheel] Handbrake (Axis Ry)" : "[Xbox] Right Stick X (Camera / Look Horizontal)" });
            list.Add(new PresetBindingItem { PhysicalName = "Right Stick Y", Type = InputType.Axis, PhysicalIndex = 4, DefaultTargetOutput = targetIsWheel ? "[Wheel] Combined Slider 0" : "[Xbox] Right Stick Y (Camera / Look Vertical)" });
            list.Add(new PresetBindingItem { PhysicalName = "Right Trigger (RT)", Type = InputType.Axis, PhysicalIndex = 5, DefaultTargetOutput = targetIsWheel ? "[Wheel] Gas / Throttle (Axis Y)" : "[Xbox] Right Trigger (RT / Gas Axis)" });

            list.Add(new PresetBindingItem { PhysicalName = "A Button", Type = InputType.Button, PhysicalIndex = 0, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 1" : "[Xbox] Xbox A (Cross / South / Handbrake)" });
            list.Add(new PresetBindingItem { PhysicalName = "B Button", Type = InputType.Button, PhysicalIndex = 1, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 2" : "[Xbox] Xbox B (Circle / East)" });
            list.Add(new PresetBindingItem { PhysicalName = "X Button", Type = InputType.Button, PhysicalIndex = 2, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 3" : "[Xbox] Xbox X (Square / West / Shift Down)" });
            list.Add(new PresetBindingItem { PhysicalName = "Y Button", Type = InputType.Button, PhysicalIndex = 3, DefaultTargetOutput = targetIsWheel ? "[Wheel] Button 4" : "[Xbox] Xbox Y (Triangle / North / Shift Up)" });
            list.Add(new PresetBindingItem { PhysicalName = "LB Bumper", Type = InputType.Button, PhysicalIndex = 4, DefaultTargetOutput = targetIsWheel ? "[Wheel] Paddle Down (Left Shift)" : "[Xbox] Xbox LB (Left Bumper / L1 / Clutch)" });
            list.Add(new PresetBindingItem { PhysicalName = "RB Bumper", Type = InputType.Button, PhysicalIndex = 5, DefaultTargetOutput = targetIsWheel ? "[Wheel] Paddle Up (Right Shift)" : "[Xbox] Xbox RB (Right Bumper / R1)" });
        }

        return list;
    }
}