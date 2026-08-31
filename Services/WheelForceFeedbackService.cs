using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ECCR.Models;
using SharpDX.DirectInput;
using vJoyInterfaceWrap;

namespace ECCR.Services;

/// <summary>
/// Relays force-feedback from a game back to the real wheel: a game creates/updates/plays PID
/// (HID Physical Interface Device) effects on the vJoy virtual wheel exactly like it would on
/// any real FFB wheel; vJoy hands the raw PID packets to <see cref="OnFfbPacket"/> via
/// <c>FfbRegisterGenCB</c> instead of discarding them (vJoy itself has no physical actuator);
/// this class decodes each packet with the vJoy wrapper's <c>Ffb_h_*</c> helpers and mirrors it
/// as a matching SharpDX.DirectInput <see cref="Effect"/> played on the real wheel, which
/// <see cref="VJoyFeederService"/> was never able to do (vJoy is otherwise output-only).
///
/// <para>
/// One <see cref="WheelTarget"/> per Player slot (vJoy device ID 1-4) that currently has a
/// physical wheel's "[Wheel] Steering" mapping - see
/// <c>MainWindowViewModel.SyncWheelForceFeedbackMappings</c>, which resolves that mapping and
/// calls <see cref="UpdateWheelMapping"/>/<see cref="RemoveWheelMapping"/> whenever it changes.
/// Each target holds its own exclusive-mode <see cref="Joystick"/> handle on the physical wheel
/// (separate from <see cref="InputDeviceManager"/>'s own non-exclusive polling handle on the
/// same device - DirectInput allows both to coexist within one process) so this class can create
/// and play effects on it; exclusive access only actually succeeds if nothing else (a game) can
/// see the raw device, which is exactly why a wheel that wants FFB should be HidHide-cloaked the
/// same as it already needs to be for button/axis remapping to be authoritative.
/// </para>
///
/// <para>
/// Like <see cref="ControllerHidService"/>'s reverse-engineered Sony HID offsets, the unit
/// conventions assumed below (effect magnitudes/coefficients/saturations as DirectInput's
/// native -10000..10000 / 0..10000 range, durations in microseconds) follow the vJoy SDK's
/// documented FFB struct shapes but have not been validated against a real FFB wheel + game -
/// see <c>%AppData%/ECCR/ffb-debug.log</c> for a running trace of every decoded packet to help
/// diagnose/tune against real hardware.
/// </para>
/// </summary>
public class WheelForceFeedbackService : IDisposable
{
    private sealed class EffectState
    {
        public FFBEType Type = FFBEType.ET_NONE;
        public int Gain = 255;
        public int DurationRaw;
        public short? ConstantMagnitude;
        public short? RampStart, RampEnd;
        public int? PeriodMagnitude, PeriodOffset, PeriodPhase;
        public uint? PeriodPeriod;
        public vJoy.FFB_EFF_COND? Condition;
        public vJoy.FFB_EFF_ENVLP? Envelope;
        public Effect? Live;
        public bool IsPlaying;
    }

    private sealed class WheelTarget
    {
        public required uint TargetDeviceId;
        public required Guid InstanceGuid;
        public required WheelDeviceSettings Settings;
        public Joystick? PhysicalDevice;
        public bool FfbStarted;
        public int DeviceGainByte = 255;
        public readonly Dictionary<byte, EffectState> Effects = new();
    }

    private const int Infinite = -1;

    private readonly DirectInput _directInput = new();
    private readonly vJoy _vJoy = new();
    private readonly Dictionary<uint, WheelTarget> _targets = new();
    private readonly object _lock = new();
    private readonly vJoy.FfbCbFunc _callback;
    private bool _isActive = true;
    private bool _disposed;
    private IntPtr _windowHandle = IntPtr.Zero;

    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECCR", "ffb-debug.log");

