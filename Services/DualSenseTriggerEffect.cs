using System;
using ECCR.Models;

namespace ECCR.Services;

/// <summary>
/// Encodes a <see cref="TriggerEffectSettings"/> into the 11-byte per-trigger block of a DualSense
/// output report (one mode byte + 10 parameter bytes) - see <see cref="ControllerHidService"/>, which
/// copies it to offset 11 (right trigger) / 22 (left trigger) of report 0x02.
///
/// <para>
/// Mode bytes and parameter layout follow the community-documented DualSense trigger formats
/// (Nielk1's trigger-effect notes, as used by DualSenseX/dualsensectl): Feedback 0x21, Weapon 0x25,
/// Vibration 0x26, Off 0x05. Resistance and Vibration describe the active region as a 10-bit zone
/// mask plus 3 bits of strength per zone; Weapon is a start/stop zone pair plus one strength value.
/// Not yet confirmed against a physical pad - the Vibration frequency byte in particular is the
/// least certain part.
/// </para>
/// </summary>
public static class DualSenseTriggerEffect
{
    /// <summary>Mode byte plus ten parameter bytes.</summary>
    public const int Length = 11;

    private const byte ModeOff = 0x05;
    private const byte ModeFeedback = 0x21;
    private const byte ModeWeapon = 0x25;
    private const byte ModeVibration = 0x26;

    public static byte[] Off() => new byte[Length] { ModeOff, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    public static bool IsOff(byte[] effect) => effect[0] == ModeOff;

    public static byte[] Encode(TriggerEffectSettings settings)
    {
        switch (settings.Mode)
        {
            case TriggerEffectMode.Resistance:
                return EncodeZones(ModeFeedback, settings.StartPosition, settings.Strength);

            case TriggerEffectMode.Vibration:
            {
                var effect = EncodeZones(ModeVibration, settings.StartPosition, settings.Strength);
                effect[9] = (byte)Math.Clamp(settings.Frequency, 1, 255); // parameter 8 of 0-9
                return effect;
            }

            case TriggerEffectMode.Weapon:
            {
                // Hardware limits: start 2-7, end 3-8 and strictly after start.
                int start = Math.Clamp(settings.WeaponStart, 2, 7);
                int end = Math.Clamp(settings.WeaponEnd, start + 1, 8);
                int zones = (1 << start) | (1 << end);

                var effect = new byte[Length];
                effect[0] = ModeWeapon;
                effect[1] = (byte)(zones & 0xFF);
                effect[2] = (byte)((zones >> 8) & 0xFF);
                effect[3] = (byte)(Math.Clamp(settings.Strength, 1, 8) - 1);
                return effect;
            }

            default:
                return Off();
        }
    }

    /// <summary>Active-zone mask (every zone from <paramref name="position"/> to 9) followed by 3-bit-per-zone strength, all zones equal.</summary>
    private static byte[] EncodeZones(byte mode, int position, int strength)
    {
        position = Math.Clamp(position, 0, 9);
        uint force = (uint)(Math.Clamp(strength, 1, 8) - 1);

        uint strengthZones = 0;
        int activeZones = 0;
        for (int zone = position; zone < 10; zone++)
        {
            strengthZones |= force << (3 * zone);
            activeZones |= 1 << zone;
        }

        var effect = new byte[Length];
        effect[0] = mode;
        effect[1] = (byte)(activeZones & 0xFF);
        effect[2] = (byte)((activeZones >> 8) & 0xFF);
        effect[3] = (byte)(strengthZones & 0xFF);
        effect[4] = (byte)((strengthZones >> 8) & 0xFF);
        effect[5] = (byte)((strengthZones >> 16) & 0xFF);
        effect[6] = (byte)((strengthZones >> 24) & 0xFF);
        return effect;
    }
}
