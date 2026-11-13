using System;
using System.IO;
using System.Reflection;
using BepInEx;
using MoonFramework;
using UnityEngine;

namespace QuantumConveyors;

// The adapter's model, its textures and the build-menu pictures are carried inside QuantumConveyors.dll (Assets/ in the
// source). MoonFramework reads them from files, so they are unpacked once per version into
// BepInEx/cache/QuantumConveyors/<version>/ and handed to it by full path. Both adapters use the same gateway model,
// turned to face their port.
internal static class Assets
{
    internal static string Folder { get; private set; }

    internal static void Unpack()
    {
        Folder = Path.Combine(Paths.CachePath, "QuantumConveyors", Plugin.PluginVersion);
        Directory.CreateDirectory(Folder);
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames())
        {
            const string prefix = "Assets/";
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var path = Path.Combine(Folder, name.Substring(prefix.Length));
            using var src = asm.GetManifestResourceStream(name);
            if (File.Exists(path) && new FileInfo(path).Length == src.Length) continue;
            var tmp = path + ".tmp";
            using (var dst = File.Create(tmp)) src.CopyTo(dst);
            File.Move(tmp, path, true);
        }
    }

    internal static string PathOf(string name) => Path.Combine(Folder, name);

    internal static ModelSpec Model(bool sender) => new()
    {
        File = PathOf("quantum-adapter.obj"),
        CollisionFile = PathOf("quantum-endpoint-collision.obj"),
        Offset = new Vector3(0f, 0.85f, sender ? -1.05f : 1.05f),
        Turn = sender ? 0f : 180f,
        HideBody = true,
        KeepBodyColliders = false,
        Parts = new[] { "Indicator" },
    };

    // the picture matches the name players see (Quantum Input = the sender, unless Brain Fart Naming swaps the names)
    internal static string Picture(bool sender) =>
        PathOf(Plugin.DisplayNameFor(sender) == "Quantum Input" ? "quantum-input-preview.png" : "quantum-output-preview.png");
}
