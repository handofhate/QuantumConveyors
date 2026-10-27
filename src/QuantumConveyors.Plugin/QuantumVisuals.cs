using UnityEngine;

namespace QuantumConveyors;

internal enum QuantumIndicatorState
{
    WaitingForSetup,
    NoMatchingAdapter,
    ConveyorMissing,
    Ready,
    Active,
    OutputBeltFull,
}

internal static class QuantumVisuals
{
    private static readonly Color Red = new(1f, 0.06f, 0.03f, 1f);
    private static readonly Color Yellow = new(1f, 0.78f, 0.04f, 1f);
    private static readonly Color Green = new(0.08f, 1f, 0.18f, 1f);
    private static readonly Color InteractionDebugColor = new(1f, 0.05f, 1f, 1f);

    public static bool TrySetIndicator(GameObject part, QuantumIndicatorState state)
    {
        if (part == null) return false;
        var renderer = part.GetComponent<Renderer>();
        if (renderer == null) return false;

        var flashing = state is QuantumIndicatorState.ConveyorMissing or
            QuantumIndicatorState.Active or QuantumIndicatorState.OutputBeltFull;
        var visible = !flashing || Mathf.FloorToInt(Time.unscaledTime * 4f) % 2 == 0;
        var color = state switch
        {
            QuantumIndicatorState.NoMatchingAdapter or QuantumIndicatorState.OutputBeltFull => Yellow,
            QuantumIndicatorState.Ready or QuantumIndicatorState.Active => Green,
            _ => Red,
        };
        var displayColor = visible ? color : color * 0.12f;
        var material = renderer.material;
        material.color = displayColor;
        material.SetColor("_Color", displayColor);
        material.SetColor("_BaseColor", displayColor);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", displayColor * (visible ? 2.2f : 0.05f));

        var light = part.GetComponent<Light>() ?? part.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = 0.75f;
        light.intensity = visible ? 1.3f : 0.05f;
        light.transform.localPosition = new Vector3(0f, 0.11f, 0f);
        return true;
    }

    public static void DrawInteractionBounds(GameObject view)
    {
        if (view == null) return;

        var old = view.transform.Find("Quantum Interaction Bounds (debug)");
        if (old != null) UnityEngine.Object.Destroy(old.gameObject);

        var colliders = view.GetComponentsInChildren<Collider>(true);
        if (colliders == null || colliders.Length == 0) return;

        var root = new GameObject("Quantum Interaction Bounds (debug)");
        root.transform.SetParent(view.transform, false);
        var material = CreateDebugMaterial();
        if (material == null) return;

        foreach (var collider in colliders)
        {
            if (collider == null || collider.transform.IsChildOf(root.transform)) continue;
            DrawBounds(root.transform, collider.bounds, material);
        }
    }

    private static Material CreateDebugMaterial()
    {
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        if (shader == null) return null;
        var material = new Material(shader) { color = InteractionDebugColor };
        material.SetColor("_Color", InteractionDebugColor);
        return material;
    }

    private static void DrawBounds(Transform parent, Bounds bounds, Material material)
    {
        var min = bounds.min;
        var max = bounds.max;
        var points = new[]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(max.x, max.y, max.z),
            new Vector3(min.x, max.y, max.z),
        };

        AddLine(parent, material, points[0], points[1]);
        AddLine(parent, material, points[1], points[2]);
        AddLine(parent, material, points[2], points[3]);
        AddLine(parent, material, points[3], points[0]);
        AddLine(parent, material, points[4], points[5]);
        AddLine(parent, material, points[5], points[6]);
        AddLine(parent, material, points[6], points[7]);
        AddLine(parent, material, points[7], points[4]);
        AddLine(parent, material, points[0], points[4]);
        AddLine(parent, material, points[1], points[5]);
        AddLine(parent, material, points[2], points[6]);
        AddLine(parent, material, points[3], points[7]);
    }

    private static void AddLine(Transform parent, Material material, Vector3 start, Vector3 end)
    {
        var lineObject = new GameObject("Interaction bound");
        lineObject.transform.SetParent(parent, false);
        var line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        line.startWidth = 0.018f;
        line.endWidth = 0.018f;
        line.startColor = InteractionDebugColor;
        line.endColor = InteractionDebugColor;
        line.material = material;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
    }
}
