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

        var material = renderer.material;
        material.color = paired ? Paired : Unpaired;
        return true;
    }
}
