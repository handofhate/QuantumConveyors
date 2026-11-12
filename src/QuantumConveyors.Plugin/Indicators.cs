using System;
using System.Collections.Generic;
using UnityEngine;

namespace QuantumConveyors;

internal enum QuantumIndicatorState { WaitingForSetup, NoMatchingAdapter, ConveyorMissing, Ready, Active, OutputBeltFull }

// Indicator colours, from one updater instead of every adapter every frame:
//  - an adapter whose state may have changed (channel, belt, activity) is "touched" and redrawn next frame;
//  - a round robin looks at RoundRobinPerFrame adapters a frame: re-checks a receiver's belt and lets the timed states
//    (active for 3 s, belt full after 3 s) run out;
//  - every indicator uses one of a few shared materials (red, yellow, green; steady or blinking), so an adapter is
//    only touched when its state changes, and blinking is three colour changes four times a second for all of them.
//    1.x gave every adapter its own material, a shadow-casting spot light and a halo sprite, updated every frame.
internal static class Indicators
{
    const int RoundRobinPerFrame = 25;
    static readonly List<object> all = new();
    static readonly HashSet<object> touched = new();
    static readonly Dictionary<object, (Renderer r, int look)> drawn = new();
    static int rr; static float nextBlink; static bool blinkLit = true;
    static readonly Color[] Colors = { new(1f, 0.06f, 0.03f, 1f), new(1f, 0.78f, 0.04f, 1f), new(0.08f, 1f, 0.18f, 1f) };   // red, yellow, green

    // shared materials: (the model's own indicator material, look) -> ours; look = colour * 2 + (blinking ? 1 : 0)
    static readonly Dictionary<(IntPtr, int), Material> shared = new();
    static readonly Dictionary<IntPtr, Material> templateOf = new();          // one of ours -> the model's material it copies

    internal static void Register(object m) { all.Add(m); touched.Add(m); }
    internal static void Unregister(object m) { all.Remove(m); touched.Remove(m); drawn.Remove(m); }
    internal static void Touch(object m) { if (m != null) touched.Add(m); }
    internal static void Touch(Channel c) { foreach (var o in c.Senders) touched.Add(o); foreach (var i in c.Receivers) touched.Add(i); }

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
        int n = all.Count;
        for (int k = 0; k < Math.Min(RoundRobinPerFrame, n); k++)
        {
            if (rr >= all.Count) rr = 0;
            var m = all[rr++];
            if (m is QuantumInputMachine i && i.RecheckAttached())
            {
                var c = QuantumNetwork.ChannelOf(i.ChannelId); if (c != null) Touch(c);
            }
            touched.Add(m);                                   // timed states run out here
        }
        if (Time.unscaledTime >= nextBlink)
        {
            nextBlink = Time.unscaledTime + 0.25f; blinkLit = !blinkLit;
            foreach (var kv in shared) if ((kv.Key.Item2 & 1) == 1) Paint(kv.Value, Colors[kv.Key.Item2 >> 1], blinkLit);
        }
        if (touched.Count == 0) return;
        var list = new List<object>(touched); touched.Clear();
        foreach (var m in list) Draw(m);
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
