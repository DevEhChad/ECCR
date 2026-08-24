using System;

namespace ECCR.Services;

public class RawDeviceInputState
{
    public Guid InstanceGuid { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public int[] Axes { get; set; } = [];
    public bool[] Buttons { get; set; } = [];
}