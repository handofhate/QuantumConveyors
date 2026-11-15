using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using MoonCleaners;
using MoonFramework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace QuantumConveyors;

// The channel menu: generated sprites (rounded corners, gradients, fades, icons) and the UI pieces it is built from.
internal static partial class Menu
{
    // a texture for a generated sprite: smooth, its edges not repeated
    static Texture2D NewTexture(int width, int height, string name) =>
        new Texture2D(width, height, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

    // a 9-sliced sprite: corners of the radius stay round at any size
    static Sprite Sliced(Texture2D texture, int radius) =>
        Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 200f, 0,
            SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));

    static void EnsureVisualAssets()
    {
        if (channelGradient != null) return;
        // Generate these at 2x resolution. The menu is rendered at a reduced
        // scale, so the extra source pixels make the anti-aliased corners and
        // circular help buttons noticeably smoother.
        roundedSprite = MakeRoundedSprite(256, 256, 24, "Quantum Rounded Surface");
        buttonSprite = MakeRoundedSprite(256, 256, 12, "Quantum Button Surface");
        circleSprite = MakeRoundedSprite(256, 256, 128, "Quantum Circle Surface");
        channelGradient = MakeGradient(new Color(1f, 0.478f, 0.180f, 0.22f), new Color(1f, 0.478f, 0.180f, 0f), "Quantum Channel Gradient");
        folderGradient = MakeGradient(new Color(0.110f, 0.133f, 0.196f, 0.24f), new Color(0.110f, 0.133f, 0.196f, 0f), "Quantum Folder Gradient");
        folderIcon = MakeIcon(true, new Color(0.110f, 0.133f, 0.196f, 1f), "Quantum Folder Icon");
        topFade = MakeVerticalFade(true, "Quantum Top Fade");
        bottomFade = MakeVerticalFade(false, "Quantum Bottom Fade");
    }

    static Sprite MakeGradient(Color left, Color right, string name)
    {
        const int width = 256, height = 128, radius = 24;
        var texture = NewTexture(width, height, name);
        var pixels = new Color[width * height];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var alpha = RoundedAlpha(x + 0.5f, y + 0.5f, width, height, radius);
            var colour = Color.Lerp(left, right, x / (float)(width - 1)); colour.a *= alpha;
            pixels[y * width + x] = colour;
        }
        texture.SetPixels(pixels); texture.Apply(false, true);
        return Sliced(texture, radius);
    }

    static Sprite MakeRoundedSprite(int width, int height, int radius, string name)
    {
        var texture = NewTexture(width, height, name);
        var pixels = new Color[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++) pixels[y * width + x] = new Color(1f, 1f, 1f, RoundedAlpha(x + 0.5f, y + 0.5f, width, height, radius));
        texture.SetPixels(pixels); texture.Apply(false, true);
        return Sliced(texture, radius);
    }

    static float RoundedAlpha(float x, float y, float width, float height, float radius)
    {
        var cx = Mathf.Clamp(x, radius, width - radius);
        var cy = Mathf.Clamp(y, radius, height - radius);
        var distance = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
        return Mathf.Clamp01(radius + 0.75f - distance);
    }

    static Sprite MakeVerticalFade(bool top, string name)
    {
        const int height = 64;
        var texture = new Texture2D(1, height, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[height];
        for (var y = 0; y < height; y++)
        {
            var t = y / (float)(height - 1);
            var alpha = top ? t : 1f - t;
            pixels[y] = new Color(1f, 1f, 1f, alpha);
        }
        texture.SetPixels(pixels); texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, 1, height), new Vector2(0.5f, 0.5f), 200f);
    }

    static Image AddFade(Transform parent, string name, Sprite sprite, Vector2 position)
    {
        var r = Rect(name, parent, new Vector2(620, 32), position, null);
        var image = r.gameObject.AddComponent<Image>();
        image.sprite = sprite; image.type = Image.Type.Sliced; image.color = PanelColor; image.raycastTarget = false;
        image.transform.SetAsLastSibling(); image.gameObject.SetActive(false);
        return image;
    }

    static void UpdateFadeVisibility(float maxScroll)
    {
        var canScroll = maxScroll > 0.01f;
        if (topFadeImage != null) topFadeImage.gameObject.SetActive(canScroll && scroll > 0.5f);
        if (bottomFadeImage != null) bottomFadeImage.gameObject.SetActive(canScroll && scroll < maxScroll - 0.5f);
    }

    static Sprite MakeIcon(bool folder, Color color, string name)
    {
        const int size = 48;
        var texture = NewTexture(size, size, name);
        var pixels = new Color[size * size];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;
        void Put(int x, int y, Color value) { if (x >= 0 && x < size && y >= 0 && y < size) pixels[y * size + x] = value; }
        if (folder)
        {
            for (var y = 12; y <= 36; y++) for (var x = 6; x <= 40; x++) Put(x, y, color);
            for (var y = 36; y <= 40; y++) for (var x = 10; x <= 22; x++) Put(x, y, color);
            for (var x = 8; x <= 20; x++) Put(x, 38, color);
        }
        else
        {
            // A compact radio/wireless symbol: a center point with two upward arcs.
            for (var y = 34; y <= 40; y++) for (var x = 20; x <= 26; x++) Put(x, y, color);
            for (var y = 24; y <= 36; y++) for (var x = 10; x <= 36; x++)
            {
                var dx = x - 23.5f; var dy = y - 36f; var distance = dx * dx + dy * dy;
                if (distance >= 100f && distance <= 144f) Put(x, y, color);
            }
            for (var y = 16; y <= 34; y++) for (var x = 4; x <= 44; x++)
            {
                var dx = x - 23.5f; var dy = y - 36f; var distance = dx * dx + dy * dy;
                if (distance >= 324f && distance <= 400f) Put(x, y, color);
            }
        }
        texture.SetPixels(pixels); texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 200f);
    }

    // ---------------------------------------------------------------- UI pieces
    static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 pos, Color? bg)
    {
        var go = new GameObject(name); var r = go.AddComponent<RectTransform>(); r.SetParent(parent, false);
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f); r.sizeDelta = size; r.anchoredPosition = pos;
        if (bg.HasValue)
        {
            var image = go.AddComponent<Image>(); image.sprite = roundedSprite; image.type = Image.Type.Sliced; image.color = bg.Value;
        }
        return r;
    }
    static Text Label(Transform parent, string text, Vector2 pos, Vector2 size, int fontSize, Color color, TextAnchor align)
    {
        var t = Rect("Text", parent, size, pos, null).gameObject.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); t.text = text; t.fontSize = fontSize; t.color = color; t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
        return t;
    }
    static InputField Field(Transform parent, Vector2 pos, Vector2 size)
    {
        var r = Rect("NameField", parent, size, pos, ButtonColor); var f = r.gameObject.AddComponent<InputField>();
        var surface = r.GetComponent<Image>(); if (surface != null) { surface.sprite = buttonSprite; surface.type = Image.Type.Sliced; }
        f.targetGraphic = r.GetComponent<Image>();
        f.textComponent = Label(r, string.Empty, Vector2.zero, size - new Vector2(16, 4), 18, ChannelText, TextAnchor.MiddleLeft);
        f.textComponent.rectTransform.anchoredPosition = new Vector2(8, 0); f.characterLimit = 24;
        return f;
    }
    static UnityEngine.UI.Button Button(Transform parent, string label, Vector2 pos, Vector2 size, Action action, bool accent = false)
    {
        bool close = label == "×";
        var r = Rect("Button", parent, size, pos, accent ? Accent : (close ? new Color(0, 0, 0, 0) : ButtonColor));
        var surface = r.GetComponent<Image>();
        if (surface != null) { surface.sprite = buttonSprite; surface.type = Image.Type.Sliced; }
        var b = r.gameObject.AddComponent<UnityEngine.UI.Button>(); b.targetGraphic = r.GetComponent<Image>();
        UnityAction call = action; b.onClick.AddListener(call);
        var colour = accent ? AccentText : close ? HeaderText : ChannelText;
        Label(r, label, Vector2.zero, size - new Vector2(8, 4), close ? 28 : 16, colour, TextAnchor.MiddleCenter);
        return b;
    }
}
