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

// The channel menu (E on an adapter): groups (New Group; Open, Rename, Move, Delete), New Channel; per channel Move,
// Assign/Unassign, Rename (its short name), Delete; E, Esc or right-click
// closes it. 1.x searched every adapter (and the scene for robots) every frame; this looks only when E is pressed, and
// only at adapters within 6 m of the camera that nothing stands in front of.
internal static class Menu
{
    static readonly Color PanelColor = new(0.937f, 0.922f, 0.882f, 1f), HeaderText = new(0.729f, 0.710f, 0.686f, 1f),
        RowColor = new(0.165f, 0.165f, 0.196f, 1f), ButtonColor = new(0.286f, 0.286f, 0.294f, 1f), Accent = new(1f, 0.478f, 0.180f, 1f),
        ChannelText = new(0.949f, 0.929f, 0.894f, 1f), AccentText = new(0.110f, 0.133f, 0.196f, 1f);
    static GameObject canvas; static GraphicRaycaster raycaster; static RectTransform content;
    static int endpointId; static string editing = string.Empty; static float scroll;
    static string atGroup = string.Empty; static bool unassignedView, ungroupedView; static UnityEngine.UI.Text title;   // the group shown
    static string moving, movingGroup;   // a channel or group being moved: browse to a group and press "Move here"
    static readonly HashSet<string> expandedGroups = new(StringComparer.Ordinal);
    static bool unassignedExpanded, ungroupedExpanded;
    static string focusEditingChannel = string.Empty;
    static string pendingNewChannel, pendingNewGroup;
    static bool pendingNewChannelWasUnassignedExpanded;
    static InputField pendingFocusField;
    static Sprite channelGradient, folderGradient, folderIcon, topFade, bottomFade;
    static Image topFadeImage, bottomFadeImage;
    static RectTransform viewport, transition, dragGhost;
    static CanvasGroup transitionGroup;
    static Text dragGhostLabel;
    static UnityEngine.UI.Button autoGroupButton;
    sealed class DragRow
    {
        internal RectTransform Row; internal string ChannelId; internal string GroupId; internal bool RootDrop; internal string Section; internal string AnimationKey; internal string ParentKey; internal Image Background; internal Image DropHighlight;
    }
    static readonly List<DragRow> dragRows = new();
    static DragRow dragCandidate, hoveredDrop;
    static Vector2 dragStart;
    static Vector2 dragGhostOffset;
    static bool dragging;
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
    static string revealChannelId, revealGroupId;
    static RectTransform revealRow;
    static BaseInputModule prevModule; static bool prevModuleOn;
    static MoonWalker walker; static bool prevBlocked; static bool prevWalkerEnabled;
    static InputActionAsset playerInput; static bool prevInputOn;
    static readonly List<InputActionAsset> fallbackInputAssets = new();
    static CursorLockMode prevLock; static bool prevVisible;

    internal static bool Open => canvas != null;

    internal static void Update()
    {
        var kb = Keyboard.current; var mouse = Mouse.current;
        if (canvas != null)
        {
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
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
            if (!Physics.Raycast(from, (target - from).normalized, out var hit, distance + 0.05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return true;
            var t = hit.collider != null ? hit.collider.transform : null;
            if (t != null && (t == view.transform || t.IsChildOf(view.transform))) return true;
            return hit.distance >= distance - 0.25f;
        }
        catch { return true; }
    }

    static bool AimingAtRobot(Camera cam)
    {
        try
        {
            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 5f))
                return hit.collider != null && hit.collider.GetComponentInParent(Il2CppType.Of<MoonJoonasRig>()) != null;   // non-generic: see Freeze
        }
        catch { }
        return false;
    }

    static void OpenFor(int id)
    {
        endpointId = id; editing = string.Empty; moving = movingGroup = null; unassignedView = ungroupedView = false;
        atGroup = string.Empty;
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
        viewport = Rect("ChannelViewport", win, new Vector2(620, 300), new Vector2(0, -15.5f), PanelColor);
        var view = viewport;
        view.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        content = Rect("ChannelContent", view, new Vector2(600, 44), Vector2.zero, null);
        content.anchorMin = content.anchorMax = content.pivot = new Vector2(0.5f, 1f); content.anchoredPosition = Vector2.zero;
        transition = Rect("ChannelTransition", view, new Vector2(600, 44), Vector2.zero, null);
        transition.anchorMin = transition.anchorMax = transition.pivot = new Vector2(0.5f, 1f); transition.anchoredPosition = Vector2.zero;
        transition.SetSiblingIndex(content.GetSiblingIndex());
        transitionGroup = transition.gameObject.AddComponent<CanvasGroup>(); transitionGroup.blocksRaycasts = false; transitionGroup.interactable = false;
        topFadeImage = AddFade(view, "TopFade", topFade, new Vector2(0, 134));
        bottomFadeImage = AddFade(view, "BottomFade", bottomFade, new Vector2(0, -134));
        var autoPanel = Rect("AutoGroupPanel", win, new Vector2(580, 100), new Vector2(0, -232), PanelColor);
        Label(autoPanel, "Auto Group", new Vector2(-88, 28), new Vector2(160, 30), 18, NameText, TextAnchor.MiddleCenter);
        var explanation = Label(autoPanel,
            "Turn this on to automatically nest new channels inside matching groups. For example, 'dust-left' and 'dust-right' become channels inside a 'dust' group, named 'left' and 'right'. 'dust 1' and 'dust 2' are recognized the same way.",
            new Vector2(6, -22), new Vector2(467, 52), 12, Dim, TextAnchor.UpperLeft);
        explanation.horizontalOverflow = HorizontalWrapMode.Wrap; explanation.verticalOverflow = VerticalWrapMode.Overflow;
        autoGroupButton = Button(autoPanel, QuantumNetwork.AutoGroupOn ? "On" : "Off", new Vector2(88, 28), new Vector2(84, 34), ToggleAutoGroup, QuantumNetwork.AutoGroupOn);
        Refresh();
        var es = EventSystem.current;
        if (es == null) { var go = new GameObject("Quantum Conveyors EventSystem"); es = go.AddComponent<EventSystem>(); UnityEngine.Object.DontDestroyOnLoad(go); }
        prevModule = es.GetComponent<BaseInputModule>(); if (prevModule != null) { prevModuleOn = prevModule.enabled; prevModule.enabled = false; }
        Freeze();
    }

