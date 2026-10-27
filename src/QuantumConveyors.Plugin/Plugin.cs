using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace QuantumConveyors;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "tylerun.moonsweepers.quantumconveyors";
    public const string PluginName = "Quantum Conveyors";
    public const string PluginVersion = "0.1.0-dev";

    public override void Load()
    {
        Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
        Log.LogInfo("Prototype stage: runtime diagnostics only; no gameplay changes are enabled.");

        DumpRelevantTypes();
    }

    private void DumpRelevantTypes()
    {
        var matches = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(GetTypesSafely)
            .Where(type =>
                type.FullName?.Contains("MoonConveyor", StringComparison.OrdinalIgnoreCase) == true ||
                type.FullName?.Contains("MoonBuild", StringComparison.OrdinalIgnoreCase) == true ||
                type.FullName?.Contains("MoonMachine", StringComparison.OrdinalIgnoreCase) == true ||
                type.FullName?.Contains("MoonDepot", StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(type => type.FullName)
            .ToArray();

        Log.LogInfo($"Diagnostic type scan found {matches.Length} relevant loaded types.");

        foreach (var type in matches)
        {
            Log.LogInfo($"TYPE {type.FullName}");

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                Log.LogInfo($"  FIELD {field.FieldType.FullName} {field.Name}");
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                         .Where(method => !method.IsSpecialName)
                         .OrderBy(method => method.Name))
            {
                var parameters = string.Join(", ", method.GetParameters().Select(parameter => $"{parameter.ParameterType.FullName} {parameter.Name}"));
                Log.LogInfo($"  METHOD {method.ReturnType.FullName} {method.Name}({parameters})");
            }
        }
    }

    private static IEnumerable<Type> GetTypesSafely(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null)!;
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }
}
