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

// The channel menu's list: special sections, folders that open inline, channels; rebuilt by Refresh() with rows
// sliding from where they were.
internal static partial class Menu
{
    // ---------------------------------------------------------------- the list: folders expand inline, channels remain alphabetized
    // Groups are folders. Their children are rendered inline when expanded so the menu behaves like a small file explorer
    // instead of navigating to a separate view for every folder.
    static readonly UnityEngine.Color Dim = new(0.47f, 0.45f, 0.43f, 1f);
    static readonly UnityEngine.Color NameText = new(0.110f, 0.133f, 0.196f, 1f);
    const string DimTag = "<size=13><color=#77736E>";

    const float RowStep = 52f;

    // what the list showed before a rebuild: its scroll, and (when the rebuild animates) each row, moved into the
    // transition layer so it can slide and fade out
    sealed class Before
    {
        internal float Scroll, MaxScroll;
        internal Dictionary<string, Vector2> Positions;
        internal readonly Dictionary<string, RectTransform> Rows = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, DragRow> Info = new(StringComparer.Ordinal);
    }

    // the rebuild in progress: the next row's index, the rows and arrows made (by key), and what the rows need to know
    sealed class Build
    {
        internal int Row;
        internal readonly Dictionary<string, RectTransform> Rows = new(StringComparer.Ordinal), Arrows = new(StringComparer.Ordinal);
        internal Dictionary<string, (int channels, int adapters)> Counts;
        internal string Mine;                                          // the channel of the adapter the menu is open on
        internal readonly HashSet<string> MineGroups = new();          // ... and the folders it's in
    }

    static void Refresh()
    {
        if (content == null) return;
        QuantumNetwork.SendStateToCrew();                                  // the host: crew-mates see what this menu changed
        var before = TakeOldRows();
        var b = NewBuild();
        UpdateAutoGroupButton(); UpdateCrewControls();
        AddSections(b);
        PlaceScroll(before, b);
        if (animateNextRefresh) AnimateChanges(before, b);
        FocusPendingField();
    }

