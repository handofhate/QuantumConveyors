using UnityEngine;

namespace QuantumConveyors;

internal static class QuantumVisuals
{
    private static readonly Color Unpaired = new(1f, 0.06f, 0.03f, 1f);
    private static readonly Color Paired = new(0.08f, 1f, 0.18f, 1f);

    public static bool TrySetIndicator(GameObject part, bool paired)
    {
        if (part == null) return false;
        var renderer = part.GetComponent<Renderer>();
        if (renderer == null) return false;

        var color = paired ? Paired : Unpaired;
        var material = renderer.material;
        material.color = color;
        material.SetColor("_Color", color);
        material.SetColor("_BaseColor", color);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color * (paired ? 1.8f : 0.8f));

        var light = part.GetComponent<Light>() ?? part.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = 0.45f;
        light.intensity = paired ? 0.8f : 0.45f;
        return true;
    }
}
