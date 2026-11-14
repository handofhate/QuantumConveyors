using System;
using System.Collections.Generic;
using MoonCleaners;
using MoonFramework;
using UnityEngine;

namespace QuantumConveyors;

// The sending adapter (kind "quantumconveyors.output", shown as Quantum Input): takes capsules off the belt ending at
// it and passes them to a receiver on its channel. Nothing happens per frame: the work is done when MoonFramework
// offers it a capsule.
internal sealed class QuantumOutputMachine : Machine
{
    readonly Queue<PendingCapsule> pending = new();
    internal float ActiveUntil, BeltFullSince = -1f;
    internal bool HasPending => pending.Count > 0;
    internal int PendingCount => pending.Count;
    internal string ChannelId { get; private set; } = string.Empty;
    internal bool IsAttached => true;                    // as 1.x: a sender counts as attached

    public override void OnStart()
    {
        ChannelId = Data.TryGetValue("channel", out var ch) ? ch : string.Empty;
        if (Data.TryGetValue("pending", out var p) && !string.IsNullOrWhiteSpace(p))
            foreach (var s in p.Split(';')) if (QuantumNetwork.TryDecode(s, out var c)) pending.Enqueue(c);
        QuantumNetwork.Add(this);
        if (pending.Count > 0) QuantumNetwork.WithPending.Add(this);
        Status = Configure;
        Indicators.Register(this);
    }

    public override void OnStop() { QuantumNetwork.Remove(this); Indicators.Unregister(this); }

    string Configure => string.IsNullOrWhiteSpace(ChannelId) ? "Disabled: Press E to configure" : "Press E to configure";

    // Where a belt must end to feed it, as MoonFramework's input table keys it (FrameworkPatches.RefreshPrefix): asked
    // twice a second, worked out again only when the adapter's record changes (built, moved).
    IntPtr keyOf; float kx, ky, kz, kyaw, kqy, kqw; (int, int, int) key; int keyPort = -1;
    internal bool InputKey(MoonWorld world, out (int, int, int) k, out int port)
    {
        k = key; port = keyPort;
        var rec = Record; if (rec == null) return false;
        float x = rec.x, y = rec.y, z = rec.z, yaw = rec.yaw, qy = rec.qy, qw = rec.qw;
        if (keyPort >= 0 && rec.Pointer == keyOf && x == kx && y == ky && z == kz && yaw == kyaw && qy == kqy && qw == kqw) return true;
        keyPort = -1;
        var ports = Building.Definition?.conveyorPorts; var points = world.PortPoints(rec);
        if (ports == null || points == null) return false;
        for (int i = 0; i < ports.Length && i < points.Count; i++)
        {
            if (ports[i].output) continue;
            key = FrameworkPatches.Key(points.get_Item(i)); keyPort = i;
            keyOf = rec.Pointer; kx = x; ky = y; kz = z; kyaw = yaw; kqy = qy; kqw = qw;
            k = key; port = i; return true;
        }
        return false;
    }

    // Asked before MoonFramework reads an offered capsule out of the game (FrameworkPatches.OfferPrefix): false when
    // Accept would refuse it anyway, with the same bookkeeping as that refusal.
    internal bool MayAccept()
    {
        if (!Moon.IsAuthority || string.IsNullOrWhiteSpace(ChannelId) || pending.Count > 0) return false;
        var known = QuantumNetwork.KnownRefusal(ChannelId);
        if (known.HasValue) { Refused(known.Value); return false; }
        return true;
    }

    public override bool Accept(string port, Capsule capsule)
    {
        if (!Moon.IsAuthority || port != "input" || string.IsNullOrWhiteSpace(ChannelId) || pending.Count > 0) return false;
        var r = QuantumNetwork.TryTransmit(this, new PendingCapsule(capsule.Cargo, capsule.Kilograms, capsule.Capacity, capsule.Owner));
        if (r != QuantumTransferResult.Sent) { Refused(r); return false; }
        bool wasActive = ActiveUntil > Time.unscaledTime;
        BeltFullSince = -1f; ActiveUntil = Time.unscaledTime + 3f; Status = "Sending"; Tone = MachineTone.Working;
        if (!wasActive) { Indicators.Touch(this); Indicators.Due(this); }
        return true;
    }

    internal void Refused(QuantumTransferResult r)
    {
        if (r == QuantumTransferResult.DestinationUnavailable && BeltFullSince < 0f) { BeltFullSince = Time.unscaledTime; Indicators.Touch(this); Indicators.Due(this); }
        if (r == QuantumTransferResult.NoMatchingAdapter && BeltFullSince >= 0f) { BeltFullSince = -1f; Indicators.Touch(this); }
        Tone = MachineTone.Idle;
    }

