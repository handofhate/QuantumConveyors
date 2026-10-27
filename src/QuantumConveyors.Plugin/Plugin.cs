using BepInEx;
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
    public const string PluginVersion = "0.1.0-dev";
    private static GameObject interactionHost;

    public override void Load()
    {
        ClassInjector.RegisterTypeInIl2Cpp<QuantumInteractionController>();
        EnsureInteractionHost();
        Moon.WorldLoaded += _ => EnsureInteractionHost();
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
        Log.LogInfo($"MoonFramework {FrameworkPlugin.Version} is available.");

        QuantumNetwork.Initialize(Log);

        Moon.RegisterBuilding(new BuildingSpec
        {
            Id = "quantumconveyors.output",
            Name = "Quantum Output",
            Description = "Sends resources into the shared wireless Quantum Conveyors network.",
            Category = "Logistics",
            Price = 0,
            Body = VanillaKinds.Splitter,
            SaveAs = VanillaKinds.Splitter,
            Tint = new Color(0.25f, 0.75f, 1f, 1f),
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
            Name = "Quantum Input",
            Description = "Receives resources from the shared wireless Quantum Conveyors network.",
            Category = "Logistics",
            Price = 0,
            Body = VanillaKinds.Splitter,
            SaveAs = VanillaKinds.Splitter,
            Tint = new Color(1f, 0.65f, 0.2f, 1f),
            Footprint = new Vector2(0.32f, 0.32f),
            Height = 1.05f,
            Ports = new[] { PortSpec.Out("output", new Vector3(0f, 0.85f, 0f), Vector3.forward) },
            Model = EndpointAssets.InputModel,
            Machine = () => new QuantumInputMachine(),
            OnView = view => { EnsureInteractionHost(); QuantumNetwork.BindUnidentifiedView(view, false); },
            ListAfter = VanillaKinds.Splitter,
        });

        Log.LogInfo("Registered Quantum Output and Quantum Input in the Logistics Building Tool.");
    }

    internal static void EnsureInteractionHost()
    {
        if (interactionHost != null && interactionHost.GetComponent<QuantumInteractionController>() != null) return;
        interactionHost = new GameObject("Quantum Conveyors Interaction");
        UnityEngine.Object.DontDestroyOnLoad(interactionHost);
        var controller = interactionHost.AddComponent<QuantumInteractionController>();
        QuantumNetwork.Diagnostic($"Interaction host created: {controller != null}");
    }
}
