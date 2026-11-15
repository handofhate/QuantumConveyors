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

// The channel menu (E on an adapter): groups (New Group; Open, Rename, Delete), New Channel; per channel
// Assign/Unassign, Rename (its short name), Delete; E, Esc or right-click
// closes it. 1.x searched every adapter (and the scene for robots) every frame; this looks only when E is pressed, and
// only at adapters within 6 m of the camera that nothing stands in front of.
// Its parts: Menu.List.cs (the channel list), Menu.Drag.cs (dragging rows), Menu.Input.cs (the player held still while
// it is open), Menu.Parts.cs (sprites and UI pieces).
internal static partial class Menu
{
    static readonly Color PanelColor = new(0.937f, 0.922f, 0.882f, 1f), HeaderText = new(0.729f, 0.710f, 0.686f, 1f),
        RowColor = new(0.165f, 0.165f, 0.196f, 1f), ButtonColor = new(0.286f, 0.286f, 0.294f, 1f), Accent = new(1f, 0.478f, 0.180f, 1f),
        ChannelText = new(0.949f, 0.929f, 0.894f, 1f), AccentText = new(0.110f, 0.133f, 0.196f, 1f);
    static GameObject canvas; static GraphicRaycaster raycaster; static RectTransform content;
    static int endpointId; static string editing = string.Empty; static float scroll;
    static UnityEngine.UI.Text title;
    static readonly HashSet<string> expandedGroups = new(StringComparer.Ordinal);
    static bool unassignedExpanded, ungroupedExpanded;
    static string focusEditingChannel = string.Empty;
    static string pendingNewChannel, pendingNewGroup;
    static bool pendingNewChannelWasUnassignedExpanded;
    static InputField pendingFocusField;
    static Sprite roundedSprite, buttonSprite, circleSprite, channelGradient, folderGradient, folderIcon, topFade, bottomFade;
    static Image topFadeImage, bottomFadeImage;
    static RectTransform viewport, transition, dragGhost;
    static CanvasGroup transitionGroup;
    static Text dragGhostLabel;
    static UnityEngine.UI.Button autoGroupButton;
    static RectTransform autoHelp, autoTooltip, headerHelp, headerTooltip;
    static CanvasGroup autoTooltipGroup, headerTooltipGroup;
    static bool animateNextRefresh;
    static readonly Dictionary<string, bool> animatedArrows = new(StringComparer.Ordinal);
    sealed class RowAnimation
    {
        internal RectTransform Row; internal Vector2 From; internal Vector2 To; internal float Until;
    }
    sealed class ArrowAnimation
    {
        internal RectTransform Arrow; internal float From; internal float To; internal float Until;
    }
    static readonly List<RowAnimation> rowAnimations = new();
    static readonly List<ArrowAnimation> arrowAnimations = new();
    static float transitionFadeUntil;
    static float scrollAnimationFrom, scrollAnimationTo, scrollAnimationUntil, scrollAnimationMax;
    static string scrollAnchorKey;
    static string revealChannelId, revealGroupId;
    static RectTransform revealRow;

    // a check that failed is logged the first time only (they run on every E press; an exception there just means
    // "not blocked" / "not a robot")
    static readonly HashSet<string> warned = new(StringComparer.Ordinal);
    static void WarnOnce(string what, Exception e)
    {
        if (warned.Add(what)) QuantumNetwork.Log?.LogWarning("menu: " + what + " failed (only logged once): " + e.Message);
    }

    internal static bool Open => canvas != null;

