using System;
using MoonCleaners;
using MoonFramework;
using UnityEngine;

namespace QuantumConveyors;

internal sealed class QuantumEndpointInteraction : MonoBehaviour
{
    private MoonActionPromptCard prompt;
    private MoonWalker walker;
    private Rect windowRect = new(0f, 0f, 420f, 520f);
    private int endpointId;
    private bool isOutput;
    private bool menuOpen;
    private string currentChannel = string.Empty;

    public QuantumEndpointInteraction(IntPtr pointer) : base(pointer) { }

    public void Configure(int id, bool output)
    {
        endpointId = id;
        isOutput = output;
        currentChannel = QuantumNetwork.ChannelFor(endpointId);
    }

    private void Update()
    {
        if (!QuantumNetwork.IsEndpointAttached(endpointId))
        {
            prompt?.Hide();
            menuOpen = false;
            return;
        }

        if (walker == null) walker = UnityEngine.Object.FindObjectOfType<MoonWalker>();
        if (walker == null || walker.viewCamera == null || walker.interact == null) return;

        var aimed = IsAimed();
        if (aimed && !menuOpen && !walker.UIBlocked)
        {
            prompt ??= MoonActionPromptCard.Create(transform, "QuantumConveyorsPrompt", 600);
            prompt.Show("E", "Quantum Channel", true, 1f, false);
        }
        else
        {
            prompt?.Hide();
        }

        if (!walker.interact.WasPressedThisFrame()) return;
        if (menuOpen)
        {
            menuOpen = false;
            return;
        }

        if (aimed && !walker.UIBlocked)
        {
            currentChannel = QuantumNetwork.ChannelFor(endpointId);
            windowRect.x = (Screen.width - windowRect.width) * 0.5f;
            windowRect.y = (Screen.height - windowRect.height) * 0.5f;
            menuOpen = true;
        }
    }

    private bool IsAimed()
    {
        var camera = walker.viewCamera;
        if (camera == null) return false;
        var ray = new Ray(camera.transform.position, camera.transform.forward);
        if (!Physics.Raycast(ray, out var hit, 4f)) return false;
        var hitTransform = hit.collider == null ? null : hit.collider.transform;
        return hitTransform != null && (hitTransform == transform || hitTransform.IsChildOf(transform));
    }

    private void OnGUI()
    {
        if (!menuOpen) return;
        windowRect = GUI.Window(GetInstanceID(), windowRect, (Action<int>)DrawWindow, "Quantum Channel");
    }

    private void DrawWindow(int windowId)
    {
        var summaries = QuantumNetwork.Summaries();
        var y = 32f;
        GUI.Label(new Rect(18f, y, 380f, 24f), isOutput ? "This adapter sends into the wireless network." : "This adapter receives from the wireless network.");
        y += 28f;

        var assigned = !string.IsNullOrWhiteSpace(currentChannel);
        GUI.Label(new Rect(18f, y, 380f, 24f), assigned
            ? $"Current channel: {QuantumNetwork.ChannelName(currentChannel)}"
            : "Current channel: Unassigned");
        y += 34f;

        if (!Moon.IsAuthority)
        {
            GUI.Label(new Rect(18f, y, 380f, 46f), "Only the host can change Quantum Conveyor channels.");
            if (GUI.Button(new Rect(18f, windowRect.height - 48f, 150f, 30f), "Close")) menuOpen = false;
            GUI.DragWindow(new Rect(0f, 0f, 420f, 28f));
            return;
        }

        if (GUI.Button(new Rect(18f, y, 184f, 30f), assigned ? "Move to new channel" : "Create new channel"))
        {
            if (QuantumNetwork.CreateAndAssign(endpointId, out var channelId))
            {
                currentChannel = channelId;
                menuOpen = false;
            }
        }

        if (assigned && GUI.Button(new Rect(212f, y, 184f, 30f), "Remove from channel"))
        {
            if (QuantumNetwork.UnassignEndpoint(endpointId))
            {
                currentChannel = string.Empty;
                menuOpen = false;
            }
        }

        y += 44f;
        GUI.Label(new Rect(18f, y, 380f, 24f), "Add this adapter to an existing channel:");
        y += 28f;

        if (summaries.Count == 0)
            GUI.Label(new Rect(18f, y, 380f, 24f), "No channels exist yet.");

        foreach (var summary in summaries)
        {
            if (y > windowRect.height - 78f) break;
            var label = $"{summary.Name}   ({summary.Outputs} out / {summary.Inputs} in)";
            if (GUI.Button(new Rect(18f, y, 378f, 30f), label))
            {
                if (QuantumNetwork.AssignEndpoint(endpointId, summary.Id))
                {
                    currentChannel = summary.Id;
                    menuOpen = false;
                }
            }
            y += 36f;
        }

        GUI.Label(new Rect(18f, windowRect.height - 70f, 380f, 24f), "Press E again to close. Connect this adapter to a conveyor.");
        if (GUI.Button(new Rect(18f, windowRect.height - 42f, 150f, 30f), "Close")) menuOpen = false;
        GUI.DragWindow(new Rect(0f, 0f, 420f, 28f));
    }
}