    internal bool TryDeliverTo(QuantumInputMachine r)
    {
        if (pending.Count == 0 || !r.TryReceive(pending.Peek())) return false;
        bool wasActive = ActiveUntil > Time.unscaledTime;
        pending.Dequeue(); BeltFullSince = -1f; ActiveUntil = Time.unscaledTime + 3f; Status = "Sending";
        Tone = pending.Count != 0 ? MachineTone.Working : MachineTone.Idle;
        if (!wasActive) { Indicators.Touch(this); Indicators.Due(this); }
        return true;
    }

    internal void SetChannel(string channelId)
    {
        channelId ??= string.Empty;
        if (channelId == ChannelId) return;
        QuantumNetwork.Leave(this, ChannelId); ChannelId = channelId; QuantumNetwork.Join(this, ChannelId);
        if (string.IsNullOrWhiteSpace(ChannelId)) Data.Remove("channel"); else Data["channel"] = ChannelId;
        Status = Configure;
        Indicators.Touch(this);
    }

    public override void OnSave()
    {
        if (string.IsNullOrWhiteSpace(ChannelId)) Data.Remove("channel"); else Data["channel"] = ChannelId;
        var parts = new List<string>(); foreach (var c in pending) parts.Add(QuantumNetwork.Encode(c));
        Data["pending"] = string.Join(';', parts);
    }

    public override void OnView(GameObject view) => Indicators.Touch(this);

    public override void Describe(MachinePanel panel)
    {
        panel.Line("Ch: " + QuantumNetwork.ChannelLabel(ChannelId));
        panel.Line(Indicators.Text(State(), true));
    }

    internal QuantumIndicatorState State()
    {
        if (string.IsNullOrWhiteSpace(ChannelId)) return QuantumIndicatorState.WaitingForSetup;
        if (!QuantumNetwork.HasChannelInput(ChannelId)) return QuantumIndicatorState.NoMatchingAdapter;
        if (!QuantumNetwork.HasAttachedChannelInput(ChannelId)) return QuantumIndicatorState.ConveyorMissing;
        if (ActiveUntil > Time.unscaledTime) return QuantumIndicatorState.Active;
        if (BeltFullSince >= 0f && Time.unscaledTime - BeltFullSince >= 3f) return QuantumIndicatorState.OutputBeltFull;
        return QuantumIndicatorState.Ready;
    }
    // when the timed state next runs out (active ends, or refusals have lasted 3 s and the belt shows full); 0: never
    internal float NextChange()
    {
        float now = Time.unscaledTime, a = ActiveUntil > now ? ActiveUntil : 0f;
        float f = BeltFullSince >= 0f && BeltFullSince + 3f > now ? BeltFullSince + 3f : 0f;
        return a == 0f ? f : f == 0f ? a : Math.Min(a, f);
    }
    internal GameObject IndicatorPart() => Part("Indicator");
}

// The receiving adapter (kind "quantumconveyors.input", shown as Quantum Output): puts capsules from its channel on
// the belt starting at it. Whether a belt is attached is checked when a capsule is placed and when the world's belts
// change (Indicators.cs), not per frame.
internal sealed class QuantumInputMachine : Machine
{
    internal float ActiveUntil, BeltFullSince = -1f;
    internal float LastIn = -1f;                             // when it last put a capsule on its belt (Flow)
    internal int FullPass;                                   // refused in this hand-off pass (Network.TryTransmit)
    internal string ChannelId { get; private set; } = string.Empty;
    internal bool IsAttached { get; private set; }

    // The output port's point in the world and the owner new capsules get, worked out again only when the adapter's
    // record changes (built, moved). The belt itself is looked up fresh every time, so a rebuilt belt is always found.
    IntPtr portOf; float px, py, pz, pyaw, pqy, pqw; Vector3 port; string owner; bool portKnown;

    public override void OnStart()
    {
        ChannelId = Data.TryGetValue("channel", out var ch) ? ch : string.Empty;
        IsAttached = Belt() != null;
        QuantumNetwork.Add(this);
        Tone = MachineTone.Idle;
        Status = Configure;
        Indicators.Register(this);
    }

    public override void OnStop() { QuantumNetwork.Remove(this); Indicators.Unregister(this); }

    string Configure => string.IsNullOrWhiteSpace(ChannelId) ? "Disabled: Press E to configure" : "Press E to configure";

