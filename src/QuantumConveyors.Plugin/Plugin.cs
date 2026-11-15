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
//  - one file: the model, textures and pictures are inside the DLL (Assets.cs);
//  - it can be reloaded in place while the game runs (Conveyors, below), and still ships as a normal BepInEx plugin.
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(FrameworkPlugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "tylerun.moonsweepers.quantumconveyors";
    public const string PluginName = "Quantum Conveyors";
    public const string PluginVersion = "2.1.4";

    internal static ConfigEntry<int> adapterPrice;
    internal static ConfigEntry<bool> diagnostics, brainFart;

    internal static int AdapterPrice => Math.Max(1, adapterPrice?.Value ?? 5);
    internal static bool DiagnosticsEnabled => diagnostics?.Value ?? false;
    internal static bool BrainFartNaming => brainFart?.Value ?? false;

    // The machine classes keep their historical names for save and network compatibility: the "output" machine takes
    // capsules off a belt and sends them, so players see it as the Quantum Input (unless Brain Fart Naming swaps them).
    internal static string DisplayNameFor(bool sends) => BrainFartNaming == sends ? "Quantum Output" : "Quantum Input";
    internal static string DisplayCounts(int senders, int receivers) =>
        BrainFartNaming ? $"{senders} out / {receivers} in" : $"{senders} in / {receivers} out";

    internal static Conveyors Running;

    // The normal BepInEx way in: Conveyors runs from here, driven every frame by a Driver of our own.
    public override void Load()
    {
        Conveyors.Config = Config; Conveyors.Log = Log;
        Conveyors.Boot();
        Running = new Conveyors(); Running.Start(null);
        ClassInjector.RegisterTypeInIl2Cpp<Driver>();
        var host = new GameObject("Quantum Conveyors"); UnityEngine.Object.DontDestroyOnLoad(host); host.AddComponent<Driver>();
    }

    // HTTP requests a mod that serves HTTP passes on to plugins (Http.cs): /quantum/... answered, anything else isn't
    // ours (null). Without such a mod nothing calls this.
    public string HandleHttp(string method, string path, System.Collections.Specialized.NameValueCollection query, string body) => Http.Handle(method, path, query);
    public string[] HttpHelp() => Http.Help;
    public string UpdateFeed() => "github:handofhate/QuantumConveyors";
}

// Everything Quantum Conveyors does, started and stopped as a whole. BepInEx starts it through Plugin. A loader that
// reloads plugins in place while the game runs can use it directly: Boot() once at game start (it registers the two
// adapter buildings, which MoonFramework only accepts then), Start(state) after loading, Update() and LateUpdate() every
// frame, and Stop() before dropping a copy. On a reload the adapters' machines are swapped: Stop() saves each live
// adapter machine (OnSave), stops it and gives its saved data back to MoonFramework; the new copy's Start() puts its own
// machine factory on the two buildings and has MoonFramework create the new machines at once from that data. Channels and
// groups live in the world's saved data and are read again; the flow counters start again.
public sealed class Conveyors
{
    internal static ConfigFile Config;                                    // set by Plugin; otherwise opened here
    internal static BepInEx.Logging.ManualLogSource Log;
    const string SenderId = "quantumconveyors.output", ReceiverId = "quantumconveyors.input";
    BepInEx.Logging.ManualLogSource ownLog;
    Harmony harmony;

    static void Settings()
    {
        var cfg = Config ?? new ConfigFile(System.IO.Path.Combine(Paths.ConfigPath, Plugin.PluginGuid + ".cfg"), true);
        Plugin.adapterPrice = cfg.Bind("Quantum Conveyors", "Adapter Price", 5,
            new ConfigDescription("Build price for each Quantum adapter in dollars.", new AcceptableValueRange<int>(1, 1000)));
        var legacy = cfg.Bind("Quantum Conveyors", "Enable Diagnostics", false, "Renamed to Diagnostics.");   // 1.1.0's name
        cfg.Remove(legacy.Definition);
        Plugin.diagnostics = cfg.Bind("Quantum Conveyors", "Diagnostics", legacy.Value, "Enable verbose troubleshooting logs.");
        Plugin.brainFart = cfg.Bind("Quantum Conveyors", "Brain Fart Naming", false,
            "This swaps the names of the adapters. Proper semantics is now optional.");
    }

