using UnityEngine;
using UnityEngine.Rendering;

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
    private static Texture2D haloTexture;

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

        // Keep the glow parented to the actual rendered indicator mesh. A
        // light attached to the adapter wrapper can retain an old world-space
        // position when the framework finishes moving the imported model.
        var glowTransform = renderer.transform.Find("QuantumIndicatorGlow");
        if (glowTransform == null)
        {
            var glowObject = new GameObject("QuantumIndicatorGlow");
            glowTransform = glowObject.transform;
            glowTransform.SetParent(renderer.transform, false);
        }

        var oldDirectLight = part.GetComponent<Light>();
        if (oldDirectLight != null && oldDirectLight.gameObject != glowTransform.gameObject)
            UnityEngine.Object.Destroy(oldDirectLight);

        var light = glowTransform.GetComponent<Light>() ?? glowTransform.gameObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = 1.25f;
        light.intensity = visible ? 1.5f : 0.05f;
        light.shadows = LightShadows.Hard;
        light.shadowStrength = 1f;
        light.shadowBias = 0.005f;
        light.shadowNormalBias = 0.01f;
        light.shadowNearPlane = 0.02f;
        // Keep the added glow at the modeled LED mesh itself. The physical
        // Indicator mesh is the source of truth; do not move that mesh.
        var indicatorCenter = renderer.bounds.center;
        glowTransform.localPosition = renderer.transform.InverseTransformPoint(indicatorCenter);

        var haloTransform = renderer.transform.Find("QuantumIndicatorHalo");
        if (haloTransform == null)
        {
            var haloObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            haloObject.name = "QuantumIndicatorHalo";
            haloTransform = haloObject.transform;
            haloTransform.SetParent(renderer.transform, false);
            var haloCollider = haloObject.GetComponent<Collider>();
            if (haloCollider != null) UnityEngine.Object.Destroy(haloCollider);
            haloTransform.localScale = Vector3.one * 0.52f;
        }

        haloTransform.localPosition = renderer.transform.InverseTransformPoint(indicatorCenter);
        var camera = Camera.main;
        if (camera != null)
            haloTransform.rotation = Quaternion.LookRotation(haloTransform.position - camera.transform.position);

        var haloRenderer = haloTransform.GetComponent<Renderer>();
        if (haloRenderer != null)
        {
            var haloMaterial = haloRenderer.material;
            haloMaterial.shader = HaloShader();
            haloMaterial.mainTexture = HaloTexture();
            var haloColor = new Color(color.r, color.g, color.b, visible ? 0.42f : 0.015f);
            haloMaterial.color = haloColor;
            haloMaterial.SetColor("_Color", haloColor);
            haloMaterial.SetColor("_BaseColor", haloColor);
            haloMaterial.EnableKeyword("_EMISSION");
            haloMaterial.SetColor("_EmissionColor", new Color(color.r, color.g, color.b, 1f) * (visible ? 2.5f : 0.02f));
            if (haloMaterial.HasProperty("_ZWrite")) haloMaterial.SetInt("_ZWrite", 0);
            if (haloMaterial.HasProperty("_Cull")) haloMaterial.SetInt("_Cull", 0);
            haloMaterial.renderQueue = 3000;
        }
        return true;
    }

    private static Shader HaloShader()
    {
        return Shader.Find("Legacy Shaders/Particles/Alpha Blended") ??
            Shader.Find("Particles/Standard Unlit") ??
            Shader.Find("Unlit/Transparent") ??
            Shader.Find("Unlit/Color");
    }

    private static Texture2D HaloTexture()
    {
        if (haloTexture != null) return haloTexture;

        const int size = 32;
        haloTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "QuantumIndicatorHaloTexture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        var pixels = new Color[size * size];
        var center = (size - 1) * 0.5f;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dx = (x - center) / center;
            var dy = (y - center) / center;
            var distance = Mathf.Sqrt(dx * dx + dy * dy);
            var alpha = Mathf.Clamp01(1f - distance);
            alpha *= alpha;
            pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
        }

        haloTexture.SetPixels(pixels);
        haloTexture.Apply(false, true);
        return haloTexture;
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