    bool KnowPort(MoonWorld world)
    {
        var rec = Record; if (rec == null) return false;
        float x = rec.x, y = rec.y, z = rec.z, yaw = rec.yaw, qy = rec.qy, qw = rec.qw;
        if (portKnown && rec.Pointer == portOf && x == px && y == py && z == pz && yaw == pyaw && qy == pqy && qw == pqw) return true;
        portKnown = false;
        var ports = Building.Definition?.conveyorPorts; var points = world.PortPoints(rec);
        if (ports == null || points == null) return false;
        for (int i = 0; i < ports.Length && i < points.Count; i++)
        {
            if (!ports[i].output) continue;
            port = points.get_Item(i); portOf = rec.Pointer; px = x; py = y; pz = z; pyaw = yaw; pqy = qy; pqw = qw;
            owner = string.IsNullOrEmpty(rec.owner) ? "local" : rec.owner;
            return portKnown = true;
        }
        return false;
    }

    MoonConveyorSnapshot Belt()
    {
        try
        {
            var world = Moon.World;
            return world != null && KnowPort(world) ? world.BeltStartingAt(port) : null;
        }
        catch { portKnown = false; return null; }
    }

    // the belt check (Indicators.CheckBelts, SetChannel): true when it changed
    internal bool RecheckAttached()
    {
        bool now = Belt() != null; if (now == IsAttached) return false;
        IsAttached = now; return true;
    }

    internal bool TryReceive(PendingCapsule capsule)
    {
        if (!IsAttached && !RecheckAttached()) return false;
        if (!IsAttached) return false;
        if (!Place(capsule))
        {
            if (BeltFullSince < 0f) { BeltFullSince = Time.unscaledTime; Indicators.Touch(this); Indicators.Due(this); }
            return false;
        }
        bool wasActive = ActiveUntil > Time.unscaledTime;
        BeltFullSince = -1f; ActiveUntil = Time.unscaledTime + 3f; Status = "Receiving"; Tone = MachineTone.Working;
        LastIn = Time.unscaledTime; Flow.Moved(ChannelId);
        if (!wasActive) { Indicators.Touch(this); Indicators.Due(this); }
        return true;
    }

    bool Place(PendingCapsule c)
    {
        if (FrameworkPatches.EmitCargo == null) return Emit("output", c.Cargo, c.Kilograms);
        var world = Moon.World; var belt = Belt();
        if (belt == null) { if (IsAttached) { IsAttached = false; Indicators.Touch(this); } return false; }
        return FrameworkPatches.EmitCargo(world, belt, owner, c.Cargo, c.Kilograms, out _);
    }

    internal void SetChannel(string channelId)
    {
        channelId ??= string.Empty;
        if (channelId == ChannelId) return;
        QuantumNetwork.Leave(this, ChannelId); ChannelId = channelId; QuantumNetwork.Join(this, ChannelId);
        if (string.IsNullOrWhiteSpace(ChannelId)) { BeltFullSince = -1f; Data.Remove("channel"); } else Data["channel"] = ChannelId;
        Status = Configure;
        RecheckAttached(); Indicators.Touch(this);
    }

    public override void OnSave() { if (string.IsNullOrWhiteSpace(ChannelId)) Data.Remove("channel"); else Data["channel"] = ChannelId; }

    public override void OnView(GameObject view) => Indicators.Touch(this);

    public override void Describe(MachinePanel panel)
    {
        panel.Line("Ch: " + QuantumNetwork.ChannelLabel(ChannelId));
        panel.Line(Indicators.Text(State(), false));
    }

    internal QuantumIndicatorState State()
    {
        if (string.IsNullOrWhiteSpace(ChannelId)) return QuantumIndicatorState.WaitingForSetup;
        if (!IsAttached) return QuantumIndicatorState.ConveyorMissing;
        if (!QuantumNetwork.HasChannelOutput(ChannelId)) return QuantumIndicatorState.NoMatchingAdapter;
        if (ActiveUntil > Time.unscaledTime) return QuantumIndicatorState.Active;
        if (BeltFullSince >= 0f && Time.unscaledTime - BeltFullSince >= 3f) return QuantumIndicatorState.OutputBeltFull;
        return QuantumIndicatorState.Ready;
    }
    // when the timed state next runs out (active ends, or refusals have lasted 3 s and the belt shows full); 0: never
    internal float NextChange()
    {
        float now = Time.unscaledTime, a = ActiveUntil > now ? ActiveUntil : 0f;
        float f = BeltFullSince >= 0f && BeltFullSince + 3f > now ? BeltFullSince + 3f : 0f;
        return a == 0f ? f : f == 0f ? a : Math.Min(a, f);
    }
    internal GameObject IndicatorPart() => Part("Indicator");
}
