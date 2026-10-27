using System;
using MoonCleaners;
using MoonFramework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace QuantumConveyors;

public sealed class QuantumEndpointInteraction : MonoBehaviour
{
    private MoonActionPromptCard prompt;
    private MoonWalker walker;
    private BoxCollider interactionCollider;
    private Rect windowRect = new(0f, 0f, 420f, 520f);
    private int endpointId;
    private bool isOutput;
    private bool menuOpen;
    private bool aimed;
    private string currentChannel = string.Empty;

    public QuantumEndpointInteraction(IntPtr pointer) : base(pointer) { }

    public static QuantumEndpointInteraction Attach(GameObject view)
    {
        if (view == null) return null;
        var interaction = view.GetComponent<QuantumEndpointInteraction>() ?? view.AddComponent<QuantumEndpointInteraction>();
        interaction.EnsureInteractionCollider();
        QuantumNetwork.ConfigureInteraction(view, interaction);
        return interaction;
    }

    public void Configure(int id, bool output)
    {
        endpointId = id;
        isOutput = output;
        currentChannel = QuantumNetwork.ChannelFor(endpointId);
        EnsureInteractionCollider();
    }

    public void Update()
    {
        if (walker == null) walker = UnityEngine.Object.FindObjectOfType<MoonWalker>();
        var camera = walker?.viewCamera ?? Camera.main;
        if (camera == null) return;

        aimed = IsAimed(camera);
        var uiBlocked = walker != null && walker.UIBlocked;
        if (aimed && !menuOpen && !uiBlocked)
        {
            prompt ??= MoonActionPromptCard.Create(transform, "QuantumConveyorsPrompt", 600);
            prompt.Show("E", "Quantum Channel", true, 1f, false);
        }
        else
        {
            prompt?.Hide();
        }

        if (!InteractPressed()) return;
        if (menuOpen)
        {
            menuOpen = false;
            return;
        }

        if (aimed && !uiBlocked)
        {
            currentChannel = QuantumNetwork.ChannelFor(endpointId);
            windowRect.x = (Screen.width - windowRect.width) * 0.5f;
            windowRect.y = (Screen.height - windowRect.height) * 0.5f;
            menuOpen = true;
        }
    }

    private bool InteractPressed()
    {
        if (walker?.interact != null && walker.interact.WasPressedThisFrame()) return true;
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
    }

    private bool IsAimed(Camera camera)
    {
        var ray = new Ray(camera.transform.position, camera.transform.forward);
        if (Physics.Raycast(ray, out var hit, 4f))
        {
            var hitTransform = hit.collider == null ? null : hit.collider.transform;
            if (hitTransform != null && (hitTransform == transform || hitTransform.IsChildOf(transform))) return true;
        }

        // Some game surfaces can win the raycast even when the player is clearly aiming at this tiny port.
        // Keep interaction forgiving without making the endpoint interactable from across the room.
        var target = transform.TransformPoint(new Vector3(0f, 1.0f, 0f));
        var toTarget = target - camera.transform.position;
        return toTarget.magnitude <= 4f && Vector3.Angle(camera.transform.forward, toTarget) <= 10f;
    }

    private void EnsureInteractionCollider()
    {
        if (interactionCollider == null) interactionCollider = gameObject.GetComponent<BoxCollider>();
        if (interactionCollider == null) interactionCollider = gameObject.AddComponent<BoxCollider>();
        interactionCollider.center = new Vector3(0f, 1.0f, 0f);
        interactionCollider.size = new Vector3(0.55f, 0.38f, 0.55f);
        interactionCollider.isTrigger = false;
    }

    public void OnGUI()
    {
        if (aimed && !menuOpen) GUI.Label(new Rect(Screen.width * 0.5f - 110f, Screen.height * 0.62f, 220f, 30f), "[E]  Quantum Channel");
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