    // ---------------------------------------------------------------- the list: folders expand inline, channels remain alphabetized
    // Groups are folders. Their children are rendered inline when expanded so the menu behaves like a small file explorer
    // instead of navigating to a separate view for every folder.
    static readonly UnityEngine.Color Dim = new(0.47f, 0.45f, 0.43f, 1f);
    static readonly UnityEngine.Color NameText = new(0.110f, 0.133f, 0.196f, 1f);
    const string DimTag = "<size=13><color=#77736E>";

    static void Back()
    {
        if (unassignedView || ungroupedView) unassignedView = ungroupedView = false;
        else if (!string.IsNullOrEmpty(atGroup)) atGroup = QuantumNetwork.GroupParent(atGroup);
        editing = string.Empty; Refresh();
    }

    static void RegisterDragRow(RectTransform row, string channelId, string groupId, bool rootDrop, string section = null, string animationKey = null, string parentKey = null)
    {
        var highlightRoot = new GameObject("Drop Highlight");
        var highlightRect = highlightRoot.AddComponent<RectTransform>(); highlightRect.SetParent(row, false);
        highlightRect.anchorMin = Vector2.zero; highlightRect.anchorMax = Vector2.one; highlightRect.offsetMin = Vector2.zero; highlightRect.offsetMax = Vector2.zero;
        var highlight = highlightRoot.AddComponent<Image>(); highlight.color = new Color(1f, 0.478f, 0.180f, 0.28f); highlight.raycastTarget = false; highlightRoot.SetActive(false);
        var item = new DragRow
        {
            Row = row,
            ChannelId = channelId,
            GroupId = groupId,
            RootDrop = rootDrop,
            Section = section,
            AnimationKey = animationKey,
            ParentKey = parentKey,
            Background = row.GetComponent<Image>(),
            DropHighlight = highlight
        };
        dragRows.Add(item);
        if ((!string.IsNullOrEmpty(channelId) && channelId == revealChannelId) || (!string.IsNullOrEmpty(groupId) && groupId == revealGroupId)) revealRow = row;
    }

    static string RowKey(DragRow row)
    {
        if (!string.IsNullOrEmpty(row.AnimationKey)) return row.AnimationKey;
        if (!string.IsNullOrEmpty(row.ChannelId)) return "c:" + row.ChannelId;
        if (!string.IsNullOrEmpty(row.GroupId)) return "g:" + row.GroupId;
        if (!string.IsNullOrEmpty(row.Section)) return "s:" + row.Section;
        return null;
    }

    static DragRow RowAt(Vector2 position, bool includeInteractive, out bool overInteractive)
    {
        overInteractive = false;
        var es = EventSystem.current;
        if (raycaster == null || es == null) return null;
        var ev = new PointerEventData(es) { position = position };
        var hits = new Il2CppSystem.Collections.Generic.List<RaycastResult>(); raycaster.Raycast(ev, hits);
        var en = hits.GetEnumerator();
        while (en.MoveNext())
        {
            var go = en.Current.gameObject; if (go == null) continue;
            var interactive = go.GetComponentInParent<Button>() != null || go.GetComponentInParent<InputField>() != null;
            if (interactive) overInteractive = true;
            if (interactive && !includeInteractive) continue;
            var t = go.transform;
            while (t != null && t.name != "Row") t = t.parent;
            if (t == null) continue;
            var row = t as RectTransform;
            foreach (var candidate in dragRows) if (candidate.Row == row) return candidate;
        }
        return null;
    }

    static void BeginDragCandidate(Vector2 position)
    {
        var row = RowAt(position, false, out var overInteractive);
        if (overInteractive || row == null || (row.ChannelId == null && row.GroupId == null && row.Section == null)) return;
        dragCandidate = row; dragStart = position; dragGhostOffset = RectTransformUtility.WorldToScreenPoint(null, row.Row.position) - position; dragging = false;
    }

    static bool CanDropOn(DragRow target)
    {
        if (target == null || dragCandidate == null) return false;
        if (target.RootDrop) return true;
        if (string.IsNullOrEmpty(target.GroupId)) return false;
        if (!string.IsNullOrEmpty(dragCandidate.ChannelId)) return true;
        return dragCandidate.GroupId != target.GroupId && CanHold(target.GroupId, dragCandidate.GroupId);
    }

    static void SetHoveredDrop(DragRow target)
    {
        if (hoveredDrop == target) return;
        if (hoveredDrop?.Background != null) hoveredDrop.Background.color = Color.white;
        if (hoveredDrop?.DropHighlight != null) hoveredDrop.DropHighlight.gameObject.SetActive(false);
        hoveredDrop = CanDropOn(target) ? target : null;
        if (hoveredDrop?.Background != null) hoveredDrop.Background.color = new Color(1f, 0.72f, 0.28f, 1f);
        if (hoveredDrop?.DropHighlight != null) hoveredDrop.DropHighlight.gameObject.SetActive(true);
    }

    static void UpdateDrag(Mouse mouse)
    {
        if (dragCandidate == null || mouse == null) return;
        var position = mouse.position.ReadValue();
        if (mouse.leftButton.isPressed)
        {
            if (!dragging && (position - dragStart).sqrMagnitude >= 64f)
            {
                dragging = true;
                CreateDragGhost();
            }
            if (dragging)
            {
                UpdateDragGhost(position);
                AutoScrollDuringDrag(position);
                SetHoveredDrop(RowAt(position, true, out _));
            }
            return;
        }
        if (!mouse.leftButton.wasReleasedThisFrame) return;
        if (dragging) SetHoveredDrop(RowAt(position, true, out _));
        if (dragging && hoveredDrop != null)
        {
            var targetGroup = hoveredDrop.RootDrop ? string.Empty : hoveredDrop.GroupId;
            var sourceGroup = !string.IsNullOrEmpty(dragCandidate.ChannelId)
                ? QuantumNetwork.GroupOf(dragCandidate.ChannelId)
                : !string.IsNullOrEmpty(dragCandidate.GroupId) ? QuantumNetwork.GroupParent(dragCandidate.GroupId) : null;
            var moved = false;
            if (!string.IsNullOrEmpty(dragCandidate.ChannelId))
            {
                moved = QuantumNetwork.MoveChannel(dragCandidate.ChannelId, targetGroup);
                if (moved) PrepareReveal(targetGroup, dragCandidate.ChannelId, null);
            }
            else if (!string.IsNullOrEmpty(dragCandidate.GroupId))
            {
                moved = QuantumNetwork.MoveGroup(dragCandidate.GroupId, targetGroup);
                if (moved) PrepareReveal(targetGroup, null, dragCandidate.GroupId);
            }
            if (moved)
            {
                animateNextRefresh = true;
                if (!string.IsNullOrEmpty(sourceGroup) && sourceGroup != targetGroup && !IsAncestorOf(sourceGroup, targetGroup))
                {
                    if (expandedGroups.Remove(sourceGroup)) QueueArrowAnimation("g:" + sourceGroup, true);
                }
            }
            ClearDragState(); Refresh();
            return;
        }
        if (!dragging && dragCandidate.GroupId != null)
        {
            var wasOpen = expandedGroups.Contains(dragCandidate.GroupId);
            if (wasOpen) expandedGroups.Remove(dragCandidate.GroupId);
            else expandedGroups.Add(dragCandidate.GroupId);
            QueueArrowAnimation("g:" + dragCandidate.GroupId, wasOpen);
        }
        else if (!dragging && dragCandidate.Section == "unassigned")
        {
            var wasOpen = unassignedExpanded; unassignedExpanded = !unassignedExpanded;
            QueueArrowAnimation("s:unassigned", wasOpen);
        }
        else if (!dragging && dragCandidate.Section == "ungrouped")
        {
            var wasOpen = ungroupedExpanded; ungroupedExpanded = !ungroupedExpanded;
            QueueArrowAnimation("s:ungrouped", wasOpen);
        }
        ClearDragState(); Refresh();
    }