    private static void LogDebug(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
            File.AppendAllText(LogFilePath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    public WheelForceFeedbackService()
    {
        _callback = OnFfbPacket;
        try { _vJoy.FfbRegisterGenCB(_callback, null); }
        catch (Exception ex) { LogDebug($"FfbRegisterGenCB failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    /// <summary>
    /// Set once the app's main window has a real platform handle (see
    /// <c>MainWindow.OnOpened</c>/<c>MainWindowViewModel.SetNativeWindowHandle</c>) - exclusive
    /// DirectInput acquisition needs a real HWND, which doesn't exist yet at construction time.
    /// Retries any target that was waiting on this.
    /// </summary>
    public void SetWindowHandle(IntPtr handle)
    {
        lock (_lock)
        {
            _windowHandle = handle;
            if (handle == IntPtr.Zero) return;
            foreach (var target in _targets.Values.Where(t => !t.FfbStarted)) EnsureStarted(target);
        }
    }

    public void SetActive(bool active)
    {
        lock (_lock)
        {
            _isActive = active;
            if (!active) foreach (var target in _targets.Values) ReleaseTarget(target);
        }
    }

    /// <summary>Best-effort capability probe for the Wheel Settings tab's "not FFB capable" banner.</summary>
    public bool DeviceSupportsForceFeedback(Guid instanceGuid)
    {
        if (instanceGuid == Guid.Empty) return false;
        try
        {
            using var probe = new Joystick(_directInput, instanceGuid);
            return probe.Capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback);
        }
        catch { return false; }
    }

    /// <summary>
    /// Called whenever the physical wheel mapped to this Player slot's "[Wheel] Steering"
    /// target (or that wheel's <see cref="WheelDeviceSettings"/>) changes - idempotent, safe to
    /// call every time mappings are recalculated even when nothing for this slot actually
    /// changed.
    /// </summary>
    public void UpdateWheelMapping(uint targetDeviceId, Guid instanceGuid, WheelDeviceSettings settings)
    {
        lock (_lock)
        {
            if (_targets.TryGetValue(targetDeviceId, out var existing) && existing.InstanceGuid != instanceGuid)
            {
                ReleaseTarget(existing);
                _targets.Remove(targetDeviceId);
            }

            if (!_targets.TryGetValue(targetDeviceId, out var target))
            {
                target = new WheelTarget { TargetDeviceId = targetDeviceId, InstanceGuid = instanceGuid, Settings = settings };
                _targets[targetDeviceId] = target;
            }

            EnsureStarted(target);
        }
    }

    public void RemoveWheelMapping(uint targetDeviceId)
    {
        lock (_lock)
        {
            if (!_targets.TryGetValue(targetDeviceId, out var target)) return;
            ReleaseTarget(target);
            _targets.Remove(targetDeviceId);
        }
    }

    private void EnsureStarted(WheelTarget target)
    {
        if (!_isActive || !target.Settings.ForceFeedbackEnabled || _windowHandle == IntPtr.Zero)
        {
            ReleaseTarget(target);
            return;
        }

        if (target.FfbStarted) return;

        try
        {
            if (target.PhysicalDevice == null)
            {
                var joystick = new Joystick(_directInput, target.InstanceGuid);
                joystick.SetCooperativeLevel(_windowHandle, CooperativeLevel.Exclusive | CooperativeLevel.Background);
                joystick.Acquire();
                try { joystick.Properties.AutoCenter = false; } catch { }
                target.PhysicalDevice = joystick;
            }

            if (!_vJoy.IsDeviceFfb(target.TargetDeviceId))
            {
                LogDebug($"Target {target.TargetDeviceId}: vJoy device does not report FFB support - enable it for this device in the vJoy Configuration utility.");
                return;
            }

            target.FfbStarted = true;
            LogDebug($"Target {target.TargetDeviceId}: FFB relay started for physical device {target.InstanceGuid}.");
        }
        catch (Exception ex)
        {
            LogDebug($"Target {target.TargetDeviceId}: EnsureStarted failed: {ex.GetType().Name}: {ex.Message}");
            ReleaseTarget(target);
        }
    }

    private void ReleaseTarget(WheelTarget target)
    {
        target.FfbStarted = false;

        foreach (var state in target.Effects.Values)
        {
            try { state.Live?.Stop(); } catch { }
            try { state.Live?.Unload(); } catch { }
            try { state.Live?.Dispose(); } catch { }
        }
        target.Effects.Clear();

        if (target.PhysicalDevice != null)
        {
            try { target.PhysicalDevice.Unacquire(); } catch { }
            try { target.PhysicalDevice.Dispose(); } catch { }
            target.PhysicalDevice = null;
        }
    }

    // --- vJoy FFB callback: fires on vJoy's own thread whenever a game writes a PID report to
    // any registered vJoy device. Packets from every device share this one callback and are
    // demuxed below by the DeviceID field vJoy stamps into every packet. ---
    private void OnFfbPacket(IntPtr data, object userData)
    {
        try
        {
            uint deviceId = 0;
            if (_vJoy.Ffb_h_DeviceID(data, ref deviceId) != 0) return;

            WheelTarget? target;
            lock (_lock) { _targets.TryGetValue(deviceId, out target); }
            if (target?.PhysicalDevice == null) return;

            FFBPType packetType = default;
            if (_vJoy.Ffb_h_Type(data, ref packetType) != 0) return;

            lock (_lock)
            {
                switch (packetType)
                {
                    case FFBPType.PT_EFFREP: HandleEffectReport(target, data); break;
                    case FFBPType.PT_CONSTREP: HandleConstant(target, data); break;
                    case FFBPType.PT_RAMPREP: HandleRamp(target, data); break;
                    case FFBPType.PT_CONDREP: HandleCondition(target, data); break;
                    case FFBPType.PT_PRIDREP: HandlePeriodic(target, data); break;
                    case FFBPType.PT_ENVREP: HandleEnvelope(target, data); break;
                    case FFBPType.PT_EFOPREP: HandleEffectOp(target, data); break;
                    case FFBPType.PT_CTRLREP: HandleDeviceControl(target, data); break;
                    case FFBPType.PT_GAINREP: HandleDeviceGain(target, data); break;
                    default: break; // block-allocation bookkeeping (NEWEFREP/BLKFRREP/BLKLDREP/POOLREP/STATEREP) - vJoy's driver owns this, nothing for us to react to.
                }
            }
        }
        catch (Exception ex)
        {
            LogDebug($"OnFfbPacket threw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static EffectState GetOrCreateEffect(WheelTarget target, byte blockIndex)
    {
        if (!target.Effects.TryGetValue(blockIndex, out var state))
        {
            state = new EffectState();
            target.Effects[blockIndex] = state;
        }
        return state;
    }

    private void HandleEffectReport(WheelTarget target, IntPtr data)
    {
        vJoy.FFB_EFF_REPORT rep = default;
        if (_vJoy.Ffb_h_Eff_Report(data, ref rep) != 0) return;

        var state = GetOrCreateEffect(target, rep.EffectBlockIndex);
        state.Type = rep.EffectType;
        state.Gain = rep.Gain;
        state.DurationRaw = rep.Duration;
        RebuildEffect(target, rep.EffectBlockIndex);
    }

    private void HandleConstant(WheelTarget target, IntPtr data)
    {
        vJoy.FFB_EFF_CONSTANT c = default;
        if (_vJoy.Ffb_h_Eff_Constant(data, ref c) != 0) return;
        GetOrCreateEffect(target, c.EffectBlockIndex).ConstantMagnitude = c.Magnitude;
        RebuildEffect(target, c.EffectBlockIndex);
    }

    private void HandleRamp(WheelTarget target, IntPtr data)
    {
        vJoy.FFB_EFF_RAMP r = default;
        if (_vJoy.Ffb_h_Eff_Ramp(data, ref r) != 0) return;
        var state = GetOrCreateEffect(target, r.EffectBlockIndex);
        state.RampStart = r.Start;
        state.RampEnd = r.End;
        RebuildEffect(target, r.EffectBlockIndex);
    }

    private void HandlePeriodic(WheelTarget target, IntPtr data)
    {
        vJoy.FFB_EFF_PERIOD p = default;
        if (_vJoy.Ffb_h_Eff_Period(data, ref p) != 0) return;
        var state = GetOrCreateEffect(target, p.EffectBlockIndex);
        state.PeriodMagnitude = (int)p.Magnitude;
        state.PeriodOffset = p.Offset;
        state.PeriodPhase = (int)p.Phase;
        state.PeriodPeriod = p.Period;
        RebuildEffect(target, p.EffectBlockIndex);
    }

    private void HandleCondition(WheelTarget target, IntPtr data)
    {
        vJoy.FFB_EFF_COND c = default;
        if (_vJoy.Ffb_h_Eff_Cond(data, ref c) != 0) return;
        if (c.isY) return; // the wheel's FFB axis is X (steering) only - see MapTargetToAxis in VJoyFeederService.
        GetOrCreateEffect(target, c.EffectBlockIndex).Condition = c;
        RebuildEffect(target, c.EffectBlockIndex);
    }

    private void HandleEnvelope(WheelTarget target, IntPtr data)
    {
        vJoy.FFB_EFF_ENVLP e = default;
        if (_vJoy.Ffb_h_Eff_Envlp(data, ref e) != 0) return;
        GetOrCreateEffect(target, e.EffectBlockIndex).Envelope = e;
        RebuildEffect(target, e.EffectBlockIndex);
    }

    private void HandleEffectOp(WheelTarget target, IntPtr data)
    {
        vJoy.FFB_EFF_OP op = default;
        if (_vJoy.Ffb_h_EffOp(data, ref op) != 0) return;
        if (!target.Effects.TryGetValue(op.EffectBlockIndex, out var state)) return;

        switch (op.EffectOp)
        {
            case FFBOP.EFF_START:
                state.IsPlaying = true;
                try { state.Live?.Start(op.LoopCount > 0 ? op.LoopCount : 1); } catch { }
                break;
            case FFBOP.EFF_SOLO:
                state.IsPlaying = true;
                try { state.Live?.Start(EffectPlayFlags.Solo); } catch { }
                break;
            case FFBOP.EFF_STOP:
                state.IsPlaying = false;
                try { state.Live?.Stop(); } catch { }
                break;
        }
    }

    private void HandleDeviceControl(WheelTarget target, IntPtr data)
    {
        FFB_CTRL ctrl = default;
        if (_vJoy.Ffb_h_DevCtrl(data, ref ctrl) != 0) return;

        switch (ctrl)
        {
            case FFB_CTRL.CTRL_DEVRST:
                foreach (var state in target.Effects.Values)
                {
                    try { state.Live?.Stop(); } catch { }
                    try { state.Live?.Unload(); } catch { }
                    try { state.Live?.Dispose(); } catch { }
                }
                target.Effects.Clear();
                break;
            case FFB_CTRL.CTRL_STOPALL:
            case FFB_CTRL.CTRL_DISACT:
            case FFB_CTRL.CTRL_DEVPAUSE:
                foreach (var state in target.Effects.Values) { state.IsPlaying = false; try { state.Live?.Stop(); } catch { } }
                break;
            case FFB_CTRL.CTRL_ENACT:
            case FFB_CTRL.CTRL_DEVCONT:
                foreach (var state in target.Effects.Values.Where(s => s.IsPlaying)) { try { state.Live?.Start(); } catch { } }
                break;
        }
    }

    private void HandleDeviceGain(WheelTarget target, IntPtr data)
    {
        byte gain = 255;
        if (_vJoy.Ffb_h_DevGain(data, ref gain) != 0) return;
        target.DeviceGainByte = gain;
        foreach (var blockIndex in target.Effects.Keys.ToList()) RebuildEffect(target, blockIndex);
    }

    private static bool IsEffectTypeEnabled(FFBEType type, WheelDeviceSettings s) => type switch
    {
        FFBEType.ET_SPRNG => s.SpringEffectEnabled,
        FFBEType.ET_DMPR or FFBEType.ET_INRT => s.DamperEffectEnabled,
        FFBEType.ET_FRCTN => s.FrictionEffectEnabled,
        _ => true
    };

    private static Guid? ResolveEffectGuid(FFBEType type) => type switch
    {
        FFBEType.ET_CONST => EffectGuid.ConstantForce,
        FFBEType.ET_RAMP => EffectGuid.RampForce,
        FFBEType.ET_SQR => EffectGuid.Square,
        FFBEType.ET_SINE => EffectGuid.Sine,
        FFBEType.ET_TRNGL => EffectGuid.Triangle,
        FFBEType.ET_STUP => EffectGuid.SawtoothUp,
        FFBEType.ET_STDN => EffectGuid.SawtoothDown,
        FFBEType.ET_SPRNG => EffectGuid.Spring,
        FFBEType.ET_DMPR => EffectGuid.Damper,
        FFBEType.ET_INRT => EffectGuid.Inertia,
        FFBEType.ET_FRCTN => EffectGuid.Friction,
        _ => null // ET_CSTM (custom sample-based force) and ET_NONE aren't supported
    };

    private static int Clamp(double v, int min, int max) => Math.Clamp((int)Math.Round(v), min, max);

    /// <summary>
    /// (Re)creates or updates the real SharpDX effect for one vJoy effect block from whatever
    /// pieces of it have been decoded so far, applying <see cref="WheelDeviceSettings"/>
    /// (strength scaling, per-type enable, invert) and the device's own reported gain. Called
    /// after every packet that touches this block's parameters - PID effects are configured
    /// across several separate reports (general params, type-specific magnitude, envelope), so
    /// this may run several times before a game actually starts the effect.
    /// </summary>
    private void RebuildEffect(WheelTarget target, byte blockIndex)
    {
        if (target.PhysicalDevice == null) return;
        if (!target.Effects.TryGetValue(blockIndex, out var state)) return;
        if (state.Type == FFBEType.ET_NONE) return;

        var settings = target.Settings;
        if (!IsEffectTypeEnabled(state.Type, settings))
        {
            try { state.Live?.Stop(); } catch { }
            return;
        }

        var effectGuid = ResolveEffectGuid(state.Type);
        if (effectGuid == null) return;

        double scale = Math.Clamp(settings.ForceFeedbackStrengthPercent / 100.0, 0.0, 2.0) * (target.DeviceGainByte / 255.0);
        int sign = settings.InvertForce ? -1 : 1;

        var parameters = new EffectParameters
        {
            Axes = [(int)JoystickOffset.X],
            Directions = [0],
            Duration = state.DurationRaw >= 0xFFFF ? Infinite : state.DurationRaw,
            Gain = Clamp(state.Gain / 255.0 * 10000, 0, 10000),
            SamplePeriod = 0,
            StartDelay = 0,
            TriggerButton = Infinite,
            TriggerRepeatInterval = Infinite
        };

        switch (state.Type)
        {
            case FFBEType.ET_CONST:
                parameters.Parameters = new ConstantForce { Magnitude = Clamp(state.ConstantMagnitude.GetValueOrDefault() * scale * sign, -10000, 10000) };
                break;

            case FFBEType.ET_RAMP:
                parameters.Parameters = new RampForce
                {
                    Start = Clamp(state.RampStart.GetValueOrDefault() * scale * sign, -10000, 10000),
                    End = Clamp(state.RampEnd.GetValueOrDefault() * scale * sign, -10000, 10000)
                };
                break;

            case FFBEType.ET_SQR:
            case FFBEType.ET_SINE:
            case FFBEType.ET_TRNGL:
            case FFBEType.ET_STUP:
            case FFBEType.ET_STDN:
                parameters.Parameters = new PeriodicForce
                {
                    Magnitude = Clamp(state.PeriodMagnitude.GetValueOrDefault() * scale, 0, 10000),
                    Offset = Clamp(state.PeriodOffset.GetValueOrDefault() * scale * sign, -10000, 10000),
                    Phase = state.PeriodPhase.GetValueOrDefault(),
                    Period = (int)state.PeriodPeriod.GetValueOrDefault()
                };
                break;

            case FFBEType.ET_SPRNG:
            case FFBEType.ET_DMPR:
            case FFBEType.ET_INRT:
            case FFBEType.ET_FRCTN:
                if (state.Condition == null) return;
                var cond = state.Condition.Value;
                parameters.Parameters = new ConditionSet
                {
                    Conditions =
                    [
                        new Condition
                        {
                            Offset = Clamp(cond.CenterPointOffset * sign, -10000, 10000),
                            PositiveCoefficient = Clamp(cond.PosCoeff * scale, -10000, 10000),
                            NegativeCoefficient = Clamp(cond.NegCoeff * scale, -10000, 10000),
                            PositiveSaturation = Clamp(cond.PosSatur * scale, 0, 10000),
                            NegativeSaturation = Clamp(cond.NegSatur * scale, 0, 10000),
                            DeadBand = Clamp(cond.DeadBand, 0, 10000)
                        }
                    ]
                };
                break;

            default:
                return;
        }

        if (state.Envelope != null)
        {
            var env = state.Envelope.Value;
            parameters.Envelope = new Envelope
            {
                AttackLevel = Clamp(env.AttackLevel * scale, 0, 10000),
                AttackTime = (int)env.AttackTime,
                FadeLevel = Clamp(env.FadeLevel * scale, 0, 10000),
                FadeTime = (int)env.FadeTime
            };
        }

        try
        {
            if (state.Live == null) state.Live = new Effect(target.PhysicalDevice, effectGuid.Value, parameters);
            else state.Live.SetParameters(parameters);

            if (state.IsPlaying) state.Live.Start();
        }
        catch (Exception ex)
        {
            LogDebug($"RebuildEffect target={target.TargetDeviceId} block={blockIndex} type={state.Type} failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            foreach (var target in _targets.Values) ReleaseTarget(target);
            _targets.Clear();
        }

        try { _directInput.Dispose(); } catch { }
    }
}
