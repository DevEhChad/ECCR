using System;

namespace ECCR.Services;

/// <summary>
/// Contract for a virtual-output backend: takes a calibrated 0.0-1.0 axis value or a button
/// state, tagged with the destination virtual device number (<paramref name="targetDeviceId"/>,
/// i.e. Player 1-4) and the target channel's display string (<paramref name="targetOutput"/>,
/// e.g. "[Xbox] Xbox A ..." or "[Wheel] Steering ..."), and drives the underlying virtual
/// device. Implemented by <see cref="VirtualFeederService"/> (ViGEm) and
/// <see cref="VJoyFeederService"/> (vJoy); <see cref="CompositeFeederService"/> composes both
/// behind this same interface so <c>MainWindowViewModel</c> can feed every mapping through
/// one call site without caring which backend a given entry's target string routes to.
/// </summary>
public interface IVirtualFeeder : IDisposable
{
    void SetActive(bool active);
    void UpdateAxis(uint targetDeviceId, string targetOutput, double normalizedValue);
    void UpdateButton(uint targetDeviceId, string targetOutput, bool isPressed);
    void Reset();
}