    static Before TakeOldRows()
    {
        var before = new Before { Scroll = scroll, MaxScroll = Mathf.Max(0f, content.rect.height - viewport.rect.height) };
        if (transition != null)
        {
            for (var i = transition.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(transition.GetChild(i).gameObject);
            transitionFadeUntil = 0f;
            if (transitionGroup != null) transitionGroup.alpha = 1f;
            transition.anchoredPosition = new Vector2(0f, before.Scroll);
        }
        if (animateNextRefresh)
        {
            before.Positions = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            foreach (var oldRow in dragRows)
            {
                var key = RowKey(oldRow);
                if (string.IsNullOrEmpty(key) || oldRow.Row == null) continue;
                before.Positions[key] = oldRow.Row.anchoredPosition;
                before.Rows[key] = oldRow.Row;
                before.Info[key] = oldRow;
                if (transition != null) oldRow.Row.SetParent(transition, false);
            }
        }
        else
        {
            rowAnimations.Clear(); arrowAnimations.Clear(); scrollAnimationUntil = 0f;
        }
        for (int i = content.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);
        dragRows.Clear(); ClearDragState();
        revealRow = null;
        pendingFocusField = null;
        return before;
    }

    static Build NewBuild()
    {
        if (title != null) title.text = "Quantum Conveyors";
        var b = new Build { Counts = QuantumNetwork.GroupCounts(), Mine = QuantumNetwork.ChannelFor(endpointId) };
        for (string g = QuantumNetwork.GroupOf(b.Mine); !string.IsNullOrEmpty(g) && b.MineGroups.Add(g); g = QuantumNetwork.GroupParent(g)) { }
        return b;
    }

    // the crew line and the New buttons: a crew-mate without the host's leave has nothing to press
    static void UpdateCrewControls()
    {
        if (newChannelButton != null) newChannelButton.gameObject.SetActive(MayChange);
        if (newGroupButton != null) newGroupButton.gameObject.SetActive(MayChange);
        if (crewButton == null) return;
        var mode = QuantumNetwork.CrewChanges;
        var text = crewButton.GetComponentInChildren<Text>(); if (text != null) text.text = mode;
        var image = crewButton.GetComponent<Image>(); if (image != null) image.color = mode != "Off" ? Accent : ButtonColor;
    }

    static void UpdateAutoGroupButton()
    {
        if (autoGroupButton == null) return;
        var autoGroup = QuantumNetwork.AutoGroupShown;
        var buttonText = autoGroupButton.GetComponentInChildren<Text>();
        if (buttonText != null) buttonText.text = autoGroup ? "On" : "Off";
        var buttonImage = autoGroupButton.GetComponent<Image>();
        if (buttonImage != null) buttonImage.color = autoGroup ? Accent : ButtonColor;
    }

    // Unassigned, Not Grouped, then the top-level folders
    static void AddSections(Build b)
    {
        foreach (var r in new List<QuantumNetwork.Pending>(QuantumNetwork.PendingRequests)) AddRequest(b, r);
        var unassigned = new List<ChannelSummary>();
        foreach (var c in QuantumNetwork.Summaries()) if (c.Outputs + c.Inputs == 0) unassigned.Add(c);
        unassigned.Sort((x, y) => QuantumNetwork.Natural(x.Name, y.Name));
        AddSpecial(b, "Unassigned", "(" + CountPhrase(unassigned.Count, "channel") + " with no adapters)", unassignedExpanded,
            () => ToggleSection(ref unassignedExpanded, "s:unassigned"), unassigned);

        var ungrouped = new List<ChannelSummary>();
        foreach (var c in QuantumNetwork.Summaries())
            if (string.IsNullOrEmpty(QuantumNetwork.GroupOf(c.Id)) && c.Outputs + c.Inputs > 0) ungrouped.Add(c);
        ungrouped.Sort((x, y) => QuantumNetwork.Natural(x.Name, y.Name));
        AddSpecial(b, "Not Grouped", "(" + CountPhrase(ungrouped.Count, "channel") + " not assigned to a group)", ungroupedExpanded,
            () => ToggleSection(ref ungroupedExpanded, "s:ungrouped"), ungrouped);

        foreach (var gid in QuantumNetwork.ChildGroups(string.Empty)) AddGroup(b, gid, 0);
    }

    // a crew-mate's change waiting for the host (Crew Changes: Ask): who, what, Allow and Deny
    static void AddRequest(Build b, QuantumNetwork.Pending req)
    {
        var r = AddRow(b, false);
        AddName(r, req.Who + Counts("asks to " + req.What), new Vector2(-95, 0), 360);
        Button(r, "Allow", new Vector2(160, 0), new Vector2(84, 34), () => { QuantumNetwork.Answer(req, true); Refresh(); }, true);
        Button(r, "Deny", new Vector2(250, 0), new Vector2(84, 34), () => { QuantumNetwork.Answer(req, false); Refresh(); });
    }

    static void ToggleSection(ref bool expanded, string key)
    {
        var wasOpen = expanded; expanded = !expanded;
        if (!wasOpen) scrollAnchorKey = key;
        QueueArrowAnimation(key, wasOpen); Refresh();
    }

    // ---- rows

    static RectTransform AddRow(Build b, bool folder)
    {
        var r = Rect("Row", content, new Vector2(600, 46), new Vector2(0, -b.Row * RowStep), null);
        var image = r.gameObject.AddComponent<Image>(); image.sprite = roundedSprite; image.type = Image.Type.Sliced; image.color = PanelColor;
        var gradient = Rect("Row Gradient", r, Vector2.zero, Vector2.zero, null);
        gradient.anchorMin = Vector2.zero; gradient.anchorMax = Vector2.one; gradient.offsetMin = Vector2.zero; gradient.offsetMax = Vector2.zero;
        var gradientImage = gradient.gameObject.AddComponent<Image>();
        gradientImage.sprite = folder ? folderGradient : channelGradient; gradientImage.type = Image.Type.Sliced;
        gradientImage.color = Color.white; gradientImage.raycastTarget = false;
        gradient.SetAsFirstSibling();
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 1f); r.anchoredPosition = new Vector2(0, -b.Row * RowStep); b.Row++;
        return r;
    }

