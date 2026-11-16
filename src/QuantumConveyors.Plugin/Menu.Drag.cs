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

// The channel menu: dragging a channel or folder onto another folder (or out to the top level).
internal static partial class Menu
{
    sealed class DragRow
    {
        internal RectTransform Row; internal string ChannelId; internal string GroupId; internal bool RootDrop;
        internal string Section; internal string AnimationKey; internal string ParentKey;
        internal Image Background; internal Image DropHighlight;
    }
    static readonly List<DragRow> dragRows = new();
    static DragRow dragCandidate, hoveredDrop;
    static Vector2 dragStart;
    static Vector2 dragGhostOffset;
    static bool dragging;

    static void RegisterDragRow(RectTransform row, string channelId, string groupId, bool rootDrop, string section = null,
        string animationKey = null, string parentKey = null)
    {
        var highlightRoot = new GameObject("Drop Highlight");
        var highlightRect = highlightRoot.AddComponent<RectTransform>(); highlightRect.SetParent(row, false);
        highlightRect.anchorMin = Vector2.zero; highlightRect.anchorMax = Vector2.one;
        highlightRect.offsetMin = Vector2.zero; highlightRect.offsetMax = Vector2.zero;
        var highlight = highlightRoot.AddComponent<Image>(); highlight.sprite = roundedSprite; highlight.type = Image.Type.Sliced;
        highlight.color = new Color(1f, 0.478f, 0.180f, 0.28f); highlight.raycastTarget = false; highlightRoot.SetActive(false);
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
            // TryCast, not "as": the interop hands back a Transform wrapper, and "as RectTransform" only works while it
            // still has this object's RectTransform wrapper cached - after a garbage collection it's null
            var row = t.TryCast<RectTransform>();
            foreach (var candidate in dragRows) if (candidate.Row == row) return candidate;
        }
        return null;
    }

    static void BeginDragCandidate(Vector2 position)
    {
        if (!MayChange) return;                                        // a crew-mate without the host's leave
        var row = RowAt(position, false, out var overInteractive);
        if (overInteractive || row == null || (row.ChannelId == null && row.GroupId == null && row.Section == null)) return;
        dragCandidate = row; dragStart = position; dragging = false;
        dragGhostOffset = RectTransformUtility.WorldToScreenPoint(null, row.Row.position) - position;
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
            // Unassigned and Not Grouped are virtual sections, not movable
            // folders. They still need to receive a normal click so they can
            // expand/collapse, but must never enter drag mode: they have no
            // GroupId and would otherwise crash drag-preview text generation.
            if (!dragging && dragCandidate.Section == null && (position - dragStart).sqrMagnitude >= 64f)
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
                moved = Crewmate ? Request("movechannel", dragCandidate.ChannelId, targetGroup)
                    : QuantumNetwork.MoveChannel(dragCandidate.ChannelId, targetGroup);
                if (moved) PrepareReveal(targetGroup, dragCandidate.ChannelId, null);
            }
            else if (!string.IsNullOrEmpty(dragCandidate.GroupId))
            {
                moved = Crewmate ? Request("movegroup", dragCandidate.GroupId, targetGroup)
                    : QuantumNetwork.MoveGroup(dragCandidate.GroupId, targetGroup);
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
            else { expandedGroups.Add(dragCandidate.GroupId); scrollAnchorKey = "g:" + dragCandidate.GroupId; }
            QueueArrowAnimation("g:" + dragCandidate.GroupId, wasOpen);
        }
        else if (!dragging && dragCandidate.Section == "unassigned")
        {
            var wasOpen = unassignedExpanded; unassignedExpanded = !unassignedExpanded;
            if (!wasOpen) scrollAnchorKey = "s:unassigned";
            QueueArrowAnimation("s:unassigned", wasOpen);
        }
        else if (!dragging && dragCandidate.Section == "ungrouped")
        {
            var wasOpen = ungroupedExpanded; ungroupedExpanded = !ungroupedExpanded;
            if (!wasOpen) scrollAnchorKey = "s:ungrouped";
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
        return QuantumNetwork.GroupName(dragCandidate.GroupId)
            + Counts("(" + CountPhrase(count.channels, "channel") + ", " + CountPhrase(count.adapters, "adapter") + ")");
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
}
