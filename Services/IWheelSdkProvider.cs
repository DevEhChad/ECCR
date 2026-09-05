namespace ECCR.Services;

/// <summary>
/// Extensibility seam for reading a wheel/pedal vendor's own native button/axis names (e.g. via
/// a Logitech/Moza/Fanatec/Simucube/Thrustmaster SDK), keyed by USB VID/PID rather than a
/// specific hardware category so one provider can cover several categories from the same brand.
/// No implementation of this ships today - <see cref="DefaultWheelSdkProvider"/> is the only
/// one, and it always declines - real vendor SDKs are closed/licensed and weren't available to
/// integrate here. <see cref="DevicePresetService.GetButtonDisplayName"/>/<see cref="DevicePresetService.GetAxisDisplayName"/>
/// already accept an optional provider and prefer its answer over their own hardcoded per-brand
/// tables, so wiring in a real SDK later needs no changes at any of their call sites - only a
/// new class implementing this interface, passed in wherever those methods are already called.
/// </summary>
public interface IWheelSdkProvider
{
    /// <summary>Attempts to resolve a physical button's manufacturer-defined name. Returns false (with <paramref name="name"/> null) when this provider doesn't recognize the device or button index.</summary>
    bool TryGetNativeButtonName(int vendorId, int productId, int buttonIndex, out string? name);

    /// <summary>Attempts to resolve a physical axis's manufacturer-defined name. Returns false (with <paramref name="name"/> null) when this provider doesn't recognize the device or axis index.</summary>
    bool TryGetNativeAxisName(int vendorId, int productId, int axisIndex, out string? name);
}
