namespace ECCR.Services;

/// <summary>No-op <see cref="IWheelSdkProvider"/> - always declines, preserving today's behavior (the hardcoded per-brand tables in <see cref="DevicePresetService"/> are the only naming source) until a real vendor SDK integration replaces it.</summary>
public sealed class DefaultWheelSdkProvider : IWheelSdkProvider
{
    public bool TryGetNativeButtonName(int vendorId, int productId, int buttonIndex, out string? name)
    {
        name = null;
        return false;
    }

    public bool TryGetNativeAxisName(int vendorId, int productId, int axisIndex, out string? name)
    {
        name = null;
        return false;
    }
}
