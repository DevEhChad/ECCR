using System;

namespace ECCR.Services;

public interface IVirtualFeeder : IDisposable
{
    void SetActive(bool active);
    void UpdateAxis(uint targetDeviceId, string targetOutput, double normalizedValue);
    void UpdateButton(uint targetDeviceId, string targetOutput, bool isPressed);
    void Reset();
}