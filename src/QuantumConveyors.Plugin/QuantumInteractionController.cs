using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using MoonCleaners;
using MoonFramework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace QuantumConveyors;

/// <summary>
/// Finds the adapter under the crosshair and opens a small native Unity UI.
/// IMGUI is deliberately not used because Moon Sweepers' preview build does
/// not unstrip Unity's IMGUI methods.
/// </summary>
public sealed class QuantumInteractionController : MonoBehaviour
{
    private QuantumEndpointView? target;
    private GameObject menuCanvas;
    private RectTransform channelContent;
    private float channelScrollOffset;
    private string selectedChannelId = string.Empty;
    private string editingChannelId = string.Empty;
    private int menuEndpointId;
    private MoonWalker walker;
    private bool previousUiBlocked;
    private bool previousWalkerEnabled;
    private UnityEngine.InputSystem.InputActionAsset playerInputAsset;
    private bool previousPlayerInputEnabled;
    private readonly List<UnityEngine.InputSystem.InputActionAsset> fallbackInputAssets = new();
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private GraphicRaycaster menuRaycaster;
    private UnityEngine.EventSystems.BaseInputModule previousInputModule;
    private bool previousInputModuleEnabled;
    private bool updateReported;
    private int lastViewCount = -1;
    private bool? cameraAvailable;
    private bool updateErrorReported;

    public QuantumInteractionController(IntPtr pointer) : base(pointer) { }

    public void Awake() => QuantumNetwork.Diagnostic("Interaction host Awake");

    public void OnDestroy()
    {
        CloseMenu();
        QuantumNetwork.Diagnostic("Interaction host OnDestroy");
    }

    public void OnDisable() => QuantumNetwork.Diagnostic("Interaction host OnDisable");

