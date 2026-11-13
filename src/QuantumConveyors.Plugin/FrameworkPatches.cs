using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using MoonCleaners;
using MoonFramework;
using UnityEngine;

namespace QuantumConveyors;

// Changes to how MoonFramework treats our adapters. Every patch fails closed: if MoonFramework changed, the patch is
// skipped and the adapters still work the slow way.
//
//  1. Tick / Animate / PowerSystem.Step: adapters need none of them, so those loops run over the other framework machines
//     only (a list rebuilt after each Machines.Sync). With ~1500 adapters this was the biggest per-frame cost.
//  2. Intake.Process (once a frame: every capsule waiting at a framework input is offered to its machine): each pass is a
//     numbered "hand-off pass". Belts don't move during a pass, so a receiver that refused a capsule stays full until the
//     pass ends; Network.cs skips it, and a channel whose receivers are all full refuses at once. Exact, not a delay.
//     Machines.Offer: a capsule offered to a sender that would refuse anyway (no channel, a saved backlog, no receivers,
//     channel full this pass) is refused before the framework reads the capsule out of the game.
//  3. Capsules at a Quantum Input don't go into MoonFramework's waiting list (Intake.AtInput): they wait in their
//     channel's own list, and each pass a channel is offered its capsules only until one is refused for a full channel.
//     MoonFramework offered every waiting capsule every frame; a full base now costs per channel, not per capsule.
//  4. Intake.Refresh (twice a second: the table of every framework input's position, rounded to the centimetre) is done
//     here: other machines exactly as MoonFramework does it, while a Quantum Input's key is remembered and the game asked
//     for its port only when the adapter's record changed (built, moved), and receivers (no inputs) are skipped. At
//     ~1500 adapters that was ~1500 calls into the game in one frame, twice a second. Each Quantum Input's port also
//     gets the 26 neighbouring centimetre keys, unless another machine's input has one: a belt end a few millimetres
//     from the port can round to the next centimetre and would never hand over a capsule (a "dead line").
//  5. Native.EmitCargo is called directly by receivers, with their belt's port point remembered (Machines.cs), instead of
//     Machine.Emit looking the port up by name and asking the game for every port point on every capsule.
internal static class FrameworkPatches
{
    const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    // ---------------------------------------------------------------- 1. loops without our adapters
    static Dictionary<int, Machine> live;
    static Dictionary<Machine, int> errors;
    static Action<Machine, string, Action> call;
    static Func<Machine, bool> paused;
    static readonly List<Machine> others = new();

    // ---------------------------------------------------------------- 2. hand-off passes
    internal static int Pass = 1;          // incremented at the start of each Intake.Process
    internal static bool InPass;           // true while Intake.Process runs

    // ---------------------------------------------------------------- 3. channel waiting lists
    delegate bool OfferFn(MoonWorld world, Machine m, int input, int capsuleId);
    static OfferFn offer;
    static bool ownQueue;
    static readonly Dictionary<Channel, List<(int id, QuantumOutputMachine m, int port)>> waiting = new();
    static readonly List<Channel> waitingChannels = new();
    static readonly HashSet<int> queued = new();
    static readonly Dictionary<int, float> handedOver = new();        // taken by a sender, until the belt lets go of it
    static readonly List<int> forget = new();
    const float ForgetHandedAfter = 10f;                              // MoonFramework's own figure

    // ---------------------------------------------------------------- 4. the input table
    static Dictionary<(int, int, int), (int, int)> inputs;
    static readonly List<((int, int, int) key, (int, int) at)> ours = new();

    // ---------------------------------------------------------------- 5. emitting
    internal delegate bool EmitCargoFn(MoonWorld world, MoonConveyorSnapshot belt, string owner, MoonCargo cargo, double kg, out string reason);
    internal static EmitCargoFn EmitCargo;

