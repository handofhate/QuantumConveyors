using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using MoonCleaners;
using MoonFramework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace QuantumConveyors;

// The channel menu: while it is open the player stands still and their input is off; all of it comes back on close.
internal static partial class Menu
{
    static BaseInputModule prevModule; static bool prevModuleOn;
    static MoonWalker walker; static bool prevBlocked; static bool prevWalkerEnabled;
    static InputActionAsset playerInput; static bool prevInputOn;
    static readonly List<InputActionAsset> fallbackInputAssets = new();
    static CursorLockMode prevLock; static bool prevVisible;


    static void Freeze()
    {
        prevLock = Cursor.lockState; prevVisible = Cursor.visible;
        var found = FindWalker();
        if (found != null)
        {
            prevBlocked = found.UIBlocked;
            prevWalkerEnabled = found.enabled;
            playerInput = found.inputTemplate;
            if (playerInput != null)
            {
                prevInputOn = playerInput.enabled;
                playerInput.Disable();
            }
            else
            {
                DisableFallbackInputAssets();
            }
            found.UIBlocked = true;
        }
        else
        {
            QuantumNetwork.Log?.LogWarning("menu: player controller not found; disabling live input assets as fallback");
            DisableFallbackInputAssets();
        }
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }
    static void Unfreeze()
    {
        try
        {
            var found = FindWalker();
            if (found != null)
            {
                found.UIBlocked = prevBlocked;
                found.enabled = prevWalkerEnabled;
            }
        }
        catch (Exception e) { QuantumNetwork.Log?.LogWarning("menu: player input restore skipped: " + e.Message); }
        try
        {
            if (playerInput != null && prevInputOn) playerInput.Enable();
        }
        catch (Exception e) { QuantumNetwork.Log?.LogWarning("menu: primary player input restore skipped: " + e.Message); }
        playerInput = null;
        foreach (var asset in fallbackInputAssets)
        {
            try { if (asset != null) asset.Enable(); }
            catch (Exception e) { QuantumNetwork.Log?.LogWarning("menu: fallback input restore skipped: " + e.Message); }
        }
        fallbackInputAssets.Clear();
        Cursor.lockState = prevLock; Cursor.visible = prevVisible;
    }

    // The lookups below take what the game hands back (a Component or Object wrapper) with TryCast: "as MoonWalker" on
    // it is null unless the interop happens to have the right wrapper cached, which quietly skipped these paths.
    static MoonWalker FindWalker()
    {
        if (walker != null) return walker;

        // Prefer direct typed lookups. The PR's previous Il2CppType/TryCast
        // chain could fail with "Method unstripping failed" on the preview
        // build before it ever reached the input-disable step.
        var cam = Camera.main ?? Camera.current;
        if (cam != null)
        {
            var found = cam.GetComponent(Il2CppType.Of<MoonWalker>());
            if (found == null) found = cam.GetComponentInParent(Il2CppType.Of<MoonWalker>());
            if (found == null) found = cam.transform.root.GetComponentInChildren(Il2CppType.Of<MoonWalker>());
            walker = found?.TryCast<MoonWalker>();
            if (walker != null) return walker;
        }

        try
        {
            var globalWalkers = UnityEngine.Object.FindObjectsOfType<MoonWalker>(true);
            foreach (var candidate in globalWalkers)
            {
                if (candidate == null) continue;
                walker = candidate;
                return walker;
            }
        }
        catch (Exception e) { QuantumNetwork.Log?.LogWarning("menu: global player search failed: " + e.Message); }

        for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            var scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                var found = root.GetComponentInChildren(Il2CppType.Of<MoonWalker>());
                walker = found?.TryCast<MoonWalker>();
                if (walker != null) return walker;

                var production = root.GetComponentInChildren(Il2CppType.Of<MoonProductionInteraction>())?.TryCast<MoonProductionInteraction>();
                if (production != null && production.walker != null)
                {
                    walker = production.walker;
                    return walker;
                }
            }
        }

        var persistentObjects = Resources.FindObjectsOfTypeAll(Il2CppType.Of<MoonWalker>());
        foreach (var item in persistentObjects)
        {
            walker = item?.TryCast<MoonWalker>();
            if (walker != null && walker.gameObject.scene.IsValid()) return walker;
        }
        return walker;
    }

    static void DisableFallbackInputAssets()
    {
        var disabledNames = new List<string>();
        var assets = Resources.FindObjectsOfTypeAll(Il2CppType.Of<InputActionAsset>());
        foreach (var item in assets)
        {
            var asset = item?.TryCast<InputActionAsset>();
            if (asset == null || !asset.enabled || IsUiInputAsset(asset)) continue;
            asset.Disable(); fallbackInputAssets.Add(asset); disabledNames.Add(asset.name);
        }
        try
        {
            var globalAssets = UnityEngine.Object.FindObjectsOfType<InputActionAsset>(true);
            foreach (var asset in globalAssets)
            {
                if (asset == null || !asset.enabled || IsUiInputAsset(asset) || fallbackInputAssets.Contains(asset)) continue;
                asset.Disable(); fallbackInputAssets.Add(asset); disabledNames.Add(asset.name);
            }
        }
        catch (Exception e) { QuantumNetwork.Log?.LogWarning("menu: global input asset search failed: " + e.Message); }
        QuantumNetwork.Log?.LogInfo("menu: fallback input freeze disabled " + fallbackInputAssets.Count + " asset(s): " + string.Join(", ", disabledNames));
    }

    static bool IsUiInputAsset(InputActionAsset asset)
    {
        var name = asset.name ?? string.Empty;
        return name.IndexOf("ui", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("menu", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
