using System.Collections.Generic;

namespace ECCR.Models;

public enum VirtualEmulationMode
{
    DirectInputWheel, // vJoy (Multi-Axis DirectInput Sim Wheel)
    XboxController     // ViGEm (XInput Gamepad)
}

public class UserProfile
{
    public string ProfileName { get; set; } = "Default";
    public VirtualEmulationMode OutputMode { get; set; } = VirtualEmulationMode.DirectInputWheel;
    public List<MappingEntry> Mappings { get; set; } = new();
}