    internal static void Install(Harmony h, ManualLogSource log)
    {
        var asm = typeof(Machine).Assembly;
        var mt = asm.GetType("MoonFramework.Internal.Machines", false);
        var it = asm.GetType("MoonFramework.Internal.Intake", false);
        Try(log, "adapters left out of MoonFramework's Tick/Animate/power loops", () =>
        {
            live = (Dictionary<int, Machine>)mt.GetField("live", S).GetValue(null);
            errors = (Dictionary<Machine, int>)mt.GetField("errors", S).GetValue(null);
            call = (Action<Machine, string, Action>)Delegate.CreateDelegate(typeof(Action<Machine, string, Action>), mt.GetMethod("Call", S));
            paused = (Func<Machine, bool>)Delegate.CreateDelegate(typeof(Func<Machine, bool>), mt.GetMethod("Paused", S));
            var step = asm.GetType("MoonFramework.Internal.PowerSystem", true).GetMethod("Step", S) ?? throw new MissingMethodException("PowerSystem.Step");
            Patch(h, mt.GetMethod("Sync", S), postfix: nameof(Rebuild));
            Patch(h, mt.GetMethod("StopAll", S), postfix: nameof(Rebuild));
            Patch(h, mt.GetMethod("Tick", S), prefix: nameof(TickPrefix));
            Patch(h, mt.GetMethod("Animate", S), prefix: nameof(AnimatePrefix));
            Patch(h, step, prefix: nameof(PowerPrefix));
        });
        Try(log, "capsule hand-off passes tracked; refused offers stopped early", () =>
        {
            Patch(h, it.GetMethod("Process", S), prefix: nameof(PassStart), finalizer: nameof(PassEnd));
            Patch(h, mt.GetMethod("Offer", S), prefix: nameof(OfferPrefix));
        });
        // 3 and 4 share the input table; everything is looked up before anything is patched
        Try(log, "the input table kept for Quantum Inputs, with belts ending within a centimetre of the port", () =>
        {
            inputs = (Dictionary<(int, int, int), (int, int)>)it.GetField("inputs", S).GetValue(null);
            live ??= (Dictionary<int, Machine>)mt.GetField("live", S).GetValue(null);
            Patch(h, it.GetMethod("Refresh", S), prefix: nameof(RefreshPrefix));
        });
        Try(log, "capsules at Quantum Inputs wait in their channel", () =>
        {
            if (inputs == null) throw new InvalidOperationException("the input table isn't patched");
            offer = (OfferFn)Delegate.CreateDelegate(typeof(OfferFn), mt.GetMethod("Offer", S));
            var atInput = it.GetMethod("AtInput", S); var reset = it.GetMethod("Reset", S);
            if (atInput == null || reset == null) throw new MissingMethodException("Intake.AtInput/Reset");
            Patch(h, atInput, prefix: nameof(AtInputPrefix));
            Patch(h, reset, postfix: nameof(ResetPostfix));
            ownQueue = true;
        });
        Try(log, "receivers emit straight onto their belt", () =>
        {
            var nt = asm.GetType("MoonFramework.Internal.Native", true);
            EmitCargo = (EmitCargoFn)Delegate.CreateDelegate(typeof(EmitCargoFn), nt.GetMethod("EmitCargo", S));
        });
    }

    static void Try(ManualLogSource log, string what, Action install)
    {
        try { install(); log.LogInfo("Quantum Conveyors: " + what); }
        catch (Exception e) { log.LogWarning($"Quantum Conveyors: not done ({what}): {e.Message}. Adapters still work."); }
    }

    static void Patch(Harmony h, MethodInfo target, string prefix = null, string postfix = null, string finalizer = null)
    {
        if (target == null) throw new MissingMethodException("a MoonFramework method changed");
        HarmonyMethod M(string name) => name == null ? null : new HarmonyMethod(typeof(FrameworkPatches).GetMethod(name, S));
        h.Patch(target, prefix: M(prefix), postfix: M(postfix), finalizer: M(finalizer));
    }

    internal static bool Ours(Machine m) => m is QuantumOutputMachine || m is QuantumInputMachine;

    // ---------------------------------------------------------------- 1.
    static void Rebuild()
    {
        others.Clear();
        foreach (var m in live.Values) if (!Ours(m)) others.Add(m);
    }

    static bool TickPrefix(float dt)
    {
        for (int i = 0; i < others.Count; i++)
        {
            var m = others[i];
            if (errors.TryGetValue(m, out var n) && n >= 20) continue;
            call(m, "Tick", () => m.Tick(dt));
        }
        return false;
    }

    static bool AnimatePrefix(float dt)
    {
        for (int i = 0; i < others.Count; i++)
        {
            var m = others[i];
            if (m.View == null || paused(m)) continue;
            call(m, "Animate", () => m.Animate(dt));
        }
        return false;
    }

    // adapters draw no power and make none
    static void PowerPrefix(ref IEnumerable<Machine> machines) => machines = others;