    static void Remember(Build b, string key, RectTransform r)
    {
        if (!string.IsNullOrEmpty(key)) b.Rows[key] = r;
    }

    static void AddArrow(Build b, RectTransform parent, string key, bool open, Action toggle)
    {
        var arrow = Button(parent, "▶", new Vector2(276, 0), new Vector2(48, 34), toggle, true);
        arrow.transform.SetAsLastSibling();
        var text = arrow.GetComponentInChildren<Text>();
        if (text == null) return;
        text.fontSize = 8;
        var initial = open ? -90f : 0f;
        if (animateNextRefresh && animatedArrows.TryGetValue(key, out var wasOpen)) initial = wasOpen ? -90f : 0f;
        text.rectTransform.localRotation = Quaternion.Euler(0f, 0f, initial);
        b.Arrows[key] = text.rectTransform;
    }

    static void AddEmpty(Build b, string text)
    {
        var r = Rect("Empty", content, new Vector2(580, 46), new Vector2(0, -b.Row * RowStep), null);
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 1f); r.anchoredPosition = new Vector2(0, -b.Row * RowStep); b.Row++;
        var t = Label(r, text, new Vector2(0, 0), new Vector2(540, 40), 13, Dim, TextAnchor.MiddleCenter);
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Truncate;
    }

    static void AddIcon(Transform parent, int depth)
    {
        var r = Rect("Icon", parent, new Vector2(22, 22), new Vector2(-270 + depth * 14, 0), null);
        var image = r.gameObject.AddComponent<Image>(); image.sprite = folderIcon; image.preserveAspect = true;
    }

    // a name and its counts, left-aligned, rich text that may run past its box
    static void AddName(RectTransform r, string text, Vector2 pos, float width)
    {
        var t = Label(r, text, pos, new Vector2(width, 40), 15, NameText, TextAnchor.MiddleLeft);
        t.supportRichText = true;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
    }

    static string Counts(string text) => "  " + DimTag + text + "</color></size>";
    const string ThisAdapter = "  <color=#FF7A2E>(this adapter)</color>";

    // ---- a channel: its name, then Assign/Unassign, Rename, Delete (or, being renamed, a field with Save and Cancel)

    static void AddChannel(Build b, ChannelSummary s, int depth, bool fullName, string section = null)
    {
        var r = AddRow(b, false);
        var key = section == null ? "c:" + s.Id : "x:" + section + ":" + s.Id;
        var group = QuantumNetwork.GroupOf(s.Id);
        var parentKey = section != null ? "s:" + section : string.IsNullOrEmpty(group) ? null : "g:" + group;
        Remember(b, key, r);
        RegisterDragRow(r, s.Id, null, false, null, key, parentKey);
        var indent = depth * 14;
        string id = s.Id;
        if (editing == "c:" + id)
        {
            var field = Field(r, new Vector2(-145 + indent * 0.5f, 0), new Vector2(260 - indent, 34)); field.text = QuantumNetwork.ShortName(id);
            if (focusEditingChannel == id) pendingFocusField = field;
            Button(r, "Save", new Vector2(28, 0), new Vector2(76, 34), () => SaveChannelName(id, field.text), true);
            Button(r, "Cancel", new Vector2(115, 0), new Vector2(88, 34), () => CancelChannelEdit(id));
            return;
        }
        string label = fullName || depth == 0 ? s.Name : QuantumNetwork.ShortName(id);
        AddName(r, label + (b.Mine == id ? ThisAdapter : "") + Counts("(" + Plugin.DisplayCounts(s.Outputs, s.Inputs) + ")"),
            new Vector2(-125 + indent, 0), 260 - indent);
        // the channel controls sit on the same column as a folder's Delete: room on the right, and every row's action
        // columns line up
        if (!MayChange) return;
        Button(r, b.Mine == id ? "Unassign" : "Assign", new Vector2(22, 0), new Vector2(86, 34), () => Toggle(id));
        Button(r, "Rename", new Vector2(114, 0), new Vector2(88, 34), () => { editing = "c:" + id; Refresh(); });
        Button(r, "Delete", new Vector2(205, 0), new Vector2(84, 34), () => DeleteChannel(id));
    }

    static void SaveChannelName(string id, string name)
    {
        if (Crewmate) { QuantumNetwork.Request("rename", id, name); editing = string.Empty; Refresh(); return; }
        if (!QuantumNetwork.RenameChannel(id, name)) return;
        if (pendingNewChannel == id) pendingNewChannel = null;
        editing = string.Empty; Refresh();
    }

    static void DeleteChannel(string id)
    {
        if (Crewmate) { QuantumNetwork.Request("delete", id); return; }
        if (!QuantumNetwork.DeleteChannel(id)) return;
        if (editing == "c:" + id) editing = string.Empty;
        if (pendingNewChannel == id) pendingNewChannel = null;
        Refresh();
    }

    // ---- a folder: icon, name and counts, Rename, Delete, its arrow; open, its folders and channels under it

    static void AddGroup(Build b, string gid, int depth)
    {
        var r = AddRow(b, true); Remember(b, "g:" + gid, r); AddIcon(r, depth);
        var parentGroup = QuantumNetwork.GroupParent(gid);
        RegisterDragRow(r, null, gid, false, null, "g:" + gid, string.IsNullOrEmpty(parentGroup) ? null : "g:" + parentGroup);
        var indent = depth * 14;
        if (editing == "g:" + gid)
        {
            var field = Field(r, new Vector2(-145 + indent * 0.5f, 0), new Vector2(260 - indent, 34)); field.text = QuantumNetwork.GroupName(gid);
            Button(r, "Save", new Vector2(28, 0), new Vector2(76, 34), () => SaveGroupName(gid, field.text), true);
            Button(r, "Cancel", new Vector2(115, 0), new Vector2(88, 34), () => CancelGroupEdit(gid));
            return;
        }
        b.Counts.TryGetValue(gid, out var n);
        AddName(r, QuantumNetwork.GroupName(gid) + (b.MineGroups.Contains(gid) ? ThisAdapter : "")
            + Counts("(" + CountPhrase(n.channels, "channel") + ", " + CountPhrase(n.adapters, "adapter") + ")"),
            new Vector2(-95 + indent, 0), 320 - indent);
        if (MayChange)
        {
            Button(r, "Rename", new Vector2(115, 0), new Vector2(86, 34), () => { editing = "g:" + gid; Refresh(); });
            Button(r, "Delete", new Vector2(205, 0), new Vector2(84, 34), () => DeleteGroup(gid));
        }
        var open = expandedGroups.Contains(gid);
        AddArrow(b, r, "g:" + gid, open, () => ToggleGroup(gid));
        if (!open) return;
        var childGroups = QuantumNetwork.ChildGroups(gid); var childChannels = QuantumNetwork.ChannelsIn(gid);
        if (childGroups.Count == 0 && childChannels.Count == 0) { AddEmpty(b, "This folder is empty."); return; }
        foreach (var child in childGroups) AddGroup(b, child, depth + 1);
        foreach (var channel in childChannels) AddChannel(b, channel, depth + 1, false);
    }

    static void DeleteGroup(string gid)
    {
        if (Crewmate) { QuantumNetwork.Request("deletegroup", gid); expandedGroups.Remove(gid); return; }
        if (!QuantumNetwork.DeleteGroup(gid)) return;
        expandedGroups.Remove(gid); Refresh();
    }

    static void SaveGroupName(string gid, string name)
    {
        if (Crewmate) { QuantumNetwork.Request("renamegroup", gid, name); editing = string.Empty; Refresh(); return; }
        if (!QuantumNetwork.RenameGroup(gid, name)) return;
        if (pendingNewGroup == gid) pendingNewGroup = null;
        editing = string.Empty; Refresh();
    }

    static void ToggleGroup(string gid)
    {
        var wasOpen = expandedGroups.Contains(gid);
        if (wasOpen) expandedGroups.Remove(gid); else { expandedGroups.Add(gid); scrollAnchorKey = "g:" + gid; }
        QueueArrowAnimation("g:" + gid, wasOpen); Refresh();
    }

    // ---- Unassigned and Not Grouped: a folder-like row, its channels under it when open

    static void AddSpecial(Build b, string label, string count, bool expanded, Action toggle, List<ChannelSummary> channels)
    {
        bool notGrouped = label == "Not Grouped";
        var section = notGrouped ? "ungrouped" : "unassigned";
        var r = AddRow(b, true); Remember(b, "s:" + section, r); AddIcon(r, 0);
        RegisterDragRow(r, null, null, notGrouped, section);
        var t = Label(r, label + Counts(count), new Vector2(-23.5f, 0), new Vector2(467, 40), 15, NameText, TextAnchor.MiddleLeft);
        t.supportRichText = true;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Truncate;
        AddArrow(b, r, "s:" + section, expanded, toggle);
        if (!expanded) return;
        if (channels.Count == 0) AddEmpty(b, notGrouped ? "Every channel is inside a folder." : "No channels are currently unassigned.");
        foreach (var channel in channels) AddChannel(b, channel, 1, true, section);
    }

    // ---- after the rows: where the list scrolls to, and the slide from the old rows

    static void PlaceScroll(Before before, Build b)
    {
        var maxScroll = Mathf.Max(0f, b.Row * RowStep - viewport.rect.height);
        var targetScroll = Mathf.Clamp(scroll, 0f, maxScroll);
        bool grewAtBottom = maxScroll > before.MaxScroll && before.Scroll >= before.MaxScroll - 1f;
        if (animateNextRefresh && grewAtBottom) targetScroll = maxScroll;
        content.sizeDelta = new Vector2(600, Math.Max(44f, b.Row * RowStep));
        if (revealRow != null)
        {
            var itemTop = -revealRow.anchoredPosition.y;
            var itemBottom = itemTop + RowStep;
            if (itemTop < targetScroll) targetScroll = itemTop;
            else if (itemBottom > targetScroll + viewport.rect.height) targetScroll = itemBottom - viewport.rect.height;
            targetScroll = Mathf.Clamp(targetScroll, 0f, maxScroll);
            revealRow = null; revealChannelId = revealGroupId = null;
        }
        if (!string.IsNullOrEmpty(scrollAnchorKey) && b.Rows.TryGetValue(scrollAnchorKey, out var anchorRow) && grewAtBottom)
            targetScroll = Mathf.Clamp(-anchorRow.anchoredPosition.y, 0f, maxScroll);
        scrollAnchorKey = null;
        if (animateNextRefresh && Mathf.Abs(before.Scroll - targetScroll) > 0.01f)
        {
            scrollAnimationFrom = before.Scroll; scrollAnimationTo = targetScroll; scrollAnimationMax = maxScroll;
            scrollAnimationUntil = Time.unscaledTime + 0.22f; scroll = before.Scroll;
        }
        else scroll = targetScroll;
        content.anchoredPosition = new Vector2(0f, scroll);
        if (transition != null) transition.anchoredPosition = new Vector2(0f, scroll);
        UpdateFadeVisibility(maxScroll);
    }

    // rows that stay slide from where they were (new ones drop in from just above), rows that went slide into their
    // parent and fade, and the arrows that changed turn
    static void AnimateChanges(Before before, Build b)
    {
        rowAnimations.Clear(); arrowAnimations.Clear();
        float until = Time.unscaledTime + 0.22f;
        foreach (var item in b.Rows)
        {
            var to = item.Value.anchoredPosition;
            var from = to + new Vector2(0f, 16f);
            if (before.Positions != null && before.Positions.TryGetValue(item.Key, out var oldPosition)) from = oldPosition;
            if ((from - to).sqrMagnitude <= 0.01f) continue;
            item.Value.anchoredPosition = from;
            rowAnimations.Add(new RowAnimation { Row = item.Value, From = from, To = to, Until = until });
        }
        var hasClosingRows = false;
        foreach (var old in before.Rows)
        {
            if (b.Rows.ContainsKey(old.Key)) { UnityEngine.Object.DestroyImmediate(old.Value.gameObject); continue; }
            if (transition == null || !before.Info.TryGetValue(old.Key, out var info)) continue;
            var from = before.Positions[old.Key];
            var to = CollapseTarget(info, before.Positions, b.Rows);
            if ((from - to).sqrMagnitude > 0.01f) rowAnimations.Add(new RowAnimation { Row = old.Value, From = from, To = to, Until = until });
            hasClosingRows = true;
        }
        if (hasClosingRows) { transitionGroup.alpha = 1f; transitionFadeUntil = until; }
        foreach (var queuedArrow in animatedArrows)
        {
            if (!b.Arrows.TryGetValue(queuedArrow.Key, out var arrowTransform)) continue;
            var from = arrowTransform.localEulerAngles.z;
            if (from > 180f) from -= 360f;
            var to = ArrowIsOpen(queuedArrow.Key) ? -90f : 0f;
            if (Mathf.Abs(from - to) > 0.01f) arrowAnimations.Add(new ArrowAnimation { Arrow = arrowTransform, From = from, To = to, Until = until });
        }
        animateNextRefresh = false;
        animatedArrows.Clear();
    }

    // a channel just made (New Channel) gets its name field focused, ready to type
    static void FocusPendingField()
    {
        if (string.IsNullOrEmpty(focusEditingChannel)) return;
        var es = EventSystem.current;
        if (pendingFocusField != null && es != null)
        {
            es.SetSelectedGameObject(pendingFocusField.gameObject);
            pendingFocusField.ActivateInputField();
        }
        pendingFocusField = null;
        focusEditingChannel = string.Empty;
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
        if (Crewmate)
        {
            if (QuantumNetwork.ChannelFor(endpointId) == channelId) QuantumNetwork.Request("unassign", Id(endpointId));
            else QuantumNetwork.Request("assign", Id(endpointId), channelId);
            return;
        }
        if (!Moon.IsAuthority) return;
        if (QuantumNetwork.ChannelFor(endpointId) == channelId) QuantumNetwork.UnassignEndpoint(endpointId);
        else QuantumNetwork.AssignEndpoint(endpointId, channelId);
        Refresh();
    }
    static void NewChannel()
    {
        // a crew-mate: the host makes it ("New Channel", under Unassigned); rename it once it shows
        if (Crewmate) { QuantumNetwork.Request("create", string.Empty); unassignedExpanded = true; return; }
        if (!Moon.IsAuthority || !QuantumNetwork.CreateChannelIn(string.Empty, out var channelId)) return;
        pendingNewChannelWasUnassignedExpanded = unassignedExpanded;
        animateNextRefresh = true;
        if (!unassignedExpanded)
        {
            scrollAnchorKey = "s:unassigned";
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
        if (!Moon.IsAuthority && !Crewmate) return;
        string parent = string.Empty;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in QuantumNetwork.ChildGroups(parent)) names.Add(QuantumNetwork.GroupName(g));
        string name = "New Group"; int n = 2; while (names.Contains(name)) name = "New Group " + n++;
        if (Crewmate) { QuantumNetwork.Request("newgroup", parent, name); return; }
        if (QuantumNetwork.CreateGroup(parent, name, out var gid)) { pendingNewGroup = gid; editing = "g:" + gid; animateNextRefresh = true; Refresh(); }
    }
    static void ToggleAutoGroup()
    {
        if (Crewmate) { QuantumNetwork.Request("auto", QuantumNetwork.AutoGroupShown ? "0" : "1"); return; }
        if (!Moon.IsAuthority) return;
        QuantumNetwork.SetAutoGroup(!QuantumNetwork.AutoGroupOn);
        Refresh();
    }
}
