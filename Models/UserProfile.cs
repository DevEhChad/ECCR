using System.Collections.Generic;

namespace ECCR.Models;

public class UserProfile
{
    public string ProfileName { get; set; } = "Default";
    public VirtualEmulationMode OutputMode { get; set; } = VirtualEmulationMode.DirectInputWheel;
    public List<MappingEntry> Mappings { get; set; } = [];
}