    // ---------------------------------------------------------------- 2.
    static void PassStart(MoonWorld world)
    {
        Pass++; InPass = true;
        if (ownQueue) Drain(world);
    }
    static Exception PassEnd(Exception __exception) { InPass = false; return __exception; }

    static bool OfferPrefix(Machine m, ref bool __result)
    {
        if (m is QuantumOutputMachine o && !o.MayAccept()) { __result = false; return false; }
        return true;
    }

    // ---------------------------------------------------------------- 3.
    // MoonFramework's Intake.Key: the same rounding, so the same belt ends match
    internal static (int, int, int) Key(Vector3 p) => (Mathf.RoundToInt(p.x * 100f), Mathf.RoundToInt(p.y * 100f), Mathf.RoundToInt(p.z * 100f));

    // the game asks, for a capsule at the end of a belt, whether a framework input takes it (true) and whether it has
    // already been taken (handedOff): ours answer for Quantum Inputs, as MoonFramework would, but queue per channel
    static bool AtInputPrefix(Vector3 end, int capsuleId, ref bool handedOff, ref bool __result)
    {
        if (!inputs.TryGetValue(Key(end), out var at) || !live.TryGetValue(at.Item1, out var m) || m is not QuantumOutputMachine o) return true;
        __result = true;
        if (handedOver.Remove(capsuleId)) { handedOff = true; return false; }
        handedOff = false;
        if (Moon.IsAuthority && queued.Add(capsuleId))
        {
            var c = QuantumNetwork.ChannelOf(o.ChannelId);
            if (c != null)
            {
                if (!waiting.TryGetValue(c, out var list)) waiting[c] = list = new();
                if (list.Count == 0) waitingChannels.Add(c);
                list.Add((capsuleId, o, at.Item2));
            }
        }
        return false;
    }

    // each pass: a channel's capsules are offered in the order they arrived until the channel is full
    static void Drain(MoonWorld world)
    {
        float now = Time.unscaledTime;
        if (handedOver.Count > 0)
        {
            foreach (var kv in handedOver) if (now - kv.Value > ForgetHandedAfter) forget.Add(kv.Key);
            foreach (var id in forget) handedOver.Remove(id);
            forget.Clear();
        }
        foreach (var c in waitingChannels)
        {
            var list = waiting[c];
            int i = 0;
            for (; i < list.Count && c.Receivers.Count > 0 && c.FullPass != Pass; i++)
            {
                var (id, m, port) = list[i];
                try { if (offer(world, m, port, id)) handedOver[id] = now; }
                catch (Exception e) { QuantumNetwork.Log?.LogError($"Quantum Conveyors: offering capsule {id}: {e}"); }
            }
            // the rest aren't offered: their senders note the refusal they'd have had (for their lights)
            var why = c.Receivers.Count == 0 ? QuantumTransferResult.NoMatchingAdapter : QuantumTransferResult.DestinationUnavailable;
            for (; i < list.Count; i++) list[i].m.Refused(why);
            list.Clear();
        }
        waitingChannels.Clear(); queued.Clear();
    }

    static void ResetPostfix()
    {
        foreach (var c in waitingChannels) waiting[c].Clear();
        waitingChannels.Clear(); queued.Clear(); handedOver.Clear();
    }

    // ---------------------------------------------------------------- 4.
    // MoonFramework's Intake.Refresh, with remembered keys for Quantum Inputs
    static bool RefreshPrefix(MoonWorld world)
    {
        inputs.Clear(); ours.Clear();
        foreach (var m in live.Values)
        {
            if (m is QuantumInputMachine) continue;                                  // an output port only
            if (m is QuantumOutputMachine o)
            {
                if (o.InputKey(world, out var key, out var port)) { inputs[key] = (m.Id, port); ours.Add((key, (m.Id, port))); }
                continue;
            }
            if (m.Building.Spec.Router) continue;
            var ports = m.Building.Definition?.conveyorPorts; if (ports == null) continue;
            var points = world.PortPoints(m.Record); if (points == null) continue;
            for (int i = 0; i < ports.Length && i < points.Count; i++)
                if (!ports[i].output) inputs[Key(points.get_Item(i))] = (m.Id, i);
        }
        foreach (var (k, at) in ours)
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                if (dx != 0 || dy != 0 || dz != 0) inputs.TryAdd((k.Item1 + dx, k.Item2 + dy, k.Item3 + dz), at);   // a machine's own key wins
        ours.Clear();
        return false;
    }
}
