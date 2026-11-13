using System;
using System.Collections.Generic;
using MoonFramework;
using UnityEngine;

namespace QuantumConveyors;

internal enum QuantumIndicatorState { WaitingForSetup, NoMatchingAdapter, ConveyorMissing, Ready, Active, OutputBeltFull }

// Indicator colours, from one updater, with no work per adapter per frame:
//  - an adapter whose state may have changed (channel, belt, a capsule sent or refused) is "touched" and redrawn next
//    frame; a redraw that comes out the same changes nothing;
//  - the timed states (active for 3 s after a capsule, belt full after 3 s of refusals) are run out by a queue of due
//    times, one entry per adapter (Due);
//  - receivers re-check their belt only when the world's belt count has changed (looked at every 0.5 s); a receiver
//    also finds out on its own when it places a capsule;
//  - every indicator uses one of a few shared materials (red, yellow, green; steady or blinking), so blinking is three
//    colour changes four times a second for all of them. 1.x gave every adapter its own material, a shadow-casting spot
//    light and a halo sprite, updated every frame.
internal static class Indicators
{
    const float BeltCheckEvery = 0.5f;
    static readonly List<QuantumInputMachine> receivers = new();
    static readonly HashSet<object> touched = new();
    static readonly List<object> drawing = new();
    static readonly Dictionary<object, (Renderer r, int look)> drawn = new();
    static readonly PriorityQueue<object, float> due = new();
    static readonly Dictionary<object, float> dueAt = new();                  // each adapter's one queued time
    static float nextBlink, nextBeltCheck; static bool blinkLit = true; static int beltCount = -1;
    static readonly Color[] Colors = { new(1f, 0.06f, 0.03f, 1f), new(1f, 0.78f, 0.04f, 1f), new(0.08f, 1f, 0.18f, 1f) };   // red, yellow, green

    // shared materials: (the model's own indicator material, look) -> ours; look = colour * 2 + (blinking ? 1 : 0)
    static readonly Dictionary<(IntPtr, int), Material> shared = new();
    static readonly Dictionary<IntPtr, Material> templateOf = new();          // one of ours -> the model's material it copies

    internal static void Register(object m) { if (m is QuantumInputMachine i) receivers.Add(i); touched.Add(m); }
    internal static void Unregister(object m)
    {
        if (m is QuantumInputMachine i) receivers.Remove(i);
        touched.Remove(m); drawn.Remove(m); dueAt.Remove(m);                  // a stale queue entry is skipped when it comes up
    }
    internal static void Touch(object m) { if (m != null) touched.Add(m); }
    internal static void Touch(Channel c) { foreach (var o in c.Senders) touched.Add(o); foreach (var i in c.Receivers) touched.Add(i); }

    // a timed state started: redraw when it runs out (the adapter is asked again then, in case it was extended)
    internal static void Due(object m)
    {
        float at = NextChange(m);
        if (at <= 0f || (dueAt.TryGetValue(m, out var q) && q <= at)) return;
        dueAt[m] = at; due.Enqueue(m, at);
    }

    static float NextChange(object m) => m is QuantumOutputMachine o ? o.NextChange() : ((QuantumInputMachine)m).NextChange();

    internal static string Text(QuantumIndicatorState s, bool sender) => s switch
    {
        QuantumIndicatorState.WaitingForSetup => "Waiting for setup",
        QuantumIndicatorState.NoMatchingAdapter => "No matching adapter",
        QuantumIndicatorState.ConveyorMissing => "Conveyor missing",
        QuantumIndicatorState.Active => sender ? "Sending" : "Receiving",
        QuantumIndicatorState.OutputBeltFull => "Output belt full",
        _ => sender ? "Ready to send" : "Ready to receive",
    };

    static QuantumIndicatorState StateOf(object m) => m is QuantumOutputMachine o ? o.State() : ((QuantumInputMachine)m).State();

    static int Look(QuantumIndicatorState s)
    {
        int colour = s switch
        {
            QuantumIndicatorState.NoMatchingAdapter or QuantumIndicatorState.OutputBeltFull => 1,
            QuantumIndicatorState.Ready or QuantumIndicatorState.Active => 2,
            _ => 0,
        };
        bool blinks = s is QuantumIndicatorState.ConveyorMissing or QuantumIndicatorState.NoMatchingAdapter or QuantumIndicatorState.OutputBeltFull;
        return colour * 2 + (blinks ? 1 : 0);
    }

    // once a frame, from the plugin's updater
    internal static void Frame()
    {
        float now = Time.unscaledTime;
        while (due.TryPeek(out var m, out var at) && at <= now)
        {
            due.Dequeue();
            if (!dueAt.TryGetValue(m, out var q) || q != at) continue;       // superseded, or the adapter is gone
            dueAt.Remove(m); touched.Add(m); Due(m);                          // extended meanwhile: queued again
        }
        if (now >= nextBeltCheck) { nextBeltCheck = now + BeltCheckEvery; CheckBelts(); }
        if (now >= nextBlink)
        {
            nextBlink = now + 0.25f; blinkLit = !blinkLit;
            foreach (var kv in shared) if ((kv.Key.Item2 & 1) == 1) Paint(kv.Value, Colors[kv.Key.Item2 >> 1], blinkLit);
        }
        if (touched.Count == 0) return;
        drawing.AddRange(touched); touched.Clear();
        foreach (var m in drawing) Draw(m);
        drawing.Clear();
    }

    // belts built or removed since the last look: every receiver checks its own (a rebuild that keeps the count is
    // noticed by the receiver when it next places a capsule)
    static void CheckBelts()
    {
        var belts = Moon.World?.conveyors; if (belts == null) return;
        int n = belts.Count; if (n == beltCount) return;
        beltCount = n;
        foreach (var r in receivers)
            if (r.RecheckAttached()) { var c = QuantumNetwork.ChannelOf(r.ChannelId); if (c != null) Touch(c); else touched.Add(r); }
    }

    static void Draw(object m)
    {
        try
        {
            int look = Look(StateOf(m));
            if (drawn.TryGetValue(m, out var d) && d.look == look && d.r != null) return;
            var r = d.r;
            if (r == null)
            {
                var part = m is QuantumOutputMachine o ? o.IndicatorPart() : ((QuantumInputMachine)m).IndicatorPart();
                if (part == null) return;                              // no view yet: drawn when it gets one (OnView)
                r = part.GetComponentInChildren<Renderer>(true); if (r == null) return;
            }
            var current = r.sharedMaterial; if (current == null) return;
            var template = templateOf.TryGetValue(current.Pointer, out var t) ? t : current;
            r.sharedMaterial = Shared(template, look);
            drawn[m] = (r, look);
        }
        catch (Exception e) { QuantumNetwork.Log?.LogWarning("indicator: " + e.Message); drawn.Remove(m); }
    }

    static Material Shared(Material template, int look)
    {
        var key = (template.Pointer, look);
        if (shared.TryGetValue(key, out var mat) && mat != null) return mat;
        mat = new Material(template) { name = "Quantum indicator " + look };
        mat.EnableKeyword("_EMISSION");
        Paint(mat, Colors[look >> 1], (look & 1) == 0 || blinkLit);
        shared[key] = mat; templateOf[mat.Pointer] = template;
        return mat;
    }

    static void Paint(Material mat, Color baseC, bool lit)
    {
        if (mat == null) return;
        Color c = lit ? baseC : baseC * 0.12f;
        mat.color = c; mat.SetColor("_Color", c); mat.SetColor("_BaseColor", c);
        mat.SetColor("_EmissionColor", c * (lit ? 2.2f : 0.05f));
    }
}