    static void ClearDragState()
    {
        if (hoveredDrop?.Background != null) hoveredDrop.Background.color = Color.white;
        if (hoveredDrop?.DropHighlight != null) hoveredDrop.DropHighlight.gameObject.SetActive(false);
        if (dragGhost != null) UnityEngine.Object.Destroy(dragGhost.gameObject);
        dragGhost = null; dragGhostLabel = null;
        dragGhostOffset = Vector2.zero;
        dragCandidate = hoveredDrop = null; dragging = false;
    }

    static void PrepareReveal(string targetGroup, string channelId, string groupId)
    {
        revealChannelId = channelId; revealGroupId = groupId;
        if (!string.IsNullOrEmpty(targetGroup))
        {
            if (!expandedGroups.Contains(targetGroup))
            {
                QueueArrowAnimation("g:" + targetGroup, false);
            }
            for (var parent = targetGroup; !string.IsNullOrEmpty(parent); parent = QuantumNetwork.GroupParent(parent)) expandedGroups.Add(parent);
        }
        else if (!string.IsNullOrEmpty(channelId))
        {
            var wasOpen = ungroupedExpanded;
            if (!wasOpen) QueueArrowAnimation("s:ungrouped", false);
            ungroupedExpanded = true;
        }
    }

    static void CreateDragGhost()
    {
        if (canvas == null || dragCandidate == null) return;
        dragGhost = Rect("Quantum Drag Preview", canvas.transform, new Vector2(600, 46), Vector2.zero, null);
        dragGhost.localScale = new Vector3(0.54f, 0.54f, 1f);
        var image = dragGhost.gameObject.AddComponent<Image>();
        image.sprite = !string.IsNullOrEmpty(dragCandidate.ChannelId) ? channelGradient : folderGradient;
        image.color = Color.white; image.raycastTarget = false;
        var group = dragGhost.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0.92f; group.blocksRaycasts = false;
        if (!string.IsNullOrEmpty(dragCandidate.GroupId))
        {
            var iconRoot = Rect("Drag Folder Icon", dragGhost, new Vector2(22, 22), new Vector2(-270, 0), null);
            var icon = iconRoot.gameObject.AddComponent<Image>(); icon.sprite = folderIcon; icon.preserveAspect = true; icon.raycastTarget = false;
        }
        dragGhostLabel = Label(dragGhost, DragGhostText(), new Vector2(-165, 0), new Vector2(185, 40), 15, NameText, TextAnchor.MiddleLeft);
        dragGhostLabel.supportRichText = true;
        dragGhostLabel.raycastTarget = false;
        dragGhost.SetAsLastSibling();
    }

    static string DragGhostText()
    {
        if (!string.IsNullOrEmpty(dragCandidate.ChannelId))
        {
            foreach (var summary in QuantumNetwork.Summaries())
                if (summary.Id == dragCandidate.ChannelId)
                    return summary.Name + "  " + DimTag + "(" + Plugin.DisplayCounts(summary.Outputs, summary.Inputs) + ")</color></size>";
            return QuantumNetwork.ShortName(dragCandidate.ChannelId);
        }
        var counts = QuantumNetwork.GroupCounts();
        counts.TryGetValue(dragCandidate.GroupId, out var count);
        return QuantumNetwork.GroupName(dragCandidate.GroupId) + "  " + DimTag + "(" + count.channels + " ch, " + count.adapters + " adapters)</color></size>";
    }

    static void UpdateDragGhost(Vector2 position)
    {
        if (dragGhost == null) return;
        var ghostPosition = position + dragGhostOffset;
        dragGhost.position = new Vector3(ghostPosition.x, ghostPosition.y, 0f);
    }

