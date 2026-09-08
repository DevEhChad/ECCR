using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        // Reconstruction-filter state (see WheelDeviceSettings.ReconstructionFilterLevel):
        // Target* is the freshly-decoded, scaled value from the latest packet; Applied* is what
        // was actually last sent to the device, nudged toward Target* by ReconstructionTick
        // instead of snapping straight to it. Only meaningful for ET_CONST/periodic types.
        public double? TargetConstMag;
        public double? AppliedConstMag;
        public double? TargetPeriodMag;
        public double? AppliedPeriodMag;

        /// <summary>The scaled+signed Offset RebuildEffect computed for the current periodic packet - cached so ReconstructionTick's magnitude-only updates can reuse it without needing RebuildEffect's local scale/sign.</summary>
        public double ScaledPeriodOffset;
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

        // Synthetic, packet-independent effects - see EnsureStandaloneEffects.
        public Effect? CenterSpring;
        public Effect? SoftLock;
    }

    /// <summary>
    /// Mode B: a physical wheel whose steering axis is routed to an "[Xbox]"/"[DS4]" target
    /// instead of vJoy's "[Wheel] Steering" - see <see cref="OnSynthRumble"/>. Keyed directly by
    /// the physical device's own DirectInput instance GUID (no vJoy device is involved at all in
    /// this path, unlike <see cref="WheelTarget"/> which is keyed by vJoy device ID).
    /// </summary>
    private sealed class SynthTarget
    {
        public required Guid InstanceGuid;
        public required WheelDeviceSettings Settings;
        public Joystick? PhysicalDevice;
        public bool Started;
        public Effect? LowTexture;
        public Effect? HighTexture;
        public Effect? Damper;
        public Effect? CenterSpring;
        public Effect? SoftLock;
    }

    private const int Infinite = -1;

    private readonly DirectInput _directInput = new();
    private readonly vJoy _vJoy = new();
    private readonly Dictionary<uint, WheelTarget> _targets = new();
    private readonly Dictionary<Guid, SynthTarget> _synthTargets = new();
    private readonly object _lock = new();
    private readonly vJoy.FfbCbFunc _callback;
    private bool _isActive = true;
    private bool _disposed;
    private IntPtr _windowHandle = IntPtr.Zero;
    private Timer? _reconstructionTimer;
    private const int ReconstructionTickMs = 8;

    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECCR", "ffb-debug.log");

    private const long MaxLogSizeBytes = 5 * 1024 * 1024;

    private static void LogDebug(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
            if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MaxLogSizeBytes)
                File.Delete(LogFilePath);
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
            foreach (var target in _synthTargets.Values.Where(t => !t.Started)) EnsureSynthStarted(target);
        }
    }

    public void SetActive(bool active)
    {
        lock (_lock)
        {
            _isActive = active;
            if (!active)
            {
                foreach (var target in _targets.Values) ReleaseTarget(target);
                foreach (var target in _synthTargets.Values) ReleaseSynthTarget(target);
            }
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
            if (target.PhysicalDevice != null) EnsureStandaloneEffects(target.PhysicalDevice, target.Settings, ref target.CenterSpring, ref target.SoftLock);
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

    /// <summary>
    /// Mode B registration - see <see cref="SynthTarget"/> and <see cref="OnSynthRumble"/>. Called
    /// from <c>MainWindowViewModel.SyncXInputFfbSynthesisMappings</c> whenever a wheel/pedal
    /// device's steering axis is routed to an "[Xbox]"/"[DS4]" target and the user has opted in
    /// via <see cref="WheelDeviceSettings.SynthesizeFfbFromRumbleEnabled"/>. Idempotent, safe to
    /// call every time mappings/settings are recalculated even when nothing changed.
    /// </summary>
    public void UpdateSynthMapping(Guid instanceGuid, WheelDeviceSettings settings)
    {
        lock (_lock)
        {
            if (!_synthTargets.TryGetValue(instanceGuid, out var target))
            {
                target = new SynthTarget { InstanceGuid = instanceGuid, Settings = settings };
                _synthTargets[instanceGuid] = target;
            }

            EnsureSynthStarted(target);
            if (target.PhysicalDevice != null) EnsureStandaloneEffects(target.PhysicalDevice, target.Settings, ref target.CenterSpring, ref target.SoftLock);
        }
    }

    public void RemoveSynthMapping(Guid instanceGuid)
    {
        lock (_lock)
        {
            if (!_synthTargets.TryGetValue(instanceGuid, out var target)) return;
            ReleaseSynthTarget(target);
            _synthTargets.Remove(instanceGuid);
        }
    }

    /// <summary>
    /// Opens and exclusively acquires (<c>CooperativeLevel.Exclusive | CooperativeLevel.Background</c>)
    /// a fresh <see cref="Joystick"/> handle on a physical device, with autocentering disabled so
    /// it doesn't fight whatever effects get played on it. Shared by <see cref="EnsureStarted"/>
    /// (Mode A), <see cref="EnsureSynthStarted"/> (Mode B), and the one-shot test-pulse methods.
    /// Requires <see cref="_windowHandle"/> to already be set (a real HWND, not available until
    /// the main window opens) - returns null otherwise or on any acquisition failure (most often
    /// because the device isn't HidHide-cloaked from whatever else might also have it open).
    /// </summary>
    private Joystick? AcquireExclusive(Guid instanceGuid)
    {
        if (_windowHandle == IntPtr.Zero) return null;
        try
        {
            var joystick = new Joystick(_directInput, instanceGuid);
            joystick.SetCooperativeLevel(_windowHandle, CooperativeLevel.Exclusive | CooperativeLevel.Background);
            joystick.Acquire();
            try { joystick.Properties.AutoCenter = false; } catch { }
            return joystick;
        }
        catch (Exception ex)
        {
            LogDebug($"AcquireExclusive({instanceGuid}) failed: {ex.GetType().Name}: {ex.Message}");
            return null;
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

        if (target.PhysicalDevice == null)
        {
            target.PhysicalDevice = AcquireExclusive(target.InstanceGuid);
            if (target.PhysicalDevice == null) return;
        }

        try
        {
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

    /// <summary>Mode B equivalent of <see cref="EnsureStarted"/> - gated on both the master <see cref="WheelDeviceSettings.ForceFeedbackEnabled"/> switch and the opt-in <see cref="WheelDeviceSettings.SynthesizeFfbFromRumbleEnabled"/>, since Mode B is still "force feedback," just via a different signal path.</summary>
    private void EnsureSynthStarted(SynthTarget target)
    {
        if (!_isActive || !target.Settings.ForceFeedbackEnabled || !target.Settings.SynthesizeFfbFromRumbleEnabled || _windowHandle == IntPtr.Zero)
        {
            ReleaseSynthTarget(target);
            return;
        }

        if (target.Started) return;

        if (target.PhysicalDevice == null)
        {
            target.PhysicalDevice = AcquireExclusive(target.InstanceGuid);
            if (target.PhysicalDevice == null) return;
        }

        target.Started = true;
        LogDebug($"Synth target {target.InstanceGuid}: rumble-synthesis relay started.");
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

        DisposeEffect(ref target.CenterSpring);
        DisposeEffect(ref target.SoftLock);

        if (target.PhysicalDevice != null)
        {
            try { target.PhysicalDevice.Unacquire(); } catch { }
            try { target.PhysicalDevice.Dispose(); } catch { }
            target.PhysicalDevice = null;
        }
    }

    private void ReleaseSynthTarget(SynthTarget target)
    {
        target.Started = false;

        DisposeEffect(ref target.LowTexture);
        DisposeEffect(ref target.HighTexture);
        DisposeEffect(ref target.Damper);
        DisposeEffect(ref target.CenterSpring);
        DisposeEffect(ref target.SoftLock);

        if (target.PhysicalDevice != null)
        {
            try { target.PhysicalDevice.Unacquire(); } catch { }
            try { target.PhysicalDevice.Dispose(); } catch { }
            target.PhysicalDevice = null;
        }
    }

    private static void DisposeEffect(ref Effect? effect)
    {
        if (effect == null) return;
        try { effect.Stop(); } catch { }
        try { effect.Unload(); } catch { }
        try { effect.Dispose(); } catch { }
        effect = null;
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
        FFBEType.ET_DMPR => s.DamperEffectEnabled,
        FFBEType.ET_INRT => s.InertiaEffectEnabled,
        FFBEType.ET_FRCTN => s.FrictionEffectEnabled,
        _ => true
    };

    /// <summary>Per-effect-type gain (0-200%, see <see cref="WheelDeviceSettings.SpringGainPercent"/> and its siblings) layered on top of the device's overall <see cref="WheelDeviceSettings.ForceFeedbackStrengthPercent"/>.</summary>
    private static double GetEffectGain(FFBEType type, WheelDeviceSettings s) => (type switch
    {
        FFBEType.ET_SPRNG => s.SpringGainPercent,
        FFBEType.ET_DMPR => s.DamperGainPercent,
        FFBEType.ET_INRT => s.InertiaGainPercent,
        FFBEType.ET_FRCTN => s.FrictionGainPercent,
        _ => 100
    }) / 100.0;

    /// <summary>
    /// Boosts a nonzero force magnitude (DirectInput's -10000..10000 range) up to
    /// <see cref="WheelDeviceSettings.MinimumForcePercent"/>'s floor while preserving sign, so
    /// weak commanded forces (e.g. light road texture) aren't lost to the wheel's own static
    /// friction. Exact zero (no force at all) is left untouched - this only raises the floor
    /// under an already-nonzero force, it never invents one.
    /// </summary>
    private static double ApplyMinForceFloor(double magnitude, WheelDeviceSettings s)
    {
        if (s.MinimumForcePercent <= 0 || magnitude == 0) return magnitude;
        double floor = s.MinimumForcePercent / 100.0 * 10000;
        double abs = Math.Abs(magnitude);
        return abs >= floor ? magnitude : floor * Math.Sign(magnitude);
    }

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

    /// <summary>Force-magnitude ceiling in DirectInput's native 0-10000 units, from <see cref="WheelDeviceSettings.MaxTorquePercent"/> (100 = no extra cap beyond DirectInput's own native range).</summary>
    private static int TorqueCapUnits(WheelDeviceSettings s) => Clamp(Math.Clamp(s.MaxTorquePercent, 0, 100) / 100.0 * 10000, 0, 10000);

    /// <summary>
    /// (Re)creates or updates the real SharpDX effect for one vJoy effect block from whatever
    /// pieces of it have been decoded so far, applying <see cref="WheelDeviceSettings"/>
    /// (strength/per-effect gain scaling, per-type enable, invert, minimum-force floor) and the
    /// device's own reported gain. Called after every packet that touches this block's
    /// parameters - PID effects are configured across several separate reports (general params,
    /// type-specific magnitude, envelope), so this may run several times before a game actually
    /// starts the effect. Also updates <see cref="WheelDeviceSettings.LiveOutputForcePercent"/>/
    /// <see cref="WheelDeviceSettings.IsClipping"/> as an approximate clipping meter: whether any
    /// of this effect's own force values needed clamping to DirectInput's -10000..10000 range at
    /// the current Strength/gain settings. This is not a real hardware torque reading (SharpDX/
    /// DirectInput exposes none) - it only signals "your gain is high enough that some commanded
    /// forces are being clipped."
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

        double scale = Math.Clamp(settings.ForceFeedbackStrengthPercent / 100.0, 0.0, 2.0) * (target.DeviceGainByte / 255.0) * GetEffectGain(state.Type, settings);
        int sign = settings.InvertForce ? -1 : 1;
        int cap = TorqueCapUnits(settings);

        double peakMagnitude = 0;
        bool clipped = false;
        void Track(double raw)
        {
            double abs = Math.Abs(raw);
            if (abs > peakMagnitude) peakMagnitude = abs;
            if (abs > 10000) clipped = true;
        }

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
            {
                double target1 = ApplyMinForceFloor(state.ConstantMagnitude.GetValueOrDefault() * scale * sign, settings);
                state.TargetConstMag = target1;
                double applied = settings.ReconstructionFilterLevel > 0 && state.AppliedConstMag.HasValue ? state.AppliedConstMag.Value : target1;
                state.AppliedConstMag = applied;
                Track(applied);
                parameters.Parameters = new ConstantForce { Magnitude = Clamp(applied, -cap, cap) };
                if (settings.ReconstructionFilterLevel > 0) EnsureReconstructionTimer();
                break;
            }

            case FFBEType.ET_RAMP:
                double rampStart = state.RampStart.GetValueOrDefault() * scale * sign;
                double rampEnd = state.RampEnd.GetValueOrDefault() * scale * sign;
                Track(rampStart);
                Track(rampEnd);
                parameters.Parameters = new RampForce
                {
                    Start = Clamp(rampStart, -cap, cap),
                    End = Clamp(rampEnd, -cap, cap)
                };
                break;

            case FFBEType.ET_SQR:
            case FFBEType.ET_SINE:
            case FFBEType.ET_TRNGL:
            case FFBEType.ET_STUP:
            case FFBEType.ET_STDN:
            {
                double target1 = ApplyMinForceFloor(state.PeriodMagnitude.GetValueOrDefault() * scale, settings);
                state.TargetPeriodMag = target1;
                double applied = settings.ReconstructionFilterLevel > 0 && state.AppliedPeriodMag.HasValue ? state.AppliedPeriodMag.Value : target1;
                state.AppliedPeriodMag = applied;
                state.ScaledPeriodOffset = state.PeriodOffset.GetValueOrDefault() * scale * sign;
                Track(applied);
                parameters.Parameters = new PeriodicForce
                {
                    Magnitude = Clamp(applied, 0, cap),
                    Offset = Clamp(state.ScaledPeriodOffset, -cap, cap),
                    Phase = state.PeriodPhase.GetValueOrDefault(),
                    Period = (int)state.PeriodPeriod.GetValueOrDefault()
                };
                if (settings.ReconstructionFilterLevel > 0) EnsureReconstructionTimer();
                break;
            }

            case FFBEType.ET_SPRNG:
            case FFBEType.ET_DMPR:
            case FFBEType.ET_INRT:
            case FFBEType.ET_FRCTN:
                if (state.Condition == null) return;
                var cond = state.Condition.Value;
                double posCoeff = ApplyMinForceFloor(cond.PosCoeff * scale, settings);
                double negCoeff = ApplyMinForceFloor(cond.NegCoeff * scale, settings);
                Track(posCoeff);
                Track(negCoeff);
                parameters.Parameters = new ConditionSet
                {
                    Conditions =
                    [
                        new Condition
                        {
                            Offset = Clamp(cond.CenterPointOffset * sign, -cap, cap),
                            PositiveCoefficient = Clamp(posCoeff, -cap, cap),
                            NegativeCoefficient = Clamp(negCoeff, -cap, cap),
                            PositiveSaturation = Clamp(cond.PosSatur * scale, 0, cap),
                            NegativeSaturation = Clamp(cond.NegSatur * scale, 0, cap),
                            DeadBand = Clamp(cond.DeadBand, 0, 10000) // a positional dead-zone, not a force magnitude - not subject to the torque cap
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
                AttackLevel = Clamp(env.AttackLevel * scale, 0, cap),
                AttackTime = (int)env.AttackTime,
                FadeLevel = Clamp(env.FadeLevel * scale, 0, cap),
                FadeTime = (int)env.FadeTime
            };
        }

        settings.LiveOutputForcePercent = Math.Min(peakMagnitude / 10000.0, 1.5);
        settings.IsClipping = clipped;

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

    // --- Standalone effects: a synthetic center spring and/or soft-lock bump-stop wall, built
    // and maintained independent of any PID packet or rumble signal - shared by Mode A
    // (WheelTarget) and Mode B (SynthTarget) via ref parameters into whichever holds them. ---

    /// <summary>
    /// (Re)builds up to two persistent, packet-independent <see cref="Effect"/>s on
    /// <paramref name="device"/>: a symmetric centering <c>Spring</c> from
    /// <see cref="WheelDeviceSettings.StandaloneCenterSpringEnabled"/>, and a narrow-deadband
    /// <c>Spring</c> "bump stop" from <see cref="WheelDeviceSettings.SoftLockEnabled"/>. Safe to
    /// call repeatedly (e.g. every settings change) - creates on first call, updates parameters
    /// thereafter, tears down if a toggle is switched off. DirectInput sums concurrent effects on
    /// one device, so the center spring safely layers under a real in-game spring rather than
    /// needing "is the game already sending one" detection (documented trade-off: slightly
    /// stiffer center when both are active). The soft-lock wall deliberately ignores
    /// <see cref="WheelDeviceSettings.MaxTorquePercent"/> - it needs full authority to actually
    /// stop the wheel; capping it would make it an unreliable wall.
    /// </summary>
    private void EnsureStandaloneEffects(Joystick device, WheelDeviceSettings settings, ref Effect? centerSpring, ref Effect? softLock)
    {
        if (settings.StandaloneCenterSpringEnabled)
        {
            int cap = TorqueCapUnits(settings);
            int strength = Clamp(Math.Clamp(settings.StandaloneCenterSpringStrengthPercent, 0, 100) / 100.0 * cap, 0, 10000);
            var parameters = new EffectParameters
            {
                Axes = [(int)JoystickOffset.X],
                Directions = [0],
                Duration = Infinite,
                Gain = 10000,
                SamplePeriod = 0,
                StartDelay = 0,
                TriggerButton = Infinite,
                TriggerRepeatInterval = Infinite,
                Parameters = new ConditionSet
                {
                    Conditions =
                    [
                        new Condition { Offset = 0, PositiveCoefficient = strength, NegativeCoefficient = strength, PositiveSaturation = cap, NegativeSaturation = cap, DeadBand = 0 }
                    ]
                }
            };
            try
            {
                if (centerSpring == null) { centerSpring = new Effect(device, EffectGuid.Spring, parameters); centerSpring.Start(); }
                else centerSpring.SetParameters(parameters);
            }
            catch (Exception ex) { LogDebug($"Standalone center spring failed: {ex.GetType().Name}: {ex.Message}"); }
        }
        else
        {
            DisposeEffect(ref centerSpring);
        }

        if (settings.SoftLockEnabled && settings.RotationDegrees > 0)
        {
            double fraction = Math.Clamp((double)settings.SoftLockRotationDegrees / settings.RotationDegrees, 0.0, 1.0);
            int deadBand = Clamp(fraction * 10000, 0, 10000);
            int strength = Clamp(Math.Clamp(settings.SoftLockStrengthPercent, 0, 100) / 100.0 * 10000, 0, 10000);
            var parameters = new EffectParameters
            {
                Axes = [(int)JoystickOffset.X],
                Directions = [0],
                Duration = Infinite,
                Gain = 10000,
                SamplePeriod = 0,
                StartDelay = 0,
                TriggerButton = Infinite,
                TriggerRepeatInterval = Infinite,
                Parameters = new ConditionSet
                {
                    Conditions =
                    [
                        new Condition { Offset = 0, PositiveCoefficient = strength, NegativeCoefficient = strength, PositiveSaturation = 10000, NegativeSaturation = 10000, DeadBand = deadBand }
                    ]
                }
            };
            try
            {
                if (softLock == null) { softLock = new Effect(device, EffectGuid.Spring, parameters); softLock.Start(); }
                else softLock.SetParameters(parameters);
            }
            catch (Exception ex) { LogDebug($"Soft-lock effect failed: {ex.GetType().Name}: {ex.Message}"); }
        }
        else
        {
            DisposeEffect(ref softLock);
        }
    }

    // --- Reconstruction filter: see WheelDeviceSettings.ReconstructionFilterLevel and
    // EffectState's Target*/Applied* fields. A single shared timer, started lazily only once
    // something needs smoothing and stopped again once nothing does. ---

    private static double ReconstructionAlpha(int level) => level switch
    {
        1 => 0.50,
        2 => 0.32,
        3 => 0.20,
        4 => 0.12,
        5 => 0.06,
        _ => 1.0 // level 0 never reaches here (callers gate on > 0), 1.0 = instant as a safe fallback
    };

    private static bool IsPeriodicType(FFBEType t) =>
        t is FFBEType.ET_SQR or FFBEType.ET_SINE or FFBEType.ET_TRNGL or FFBEType.ET_STUP or FFBEType.ET_STDN;

    private void EnsureReconstructionTimer()
    {
        _reconstructionTimer ??= new Timer(ReconstructionTick, null, ReconstructionTickMs, ReconstructionTickMs);
    }

    private void ReconstructionTick(object? _)
    {
        lock (_lock)
        {
            if (_disposed) return;

            bool anyEnabled = false;
            foreach (var target in _targets.Values)
            {
                var settings = target.Settings;
                if (settings.ReconstructionFilterLevel <= 0) continue;
                anyEnabled = true;

                double alpha = ReconstructionAlpha(settings.ReconstructionFilterLevel);
                int cap = TorqueCapUnits(settings);

                foreach (var state in target.Effects.Values)
                {
                    if (state.Live == null || !state.IsPlaying) continue;

                    if (state.Type == FFBEType.ET_CONST && state.TargetConstMag.HasValue && state.AppliedConstMag.HasValue)
                    {
                        double next = state.AppliedConstMag.Value + (state.TargetConstMag.Value - state.AppliedConstMag.Value) * alpha;
                        state.AppliedConstMag = next;
                        try
                        {
                            state.Live.SetParameters(new EffectParameters { Parameters = new ConstantForce { Magnitude = Clamp(next, -cap, cap) } }, EffectParameterFlags.TypeSpecificParameters);
                        }
                        catch { }
                    }
                    else if (IsPeriodicType(state.Type) && state.TargetPeriodMag.HasValue && state.AppliedPeriodMag.HasValue)
                    {
                        double next = state.AppliedPeriodMag.Value + (state.TargetPeriodMag.Value - state.AppliedPeriodMag.Value) * alpha;
                        state.AppliedPeriodMag = next;
                        try
                        {
                            state.Live.SetParameters(new EffectParameters
                            {
                                Parameters = new PeriodicForce
                                {
                                    Magnitude = Clamp(next, 0, cap),
                                    Offset = Clamp(state.ScaledPeriodOffset, -cap, cap),
                                    Phase = state.PeriodPhase.GetValueOrDefault(),
                                    Period = (int)state.PeriodPeriod.GetValueOrDefault()
                                }
                            }, EffectParameterFlags.TypeSpecificParameters);
                        }
                        catch { }
                    }
                }
            }

            if (!anyEnabled)
            {
                _reconstructionTimer?.Dispose();
                _reconstructionTimer = null;
            }
        }
    }

    // --- Mode B: XInput/DS4 rumble -> synthesized wheel forces. See SynthTarget's doc comment. ---

    private const int LowTexturePeriodUs = 90_000;  // ~11Hz - road/curb texture, driven by the "heavy" motor
    private const int HighTexturePeriodUs = 35_000; // ~28Hz - shudder/ABS-style buzz, driven by the "light" motor

    /// <summary>
    /// Called from <c>MainWindowViewModel</c>'s <c>RumbleReceived</c> handler for every physical
    /// device mapped to a ViGEm target - no-ops immediately unless <paramref name="instanceGuid"/>
    /// is a registered, started Mode B target (see <see cref="UpdateSynthMapping"/>). Updates two
    /// periodic "texture" effects from the two motor bytes, plus a modest always-on damper
    /// (reusing <see cref="WheelDeviceSettings.DamperGainPercent"/>/<see cref="WheelDeviceSettings.DamperEffectEnabled"/>
    /// rather than inventing a separate Mode B setting) so the wheel has *some* resistance while
    /// turning even though Mode B has no real velocity/torque telemetry from the game - the
    /// standalone center spring (if enabled) is what actually keeps it from feeling "limp."
    /// </summary>
    public void OnSynthRumble(Guid instanceGuid, byte largeMotor, byte smallMotor)
    {
        lock (_lock)
        {
            if (!_synthTargets.TryGetValue(instanceGuid, out var target) || !target.Started || target.PhysicalDevice == null) return;

            var settings = target.Settings;
            double scale = Math.Clamp(settings.ForceFeedbackStrengthPercent / 100.0, 0.0, 2.0);
            int cap = TorqueCapUnits(settings);

            UpdateSynthPeriodic(target, ref target.LowTexture, largeMotor, LowTexturePeriodUs, scale, cap, settings);
            UpdateSynthPeriodic(target, ref target.HighTexture, smallMotor, HighTexturePeriodUs, scale, cap, settings);

            if (settings.DamperEffectEnabled)
            {
                // 0.3 is a deliberately modest constant, not a user-facing setting - Mode B has no
                // real velocity telemetry, so this is just "some" weight rather than an attempt at
                // an accurately-tuned damper. DirectInput's own Damper condition effect reacts to
                // the device's own reported velocity once uploaded, same mechanism Mode A's
                // game-driven damper already relies on - only the coefficient here is synthetic.
                int damperStrength = Clamp(GetEffectGain(FFBEType.ET_DMPR, settings) * cap * 0.3, 0, 10000);
                var parameters = new EffectParameters
                {
                    Axes = [(int)JoystickOffset.X],
                    Directions = [0],
                    Duration = Infinite,
                    Gain = 10000,
                    SamplePeriod = 0,
                    StartDelay = 0,
                    TriggerButton = Infinite,
                    TriggerRepeatInterval = Infinite,
                    Parameters = new ConditionSet
                    {
                        Conditions =
                        [
                            new Condition { Offset = 0, PositiveCoefficient = damperStrength, NegativeCoefficient = damperStrength, PositiveSaturation = cap, NegativeSaturation = cap, DeadBand = 0 }
                        ]
                    }
                };
                try
                {
                    if (target.Damper == null) { target.Damper = new Effect(target.PhysicalDevice, EffectGuid.Damper, parameters); target.Damper.Start(); }
                    else target.Damper.SetParameters(parameters);
                }
                catch (Exception ex) { LogDebug($"Synth damper failed: {ex.GetType().Name}: {ex.Message}"); }
            }
            else
            {
                DisposeEffect(ref target.Damper);
            }
        }
    }

    private void UpdateSynthPeriodic(SynthTarget target, ref Effect? effect, byte motorByte, int periodUs, double scale, int cap, WheelDeviceSettings settings)
    {
        bool playing = motorByte > 0;
        double magnitude = ApplyMinForceFloor(motorByte / 255.0 * cap * scale, settings);

        var parameters = new EffectParameters
        {
            Axes = [(int)JoystickOffset.X],
            Directions = [0],
            Duration = Infinite,
            Gain = 10000,
            SamplePeriod = 0,
            StartDelay = 0,
            TriggerButton = Infinite,
            TriggerRepeatInterval = Infinite,
            Parameters = new PeriodicForce { Magnitude = Clamp(magnitude, 0, cap), Offset = 0, Phase = 0, Period = periodUs }
        };

        try
        {
            if (effect == null)
            {
                if (!playing) return; // nothing to create yet if this motor is already at rest
                effect = new Effect(target.PhysicalDevice!, EffectGuid.Sine, parameters);
                effect.Start();
            }
            else
            {
                effect.SetParameters(parameters);
                if (playing) effect.Start(); else effect.Stop();
            }
        }
        catch (Exception ex)
        {
            LogDebug($"Synth periodic update failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // --- "Test FFB Pulse" UI actions: fire a brief one-shot effect directly, bypassing any
    // packet/rumble driving - mirrors MainWindowViewModel.TestVibration's fire-then-auto-stop
    // shape. Reuses whichever exclusive handle Mode A/B already has open for this device if one
    // is active, otherwise briefly acquires (and releases) its own. ---

    public void TestDirectionalPulse(Guid instanceGuid, bool right)
    {
        var parameters = new EffectParameters
        {
            Axes = [(int)JoystickOffset.X],
            Directions = [0],
            Duration = Infinite,
            Gain = 10000,
            SamplePeriod = 0,
            StartDelay = 0,
            TriggerButton = Infinite,
            TriggerRepeatInterval = Infinite,
            Parameters = new ConstantForce { Magnitude = right ? 6000 : -6000 }
        };
        PlayOneShot(instanceGuid, EffectGuid.ConstantForce, parameters, TimeSpan.FromMilliseconds(300));
    }

    public void TestTexturePulse(Guid instanceGuid)
    {
        var parameters = new EffectParameters
        {
            Axes = [(int)JoystickOffset.X],
            Directions = [0],
            Duration = Infinite,
            Gain = 10000,
            SamplePeriod = 0,
            StartDelay = 0,
            TriggerButton = Infinite,
            TriggerRepeatInterval = Infinite,
            Parameters = new PeriodicForce { Magnitude = 7000, Offset = 0, Phase = 0, Period = 40_000 }
        };
        PlayOneShot(instanceGuid, EffectGuid.Sine, parameters, TimeSpan.FromSeconds(1));
    }

    public void TestSoftLock(Guid instanceGuid)
    {
        var parameters = new EffectParameters
        {
            Axes = [(int)JoystickOffset.X],
            Directions = [0],
            Duration = Infinite,
            Gain = 10000,
            SamplePeriod = 0,
            StartDelay = 0,
            TriggerButton = Infinite,
            TriggerRepeatInterval = Infinite,
            Parameters = new ConditionSet
            {
                Conditions = [ new Condition { Offset = 0, PositiveCoefficient = 10000, NegativeCoefficient = 10000, PositiveSaturation = 10000, NegativeSaturation = 10000, DeadBand = 0 } ]
            }
        };
        PlayOneShot(instanceGuid, EffectGuid.Spring, parameters, TimeSpan.FromMilliseconds(500));
    }

    private Joystick? ResolveActiveDevice(Guid instanceGuid)
    {
        foreach (var t in _targets.Values) if (t.InstanceGuid == instanceGuid && t.PhysicalDevice != null) return t.PhysicalDevice;
        foreach (var t in _synthTargets.Values) if (t.InstanceGuid == instanceGuid && t.PhysicalDevice != null) return t.PhysicalDevice;
        return null;
    }

    private void PlayOneShot(Guid instanceGuid, Guid effectGuid, EffectParameters parameters, TimeSpan duration)
    {
        Joystick? device;
        bool transient;
        lock (_lock)
        {
            device = ResolveActiveDevice(instanceGuid);
            transient = device == null;
            if (device == null) device = AcquireExclusive(instanceGuid);
        }
        if (device == null) return;

        Effect effect;
        try
        {
            effect = new Effect(device, effectGuid, parameters);
            effect.Start();
        }
        catch (Exception ex)
        {
            LogDebug($"PlayOneShot({effectGuid}) failed: {ex.GetType().Name}: {ex.Message}");
            if (transient) { try { device.Unacquire(); } catch { } try { device.Dispose(); } catch { } }
            return;
        }

        var capturedDevice = device;
        _ = Task.Delay(duration).ContinueWith(_ =>
        {
            try { effect.Stop(); } catch { }
            try { effect.Unload(); } catch { }
            try { effect.Dispose(); } catch { }
            if (transient) { try { capturedDevice.Unacquire(); } catch { } try { capturedDevice.Dispose(); } catch { } }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            foreach (var target in _targets.Values) ReleaseTarget(target);
            _targets.Clear();
            foreach (var target in _synthTargets.Values) ReleaseSynthTarget(target);
            _synthTargets.Clear();
            _reconstructionTimer?.Dispose();
            _reconstructionTimer = null;
        }

        try { _directInput.Dispose(); } catch { }
    }
}
