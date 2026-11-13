using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using MoonFramework;
using UnityEngine;

namespace QuantumConveyors;

// Quantum Conveyors 2: the same two adapters, saved data and crew messages as 1.x, so a world built with 1.x loads as it
// is. The work is done differently (see README "How it works"):
//  - nothing per adapter per frame: capsules move when MoonFramework offers one, and adapters are left out of the
//    framework's Tick/Animate/power loops (FrameworkPatches.cs);
//  - each channel keeps its own lists of adapters (Network.cs), and a channel whose receivers are all full is not
//    asked again in the same hand-off pass;
//  - indicators share a handful of materials and change only when an adapter's state changes (Indicators.cs);
//  - the E menu looks for an adapter only when E is pressed (Menu.cs), and has channel groups (Groups.cs);
//  - one file: the model, textures and pictures are inside the DLL (Assets.cs).
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(FrameworkPlugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "tylerun.moonsweepers.quantumconveyors";
    public const string PluginName = "Quantum Conveyors";
    public const string PluginVersion = "2.0.0";

    static ConfigEntry<int> adapterPrice;
    static ConfigEntry<bool> diagnostics, brainFart;

    internal static int AdapterPrice => Math.Max(1, adapterPrice?.Value ?? 5);
    internal static bool DiagnosticsEnabled => diagnostics?.Value ?? false;
    internal static bool BrainFartNaming => brainFart?.Value ?? false;

    // The machine classes keep their historical names for save and network compatibility: the "output" machine takes
    // capsules off a belt and sends them, so players see it as the Quantum Input (unless Brain Fart Naming swaps them).
    internal static string DisplayNameFor(bool sends) => BrainFartNaming == sends ? "Quantum Output" : "Quantum Input";
    internal static string DisplayCounts(int senders, int receivers) =>
        BrainFartNaming ? $"{senders} out / {receivers} in" : $"{senders} in / {receivers} out";

    public override void Load()
    {
        adapterPrice = Config.Bind("Quantum Conveyors", "Adapter Price", 5,
            new ConfigDescription("Build price for each Quantum adapter in dollars.", new AcceptableValueRange<int>(1, 1000)));
        var legacy = Config.Bind("Quantum Conveyors", "Enable Diagnostics", false, "Renamed to Diagnostics.");   // 1.1.0's name
        Config.Remove(legacy.Definition);
        diagnostics = Config.Bind("Quantum Conveyors", "Diagnostics", legacy.Value, "Enable verbose troubleshooting logs.");
        brainFart = Config.Bind("Quantum Conveyors", "Brain Fart Naming", false,
            "This swaps the names of the adapters. Proper semantics is now optional.");

        Assets.Unpack();
        QuantumNetwork.Initialize(Log);
        ClassInjector.RegisterTypeInIl2Cpp<Driver>();
        var host = new GameObject("Quantum Conveyors"); UnityEngine.Object.DontDestroyOnLoad(host); host.AddComponent<Driver>();

        Register(true, "quantumconveyors.output",
            "Takes resources from the attached conveyor and sends them through the shared wireless Quantum Conveyors network.");
        Register(false, "quantumconveyors.input",
            "Receives resources from the shared wireless Quantum Conveyors network and places them on the attached conveyor.");
        FrameworkPatches.Install(new Harmony(PluginGuid), Log);
        Log.LogInfo($"{PluginName} {PluginVersion} loaded: {DisplayNameFor(true)} and {DisplayNameFor(false)} registered.");
    }

    // The ids are 1.x's, so saved adapters load as these. A sender has an input port, a receiver an output port, both on
    // top of the body where a belt end meets them.
    static void Register(bool sender, string id, string description) => Moon.RegisterBuilding(new BuildingSpec
    {
        Id = id, Name = DisplayNameFor(sender), Description = description, Category = "Logistics", Price = AdapterPrice,
        Body = VanillaKinds.Splitter, SaveAs = VanillaKinds.Splitter, ListAfter = VanillaKinds.Splitter,
        Picture = Assets.Picture(sender), Tint = Color.white,                   // white: no framework tint on the picture
        Footprint = new Vector2(0.32f, 0.32f), Height = 1.05f,
        Ports = new[] { sender ? PortSpec.In("input", new Vector3(0f, 0.85f, 0f), Vector3.forward)
                               : PortSpec.Out("output", new Vector3(0f, 0.85f, 0f), Vector3.forward) },
        Model = Assets.Model(sender),
        Machine = () => sender ? new QuantumOutputMachine() : new QuantumInputMachine(),
    });
}

// one updater for everything per-frame: indicators (only what changed, timed states and belt changes), saved pending capsules,
// and the channel menu
public sealed class Driver : MonoBehaviour
{
    public Driver(IntPtr ptr) : base(ptr) { }
    bool failed;
    void Update()
    {
        if (failed) return;
        try { Indicators.Frame(); QuantumNetwork.DeliverPending(); Menu.Update(); }
        catch (Exception e) { QuantumNetwork.Log?.LogError("Quantum Conveyors update: " + e); failed = true; }
    }
    void LateUpdate() { try { Menu.LateUpdate(); } catch { } }
}
