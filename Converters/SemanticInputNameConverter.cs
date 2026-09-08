using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using ECCR.Models;
using ECCR.Services;

namespace ECCR.Converters;

/// <summary>
/// Resolves the Device Mapping Table's "Input Name" column for one <see cref="MappingEntry"/>: the
/// fixed semantic label from the Device Mapping diagram's hotspot table (<see cref="ControllerHotspot.Label"/>/
/// <see cref="WheelHotspot.Label"/>) when the entry was created via a diagram hotspot click
/// (<see cref="MappingEntry.SemanticHotspot"/> set), else <see cref="MappingEntry.SourceDisplayName"/>
/// (the generated name from <see cref="DevicePresetService.GetButtonDisplayName"/>/
/// <see cref="DevicePresetService.GetAxisDisplayName"/>) for entries added via "+ Add Input"
/// instead.
///
/// <para>
/// <see cref="IMultiValueConverter"/> rather than a plain single-value converter bound to the
/// whole entry (<c>{Binding Converter=...}</c>, as this used to be) deliberately: a self-path
/// binding to the whole <see cref="MappingEntry"/> object only re-evaluates when the DataContext
/// *reference* changes, not when one of the entry's own properties changes - so a physical
/// capture completing or <c>MainWindowViewModel.ClearMapping</c> resetting a row back to "Click
/// to Map" (both of which mutate <see cref="MappingEntry.SourceDisplayName"/> in place) never
/// visually updated the row's name label. Binding the three relevant properties explicitly (see
/// <c>Views/Shared/DeviceMappingTableView.axaml</c>'s MultiBinding) makes the label reactive to
/// exactly what it depends on.
/// </para>
/// </summary>
public class SemanticInputNameConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        string sourceDisplayName = values.Count > 0 ? values[0] as string ?? string.Empty : string.Empty;
        string? semanticHotspot = values.Count > 1 ? values[1] as string : null;
        string sourceDeviceName = values.Count > 2 ? values[2] as string ?? string.Empty : string.Empty;
        string? nicknameDisplay = values.Count > 3 ? values[3] as string : null;

        string baseName = Resolve(sourceDisplayName, semanticHotspot, sourceDeviceName);
        return string.IsNullOrEmpty(nicknameDisplay) ? baseName : $"{nicknameDisplay} [{baseName}]";
    }

    /// <summary>Same resolution logic as <see cref="Convert"/>, for callers (e.g. <see cref="DeviceMappingTitleConverter"/>) that already have a whole <see cref="MappingEntry"/> in hand rather than three separately-bound values.</summary>
    public static string ResolveFor(MappingEntry entry)
    {
        string baseName = Resolve(entry.SourceDisplayName, entry.SemanticHotspot, entry.SourceDeviceName);
        return string.IsNullOrEmpty(entry.NicknameDisplay) ? baseName : $"{entry.NicknameDisplay} [{baseName}]";
    }

    private static string Resolve(string sourceDisplayName, string? semanticHotspot, string sourceDeviceName)
    {
        if (!string.IsNullOrEmpty(semanticHotspot))
        {
            // SemanticButton and WheelSemanticInput deliberately share some member names
            // (FaceNorth, D-Pad*) with matching label text, but which enum a given entry's
            // string actually belongs to depends on the owning device's category, not on
            // which enum happens to parse it first.
            var category = DevicePresetService.DetectCategory(sourceDeviceName);
            bool isWheel = DevicePresetService.IsWheelOrPedalCategory(category);

            if (isWheel && Enum.TryParse<WheelSemanticInput>(semanticHotspot, out var wheelInput))
                return WheelHotspotService.GetSemanticLabel(wheelInput);

            if (!isWheel && Enum.TryParse<SemanticButton>(semanticHotspot, out var button))
                return ControllerHotspotService.GetSemanticLabel(button);
        }

        return sourceDisplayName;
    }
}
