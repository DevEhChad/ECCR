using System;

namespace ECCR.Models;

public class RawDeviceInputState
{
    public Guid InstanceGuid { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public int[] Axes { get; set; } = new int[8]; // X, Y, Z, Rx, Ry, Rz, Slider0, Slider1
    public bool[] Buttons { get; set; } = new bool[128];
    public int[] PointOfView { get; set; } = new int[4]; // D-Pad / Hat switches
}