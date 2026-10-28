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
    private static Texture2D haloTexture;
    private static Sprite haloSprite;

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
        var indicatorCenter = renderer.bounds.center;
        var bodyRenderer = FindGatewayBodyRenderer(renderer);
        var outward = bodyRenderer == null
            ? renderer.transform.forward
            : (indicatorCenter - bodyRenderer.bounds.center).normalized;
        if (outward.sqrMagnitude < 0.001f) outward = renderer.transform.forward;

        light.type = LightType.Spot;
        light.color = color;
        light.range = 1.25f;
        light.intensity = visible ? 1.5f : 0.05f;
        light.spotAngle = 95f;
        light.innerSpotAngle = 35f;
        light.shadows = LightShadows.Hard;
        light.shadowStrength = 1f;
        light.shadowBias = 0.005f;
        light.shadowNormalBias = 0.01f;
        light.shadowNearPlane = 0.02f;
        // Keep the added glow at the modeled LED mesh itself. The physical
        // Indicator mesh is the source of truth; do not move that mesh.
        glowTransform.localPosition = renderer.transform.InverseTransformPoint(indicatorCenter);
        glowTransform.rotation = Quaternion.LookRotation(outward);

        var haloTransform = renderer.transform.Find("QuantumIndicatorHalo");
        var haloSpriteRenderer = haloTransform == null ? null : haloTransform.GetComponent<SpriteRenderer>();
        if (haloSpriteRenderer == null)
        {
            if (haloTransform != null)
                UnityEngine.Object.Destroy(haloTransform.gameObject);

            var haloObject = new GameObject("QuantumIndicatorHalo");
            haloObject.name = "QuantumIndicatorHalo";
            haloTransform = haloObject.transform;
            haloTransform.SetParent(renderer.transform, false);
            haloTransform.localScale = Vector3.one * 0.52f;
            haloSpriteRenderer = haloObject.AddComponent<SpriteRenderer>();
            haloSpriteRenderer.sprite = HaloSprite();
        }

        haloTransform.localPosition = renderer.transform.InverseTransformPoint(indicatorCenter);
        var camera = Camera.main;
        if (camera != null)
            haloTransform.rotation = Quaternion.LookRotation(haloTransform.position - camera.transform.position);

        var haloColor = new Color(color.r, color.g, color.b, visible ? 0.42f : 0.015f);
        var cameraInsideGateway = camera != null && bodyRenderer != null &&
            bodyRenderer.bounds.Contains(camera.transform.position);
        haloSpriteRenderer.enabled = !cameraInsideGateway;
        if (cameraInsideGateway)
            haloColor.a = 0f;
        haloSpriteRenderer.sprite = HaloSprite();
        haloSpriteRenderer.color = haloColor;
        return true;
    }

    private static Renderer FindGatewayBodyRenderer(Renderer indicator)
    {
        var root = indicator.transform.root;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.gameObject.name == "GatewayBody") return renderer;
        }

        return null;
    }

    private static Texture2D HaloTexture()
    {
        if (haloTexture != null) return haloTexture;

        const int size = 64;
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
            var alpha = 1f - Mathf.SmoothStep(0.55f, 1f, distance);
            alpha *= 0.82f;
            pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
        }

        haloTexture.SetPixels(pixels);
        haloTexture.Apply(false, false);
        return haloTexture;
    }

    private static Sprite HaloSprite()
    {
        if (haloSprite != null) return haloSprite;
        var texture = HaloTexture();
        haloSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), texture.width);
        haloSprite.name = "QuantumIndicatorHaloSprite";
        return haloSprite;
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
