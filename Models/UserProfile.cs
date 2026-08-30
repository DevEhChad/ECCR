using System.Collections.Generic;

namespace ECCR.Models;

/// <summary>
/// One saved binding set, serialized to <c>%AppData%/ECCR/Profiles/{ProfileName}.json</c>.
/// The user can keep several profiles (e.g. per game) and switch the active one from the
/// main window; only <see cref="Mappings"/> is actually read back on load - <see cref="OutputMode"/>
/// is written for forward-compatibility but not currently consulted (each <see cref="MappingEntry"/>
/// already carries its own target type via its <c>TargetOutput</c> string prefix).
/// </summary>
public class UserProfile
{
    public string ProfileName { get; set; } = "Default";
    public VirtualEmulationMode OutputMode { get; set; } = VirtualEmulationMode.DirectInputWheel;
    public List<MappingEntry> Mappings { get; set; } = [];
}