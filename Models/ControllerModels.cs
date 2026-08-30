namespace ECCR.Models;

/// <summary>Whether a physical input (or a mapping's <c>TargetOutput</c>) is an analog axis or a digital button.</summary>
public enum InputType
{
    Axis,
    Button
}

/// <summary>
/// The two virtual output backends the app can feed: a vJoy DirectInput wheel or a ViGEm
/// Xbox 360 pad. Declared for the (currently unused) <see cref="UserProfile.OutputMode"/>
/// field - the actual routing decision is made per-mapping by
/// <see cref="ECCR.Services.CompositeFeederService"/> based on each entry's <c>TargetOutput</c>
/// string prefix ("[Wheel]" vs "[Xbox]"), not by a single mode switch.
/// </summary>
public enum VirtualEmulationMode
{
    DirectInputWheel,
    XboxController
}