    static void AutoScrollDuringDrag(Vector2 position)
    {
        if (viewport == null || content == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, position, null, out var local)) return;
        const float edge = 42f;
        var halfHeight = viewport.rect.height * 0.5f;
        var delta = 0f;
        if (local.y > halfHeight - edge) delta = -(local.y - (halfHeight - edge)) / edge;
        else if (local.y < -halfHeight + edge) delta = -((local.y + halfHeight - edge) / edge);
        if (Mathf.Abs(delta) < 0.01f) return;
        var max = Mathf.Max(0f, content.rect.height - viewport.rect.height);
        scroll = Mathf.Clamp(scroll + delta * 420f * Time.unscaledDeltaTime, 0f, max);
        content.anchoredPosition = new Vector2(0f, scroll);
        UpdateFadeVisibility(max);
    }

    static void Refresh()
    {
        if (content == null) return;
        Dictionary<string, Vector2> previousPositions = null;
        var previousRows = new Dictionary<string, RectTransform>(StringComparer.Ordinal);
        var previousRowInfo = new Dictionary<string, DragRow>(StringComparer.Ordinal);
        if (transition != null)
        {
            for (var i = transition.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(transition.GetChild(i).gameObject);
            transitionFadeUntil = 0f;
            if (transitionGroup != null) transitionGroup.alpha = 1f;
        }
        if (animateNextRefresh)
        {
            previousPositions = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            foreach (var oldRow in dragRows)
            {
                var key = RowKey(oldRow);
                if (!string.IsNullOrEmpty(key) && oldRow.Row != null)
                {
                    previousPositions[key] = oldRow.Row.anchoredPosition;
                    previousRows[key] = oldRow.Row;
                    previousRowInfo[key] = oldRow;
                    if (transition != null) oldRow.Row.SetParent(transition, false);
                }
            }
        }
        else
        {
            rowAnimations.Clear(); arrowAnimations.Clear();
        }
        for (int i = content.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);
        dragRows.Clear(); ClearDragState();
        revealRow = null;
        pendingFocusField = null;
        const float rowH = 52f;
        string what = moving != null ? "Move channel " + QuantumNetwork.ShortName(moving) : movingGroup != null ? "Move group " + QuantumNetwork.GroupName(movingGroup) : null;
        if (title != null) title.text = what != null ? what + " - pick a group" : "Quantum Conveyors";
        var counts = QuantumNetwork.GroupCounts();
        string chMine = QuantumNetwork.ChannelFor(endpointId);
        var mineGroups = new HashSet<string>();
        for (string g = QuantumNetwork.GroupOf(chMine); !string.IsNullOrEmpty(g) && mineGroups.Add(g); g = QuantumNetwork.GroupParent(g)) { }
        int row = 0;
        var newRows = new Dictionary<string, RectTransform>(StringComparer.Ordinal);
        var newArrows = new Dictionary<string, RectTransform>(StringComparer.Ordinal);
        RectTransform Row(int depth, bool folder)
        {
            var r = Rect("Row", content, new Vector2(600, 46), new Vector2(0, -row * rowH), null);
            var image = r.gameObject.AddComponent<Image>(); image.color = PanelColor;
            var gradient = Rect("Row Gradient", r, Vector2.zero, Vector2.zero, null);
            gradient.anchorMin = Vector2.zero; gradient.anchorMax = Vector2.one; gradient.offsetMin = Vector2.zero; gradient.offsetMax = Vector2.zero;
            var gradientImage = gradient.gameObject.AddComponent<Image>(); gradientImage.sprite = folder ? folderGradient : channelGradient; gradientImage.color = Color.white; gradientImage.raycastTarget = false;
            gradient.SetAsFirstSibling();
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 1f); r.anchoredPosition = new Vector2(0, -row * rowH); row++;
            return r;
        }
        void RememberRow(string key, RectTransform r)
        {
            if (!string.IsNullOrEmpty(key)) newRows[key] = r;
        }
        void AddArrow(RectTransform parent, string key, bool open, Action toggle)
        {
            var arrow = Button(parent, "▶", new Vector2(276, 0), new Vector2(48, 34), toggle, true);
            arrow.transform.SetAsLastSibling();
            var text = arrow.GetComponentInChildren<Text>();
            if (text == null) return;
            text.fontSize = 8;
            var initial = open ? -90f : 0f;
            if (animateNextRefresh && animatedArrows.TryGetValue(key, out var wasOpen)) initial = wasOpen ? -90f : 0f;
            text.rectTransform.localRotation = Quaternion.Euler(0f, 0f, initial);
            newArrows[key] = text.rectTransform;
        }
        void Empty(string text, int depth)
        {
            var r = Rect("Empty", content, new Vector2(580, 46), new Vector2(0, -row * rowH), null);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 1f); r.anchoredPosition = new Vector2(0, -row * rowH); row++;
            var t = Label(r, text, new Vector2(0, 0), new Vector2(540, 40), 13, Dim, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Truncate;
        }
        void Icon(Transform parent, int depth)
        {
            var r = Rect("Icon", parent, new Vector2(22, 22), new Vector2(-270 + depth * 14, 0), null);
            var image = r.gameObject.AddComponent<Image>(); image.sprite = folderIcon; image.preserveAspect = true;
        }

        void RenderChannel(ChannelSummary s, int depth, bool fullName, string section = null)
        {
            var r = Row(depth, false);
            var key = section == null ? "c:" + s.Id : "x:" + section + ":" + s.Id;
            var parentKey = section == null ? (string.IsNullOrEmpty(QuantumNetwork.GroupOf(s.Id)) ? null : "g:" + QuantumNetwork.GroupOf(s.Id)) : "s:" + section;
            RememberRow(key, r);
            RegisterDragRow(r, s.Id, null, false, null, key, parentKey);
            var indent = depth * 14;
            string label = fullName || depth == 0 ? s.Name : QuantumNetwork.ShortName(s.Id);
            if (editing == "c:" + s.Id)
            {
                var field = Field(r, new Vector2(-145 + indent, 0), new Vector2(260, 34)); field.text = QuantumNetwork.ShortName(s.Id);
                string id = s.Id;
                if (focusEditingChannel == id) pendingFocusField = field;
                Button(r, "Save", new Vector2(34, 0), new Vector2(76, 34), () => { if (QuantumNetwork.RenameChannel(id, field.text)) { if (pendingNewChannel == id) pendingNewChannel = null; editing = string.Empty; Refresh(); } }, true);
                Button(r, "Cancel", new Vector2(130, 0), new Vector2(88, 34), () => CancelChannelEdit(id));
                return;
            }
            var t = Label(r, label + (chMine == s.Id ? "  <color=#FF7A2E>(this adapter)</color>" : "") + "  " + DimTag + "(" + Plugin.DisplayCounts(s.Outputs, s.Inputs) + ")</color></size>", new Vector2(-165 + indent, 0), new Vector2(185 - indent, 40), 15, NameText, TextAnchor.MiddleLeft);
            t.supportRichText = true;
            string cid = s.Id;
            if (what == null) Button(r, "Move", new Vector2(-40, 0), new Vector2(80, 34), () => { moving = cid; movingGroup = null; unassignedView = ungroupedView = false; Refresh(); });
            Button(r, chMine == s.Id ? "Unassign" : "Assign", new Vector2(48, 0), new Vector2(86, 34), () => Toggle(cid));
            if (what == null)
            {
                Button(r, "Rename", new Vector2(140, 0), new Vector2(88, 34), () => { editing = "c:" + cid; Refresh(); });
                Button(r, "Delete", new Vector2(258, 0), new Vector2(84, 34), () => { if (QuantumNetwork.DeleteChannel(cid)) { if (editing == "c:" + cid) editing = string.Empty; if (pendingNewChannel == cid) pendingNewChannel = null; Refresh(); } }, true);
            }
        }

        void RenderGroup(string gid, int depth)
        {
            var r = Row(depth, true); RememberRow("g:" + gid, r); Icon(r, depth);
            var parentGroup = QuantumNetwork.GroupParent(gid);
            RegisterDragRow(r, null, gid, false, null, "g:" + gid, string.IsNullOrEmpty(parentGroup) ? null : "g:" + parentGroup);
            var indent = depth * 14;
            if (editing == "g:" + gid)
            {
                var field = Field(r, new Vector2(-120 + indent, 0), new Vector2(235, 34)); field.text = QuantumNetwork.GroupName(gid);
                string id = gid;
                Button(r, "Save", new Vector2(34, 0), new Vector2(76, 34), () => { if (QuantumNetwork.RenameGroup(id, field.text)) { if (pendingNewGroup == id) pendingNewGroup = null; editing = string.Empty; Refresh(); } }, true);
                Button(r, "Cancel", new Vector2(130, 0), new Vector2(88, 34), () => CancelGroupEdit(id));
                return;
            }
            counts.TryGetValue(gid, out var n);
            var t = Label(r, QuantumNetwork.GroupName(gid) + (mineGroups.Contains(gid) ? "  <color=#FF7A2E>(this adapter)</color>" : "") + "  " + DimTag + "(" + n.channels + " ch, " + n.adapters + " adapters)</color></size>", new Vector2(-165 + indent, 0), new Vector2(185 - indent, 40), 15, NameText, TextAnchor.MiddleLeft);
            t.supportRichText = true;
            if (what == null)
            {
                Button(r, "Move", new Vector2(-40, 0), new Vector2(80, 34), () => { movingGroup = gid; moving = null; Refresh(); });
                Button(r, "Rename", new Vector2(48, 0), new Vector2(86, 34), () => { editing = "g:" + gid; Refresh(); });
                Button(r, "Delete", new Vector2(140, 0), new Vector2(88, 34), () => { if (QuantumNetwork.DeleteGroup(gid)) { expandedGroups.Remove(gid); Refresh(); } });
            }
            if (what != null)
            {
                var can = movingGroup == null || CanHold(gid, movingGroup);
                if (can && (moving != null || movingGroup != gid)) Button(r, "Move here", new Vector2(180, 0), new Vector2(90, 34), () => MoveToGroup(gid), true);
                var open = expandedGroups.Contains(gid);
                AddArrow(r, "g:" + gid, open, () => { var wasOpen = expandedGroups.Contains(gid); if (wasOpen) expandedGroups.Remove(gid); else expandedGroups.Add(gid); QueueArrowAnimation("g:" + gid, wasOpen); Refresh(); });
                if (open)
                {
                    var childGroups = QuantumNetwork.ChildGroups(gid); var childChannels = QuantumNetwork.ChannelsIn(gid);
                    if (childGroups.Count == 0 && childChannels.Count == 0) Empty("This folder is empty.", depth + 1);
                    else
                    {
                        foreach (var child in childGroups) RenderGroup(child, depth + 1);
                        foreach (var channel in childChannels) RenderChannel(channel, depth + 1, false);
                    }
                }
            }
            else
            {
                var open = expandedGroups.Contains(gid);
                AddArrow(r, "g:" + gid, open, () => { var wasOpen = expandedGroups.Contains(gid); if (wasOpen) expandedGroups.Remove(gid); else expandedGroups.Add(gid); QueueArrowAnimation("g:" + gid, wasOpen); Refresh(); });
                if (open)
                {
                    var childGroups = QuantumNetwork.ChildGroups(gid); var childChannels = QuantumNetwork.ChannelsIn(gid);
                    if (childGroups.Count == 0 && childChannels.Count == 0) Empty("This folder is empty.", depth + 1);
                    else
                    {
                        foreach (var child in childGroups) RenderGroup(child, depth + 1);
                        foreach (var channel in childChannels) RenderChannel(channel, depth + 1, false);
                    }
                }
            }
        }

        void RenderSpecial(string label, string count, bool expanded, Action toggle, List<ChannelSummary> channels)
        {
            var section = label == "Not Grouped" ? "ungrouped" : "unassigned";
            var r = Row(0, true); RememberRow("s:" + section, r); Icon(r, 0);
            RegisterDragRow(r, null, null, label == "Not Grouped", label == "Not Grouped" ? "ungrouped" : "unassigned");
            var t = Label(r, label + "  " + DimTag + count + "</color></size>", new Vector2(-23.5f, 0), new Vector2(467, 40), 15, NameText, TextAnchor.MiddleLeft); t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Truncate;
            AddArrow(r, "s:" + section, expanded, toggle);
            if (expanded)
            {
                if (channels.Count == 0) Empty(label == "Unassigned" ? "No channels are currently unassigned." : "Every channel is inside a folder.", 1);
                foreach (var channel in channels) RenderChannel(channel, 1, true, section);
            }
        }

        if (what != null)
        {
            var mr = Row(0, false);
            Label(mr, "Into: top level (not grouped)", new Vector2(-150, 0), new Vector2(250, 40), 16, NameText, TextAnchor.MiddleLeft);
            Button(mr, "Move here", new Vector2(80, 0), new Vector2(130, 34), () => MoveToGroup(string.Empty), true);
            Button(mr, "Cancel", new Vector2(224, 0), new Vector2(110, 34), () => { moving = movingGroup = null; Refresh(); });
        }

        var autoGroup = QuantumNetwork.AutoGroupOn;
        if (autoGroupButton != null)
        {
            var buttonText = autoGroupButton.GetComponentInChildren<Text>();
            if (buttonText != null) buttonText.text = autoGroup ? "On" : "Off";
            var buttonImage = autoGroupButton.GetComponent<Image>();
            if (buttonImage != null) buttonImage.color = autoGroup ? Accent : ButtonColor;
        }

        var unassigned = new List<ChannelSummary>();
        foreach (var c in QuantumNetwork.Summaries()) if (c.Outputs + c.Inputs == 0) unassigned.Add(c);
        unassigned.Sort((a, b) => QuantumNetwork.Natural(a.Name, b.Name));
        RenderSpecial("Unassigned", "(" + unassigned.Count + " channels with no adapters)", unassignedExpanded, () => { var wasOpen = unassignedExpanded; unassignedExpanded = !unassignedExpanded; QueueArrowAnimation("s:unassigned", wasOpen); Refresh(); }, unassigned);

        var ungrouped = new List<ChannelSummary>();
        foreach (var c in QuantumNetwork.Summaries())
            if (string.IsNullOrEmpty(QuantumNetwork.GroupOf(c.Id)) && c.Outputs + c.Inputs > 0) ungrouped.Add(c);
        ungrouped.Sort((a, b) => QuantumNetwork.Natural(a.Name, b.Name));
        RenderSpecial("Not Grouped", "(" + ungrouped.Count + " channels with adapters outside folders)", ungroupedExpanded, () => { var wasOpen = ungroupedExpanded; ungroupedExpanded = !ungroupedExpanded; QueueArrowAnimation("s:ungrouped", wasOpen); Refresh(); }, ungrouped);

        foreach (var gid in QuantumNetwork.ChildGroups(string.Empty)) RenderGroup(gid, 0);
        var maxScroll = Mathf.Max(0f, row * rowH - 300f);
        scroll = Mathf.Clamp(scroll, 0f, maxScroll);
        content.sizeDelta = new Vector2(600, Math.Max(44f, row * rowH));
        if (revealRow != null)
        {
            var itemTop = -revealRow.anchoredPosition.y;
            var itemBottom = itemTop + rowH;
            if (itemTop < scroll) scroll = itemTop;
            else if (itemBottom > scroll + viewport.rect.height) scroll = itemBottom - viewport.rect.height;
            scroll = Mathf.Clamp(scroll, 0f, maxScroll);
            revealRow = null; revealChannelId = revealGroupId = null;
        }
        content.anchoredPosition = new Vector2(0f, scroll);
        UpdateFadeVisibility(maxScroll);

        if (animateNextRefresh)
        {
            rowAnimations.Clear(); arrowAnimations.Clear();
            foreach (var item in newRows)
            {
                var to = item.Value.anchoredPosition;
                var from = to + new Vector2(0f, 16f);
                if (previousPositions != null && previousPositions.TryGetValue(item.Key, out var oldPosition)) from = oldPosition;
                if ((from - to).sqrMagnitude > 0.01f)
                {
                    item.Value.anchoredPosition = from;
                    rowAnimations.Add(new RowAnimation { Row = item.Value, From = from, To = to, Until = Time.unscaledTime + 0.22f });
                }
            }
            var hasClosingRows = false;
            foreach (var old in previousRows)
            {
                if (newRows.ContainsKey(old.Key))
                {
                    UnityEngine.Object.DestroyImmediate(old.Value.gameObject);
                    continue;
                }
                if (transition == null || !previousRowInfo.TryGetValue(old.Key, out var info)) continue;
                var from = previousPositions[old.Key];
                var to = CollapseTarget(info, previousPositions, newRows);
                if ((from - to).sqrMagnitude > 0.01f)
                    rowAnimations.Add(new RowAnimation { Row = old.Value, From = from, To = to, Until = Time.unscaledTime + 0.22f });
                hasClosingRows = true;
            }
            if (hasClosingRows)
            {
                transitionGroup.alpha = 1f;
                transitionFadeUntil = Time.unscaledTime + 0.22f;
            }
            foreach (var queuedArrow in animatedArrows)
            {
                if (!newArrows.TryGetValue(queuedArrow.Key, out var arrowTransform)) continue;
                var from = arrowTransform.localEulerAngles.z;
                if (from > 180f) from -= 360f;
                var to = ArrowIsOpen(queuedArrow.Key) ? -90f : 0f;
                if (Mathf.Abs(from - to) > 0.01f)
                    arrowAnimations.Add(new ArrowAnimation { Arrow = arrowTransform, From = from, To = to, Until = Time.unscaledTime + 0.22f });
            }
            animateNextRefresh = false;
            animatedArrows.Clear();
        }

        if (!string.IsNullOrEmpty(focusEditingChannel))
        {
            var es = EventSystem.current;
            if (pendingFocusField != null && es != null)
            {
                es.SetSelectedGameObject(pendingFocusField.gameObject);
                pendingFocusField.ActivateInputField();
            }
            pendingFocusField = null;
            focusEditingChannel = string.Empty;
        }
    }

    static bool CanHold(string into, string gid)                       // a group can't go inside itself
    {
        int guard = 0;
        for (string at = into; !string.IsNullOrEmpty(at) && guard++ < 64; at = QuantumNetwork.GroupParent(at)) if (at == gid) return false;
        return true;
    }

    static bool ArrowIsOpen(string key)
    {
        if (key == "s:unassigned") return unassignedExpanded;
        if (key == "s:ungrouped") return ungroupedExpanded;
        return key != null && key.StartsWith("g:", StringComparison.Ordinal) && expandedGroups.Contains(key.Substring(2));
    }

    static Vector2 CollapseTarget(DragRow row, Dictionary<string, Vector2> previousPositions, Dictionary<string, RectTransform> newRows)
    {
        for (var parent = row.ParentKey; !string.IsNullOrEmpty(parent); )
        {
            if (newRows.ContainsKey(parent) && previousPositions.TryGetValue(parent, out var position)) return position;
            if (!parent.StartsWith("g:", StringComparison.Ordinal)) break;
            var next = QuantumNetwork.GroupParent(parent.Substring(2));
            parent = string.IsNullOrEmpty(next) ? null : "g:" + next;
        }
        return row.Row != null ? row.Row.anchoredPosition + new Vector2(0f, 16f) : Vector2.zero;
    }

    static bool IsAncestorOf(string ancestor, string group)
    {
        for (var current = group; !string.IsNullOrEmpty(current); current = QuantumNetwork.GroupParent(current))
            if (current == ancestor) return true;
        return false;
    }

    static void MoveToGroup(string gid)
    {
        if (moving != null) QuantumNetwork.MoveChannel(moving, gid);
        else if (movingGroup != null) QuantumNetwork.MoveGroup(movingGroup, gid);
        moving = movingGroup = null; Refresh();
    }
    static void MoveHere()
    {
        if (moving != null) QuantumNetwork.MoveChannel(moving, atGroup);
        else if (movingGroup != null) QuantumNetwork.MoveGroup(movingGroup, atGroup);
        moving = movingGroup = null; Refresh();
    }

    static void CancelChannelEdit(string id)
    {
        if (pendingNewChannel == id)
        {
            QuantumNetwork.DeleteChannel(id);
            var wasOpen = unassignedExpanded;
            unassignedExpanded = pendingNewChannelWasUnassignedExpanded;
            animateNextRefresh = true;
            if (wasOpen != unassignedExpanded)
            {
                QueueArrowAnimation("s:unassigned", wasOpen);
            }
            pendingNewChannel = null;
        }
        editing = string.Empty; Refresh();
    }

    static void CancelGroupEdit(string id)
    {
        if (pendingNewGroup == id)
        {
            QuantumNetwork.DeleteGroup(id);
            animateNextRefresh = true;
            pendingNewGroup = null;
        }
        editing = string.Empty; Refresh();
    }

    static void Toggle(string channelId)
    {
        if (!Moon.IsAuthority) return;
        if (QuantumNetwork.ChannelFor(endpointId) == channelId) QuantumNetwork.UnassignEndpoint(endpointId);
        else QuantumNetwork.AssignEndpoint(endpointId, channelId);
        Refresh();
    }
    static void NewChannel()
    {
        if (!Moon.IsAuthority || !QuantumNetwork.CreateChannelIn(string.Empty, out var channelId)) return;
        pendingNewChannelWasUnassignedExpanded = unassignedExpanded;
        animateNextRefresh = true;
        if (!unassignedExpanded)
        {
            QueueArrowAnimation("s:unassigned", false);
        }
        unassignedExpanded = true;
        pendingNewChannel = channelId;
        editing = "c:" + channelId;
        focusEditingChannel = channelId;
        Refresh();
    }
    static void NewGroup()
    {
        if (!Moon.IsAuthority) return;
        string parent = string.Empty;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (var g in QuantumNetwork.ChildGroups(parent)) names.Add(QuantumNetwork.GroupName(g));
        string name = "New Group"; int n = 2; while (names.Contains(name)) name = "New Group " + n++;
        if (QuantumNetwork.CreateGroup(parent, name, out var gid)) { pendingNewGroup = gid; editing = "g:" + gid; animateNextRefresh = true; Refresh(); }
    }
    static void ToggleAutoGroup()
    {
        if (!Moon.IsAuthority) return;
        QuantumNetwork.SetAutoGroup(!QuantumNetwork.AutoGroupOn);
        Refresh();
    }

    static void EnsureVisualAssets()
    {
        if (channelGradient != null) return;
        channelGradient = MakeGradient(new Color(1f, 0.478f, 0.180f, 0.22f), new Color(1f, 0.478f, 0.180f, 0f), "Quantum Channel Gradient");
        folderGradient = MakeGradient(new Color(0.110f, 0.133f, 0.196f, 0.24f), new Color(0.110f, 0.133f, 0.196f, 0f), "Quantum Folder Gradient");
        folderIcon = MakeIcon(true, new Color(0.110f, 0.133f, 0.196f, 1f), "Quantum Folder Icon");
        topFade = MakeVerticalFade(true, "Quantum Top Fade");
        bottomFade = MakeVerticalFade(false, "Quantum Bottom Fade");
    }

    static Sprite MakeGradient(Color left, Color right, string name)
    {
        const int width = 64;
        var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[width];
        for (var x = 0; x < width; x++) pixels[x] = Color.Lerp(left, right, x / (float)(width - 1));
        texture.SetPixels(pixels); texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, width, 1), new Vector2(0.5f, 0.5f), 100f);
    }

    static Sprite MakeVerticalFade(bool top, string name)
    {
        const int height = 32;
        var texture = new Texture2D(1, height, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[height];
        for (var y = 0; y < height; y++)
        {
            var t = y / (float)(height - 1);
            var alpha = top ? t : 1f - t;
            pixels[y] = new Color(1f, 1f, 1f, alpha);
        }
        texture.SetPixels(pixels); texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, 1, height), new Vector2(0.5f, 0.5f), 100f);
    }

    static Image AddFade(Transform parent, string name, Sprite sprite, Vector2 position)
    {
        var r = Rect(name, parent, new Vector2(620, 32), position, null);
        var image = r.gameObject.AddComponent<Image>(); image.sprite = sprite; image.color = PanelColor; image.raycastTarget = false;
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
        const int size = 24;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;
        void Put(int x, int y, Color value) { if (x >= 0 && x < size && y >= 0 && y < size) pixels[y * size + x] = value; }
        if (folder)
        {
            for (var y = 6; y <= 18; y++) for (var x = 3; x <= 20; x++) Put(x, y, color);
            for (var y = 18; y <= 20; y++) for (var x = 5; x <= 11; x++) Put(x, y, color);
            for (var x = 4; x <= 10; x++) Put(x, 19, color);
        }
        else
        {
            // A compact radio/wireless symbol: a center point with two upward arcs.
            for (var y = 17; y <= 20; y++) for (var x = 10; x <= 13; x++) Put(x, y, color);
            for (var y = 12; y <= 18; y++) for (var x = 5; x <= 18; x++)
            {
                var dx = x - 11.5f; var dy = y - 18f; var distance = dx * dx + dy * dy;
                if (distance >= 25f && distance <= 36f) Put(x, y, color);
            }
            for (var y = 8; y <= 17; y++) for (var x = 2; x <= 21; x++)
            {
                var dx = x - 11.5f; var dy = y - 18f; var distance = dx * dx + dy * dy;
                if (distance >= 81f && distance <= 100f) Put(x, y, color);
            }
        }
        texture.SetPixels(pixels); texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    static void Close()
    {
        if (canvas != null) UnityEngine.Object.Destroy(canvas);
        canvas = null; raycaster = null; content = null; editing = string.Empty; moving = movingGroup = null;
        pendingNewChannel = pendingNewGroup = null; pendingNewChannelWasUnassignedExpanded = false; focusEditingChannel = string.Empty; pendingFocusField = null; autoGroupButton = null;
        viewport = null; transition = null; transitionGroup = null; topFadeImage = bottomFadeImage = null;
        rowAnimations.Clear(); arrowAnimations.Clear(); animatedArrows.Clear(); transitionFadeUntil = 0f; animateNextRefresh = false;
        revealChannelId = revealGroupId = null; revealRow = null;
        ClearDragState(); dragRows.Clear();
        if (prevModule != null) prevModule.enabled = prevModuleOn; prevModule = null;
        Unfreeze();
    }

    static void Freeze()
    {
        prevLock = Cursor.lockState; prevVisible = Cursor.visible;
        var found = FindWalker();
        if (found != null)
        {
            prevBlocked = found.UIBlocked;
            prevWalkerEnabled = found.enabled;
            playerInput = found.inputTemplate;
            if (playerInput != null)
            {
                prevInputOn = playerInput.enabled;
                playerInput.Disable();
            }
            else
            {
                DisableFallbackInputAssets();
            }
            found.UIBlocked = true;
        }
        else
        {
            QuantumNetwork.Log?.LogWarning("menu: player controller not found; disabling live input assets as fallback");
            DisableFallbackInputAssets();
        }
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }
    static void Unfreeze()
    {
        try
        {
            var found = FindWalker();
            if (found != null)
            {
                found.UIBlocked = prevBlocked;
                found.enabled = prevWalkerEnabled;
            }
        }
        catch (Exception e) { QuantumNetwork.Log?.LogWarning("menu: player input restore skipped: " + e.Message); }
        try
        {
            if (playerInput != null && prevInputOn) playerInput.Enable();
        }
        catch (Exception e) { QuantumNetwork.Log?.LogWarning("menu: primary player input restore skipped: " + e.Message); }
        playerInput = null;
        foreach (var asset in fallbackInputAssets)
        {
            try { if (asset != null) asset.Enable(); }
            catch (Exception e) { QuantumNetwork.Log?.LogWarning("menu: fallback input restore skipped: " + e.Message); }
        }
        fallbackInputAssets.Clear();
        Cursor.lockState = prevLock; Cursor.visible = prevVisible;
    }

    static MoonWalker FindWalker()
    {
        if (walker != null) return walker;

        // Prefer direct typed lookups. The PR's previous Il2CppType/TryCast
        // chain could fail with "Method unstripping failed" on the preview
        // build before it ever reached the input-disable step.
        var cam = Camera.main ?? Camera.current;
        if (cam != null)
        {
            var found = cam.GetComponent(Il2CppType.Of<MoonWalker>());
            if (found == null) found = cam.GetComponentInParent(Il2CppType.Of<MoonWalker>());
            if (found == null) found = cam.transform.root.GetComponentInChildren(Il2CppType.Of<MoonWalker>());
            walker = found as MoonWalker;
            if (walker != null) return walker;
        }

        try
        {
            var globalWalkers = UnityEngine.Object.FindObjectsOfType<MoonWalker>(true);
            foreach (var candidate in globalWalkers)
            {
                if (candidate == null) continue;
                walker = candidate;
                return walker;
            }
        }
        catch (Exception e) { QuantumNetwork.Log?.LogWarning("menu: global player search failed: " + e.Message); }

        for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            var scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                var found = root.GetComponentInChildren(Il2CppType.Of<MoonWalker>());
                walker = found as MoonWalker;
                if (walker != null) return walker;

                var production = root.GetComponentInChildren(Il2CppType.Of<MoonProductionInteraction>()) as MoonProductionInteraction;
                if (production != null && production.walker != null)
                {
                    walker = production.walker;
                    return walker;
                }
            }
        }

        var persistentObjects = Resources.FindObjectsOfTypeAll(Il2CppType.Of<MoonWalker>());
        foreach (var item in persistentObjects)
        {
            walker = item as MoonWalker;
            if (walker != null && walker.gameObject.scene.IsValid()) return walker;
        }
        return walker;
    }

    static void DisableFallbackInputAssets()
    {
        var disabledNames = new List<string>();
        var assets = Resources.FindObjectsOfTypeAll(Il2CppType.Of<InputActionAsset>());
        foreach (var item in assets)
        {
            var asset = item as InputActionAsset;
            if (asset == null || !asset.enabled || IsUiInputAsset(asset)) continue;
            asset.Disable(); fallbackInputAssets.Add(asset); disabledNames.Add(asset.name);
        }
        try
        {
            var globalAssets = UnityEngine.Object.FindObjectsOfType<InputActionAsset>(true);
            foreach (var asset in globalAssets)
            {
                if (asset == null || !asset.enabled || IsUiInputAsset(asset) || fallbackInputAssets.Contains(asset)) continue;
                asset.Disable(); fallbackInputAssets.Add(asset); disabledNames.Add(asset.name);
            }
        }
        catch (Exception e) { QuantumNetwork.Log?.LogWarning("menu: global input asset search failed: " + e.Message); }
        QuantumNetwork.Log?.LogInfo("menu: fallback input freeze disabled " + fallbackInputAssets.Count + " asset(s): " + string.Join(", ", disabledNames));
    }

    static bool IsUiInputAsset(InputActionAsset asset)
    {
        var name = asset.name ?? string.Empty;
        return name.IndexOf("ui", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("menu", StringComparison.OrdinalIgnoreCase) >= 0;
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
        float max = Mathf.Max(0f, content.rect.height - 300f); if (max <= 0f) return;
        scroll = Mathf.Clamp(scroll - (Math.Abs(y) > 10f ? y / 6f : y * 30f), 0f, max);
        content.anchoredPosition = new Vector2(0, scroll);
        UpdateFadeVisibility(max);
    }

    // ---------------------------------------------------------------- UI pieces
    static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 pos, Color? bg)
    {
        var go = new GameObject(name); var r = go.AddComponent<RectTransform>(); r.SetParent(parent, false);
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f); r.sizeDelta = size; r.anchoredPosition = pos;
        if (bg.HasValue) go.AddComponent<Image>().color = bg.Value;
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
        f.targetGraphic = r.GetComponent<Image>();
        f.textComponent = Label(r, string.Empty, Vector2.zero, size - new Vector2(16, 4), 18, ChannelText, TextAnchor.MiddleLeft);
        f.textComponent.rectTransform.anchoredPosition = new Vector2(8, 0); f.characterLimit = 24;
        return f;
    }
    static UnityEngine.UI.Button Button(Transform parent, string label, Vector2 pos, Vector2 size, Action action, bool accent = false)
    {
        bool close = label == "×";
        var r = Rect("Button", parent, size, pos, accent ? Accent : (close ? new Color(0, 0, 0, 0) : ButtonColor));
        var b = r.gameObject.AddComponent<UnityEngine.UI.Button>(); b.targetGraphic = r.GetComponent<Image>();
        UnityAction call = action; b.onClick.AddListener(call);
        Label(r, label, Vector2.zero, size - new Vector2(8, 4), close ? 28 : 16, accent ? AccentText : (close ? HeaderText : ChannelText), TextAnchor.MiddleCenter);
        return b;
    }
}
