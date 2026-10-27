using System;
using MoonCleaners;
using MoonFramework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace QuantumConveyors;

public sealed class QuantumInteractionController : MonoBehaviour
{
    private MoonWalker walker;
    private QuantumEndpointView? target;
    private bool menuOpen;
    private Rect windowRect = new(0f, 0f, 440f, 540f);

    public QuantumInteractionController(IntPtr pointer) : base(pointer) { }

    private void Update()
    {
        if (walker == null) walker = UnityEngine.Object.FindObjectOfType<MoonWalker>();
        var camera = walker?.viewCamera ?? Camera.main;
        if (camera == null) return;

        target = FindTarget(camera);
        if (!target.HasValue)
        {
            menuOpen = false;
            return;
        }

        var uiBlocked = walker != null && walker.UIBlocked;
        if (uiBlocked) return;
        if (!InteractPressed()) return;

        if (menuOpen)
        {
            menuOpen = false;
            return;
        }

        windowRect.x = (Screen.width - windowRect.width) * 0.5f;
        windowRect.y = (Screen.height - windowRect.height) * 0.5f;
        menuOpen = true;
    }

    private bool InteractPressed()
    {
        if (walker?.interact != null && walker.interact.WasPressedThisFrame()) return true;
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) return true;
        return false;
    }

    private static QuantumEndpointView? FindTarget(Camera camera)
    {
        var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        var bestPixels = 150f;
        QuantumEndpointView? best = null;

        foreach (var endpoint in QuantumNetwork.EndpointViews())
        {
            if (endpoint.View == null) continue;
            var worldPoint = endpoint.View.transform.TransformPoint(new Vector3(0f, 1.0f, 0f));
            var screenPoint = camera.WorldToScreenPoint(worldPoint);
            if (screenPoint.z <= 0f) continue;

            var guiPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            var pixels = Vector2.Distance(center, guiPoint);
            if (pixels > bestPixels || Vector3.Distance(camera.transform.position, worldPoint) > 5f) continue;
            bestPixels = pixels;
            best = endpoint;
        }

        return best;
    }

    private void OnGUI()
    {
        if (!target.HasValue) return;
        var endpoint = target.Value;
        if (!menuOpen)
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 110f, Screen.height * 0.62f, 220f, 32f), "[E]  Quantum Channel");
            return;
        }

        windowRect = GUI.Window(GetInstanceID(), windowRect, (Action<int>)DrawWindow, "Quantum Channel");
    }

    private void DrawWindow(int windowId)
    {
        if (!target.HasValue) return;
        var endpoint = target.Value;
        var summaries = QuantumNetwork.Summaries();
        var currentChannel = QuantumNetwork.ChannelFor(endpoint.Id);
        var assigned = !string.IsNullOrWhiteSpace(currentChannel);
        var y = 32f;

        GUI.Label(new Rect(18f, y, 400f, 24f), endpoint.IsOutput
            ? "This adapter sends into the wireless network."
            : "This adapter receives from the wireless network.");
        y += 28f;
        GUI.Label(new Rect(18f, y, 400f, 24f), assigned
            ? $"Current channel: {QuantumNetwork.ChannelName(currentChannel)}"
            : "Current channel: Unassigned");
        y += 34f;

        if (!Moon.IsAuthority)
        {
            GUI.Label(new Rect(18f, y, 400f, 46f), "Only the host can change Quantum Conveyor channels.");
            DrawCloseButton();
            return;
        }

        if (GUI.Button(new Rect(18f, y, 192f, 30f), assigned ? "Move to new channel" : "Create new channel"))
        {
            if (QuantumNetwork.CreateAndAssign(endpoint.Id, out _)) menuOpen = false;
        }

        if (assigned && GUI.Button(new Rect(220f, y, 192f, 30f), "Remove from channel"))
        {
            if (QuantumNetwork.UnassignEndpoint(endpoint.Id)) menuOpen = false;
        }

        y += 44f;
        GUI.Label(new Rect(18f, y, 400f, 24f), "Add this adapter to an existing channel:");
        y += 28f;

        if (summaries.Count == 0) GUI.Label(new Rect(18f, y, 400f, 24f), "No channels exist yet.");
        foreach (var summary in summaries)
        {
            if (y > windowRect.height - 78f) break;
            var label = $"{summary.Name}   ({summary.Outputs} out / {summary.Inputs} in)";
            if (GUI.Button(new Rect(18f, y, 394f, 30f), label))
            {
                if (QuantumNetwork.AssignEndpoint(endpoint.Id, summary.Id)) menuOpen = false;
            }
            y += 36f;
        }

        GUI.Label(new Rect(18f, windowRect.height - 70f, 400f, 24f), "Press E again to close. Connect this adapter to a conveyor.");
        DrawCloseButton();
    }

    private void DrawCloseButton()
    {
        if (GUI.Button(new Rect(18f, windowRect.height - 42f, 150f, 30f), "Close")) menuOpen = false;
        GUI.DragWindow(new Rect(0f, 0f, 440f, 28f));
    }
}