    // once, at game start: the two adapter buildings (their machines come from whichever copy is running, see Start)
    public static void Boot()
    {
        Settings();
        Assets.Unpack();
        Register(true, SenderId,
            "Takes resources from the attached conveyor and sends them through the shared wireless Quantum Conveyors network.");
        Register(false, ReceiverId,
            "Receives resources from the shared wireless Quantum Conveyors network and places them on the attached conveyor.");
    }

    public void Start(string state)
    {
        if (Log == null) { ownLog = BepInEx.Logging.Logger.CreateLogSource(Plugin.PluginName); Log = ownLog; }
        Settings();
        Assets.Unpack();
        QuantumNetwork.Initialize(Log);
        // Remove the inert handoff machines left by a previous disable before installing this copy's factories. This
        // also makes Enable work after Disable, where Lunar Link starts us with a null state.
        Framework.HandOver(SenderId, ReceiverId);
        // this copy's machines from now on
        foreach (var (id, sender) in new[] { (SenderId, true), (ReceiverId, false) })
        {
            var b = Moon.BuildingOf(id);
            if (b != null) b.Spec.Machine = sender ? () => new QuantumOutputMachine() : () => new QuantumInputMachine();
        }
        harmony = new Harmony(Plugin.PluginGuid);
        FrameworkPatches.Install(harmony, Log);
        Framework.SyncNow();                             // reload, enable, or first hot load: rebuild machines now
        Log.LogInfo($"{Plugin.PluginName} {Plugin.PluginVersion} {(state == null ? "loaded" : "reloaded")}: {Plugin.DisplayNameFor(true)} and {Plugin.DisplayNameFor(false)}.");
    }

    public string Stop()
    {
        harmony?.UnpatchSelf(); harmony = null;
        // HandOver alone restores the saved records. If this copy is being disabled rather than replaced, the next
        // framework sync would otherwise reuse this copy's factory. Install an inert one and sync it immediately.
        Framework.UseDisabledAdapterMachines();
        int n = Framework.HandOver(SenderId, ReceiverId);
        Framework.SyncNow();
        Framework.RemoveCrewHandlers(typeof(Conveyors).Assembly);
        Menu.Stop();
        Log?.LogInfo($"{Plugin.PluginName} stopped: {n} adapters handed over");
        if (ownLog != null) { BepInEx.Logging.Logger.Sources.Remove(ownLog); ownLog = null; Log = null; }
        return "reload";
    }

    public string UpdateFeed() => "github:handofhate/QuantumConveyors";

    // indicators (only what changed, timed states and belt changes), saved pending capsules, and the channel menu
    bool failed;
    public void Update()
    {
        if (failed) return;
        try { Indicators.Frame(); QuantumNetwork.DeliverPending(); Menu.Update(); }
        catch (Exception e) { QuantumNetwork.Log?.LogError("Quantum Conveyors update: " + e); failed = true; }
    }
    public void LateUpdate() { try { Menu.LateUpdate(); } catch { } }

    public string HandleHttp(string method, string path, System.Collections.Specialized.NameValueCollection query, string body) => Http.Handle(method, path, query);
    public string[] HttpHelp() => Http.Help;

    // The ids are 1.x's, so saved adapters load as these. A sender has an input port, a receiver an output port, both on
    // top of the body where a belt end meets them.
    static void Register(bool sender, string id, string description) => Moon.RegisterBuilding(new BuildingSpec
    {
        Id = id, Name = Plugin.DisplayNameFor(sender), Description = description, Category = "Logistics", Price = Plugin.AdapterPrice,
        Body = VanillaKinds.Splitter, SaveAs = VanillaKinds.Splitter, ListAfter = VanillaKinds.Splitter,
        Picture = Assets.Picture(sender), Tint = Color.white,                   // white: no framework tint on the picture
        Footprint = new Vector2(0.32f, 0.32f), Height = 1.05f,
        Ports = new[] { sender ? PortSpec.In("input", new Vector3(0f, 0.85f, 0f), Vector3.forward)
                               : PortSpec.Out("output", new Vector3(0f, 0.85f, 0f), Vector3.forward) },
        Model = Assets.Model(sender),
        Machine = () => sender ? new QuantumOutputMachine() : new QuantumInputMachine(),
    });
}

// The BepInEx build's frame driver: calls Conveyors every frame. (A loader that drives Conveyors itself doesn't use it.)
public sealed class Driver : MonoBehaviour
{
    public Driver(IntPtr ptr) : base(ptr) { }
    void Update() => Plugin.Running?.Update();
    void LateUpdate() => Plugin.Running?.LateUpdate();
}
