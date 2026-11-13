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
    static Sprite channelGradient, folderGradient, channelIcon, folderIcon;
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
            Click(); Scroll();
            return;
        }
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;
        var cam = Camera.main ?? Camera.current; if (cam == null) return;
        var hit = Target(cam);
        if (hit.HasValue && !AimingAtRobot(cam)) OpenFor(hit.Value);
    }

    internal static void LateUpdate() { if (canvas != null) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } }

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
        expandedGroups.Clear();
        var ownGroup = QuantumNetwork.GroupOf(QuantumNetwork.ChannelFor(id));
        for (var group = ownGroup; !string.IsNullOrEmpty(group); group = QuantumNetwork.GroupParent(group)) expandedGroups.Add(group);
        EnsureVisualAssets();
        canvas = new GameObject("Quantum Conveyors Configuration"); UnityEngine.Object.DontDestroyOnLoad(canvas);
        var c = canvas.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 1000;
        canvas.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        raycaster = canvas.AddComponent<GraphicRaycaster>();
        var win = Rect("Window", canvas.transform, new Vector2(640, 520), Vector2.zero, PanelColor); win.localScale = new Vector3(0.54f, 0.54f, 1f);
        win.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = true;
        title = Label(win, "Quantum", new Vector2(0, 220), new Vector2(560, 42), 26, HeaderText, TextAnchor.MiddleCenter);
        Button(win, "< Back", new Vector2(-240, 152), new Vector2(110, 40), Back);
        Button(win, "New Group", new Vector2(-50, 152), new Vector2(160, 40), NewGroup);
        Button(win, "New Channel", new Vector2(150, 152), new Vector2(180, 40), NewChannel, true);
        Button(win, "×", new Vector2(287, 231), new Vector2(42, 42), Close);
        var view = Rect("ChannelViewport", win, new Vector2(600, 330), new Vector2(0, -45.5f), PanelColor);
        view.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        content = Rect("ChannelContent", view, new Vector2(580, 44), Vector2.zero, null);
        content.anchorMin = content.anchorMax = content.pivot = new Vector2(0.5f, 1f); content.anchoredPosition = Vector2.zero;
        Refresh();
        var es = EventSystem.current;
        if (es == null) { var go = new GameObject("Quantum Conveyors EventSystem"); es = go.AddComponent<EventSystem>(); UnityEngine.Object.DontDestroyOnLoad(go); }
        prevModule = es.GetComponent<BaseInputModule>(); if (prevModule != null) { prevModuleOn = prevModule.enabled; prevModule.enabled = false; }
        Freeze();
    }

    // ---------------------------------------------------------------- the list: folders expand inline, channels remain alphabetized
    // Groups are folders. Their children are rendered inline when expanded so the menu behaves like a small file explorer
    // instead of navigating to a separate view for every folder.
    static readonly UnityEngine.Color Dim = new(0.63f, 0.61f, 0.59f, 1f);
    const string DimTag = "<size=13><color=#A09C96>";

    static void Back()
    {
        if (unassignedView || ungroupedView) unassignedView = ungroupedView = false;
        else if (!string.IsNullOrEmpty(atGroup)) atGroup = QuantumNetwork.GroupParent(atGroup);
        editing = string.Empty; Refresh();
    }

    static void Refresh()
    {
        if (content == null) return;
        for (int i = content.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);
        const float rowH = 52f; scroll = 0f; content.anchoredPosition = Vector2.zero;
        string where = unassignedView ? "Unassigned channels" : ungroupedView ? "Not grouped" : "Quantum Conveyors";
        string what = moving != null ? "Move channel " + QuantumNetwork.ShortName(moving) : movingGroup != null ? "Move group " + QuantumNetwork.GroupName(movingGroup) : null;
        if (title != null) title.text = what != null ? what + " - pick a group" : where;
        var counts = QuantumNetwork.GroupCounts();
        string chMine = QuantumNetwork.ChannelFor(endpointId);
        var mineGroups = new HashSet<string>();
        for (string g = QuantumNetwork.GroupOf(chMine); !string.IsNullOrEmpty(g) && mineGroups.Add(g); g = QuantumNetwork.GroupParent(g)) { }
        int row = 0;
        RectTransform Row(int depth, bool folder)
        {
            var r = Rect("Row", content, new Vector2(580, 46), new Vector2(0, -row * rowH), null);
            var image = r.gameObject.AddComponent<Image>(); image.sprite = folder ? folderGradient : channelGradient; image.color = Color.white;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 1f); r.anchoredPosition = new Vector2(0, -row * rowH); row++;
            return r;
        }
        void Empty(string text) => Label(content, text, new Vector2(0, -28 - row * rowH), new Vector2(560, 40), 18, HeaderText, TextAnchor.MiddleCenter);
        void Icon(Transform parent, bool folder, int depth)
        {
            var r = Rect("Icon", parent, new Vector2(22, 22), new Vector2(-270 + depth * 18, 0), null);
            var image = r.gameObject.AddComponent<Image>(); image.sprite = folder ? folderIcon : channelIcon; image.preserveAspect = true;
        }

        void RenderChannel(ChannelSummary s, int depth)
        {
            var r = Row(depth, false); Icon(r, false, depth);
            var indent = depth * 18;
            string label = unassignedView || ungroupedView || depth == 0 ? s.Name : QuantumNetwork.ShortName(s.Id);
            if (editing == "c:" + s.Id)
            {
                var field = Field(r, new Vector2(-145 + indent, 0), new Vector2(260, 34)); field.text = QuantumNetwork.ShortName(s.Id);
                string id = s.Id;
                Button(r, "Save", new Vector2(34, 0), new Vector2(76, 34), () => { if (QuantumNetwork.RenameChannel(id, field.text)) { editing = string.Empty; Refresh(); } }, true);
                Button(r, "Cancel", new Vector2(130, 0), new Vector2(88, 34), () => { editing = string.Empty; Refresh(); });
                return;
            }
            var t = Label(r, label + (chMine == s.Id ? "  <color=#FF7A2E>(this adapter)</color>" : "") + "  " + DimTag + "(" + Plugin.DisplayCounts(s.Outputs, s.Inputs) + ")</color></size>", new Vector2(-170 + indent, 0), new Vector2(190 - indent, 40), 15, ChannelText, TextAnchor.MiddleLeft);
            t.supportRichText = true;
            string cid = s.Id;
            if (what == null) Button(r, "Move", new Vector2(-40, 0), new Vector2(80, 34), () => { moving = cid; movingGroup = null; unassignedView = ungroupedView = false; Refresh(); });
            Button(r, chMine == s.Id ? "Unassign" : "Assign", new Vector2(48, 0), new Vector2(86, 34), () => Toggle(cid));
            if (what == null)
            {
                Button(r, "Rename", new Vector2(140, 0), new Vector2(88, 34), () => { editing = "c:" + cid; Refresh(); });
                Button(r, "Delete", new Vector2(234, 0), new Vector2(84, 34), () => { if (QuantumNetwork.DeleteChannel(cid)) { if (editing == "c:" + cid) editing = string.Empty; Refresh(); } }, true);
            }
        }

        void RenderGroup(string gid, int depth)
        {
            var r = Row(depth, true); Icon(r, true, depth);
            var indent = depth * 18;
            if (editing == "g:" + gid)
            {
                var field = Field(r, new Vector2(-145 + indent, 0), new Vector2(260, 34)); field.text = QuantumNetwork.GroupName(gid);
                string id = gid;
                Button(r, "Save", new Vector2(34, 0), new Vector2(76, 34), () => { if (QuantumNetwork.RenameGroup(id, field.text)) { editing = string.Empty; Refresh(); } }, true);
                Button(r, "Cancel", new Vector2(130, 0), new Vector2(88, 34), () => { editing = string.Empty; Refresh(); });
                return;
            }
            counts.TryGetValue(gid, out var n);
            var t = Label(r, QuantumNetwork.GroupName(gid) + (mineGroups.Contains(gid) ? "  <color=#FF7A2E>(this adapter)</color>" : "") + "  " + DimTag + "(" + n.channels + " ch, " + n.adapters + " adapters)</color></size>", new Vector2(-170 + indent, 0), new Vector2(190 - indent, 40), 15, ChannelText, TextAnchor.MiddleLeft);
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
                Button(r, open ? "▾" : "▸", new Vector2(270, 0), new Vector2(40, 34), () => { if (open) expandedGroups.Remove(gid); else expandedGroups.Add(gid); Refresh(); }, true);
                if (open)
                {
                    foreach (var child in QuantumNetwork.ChildGroups(gid)) RenderGroup(child, depth + 1);
                    foreach (var channel in QuantumNetwork.ChannelsIn(gid)) RenderChannel(channel, depth + 1);
                }
            }
            else
            {
                var open = expandedGroups.Contains(gid);
                Button(r, open ? "▾" : "▸", new Vector2(234, 0), new Vector2(48, 34), () => { if (open) expandedGroups.Remove(gid); else expandedGroups.Add(gid); Refresh(); }, true);
                if (open)
                {
                    foreach (var child in QuantumNetwork.ChildGroups(gid)) RenderGroup(child, depth + 1);
                    foreach (var channel in QuantumNetwork.ChannelsIn(gid)) RenderChannel(channel, depth + 1);
                }
            }
        }

        // moving: a bar to drop it at the top level or cancel
        if (what != null && !unassignedView && !ungroupedView)
        {
            var mr = Row(0, false);
            Label(mr, "Into: " + (string.IsNullOrEmpty(atGroup) ? "top level (not grouped)" : QuantumNetwork.GroupName(atGroup)), new Vector2(-150, 0), new Vector2(250, 40), 16, ChannelText, TextAnchor.MiddleLeft);
            Button(mr, "Move here", new Vector2(80, 0), new Vector2(130, 34), () => MoveToGroup(string.Empty), true);
            Button(mr, "Cancel", new Vector2(224, 0), new Vector2(110, 34), () => { moving = movingGroup = null; Refresh(); });
        }

        bool special = unassignedView || ungroupedView;
        List<ChannelSummary> list;
        if (unassignedView) { list = new List<ChannelSummary>(); foreach (var c in QuantumNetwork.Summaries()) if (c.Outputs + c.Inputs == 0) list.Add(c); }
        else list = QuantumNetwork.ChannelsIn(string.Empty);

        if (!special && what == null)
        {
            bool on = QuantumNetwork.AutoGroupOn;                     // the tick box
            var ar = Row(0, true);
            Label(ar, (on ? "[x]" : "[  ]") + "  Auto-group by name  " + DimTag + "(dust-north + dust-south → dust folder)</color></size>", new Vector2(-95, 0), new Vector2(360, 40), 15, ChannelText, TextAnchor.MiddleLeft).supportRichText = true;
            Button(ar, on ? "On" : "Off", new Vector2(234, 0), new Vector2(84, 34), () => { QuantumNetwork.SetAutoGroup(!QuantumNetwork.AutoGroupOn); Refresh(); }, on);
            int empty = 0; foreach (var c in QuantumNetwork.Summaries()) if (c.Outputs + c.Inputs == 0) empty++;
            var ur = Row(0, true);
            Label(ur, "Unassigned  " + DimTag + "(" + empty + " channels with no adapters)</color></size>", new Vector2(-95, 0), new Vector2(360, 40), 15, ChannelText, TextAnchor.MiddleLeft).supportRichText = true;
            Button(ur, "Open", new Vector2(234, 0), new Vector2(84, 34), () => { unassignedView = true; Refresh(); }, true);
            var nr = Row(0, true);
            Label(nr, "Not grouped  " + DimTag + "(" + QuantumNetwork.UngroupedCount() + " channels outside any group)</color></size>", new Vector2(-95, 0), new Vector2(360, 40), 15, ChannelText, TextAnchor.MiddleLeft).supportRichText = true;
            Button(nr, "Open", new Vector2(234, 0), new Vector2(84, 34), () => { ungroupedView = true; Refresh(); }, true);
        }

        if (!special)
        {
            foreach (var gid in QuantumNetwork.ChildGroups(string.Empty)) RenderGroup(gid, 0);
            foreach (var channel in QuantumNetwork.ChannelsIn(string.Empty)) RenderChannel(channel, 0);
        }

        if (special)
        {
            if (list.Count == 0) Empty(unassignedView ? "Every channel has adapters." : "Every channel is in a group.");
            foreach (var s in list) RenderChannel(s, 0);
        }
        else if (row == 0) Empty("No folders or channels yet. Press New Channel or New Group.");
        content.sizeDelta = new Vector2(580, Math.Max(44f, row * rowH));
    }

    static bool CanHold(string into, string gid)                       // a group can't go inside itself
    {
        int guard = 0;
        for (string at = into; !string.IsNullOrEmpty(at) && guard++ < 64; at = QuantumNetwork.GroupParent(at)) if (at == gid) return false;
        return true;
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

    static void Toggle(string channelId)
    {
        if (!Moon.IsAuthority) return;
        if (QuantumNetwork.ChannelFor(endpointId) == channelId) QuantumNetwork.UnassignEndpoint(endpointId);
        else QuantumNetwork.AssignEndpoint(endpointId, channelId);
        Refresh();
    }
    static void NewChannel()
    {
        if (!Moon.IsAuthority || !QuantumNetwork.CreateChannelIn(unassignedView || ungroupedView ? string.Empty : atGroup, out _)) return;
        if (unassignedView || string.IsNullOrEmpty(atGroup)) { unassignedView = false; ungroupedView = true; }   // a loose one shows under Not grouped
        Refresh();
    }
    static void NewGroup()
    {
        if (!Moon.IsAuthority) return;
        string parent = unassignedView || ungroupedView ? string.Empty : atGroup ?? string.Empty;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (var g in QuantumNetwork.ChildGroups(parent)) names.Add(QuantumNetwork.GroupName(g));
        string name = "New Group"; int n = 2; while (names.Contains(name)) name = "New Group " + n++;
        if (QuantumNetwork.CreateGroup(parent, name, out var gid)) { unassignedView = ungroupedView = false; editing = "g:" + gid; Refresh(); }
    }

    static void EnsureVisualAssets()
    {
        if (channelGradient != null) return;
        channelGradient = MakeGradient(new Color(1f, 0.478f, 0.180f, 0.22f), new Color(1f, 0.478f, 0.180f, 0f), "Quantum Channel Gradient");
        folderGradient = MakeGradient(new Color(0.110f, 0.133f, 0.196f, 0.24f), new Color(0.110f, 0.133f, 0.196f, 0f), "Quantum Folder Gradient");
        channelIcon = MakeIcon(false, new Color(0.110f, 0.133f, 0.196f, 1f), "Quantum Channel Icon");
        folderIcon = MakeIcon(true, new Color(0.729f, 0.710f, 0.686f, 1f), "Quantum Folder Icon");
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
            for (var y = 4; y <= 19; y++) for (var x = 4; x <= 19; x++)
            {
                var dx = x - 11.5f; var dy = y - 11.5f; var distance = dx * dx + dy * dy;
                if (distance <= 64f && distance >= 25f) Put(x, y, color);
            }
            for (var x = 1; x <= 4; x++) Put(x, 11, color);
            for (var x = 19; x <= 22; x++) Put(x, 11, color);
            for (var y = 10; y <= 13; y++) for (var x = 10; x <= 13; x++) Put(x, y, color);
        }
        texture.SetPixels(pixels); texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    static void Close()
    {
        if (canvas != null) UnityEngine.Object.Destroy(canvas);
        canvas = null; raycaster = null; content = null; editing = string.Empty; moving = movingGroup = null;
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
        float max = Mathf.Max(0f, content.rect.height - 330f); if (max <= 0f) return;
        scroll = Mathf.Clamp(scroll - (Math.Abs(y) > 10f ? y / 6f : y * 30f), 0f, max);
        content.anchoredPosition = new Vector2(0, scroll);
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