    internal static void Update()
    {
        var kb = Keyboard.current; var mouse = Mouse.current;
        if (canvas != null)
        {
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            UpdateTooltips(mouse);
            if (mouse != null && mouse.rightButton.wasPressedThisFrame) { Close(); return; }
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { Close(); return; }
            if (kb != null && kb.eKey.wasPressedThisFrame && !RenameFocused()) { Close(); return; }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) BeginDragCandidate(mouse.position.ReadValue());
            UpdateDrag(mouse);
            Click(); Scroll();
            return;
        }
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;
        var cam = Camera.main ?? Camera.current; if (cam == null) return;
        var hit = Target(cam);
        if (hit.HasValue && !AimingAtRobot(cam)) OpenFor(hit.Value);
    }

    internal static void LateUpdate()
    {
        if (canvas != null) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        for (var i = rowAnimations.Count - 1; i >= 0; i--)
        {
            var animation = rowAnimations[i];
            var t = Mathf.Clamp01(1f - (animation.Until - Time.unscaledTime) / 0.22f);
            animation.Row.anchoredPosition = Vector2.LerpUnclamped(animation.From, animation.To, Smooth(t));
            if (t >= 1f) rowAnimations.RemoveAt(i);
        }
        for (var i = arrowAnimations.Count - 1; i >= 0; i--)
        {
            var animation = arrowAnimations[i];
            var t = Mathf.Clamp01(1f - (animation.Until - Time.unscaledTime) / 0.22f);
            animation.Arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(animation.From, animation.To, Smooth(t)));
            if (t >= 1f) arrowAnimations.RemoveAt(i);
        }
        if (scrollAnimationUntil > 0f && content != null)
        {
            var t = Mathf.Clamp01(1f - (scrollAnimationUntil - Time.unscaledTime) / 0.22f);
            scroll = Mathf.LerpUnclamped(scrollAnimationFrom, scrollAnimationTo, Smooth(t));
            content.anchoredPosition = new Vector2(0f, scroll);
            if (transition != null) transition.anchoredPosition = new Vector2(0f, scroll);
            UpdateFadeVisibility(scrollAnimationMax);
            if (t >= 1f) scrollAnimationUntil = 0f;
        }
        if (transitionGroup != null && transitionFadeUntil > 0f)
        {
            var t = Mathf.Clamp01(1f - (transitionFadeUntil - Time.unscaledTime) / 0.06f);
            transitionGroup.alpha = 1f - Smooth(t);
            if (t >= 1f)
            {
                for (var i = transition.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(transition.GetChild(i).gameObject);
                transitionGroup.alpha = 1f; transitionFadeUntil = 0f;
            }
        }
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);

    static string CountPhrase(int count, string singular) => count + " " + (count == 1 ? singular : singular + "s");

    static void UpdateTooltips(Mouse mouse)
    {
        if (mouse == null) return;
        var position = mouse.position.ReadValue();
        var showAuto = autoHelp != null && RectTransformUtility.RectangleContainsScreenPoint(autoHelp, position, null);
        var showHeader = headerHelp != null && RectTransformUtility.RectangleContainsScreenPoint(headerHelp, position, null);
        if (autoTooltipGroup != null && autoTooltipGroup.gameObject.activeSelf != showAuto) autoTooltipGroup.gameObject.SetActive(showAuto);
        if (headerTooltipGroup != null && headerTooltipGroup.gameObject.activeSelf != showHeader) headerTooltipGroup.gameObject.SetActive(showHeader);
        if (showAuto) autoTooltip?.SetAsLastSibling();
        if (showHeader) headerTooltip?.SetAsLastSibling();
    }

    static void QueueArrowAnimation(string key, bool wasOpen)
    {
        if (string.IsNullOrEmpty(key)) return;
        animatedArrows[key] = wasOpen;
        animateNextRefresh = true;
    }

    // the adapter under the crosshair: within 4.5 m, its screen ellipse containing the screen centre, nearest wins
    static int? Target(Camera cam)
    {
        Vector3 eye = cam.transform.position; var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        int? best = null; float bestD = float.MaxValue;
        foreach (var e in QuantumNetwork.EndpointViews())
        {
            if (e.View == null) continue;
            var p = e.View.transform.position; if ((p - eye).sqrMagnitude > 36f) continue;     // 6 m: cheap first cut
            var rs = e.View.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            Bounds b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            float d = (b.center - eye).magnitude; if (d > 4.5f || d < 0.01f) continue;
            Vector3 sp = cam.WorldToScreenPoint(b.center); if (sp.z <= 0f) continue;
            var spt = new Vector2(sp.x, Screen.height - sp.y);
            Vector3 ex = cam.WorldToScreenPoint(b.center + cam.transform.right * b.extents.magnitude);
            float rad = Mathf.Max(24f, Mathf.Abs(ex.x - sp.x));
            if ((spt - centre).magnitude > rad) continue;
            if (d < bestD && InSight(cam, e.View, b.center, d)) { bestD = d; best = e.Id; }
        }
        return best;
    }

    // nothing between the camera and the adapter (the short belt under it doesn't count: it shares the adapter's space)
    static bool InSight(Camera cam, GameObject view, Vector3 target, float distance)
    {
        try
        {
            var from = cam.transform.position;
            if (!Physics.Raycast(from, (target - from).normalized, out var hit, distance + 0.05f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore)) return true;
            var t = hit.collider != null ? hit.collider.transform : null;
            if (t != null && (t == view.transform || t.IsChildOf(view.transform))) return true;
            return hit.distance >= distance - 0.25f;
        }
        catch (Exception e) { WarnOnce("sight check", e); return true; }
    }

    static bool AimingAtRobot(Camera cam)
    {
        try
        {
            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 5f))
                return hit.collider != null && hit.collider.GetComponentInParent(Il2CppType.Of<MoonJoonasRig>()) != null;   // non-generic: see Freeze
        }
        catch (Exception e) { WarnOnce("robot check", e); }
        return false;
    }

    static void OpenFor(int id)
    {
        endpointId = id; editing = string.Empty;
        var ownGroup = QuantumNetwork.GroupOf(QuantumNetwork.ChannelFor(id));
        for (var group = ownGroup; !string.IsNullOrEmpty(group); group = QuantumNetwork.GroupParent(group)) expandedGroups.Add(group);
        EnsureVisualAssets();
        canvas = new GameObject("Quantum Conveyors Configuration"); UnityEngine.Object.DontDestroyOnLoad(canvas);
        var c = canvas.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 1000;
        canvas.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        raycaster = canvas.AddComponent<GraphicRaycaster>();
        var win = Rect("Window", canvas.transform, new Vector2(640, 600), Vector2.zero, PanelColor); win.localScale = new Vector3(0.54f, 0.54f, 1f);
        win.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = true;
        title = Label(win, "Quantum", new Vector2(0, 250), new Vector2(620, 42), 26, NameText, TextAnchor.MiddleCenter);
        Button(win, "New Channel", new Vector2(-88, 168), new Vector2(160, 40), NewChannel, true);
        Button(win, "New Group", new Vector2(88, 168), new Vector2(160, 40), NewGroup);
        Button(win, "×", new Vector2(287, 264), new Vector2(42, 42), Close);
        headerHelp = Rect("HeaderHelp", win, new Vector2(28, 28), new Vector2(180, 250), ButtonColor);
        headerHelp.GetComponent<Image>().sprite = circleSprite; headerHelp.GetComponent<Image>().type = Image.Type.Sliced;
        var headerHelpText = Label(headerHelp, "?", Vector2.zero, new Vector2(24, 24), 16, ChannelText, TextAnchor.MiddleCenter);
        headerHelpText.fontStyle = FontStyle.Bold;
        headerTooltip = Rect("HeaderTooltip", win, new Vector2(560, 248), new Vector2(0, 40), RowColor);
        var headerTooltipImage = headerTooltip.GetComponent<Image>(); if (headerTooltipImage != null) headerTooltipImage.raycastTarget = false;
        headerTooltipGroup = headerTooltip.gameObject.AddComponent<CanvasGroup>();
        headerTooltipGroup.blocksRaycasts = false; headerTooltipGroup.interactable = false;
        var headerTooltipText = Label(headerTooltip,
            "Quantum Conveyors sends capsules wirelessly between matching quantum input and output adapters.\n\n"
            + "1. Build a Quantum Input where capsules leave a belt and a Quantum Output where they should reappear.\n\n"
            + "2. Press 'E' on an adapter, create a new channel or choose an existing one, and assign the adapter to that channel. "
            + "Assign input and output adapters to the same channel to connect them together. Any number of inputs and outputs can "
            + "share a channel.\n\n"
            + "Channels and Groups below can be dragged and dropped to organize them as needed. Keep in mind that the list below is "
            + "not a list of individual adapters, but rather a list of the associated channels that multiple adapters may be "
            + "assigned to. In multiplayer, only the host can control channels and transfers.",
            Vector2.zero, new Vector2(534, 226), 13, ChannelText, TextAnchor.UpperLeft);
        headerTooltipText.horizontalOverflow = HorizontalWrapMode.Wrap; headerTooltipText.verticalOverflow = VerticalWrapMode.Overflow;
        headerTooltip.SetAsLastSibling(); headerTooltipGroup.gameObject.SetActive(false);
        viewport = Rect("ChannelViewport", win, new Vector2(620, 330), new Vector2(0, -26.5f), PanelColor);
        var view = viewport;
        view.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        content = Rect("ChannelContent", view, new Vector2(600, 44), Vector2.zero, null);
        content.anchorMin = content.anchorMax = content.pivot = new Vector2(0.5f, 1f); content.anchoredPosition = Vector2.zero;
        transition = Rect("ChannelTransition", view, new Vector2(600, 44), Vector2.zero, null);
        transition.anchorMin = transition.anchorMax = transition.pivot = new Vector2(0.5f, 1f); transition.anchoredPosition = Vector2.zero;
        transition.SetSiblingIndex(content.GetSiblingIndex());
        transitionGroup = transition.gameObject.AddComponent<CanvasGroup>(); transitionGroup.blocksRaycasts = false; transitionGroup.interactable = false;
        topFadeImage = AddFade(view, "TopFade", topFade, new Vector2(0, 159));
        bottomFadeImage = AddFade(view, "BottomFade", bottomFade, new Vector2(0, -159));
        var autoPanel = Rect("AutoGroupPanel", win, new Vector2(580, 60), new Vector2(0, -250), PanelColor);
        Label(autoPanel, "Auto Group", new Vector2(-88, 10), new Vector2(160, 30), 18, NameText, TextAnchor.MiddleCenter);
        autoHelp = Rect("AutoGroupHelp", autoPanel, new Vector2(28, 28), new Vector2(155, 10), ButtonColor);
        autoHelp.GetComponent<Image>().sprite = circleSprite; autoHelp.GetComponent<Image>().type = Image.Type.Sliced;
        var helpText = Label(autoHelp, "?", Vector2.zero, new Vector2(24, 24), 16, ChannelText, TextAnchor.MiddleCenter);
        helpText.fontStyle = FontStyle.Bold;
        autoGroupButton = Button(autoPanel, QuantumNetwork.AutoGroupOn ? "On" : "Off", new Vector2(88, 10), new Vector2(84, 34),
            ToggleAutoGroup, QuantumNetwork.AutoGroupOn);
        autoTooltip = Rect("AutoGroupTooltip", win, new Vector2(540, 128), new Vector2(0, -145), RowColor);
        var tooltipImage = autoTooltip.GetComponent<Image>(); if (tooltipImage != null) tooltipImage.raycastTarget = false;
        autoTooltipGroup = autoTooltip.gameObject.AddComponent<CanvasGroup>(); autoTooltipGroup.blocksRaycasts = false; autoTooltipGroup.interactable = false;
        var tooltipText = Label(autoTooltip,
            "While this is enabled, new channels will be nested inside matching groups if applicable. For example, channels "
            + "'Dust-Left' and 'Dust-Right' would become 'Left' and 'Right' inside a group called 'Dust'. 'Dust 1' and 'Dust 2' "
            + "are recognized the same way, and would result in channels named '1' and '2' inside a 'Dust' group.\n\n"
            + "This only applies to channels created while this setting is enabled and won't affect any channels created before "
            + "turning on Auto Group or after turning it off.",
            Vector2.zero, new Vector2(514, 112), 13, ChannelText, TextAnchor.UpperLeft);
        tooltipText.horizontalOverflow = HorizontalWrapMode.Wrap; tooltipText.verticalOverflow = VerticalWrapMode.Overflow;
        autoTooltip.SetAsLastSibling(); autoTooltipGroup.gameObject.SetActive(false);
        Refresh();
        var es = EventSystem.current;
        if (es == null)
        {
            ownEventSystem = new GameObject("Quantum Conveyors EventSystem"); es = ownEventSystem.AddComponent<EventSystem>();
            UnityEngine.Object.DontDestroyOnLoad(ownEventSystem);
        }
        prevModule = es.GetComponent<BaseInputModule>(); if (prevModule != null) { prevModuleOn = prevModule.enabled; prevModule.enabled = false; }
        Freeze();
    }

    static GameObject ownEventSystem;

    // Conveyors.Stop: the menu closes and the EventSystem it made goes
    internal static void Stop()
    {
        if (canvas != null) Close();
        if (ownEventSystem != null) { UnityEngine.Object.Destroy(ownEventSystem); ownEventSystem = null; }
    }

    static void Close()
    {
        if (canvas != null) UnityEngine.Object.Destroy(canvas);
        canvas = null; raycaster = null; content = null; editing = string.Empty;
        pendingNewChannel = pendingNewGroup = null; pendingNewChannelWasUnassignedExpanded = false;
        focusEditingChannel = string.Empty; pendingFocusField = null; autoGroupButton = null;
        viewport = null; transition = null; transitionGroup = null; topFadeImage = bottomFadeImage = null;
        autoHelp = autoTooltip = headerHelp = headerTooltip = null; autoTooltipGroup = headerTooltipGroup = null;
        rowAnimations.Clear(); arrowAnimations.Clear(); animatedArrows.Clear();
        transitionFadeUntil = 0f; scrollAnimationUntil = 0f; scrollAnchorKey = null; animateNextRefresh = false;
        revealChannelId = revealGroupId = null; revealRow = null;
        ClearDragState(); dragRows.Clear();
        if (prevModule != null) prevModule.enabled = prevModuleOn; prevModule = null;
        Unfreeze();
    }

    static bool RenameFocused()
    {
        var go = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        var f = go != null ? go.GetComponentInParent<InputField>() : null;
        return f != null && f.isFocused;
    }

    static void Click()
    {
        var mouse = Mouse.current; var es = EventSystem.current;
        if (raycaster == null || mouse == null || es == null || !mouse.leftButton.wasPressedThisFrame) return;
        var ev = new PointerEventData(es) { position = mouse.position.ReadValue() };
        var hits = new Il2CppSystem.Collections.Generic.List<RaycastResult>(); raycaster.Raycast(ev, hits);
        var en = hits.GetEnumerator();
        while (en.MoveNext())
        {
            var go = en.Current.gameObject; if (go == null) continue;
            var b = go.GetComponentInParent<UnityEngine.UI.Button>(); if (b != null) { b.onClick.Invoke(); break; }
            var f = go.GetComponentInParent<InputField>(); if (f != null) { es.SetSelectedGameObject(f.gameObject); f.ActivateInputField(); break; }
        }
    }

    static void Scroll()
    {
        if (content == null || Mouse.current == null) return;
        float y = Mouse.current.scroll.ReadValue().y; if (Math.Abs(y) < 0.01f) return;
        float max = Mathf.Max(0f, content.rect.height - viewport.rect.height); if (max <= 0f) return;
        scroll = Mathf.Clamp(scroll - (Math.Abs(y) > 10f ? y / 6f : y * 30f), 0f, max);
        content.anchoredPosition = new Vector2(0, scroll);
        UpdateFadeVisibility(max);
    }
}
