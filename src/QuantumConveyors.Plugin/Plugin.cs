using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using MoonFramework;
using UnityEngine;

namespace QuantumConveyors;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(FrameworkPlugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "tylerun.moonsweepers.quantumconveyors";
    public const string PluginName = "Quantum Conveyors";
    public const string PluginVersion = "1.1.2";
    private static ConfigFile config;
    private static ConfigEntry<int> adapterPrice;
    private static ConfigEntry<bool> diagnosticsEnabled;
    private static ConfigEntry<bool> brainFartNaming;
    private static GameObject interactionHost;

    internal static int AdapterPrice => Math.Max(1, adapterPrice?.Value ?? 5);
    internal static bool DiagnosticsEnabled => diagnosticsEnabled?.Value ?? false;
    internal static bool BrainFartNaming => brainFartNaming?.Value ?? false;

    // The machine classes retain their historical internal names for save and
    // network compatibility. These helpers provide the player-facing names.
    internal static string DisplayNameFor(bool sendsToWirelessNetwork)
    {
        if (BrainFartNaming)
            return sendsToWirelessNetwork ? "Quantum Output" : "Quantum Input";
        return sendsToWirelessNetwork ? "Quantum Input" : "Quantum Output";
    }

    internal static string DisplayCounts(int internalOutputs, int internalInputs)
    {
        return BrainFartNaming
            ? $"{internalOutputs} outputs / {internalInputs} inputs"
            : $"{internalOutputs} inputs / {internalInputs} outputs";
    }

    public override void Load()
    {
        config = Config;
        adapterPrice = Config.Bind(
            "Quantum Conveyors",
            "Adapter Price",
            5,
            new ConfigDescription(
                "Build price for each Quantum adapter in dollars.",
                new AcceptableValueRange<int>(1, 1000)));
        diagnosticsEnabled = BindDiagnosticsSetting();
        brainFartNaming = Config.Bind(
            "Quantum Conveyors",
            "Brain Fart Naming",
            false,
            "This swaps the names of the adapters. Proper semantics is now optional.");

        ClassInjector.RegisterTypeInIl2Cpp<QuantumInteractionController>();
        EnsureInteractionHost();
        Moon.WorldLoaded += _ => EnsureInteractionHost();
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
        Log.LogInfo($"MoonFramework {FrameworkPlugin.Version} is available.");

        QuantumNetwork.Initialize(Log);

        Moon.RegisterBuilding(new BuildingSpec
        {
            Id = "quantumconveyors.output",
            Name = DisplayNameFor(true),
            Description = "Takes resources from the attached conveyor and sends them through the shared wireless Quantum Conveyors network.",
            Category = "Logistics",
            Price = AdapterPrice,
            Body = VanillaKinds.Splitter,
            SaveAs = VanillaKinds.Splitter,
            // Use the actual gateway preview instead of the stock splitter card.
            // White disables the framework's fallback tinting for the custom PNG.
            // The default player-facing name for this internal sender is
            // Quantum Input, so its preview must follow the displayed name.
            Picture = BrainFartNaming ? "quantum-output-preview.png" : "quantum-input-preview.png",
            Tint = Color.white,
            Footprint = new Vector2(0.32f, 0.32f),
            Height = 1.05f,
            Ports = new[] { PortSpec.In("input", new Vector3(0f, 0.85f, 0f), Vector3.forward) },
            Model = EndpointAssets.OutputModel,
            Machine = () => new QuantumOutputMachine(),
            OnView = view => { EnsureInteractionHost(); QuantumNetwork.BindUnidentifiedView(view, true); },
            ListAfter = VanillaKinds.Splitter,
        });

        Moon.RegisterBuilding(new BuildingSpec
        {
            Id = "quantumconveyors.input",
            Name = DisplayNameFor(false),
            Description = "Receives resources from the shared wireless Quantum Conveyors network and places them on the attached conveyor.",
            Category = "Logistics",
            Price = AdapterPrice,
            Body = VanillaKinds.Splitter,
            SaveAs = VanillaKinds.Splitter,
            // Use the actual gateway preview instead of the stock splitter card.
            // White disables the framework's fallback tinting for the custom PNG.
            // The default player-facing name for this internal receiver is
            // Quantum Output, so its preview must follow the displayed name.
            Picture = BrainFartNaming ? "quantum-input-preview.png" : "quantum-output-preview.png",
            Tint = Color.white,
            Footprint = new Vector2(0.32f, 0.32f),
            Height = 1.05f,
            Ports = new[] { PortSpec.Out("output", new Vector3(0f, 0.85f, 0f), Vector3.forward) },
            Model = EndpointAssets.InputModel,
            Machine = () => new QuantumInputMachine(),
            OnView = view => { EnsureInteractionHost(); QuantumNetwork.BindUnidentifiedView(view, false); },
            ListAfter = VanillaKinds.Splitter,
        });

        Log.LogInfo($"Registered {DisplayNameFor(true)} and {DisplayNameFor(false)} in the Logistics Building Tool.");
    }

    internal static void EnsureInteractionHost()
    {
        if (interactionHost != null && interactionHost.GetComponent<QuantumInteractionController>() != null) return;
        interactionHost = new GameObject("Quantum Conveyors Interaction");
        UnityEngine.Object.DontDestroyOnLoad(interactionHost);
        var controller = interactionHost.AddComponent<QuantumInteractionController>();
        QuantumNetwork.Diagnostic($"Interaction host created: {controller != null}");
    }

    private static ConfigEntry<bool> BindDiagnosticsSetting()
    {
        const string section = "Quantum Conveyors";
        var legacy = config.Bind(section, "Enable Diagnostics", false,
            "Legacy setting migrated to Diagnostics.");
        var value = legacy.Value;
        config.Remove(new ConfigDefinition(section, "Enable Diagnostics"));
        return config.Bind(section, "Diagnostics", value,
            "Enable verbose troubleshooting logs.");
    }
}
