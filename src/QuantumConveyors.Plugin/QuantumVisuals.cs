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

    public static bool TrySetIndicator(GameObject part, QuantumIndicatorState state)
    {
        if (part == null) return false;
        var renderer = FindIndicatorRenderer(part);
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
        // Place the point light at the modeled indicator, then lift it slightly
        // above and outward from the surface so the glow is visible beside the
        // modeled LED rather than appearing buried inside it.
        light.transform.position = renderer.bounds.center +
            part.transform.up * 0.06f + part.transform.forward * 0.06f;
        return true;
    }

    private static Renderer FindIndicatorRenderer(GameObject part)
    {
        var ownRenderer = part.GetComponent<Renderer>();
        if (string.Equals(part.name, "Indicator", System.StringComparison.OrdinalIgnoreCase) && ownRenderer != null)
            return ownRenderer;

        foreach (var childRenderer in part.GetComponentsInChildren<Renderer>(true))
        {
            if (string.Equals(childRenderer.gameObject.name, "Indicator", System.StringComparison.OrdinalIgnoreCase))
                return childRenderer;
        }

        return ownRenderer;
    }

}
