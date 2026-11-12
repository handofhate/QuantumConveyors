using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using MoonCleaners;
using MoonFramework;

namespace QuantumConveyors;

// Changes to how MoonFramework treats our adapters. Every patch fails closed: if MoonFramework changed, the patch is
// skipped and the adapters still work the slow way.
//
//  1. Tick / Animate / PowerSystem.Step: adapters need none of them, so those loops run over the other framework machines
//     only (a list rebuilt after each Machines.Sync). With ~1500 adapters this was the biggest per-frame cost.
//  2. Intake.Process (once a frame: every capsule waiting at a framework input is offered to its machine): each pass is a
//     numbered "hand-off pass". Belts don't move during a pass, so a receiver that refused a capsule stays full until the
//     pass ends; Network.cs skips it, and a channel whose receivers are all full refuses at once. Exact, not a delay.
//  3. Machines.Offer: a capsule offered to a sender that would refuse anyway (no channel, a saved backlog, no receivers,
//     channel full this pass) is refused before the framework reads the capsule out of the game.
//  4. Native.EmitCargo is called directly by receivers, with their belt's port point remembered (Machines.cs), instead of
//     Machine.Emit looking the port up by name and asking the game for every port point on every capsule.
internal static class FrameworkPatches
{
    // ---------------------------------------------------------------- 1. loops without our adapters
    static Dictionary<int, Machine> live;
    static Dictionary<Machine, int> errors;
    static Action<Machine, string, Action> call;
    static Func<Machine, bool> paused;
    static readonly List<Machine> others = new();
    internal static bool LoopsPatched;

    // ---------------------------------------------------------------- 2. hand-off passes
    internal static int Pass = 1;          // incremented at the start of each Intake.Process
    internal static bool InPass;           // true while Intake.Process runs

    // ---------------------------------------------------------------- 4. emitting
    internal delegate bool EmitCargoFn(MoonWorld world, MoonConveyorSnapshot belt, string owner, MoonCargo cargo, double kg, out string reason);
    internal static EmitCargoFn EmitCargo;

    const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    internal static void Install(Harmony h, ManualLogSource log)
    {
        var asm = typeof(Machine).Assembly;
        Try(log, "adapters left out of MoonFramework's Tick/Animate/power loops", () =>
        {
            var mt = asm.GetType("MoonFramework.Internal.Machines", true);
            live = (Dictionary<int, Machine>)mt.GetField("live", S).GetValue(null);
            errors = (Dictionary<Machine, int>)mt.GetField("errors", S).GetValue(null);
            call = (Action<Machine, string, Action>)Delegate.CreateDelegate(typeof(Action<Machine, string, Action>), mt.GetMethod("Call", S));
            paused = (Func<Machine, bool>)Delegate.CreateDelegate(typeof(Func<Machine, bool>), mt.GetMethod("Paused", S));
            var ps = asm.GetType("MoonFramework.Internal.PowerSystem", true);
            var step = ps.GetMethod("Step", S) ?? throw new MissingMethodException("PowerSystem.Step");
            Patch(h, mt.GetMethod("Sync", S), postfix: nameof(Rebuild));
            Patch(h, mt.GetMethod("StopAll", S), postfix: nameof(Rebuild));
            Patch(h, mt.GetMethod("Tick", S), prefix: nameof(TickPrefix));
            Patch(h, mt.GetMethod("Animate", S), prefix: nameof(AnimatePrefix));
            Patch(h, step, prefix: nameof(PowerPrefix));
            LoopsPatched = true;
        });
        Try(log, "capsule hand-off passes tracked; refused offers stopped early", () =>
        {
            var it = asm.GetType("MoonFramework.Internal.Intake", true);
            var mt = asm.GetType("MoonFramework.Internal.Machines", true);
            Patch(h, it.GetMethod("Process", S), prefix: nameof(PassStart), finalizer: nameof(PassEnd));
            Patch(h, mt.GetMethod("Offer", S), prefix: nameof(OfferPrefix));
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

    static void PassStart() { Pass++; InPass = true; }
    static Exception PassEnd(Exception __exception) { InPass = false; return __exception; }

    static bool OfferPrefix(Machine m, ref bool __result)
    {
        if (m is QuantumOutputMachine o && !o.MayAccept()) { __result = false; return false; }
        return true;
    }
}
