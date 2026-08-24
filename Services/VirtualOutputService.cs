using System;

namespace ECCR.Services;

public interface IVirtualFeeder : IDisposable
{
    void UpdateAxis(uint targetDeviceId, string targetOutput, double normalizedValue);
    void UpdateButton(uint targetDeviceId, string targetOutput, bool isPressed);
    void Reset();
}