    public void Update()
    {
        try
        {
            if (!updateReported)
            {
                updateReported = true;
                QuantumNetwork.Diagnostic("Interaction host Update is running");
            }

            if (menuCanvas != null)
            {
                KeepCursorForMenu();
                if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
                    CloseMenu();
                else if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                    CloseMenu();
                else if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame && !RenameFieldFocused())
                    CloseMenu();
                else
                {
                    HandleMenuClick();
                    HandleMenuScroll();
                }
                return;
            }

            var camera = ActiveCamera();
            if (camera == null)
            {
                if (cameraAvailable != false)
                {
                    cameraAvailable = false;
                    QuantumNetwork.Diagnostic("No active camera found");
                }
                return;
            }

            if (cameraAvailable != true)
            {
                cameraAvailable = true;
                QuantumNetwork.Diagnostic($"Active camera found: {camera.name}");
            }

            var endpointViews = QuantumNetwork.EndpointViews();
            if (endpointViews.Count != lastViewCount)
            {
                lastViewCount = endpointViews.Count;
                QuantumNetwork.Diagnostic($"Interaction host sees {lastViewCount} endpoint view(s)");
            }

            target = FindTarget(camera, endpointViews);
            if (!target.HasValue || !InteractPressed()) return;

            var interaction = target.Value;
            OpenMenu(interaction.Id, interaction.IsOutput);
        }
        catch (Exception exception)
        {
            if (!updateErrorReported)
            {
                updateErrorReported = true;
                QuantumNetwork.Diagnostic($"Interaction Update exception: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private static bool InteractPressed()
    {
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
    }

    private static bool RenameFieldFocused()
    {
        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        var selected = eventSystem == null ? null : eventSystem.currentSelectedGameObject;
        var field = selected == null ? null : selected.GetComponentInParent<InputField>();
        return field != null && field.isFocused;
    }

    private static Camera ActiveCamera()
    {
        if (Camera.main != null) return Camera.main;
        return Camera.current;
    }

    private void OpenMenu(int endpointId, bool isOutput)
    {
        menuEndpointId = endpointId;
        selectedChannelId = QuantumNetwork.ChannelFor(endpointId) ?? string.Empty;

        menuCanvas = new GameObject("Quantum Conveyors Configuration");
        DontDestroyOnLoad(menuCanvas);
        var canvas = menuCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        menuCanvas.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        menuRaycaster = menuCanvas.AddComponent<GraphicRaycaster>();

        var root = MakeRect("Window", menuCanvas.transform, new Vector2(560f, 480f), Vector2.zero, new Color(0.08f, 0.10f, 0.14f, 0.98f));
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = Vector2.zero;
        root.localScale = new Vector3(0.5f, 0.5f, 1f);
        root.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = true;
        AddText(root, "Quantum Conveyors", new Vector2(0f, 220f), new Vector2(560f, 34f), 26, Color.white, TextAnchor.MiddleCenter);
        CreateButton(root, "New channel", new Vector2(-165f, 170f), new Vector2(150f, 34f), CreateNewChannel);
        CreateButton(root, "×", new Vector2(250f, 210f), new Vector2(36f, 36f), CloseMenu);

        var viewport = MakeRect("ChannelViewport", root, new Vector2(520f, 330f), new Vector2(0f, -35f), new Color(0.04f, 0.05f, 0.07f, 0.96f));
        var mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;
        channelContent = MakeRect("ChannelContent", viewport, new Vector2(500f, 44f), Vector2.zero, null);
        channelContent.anchorMin = new Vector2(0.5f, 1f);
        channelContent.anchorMax = new Vector2(0.5f, 1f);
        channelContent.pivot = new Vector2(0.5f, 1f);
        channelContent.anchoredPosition = Vector2.zero;
        RefreshChannelList();
        EnsureEventSystem();

        // Input freezing is deliberately last.  If Moon Sweepers changes its
        // player-controller implementation, a failed lookup must not prevent
        // the configuration window from being created.
        try
        {
            FreezePlayerInput();
        }
        catch (Exception exception)
        {
            QuantumNetwork.Diagnostic($"Player input freeze skipped: {exception.GetType().Name}: {exception.Message}");
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void RefreshChannelList()
    {
        if (channelContent == null) return;
        for (var i = channelContent.childCount - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(channelContent.GetChild(i).gameObject);

        var summaries = QuantumNetwork.Summaries();
        var rowHeight = 44f;
        var topPadding = 10f;
        channelContent.sizeDelta = new Vector2(500f, Math.Max(44f, topPadding + summaries.Count * rowHeight));
        channelScrollOffset = 0f;
        channelContent.anchoredPosition = Vector2.zero;
        if (summaries.Count == 0)
        {
            AddText(channelContent, "No channels yet. Press New channel to create one.", new Vector2(0f, -24f),
                new Vector2(480f, 34f), 16, new Color(0.72f, 0.75f, 0.82f), TextAnchor.MiddleCenter);
            return;
        }

        for (var i = 0; i < summaries.Count; i++)
        {
            var summary = summaries[i];
            var row = MakeRect("ChannelRow", channelContent, new Vector2(500f, 38f), new Vector2(0f, -topPadding - i * rowHeight),
                new Color(0.10f, 0.12f, 0.16f, 1f));
            row.anchorMin = new Vector2(0.5f, 1f);
            row.anchorMax = new Vector2(0.5f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.anchoredPosition = new Vector2(0f, -topPadding - i * rowHeight);
            if (summary.Id == editingChannelId)
            {
                var editor = CreateInputField(row, new Vector2(-112f, 0f), new Vector2(220f, 30f));
                editor.text = summary.Name;
                CreateButton(row, "Save", new Vector2(38f, 0f), new Vector2(62f, 28f),
                    () => SaveName(summary.Id, editor));
            }
            else
            {
                AddText(row, $"{summary.Name}  ({summary.Outputs} outputs / {summary.Inputs} inputs)",
                    new Vector2(-112f, 0f), new Vector2(220f, 32f), 14, Color.white, TextAnchor.MiddleLeft);
                var assigned = QuantumNetwork.ChannelFor(menuEndpointId) == summary.Id;
                CreateButton(row, assigned ? "Unassign" : "Assign", new Vector2(38f, 0f), new Vector2(72f, 28f),
                    () => ToggleChannel(summary.Id));
                CreateButton(row, "Rename", new Vector2(115f, 0f), new Vector2(82f, 28f),
                    () => BeginRename(summary.Id));
            }
            CreateButton(row, "×", new Vector2(220f, 0f), new Vector2(34f, 28f),
                () => DeleteChannel(summary.Id));
        }

    }

    private void BeginRename(string channelId)
    {
        editingChannelId = channelId;
        RefreshChannelList();
    }

    private void ToggleChannel(string channelId)
    {
        if (!Moon.IsAuthority)
            return;

        if (QuantumNetwork.ChannelFor(menuEndpointId) == channelId)
        {
            if (QuantumNetwork.UnassignEndpoint(menuEndpointId)) selectedChannelId = string.Empty;
        }
        else if (QuantumNetwork.AssignEndpoint(menuEndpointId, channelId))
        {
            selectedChannelId = channelId;
        }
        RefreshChannelList();
    }

    private void CreateNewChannel()
    {
        if (!Moon.IsAuthority)
            return;

        if (QuantumNetwork.CreateChannel(out var channelId))
        {
            RefreshChannelList();
        }
    }

    private void SaveName(string channelId, InputField field)
    {
        if (!Moon.IsAuthority)
            return;

        if (QuantumNetwork.RenameChannel(channelId, field == null ? string.Empty : field.text))
        {
            editingChannelId = string.Empty;
            RefreshChannelList();
        }
    }

    private void DeleteChannel(string channelId)
    {
        if (!Moon.IsAuthority)
            return;

        if (QuantumNetwork.DeleteChannel(channelId))
        {
            if (selectedChannelId == channelId) selectedChannelId = string.Empty;
            if (editingChannelId == channelId) editingChannelId = string.Empty;
            RefreshChannelList();
        }
    }

    private void CloseMenu()
    {
        if (menuCanvas != null) Destroy(menuCanvas);
        menuCanvas = null;
        menuRaycaster = null;
        if (previousInputModule != null) previousInputModule.enabled = previousInputModuleEnabled;
        previousInputModule = null;
        channelContent = null;
        selectedChannelId = string.Empty;
        editingChannelId = string.Empty;
        RestorePlayerInput();
    }

    private void FreezePlayerInput()
    {
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        var found = FindWalker();
        if (found != null)
        {
            previousUiBlocked = found.UIBlocked;
            previousWalkerEnabled = found.enabled;
            playerInputAsset = found.inputTemplate;
            if (playerInputAsset != null)
            {
                previousPlayerInputEnabled = playerInputAsset.enabled;
                playerInputAsset.Disable();
            }
            found.UIBlocked = true;
            found.enabled = false;
        }
        else
        {
            QuantumNetwork.Diagnostic("Player controller was not found from the active camera; disabling live input assets as fallback");
            DisableFallbackInputAssets();
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void RestorePlayerInput()
    {
        try
        {
            var found = FindWalker();
            if (found != null)
            {
                found.UIBlocked = previousUiBlocked;
                found.enabled = previousWalkerEnabled;
            }
            if (playerInputAsset != null && previousPlayerInputEnabled) playerInputAsset.Enable();
            playerInputAsset = null;
            foreach (var asset in fallbackInputAssets)
            {
                if (asset != null) asset.Enable();
            }
            fallbackInputAssets.Clear();
        }
        catch (Exception exception)
        {
            QuantumNetwork.Diagnostic($"Player input restore skipped: {exception.GetType().Name}: {exception.Message}");
        }
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
    }

    public void LateUpdate()
    {
        if (menuCanvas != null) KeepCursorForMenu();
    }

    private static void KeepCursorForMenu()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private MoonWalker FindWalker()
    {
        if (walker != null) return walker;

        // The preview build does not reliably expose Unity's global
        // FindObjectOfType bridge.  The player camera is parented to the
        // player, so resolve MoonWalker from that hierarchy instead.
        var camera = ActiveCamera();
        if (camera != null)
        {
            var found = camera.GetComponent(Il2CppType.Of<MoonWalker>());
            if (found == null) found = camera.GetComponentInParent(Il2CppType.Of<MoonWalker>());
            if (found == null) found = camera.transform.root.GetComponentInChildren(Il2CppType.Of<MoonWalker>());
            walker = found as MoonWalker;
            if (walker != null) return walker;
        }

        // Preview 1.0.5 can leave the player outside the camera hierarchy and
        // outside the scene-root walk. Try Unity's global component search as a
        // final direct lookup before falling back to input suppression.
        try
        {
            var globalWalkers = UnityEngine.Object.FindObjectsOfType<MoonWalker>(true);
            foreach (var candidate in globalWalkers)
            {
                if (candidate == null) continue;
                walker = candidate;
                QuantumNetwork.Diagnostic($"Player controller found by global search: {walker.gameObject.name}");
                return walker;
            }
        }
        catch (Exception exception)
        {
            QuantumNetwork.Diagnostic($"Global player search failed: {exception.GetType().Name}: {exception.Message}");
        }

        // Moon Sweepers' camera is not always parented under the player.  Walk
        // the active scene roots instead of Unity's global FindObjectOfType,
        // whose IL2CPP bridge is unavailable in Preview 1.0.5.
        for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            var scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded) continue;
            var roots = scene.GetRootGameObjects();
            foreach (var root in roots)
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

        // The player can also live in Unity's persistent scene, which is not
        // returned by SceneManager.sceneCount.
        var persistentObjects = Resources.FindObjectsOfTypeAll(Il2CppType.Of<MoonWalker>());
        foreach (var item in persistentObjects)
        {
            walker = item as MoonWalker;
            if (walker != null && walker.gameObject.scene.IsValid()) return walker;
        }

        return walker;
    }

    private void DisableFallbackInputAssets()
    {
        var disabledNames = new List<string>();
        var assets = Resources.FindObjectsOfTypeAll(Il2CppType.Of<UnityEngine.InputSystem.InputActionAsset>());
        foreach (var item in assets)
        {
            var asset = item as UnityEngine.InputSystem.InputActionAsset;
            if (asset == null || !asset.enabled || IsUiInputAsset(asset)) continue;
            asset.Disable();
            fallbackInputAssets.Add(asset);
            disabledNames.Add(asset.name);
        }

        try
        {
            var globalAssets = UnityEngine.Object.FindObjectsOfType<UnityEngine.InputSystem.InputActionAsset>(true);
            foreach (var asset in globalAssets)
            {
                if (asset == null || !asset.enabled || IsUiInputAsset(asset) || fallbackInputAssets.Contains(asset)) continue;
                asset.Disable();
                fallbackInputAssets.Add(asset);
                disabledNames.Add(asset.name);
            }
        }
        catch (Exception exception)
        {
            QuantumNetwork.Diagnostic($"Global input asset search failed: {exception.GetType().Name}: {exception.Message}");
        }

        QuantumNetwork.Diagnostic($"Fallback input freeze disabled {fallbackInputAssets.Count} asset(s): {string.Join(", ", disabledNames)}");
    }

    private static bool IsUiInputAsset(UnityEngine.InputSystem.InputActionAsset asset)
    {
        var name = asset.name ?? string.Empty;
        return name.IndexOf("ui", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("menu", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static RectTransform MakeRect(string name, Transform parent, Vector2 size, Vector2 position, Color? background)
    {
        var objectRoot = new GameObject(name);
        var rect = objectRoot.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        if (background.HasValue) objectRoot.AddComponent<Image>().color = background.Value;
        return rect;
    }

    private static Text AddText(Transform parent, string text, Vector2 position, Vector2 size, int fontSize, Color color, TextAnchor alignment)
    {
        var rect = MakeRect("Text", parent, size, position, null);
        var label = rect.gameObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.text = text;
        label.fontSize = fontSize;
        label.color = color;
        label.alignment = alignment;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        return label;
    }

    private static InputField CreateInputField(Transform parent, Vector2 position, Vector2 size)
    {
        var root = MakeRect("NameField", parent, size, position, new Color(0.18f, 0.20f, 0.24f, 1f));
        var field = root.gameObject.AddComponent<InputField>();
        field.textComponent = AddText(root, string.Empty, Vector2.zero, size - new Vector2(16f, 4f), 16, Color.white, TextAnchor.MiddleLeft);
        field.textComponent.rectTransform.anchoredPosition = new Vector2(8f, 0f);
        field.characterLimit = 24;
        return field;
    }

    private static Button CreateButton(Transform parent, string label, Vector2 position, Vector2 size, Action action)
    {
        var root = MakeRect("Button", parent, size, position, new Color(0.18f, 0.25f, 0.32f, 1f));
        var image = root.GetComponent<Image>();
        var button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        UnityAction unityAction = action;
        button.onClick.AddListener(unityAction);
        AddText(root, label, Vector2.zero, size - new Vector2(8f, 4f), 14, Color.white, TextAnchor.MiddleCenter);
        return button;
    }

    private void EnsureEventSystem()
    {
        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        if (eventSystem == null)
        {
            var eventSystemObject = new GameObject("Quantum Conveyors EventSystem");
            eventSystem = eventSystemObject.AddComponent<UnityEngine.EventSystems.EventSystem>();
            DontDestroyOnLoad(eventSystemObject);
        }

        // The game module and our direct raycast handler must not both process
        // the same click, or actions such as New channel can fire twice.
        previousInputModule = eventSystem.GetComponent<UnityEngine.EventSystems.BaseInputModule>();
        if (previousInputModule != null)
        {
            previousInputModuleEnabled = previousInputModule.enabled;
            previousInputModule.enabled = false;
        }
    }

    private void HandleMenuClick()
    {
        if (menuRaycaster == null || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        var eventSystem = UnityEngine.EventSystems.EventSystem.current;
        if (eventSystem == null) return;

        var pointer = new UnityEngine.EventSystems.PointerEventData(eventSystem)
        {
            position = Mouse.current.position.ReadValue()
        };
        var results = new Il2CppSystem.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        menuRaycaster.Raycast(pointer, results);
        foreach (var result in results)
        {
            var button = result.gameObject.GetComponentInParent<Button>();
            if (button != null)
            {
                button.onClick.Invoke();
                return;
            }

            var field = result.gameObject.GetComponentInParent<InputField>();
            if (field != null)
            {
                eventSystem.SetSelectedGameObject(field.gameObject);
                field.ActivateInputField();
                return;
            }
        }
    }

    private void HandleMenuScroll()
    {
        if (channelContent == null || Mouse.current == null) return;
        var delta = Mouse.current.scroll.ReadValue().y;
        if (Math.Abs(delta) < 0.01f) return;
        var viewportHeight = 330f;
        var maxOffset = Mathf.Max(0f, channelContent.rect.height - viewportHeight);
        if (maxOffset <= 0f) return;
        var step = Math.Abs(delta) > 10f ? delta / 6f : delta * 30f;
        channelScrollOffset = Mathf.Clamp(channelScrollOffset - step, 0f, maxOffset);
        channelContent.anchoredPosition = new Vector2(0f, channelScrollOffset);
    }

    private static QuantumEndpointView? FindTarget(Camera camera, IReadOnlyList<QuantumEndpointView> endpoints)
    {
        if (endpoints == null || endpoints.Count == 0) return null;

        var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        QuantumEndpointView? best = null;

        foreach (var endpoint in endpoints)
        {
            if (endpoint.View == null) continue;
            var bounds = EndpointBounds(endpoint.View);
            var worldPoint = bounds.center;
            var toTarget = worldPoint - camera.transform.position;
            var distance = toTarget.magnitude;
            if (distance > 12f || distance < 0.01f) continue;

            var screenPoint = camera.WorldToScreenPoint(worldPoint);
            if (screenPoint.z <= 0f) continue;
            var guiPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            GetScreenAimRadii(camera, bounds, out var aimRadiusX, out var aimRadiusY);
            var normalizedX = (guiPoint.x - center.x) / aimRadiusX;
            var normalizedY = (guiPoint.y - center.y) / aimRadiusY;
            var aimDistance = normalizedX * normalizedX + normalizedY * normalizedY;
            if (aimDistance > 1f || !HasClearLineOfSight(camera, worldPoint, distance, endpoint, endpoints)) continue;

            best = endpoint;
        }

        return best;
    }

    private static Bounds EndpointBounds(GameObject endpoint)
    {
        var renderers = endpoint.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return new Bounds(endpoint.transform.position, Vector3.one * 0.5f);

        var bounds = renderers[0].bounds;
        for (var index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        return bounds;
    }

    private static void GetScreenAimRadii(Camera camera, Bounds bounds, out float radiusX, out float radiusY)
    {
        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var minY = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;
        var center = bounds.center;
        var extents = bounds.extents;

        for (var x = -1; x <= 1; x += 2)
        for (var y = -1; y <= 1; y += 2)
        for (var z = -1; z <= 1; z += 2)
        {
            var screenPoint = camera.WorldToScreenPoint(center + Vector3.Scale(extents, new Vector3(x, y, z)));
            if (screenPoint.z <= 0f) continue;
            minX = Mathf.Min(minX, screenPoint.x);
            maxX = Mathf.Max(maxX, screenPoint.x);
            var guiY = Screen.height - screenPoint.y;
            minY = Mathf.Min(minY, guiY);
            maxY = Mathf.Max(maxY, guiY);
        }

        radiusX = (float.IsInfinity(minX) ? 180f : Mathf.Max(180f, (maxX - minX) * 0.5f + 32f)) * 1.25f;
        radiusY = (float.IsInfinity(minY) ? 120f : Mathf.Max(120f, (maxY - minY) * 0.5f + 32f)) * 1.25f;
    }

    private static bool HasClearLineOfSight(Camera camera, Vector3 target, float distance,
        QuantumEndpointView endpoint, IReadOnlyList<QuantumEndpointView> endpoints)
    {
        var direction = (target - camera.transform.position).normalized;
        if (!Physics.Raycast(camera.transform.position, direction, out var hit, distance + 0.05f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return true;

        var hitEndpoint = EndpointForHit(hit, endpoints);
        if (hitEndpoint.HasValue && hitEndpoint.Value.Id == endpoint.Id) return true;

        // The endpoint intentionally shares space with the short conveyor
        // beneath it.  Treat a hit at nearly the same depth as that overlap,
        // but reject a real structure in front of the adapter.
        return hit.distance >= distance - 0.25f;
    }

    private static QuantumEndpointView? EndpointForHit(RaycastHit hit, IReadOnlyList<QuantumEndpointView> endpoints)
    {
        var hitTransform = hit.collider == null ? null : hit.collider.transform;
        if (hitTransform == null) return null;

        foreach (var endpoint in endpoints)
        {
            if (endpoint.View == null) continue;
            var endpointTransform = endpoint.View.transform;
            if (hitTransform == endpointTransform || hitTransform.IsChildOf(endpointTransform) || endpointTransform.IsChildOf(hitTransform))
                return endpoint;
        }

        return null;
    }
}
