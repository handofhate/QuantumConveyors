using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace QuantumConveyors;

/// <summary>
/// Keyboard interaction for the adapter's stock Moon Framework machine panel.
/// IMGUI is deliberately not used: the preview build does not unstrip Unity's
/// IMGUI methods and would spam the BepInEx console with trampoline errors.
/// </summary>
public sealed class QuantumInteractionController : MonoBehaviour
{
    private QuantumEndpointView? target;
    private bool updateReported;
    private int lastViewCount = -1;
    private bool? cameraAvailable;
    private bool updateErrorReported;

    public QuantumInteractionController(IntPtr pointer) : base(pointer) { }

    public void Awake() => QuantumNetwork.Diagnostic("Interaction host Awake");

    public void OnDestroy() => QuantumNetwork.Diagnostic("Interaction host OnDestroy");

    public void OnDisable() => QuantumNetwork.Diagnostic("Interaction host OnDisable");

    public void Update()
    {
        try
        {
            if (!updateReported)
            {
                updateReported = true;
                QuantumNetwork.Diagnostic("Interaction host Update is running");
            }

            var camera = ActiveCamera();
            if (camera == null)
            {
                if (cameraAvailable != false)
                {
                    cameraAvailable = false;
                    QuantumNetwork.Diagnostic("No active camera found");
                }
                return;
            }

            if (cameraAvailable != true)
            {
                cameraAvailable = true;
                QuantumNetwork.Diagnostic($"Active camera found: {camera.name}");
            }

            var endpointViews = QuantumNetwork.EndpointViews();
            if (endpointViews.Count != lastViewCount)
            {
                lastViewCount = endpointViews.Count;
                QuantumNetwork.Diagnostic($"Interaction host sees {lastViewCount} endpoint view(s)");
            }

            target = FindTarget(camera, endpointViews);
            if (!target.HasValue || !InteractPressed()) return;

            var interaction = target.Value;
            if (IsRemovePressed())
            {
                QuantumNetwork.UnassignEndpoint(interaction.Id);
                QuantumNetwork.Diagnostic($"Keyboard interaction: removed endpoint {interaction.Id} from its channel");
                return;
            }

            if (IsNewChannelPressed())
            {
                if (QuantumNetwork.CreateAndAssign(interaction.Id, out var channelId))
                    QuantumNetwork.Diagnostic($"Keyboard interaction: endpoint {interaction.Id} assigned to new channel {channelId}");
                return;
            }

            if (QuantumNetwork.AssignNextChannel(interaction.Id))
                QuantumNetwork.Diagnostic($"Keyboard interaction: endpoint {interaction.Id} advanced to the next channel");
        }
        catch (Exception exception)
        {
            if (!updateErrorReported)
            {
                updateErrorReported = true;
                QuantumNetwork.Diagnostic($"Interaction Update exception: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private static bool InteractPressed()
    {
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
    }

    private static bool IsRemovePressed()
    {
        var keyboard = Keyboard.current;
        return keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
    }

    private static bool IsNewChannelPressed()
    {
        var keyboard = Keyboard.current;
        return keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
    }

    private static Camera ActiveCamera()
    {
        if (Camera.main != null) return Camera.main;
        return Camera.current;
    }

    private static QuantumEndpointView? FindTarget(Camera camera, IReadOnlyList<QuantumEndpointView> endpoints)
    {
        var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        var bestPixels = 300f;
        QuantumEndpointView? best = null;

        foreach (var endpoint in endpoints)
        {
            if (endpoint.View == null) continue;
            var worldPoint = endpoint.View.transform.TransformPoint(new Vector3(0f, 1.0f, 0f));
            var toTarget = worldPoint - camera.transform.position;
            if (toTarget.magnitude > 6f || Vector3.Angle(camera.transform.forward, toTarget) > 18f) continue;
            var screenPoint = camera.WorldToScreenPoint(worldPoint);
            if (screenPoint.z <= 0f) continue;

            var guiPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            var pixels = Vector2.Distance(center, guiPoint);
            if (pixels > bestPixels) continue;
            bestPixels = pixels;
            best = endpoint;
        }

        return best;
    }
}
