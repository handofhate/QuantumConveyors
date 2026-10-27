using System;
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
    private InputField nameField;
    private Text menuStatus;
    private RectTransform channelContent;
    private string selectedChannelId = string.Empty;
    private int menuEndpointId;
    private MoonWalker walker;
    private bool previousUiBlocked;
    private bool previousWalkerEnabled;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private GraphicRaycaster menuRaycaster;
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
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) CloseMenu();
                else HandleMenuClick();
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
        AddText(root, "Quantum Conveyors", new Vector2(0f, 226f), new Vector2(560f, 34f), 26, Color.white, TextAnchor.MiddleCenter);
        AddText(root, isOutput ? "Configure this output adapter" : "Configure this input adapter",
            new Vector2(0f, 193f), new Vector2(560f, 26f), 16, new Color(0.55f, 0.85f, 1f), TextAnchor.MiddleCenter);
        AddText(root, "Choose a channel, rename it, or delete it. Changes are host-authoritative.",
            new Vector2(0f, 164f), new Vector2(570f, 24f), 13, new Color(0.72f, 0.75f, 0.82f), TextAnchor.MiddleCenter);

        AddText(root, "Selected channel name", new Vector2(-150f, 137f), new Vector2(250f, 22f), 14, Color.white, TextAnchor.MiddleLeft);
        nameField = CreateInputField(root, new Vector2(-40f, 105f), new Vector2(300f, 34f));
        nameField.text = SelectedChannelName();
        CreateButton(root, "Save name", new Vector2(185f, 105f), new Vector2(120f, 34f), SaveName);

        CreateButton(root, "New channel", new Vector2(-205f, 65f), new Vector2(150f, 34f), CreateNewChannel);
        CreateButton(root, "Close", new Vector2(205f, 65f), new Vector2(120f, 34f), CloseMenu);
        menuStatus = AddText(root, Moon.IsAuthority ? "Host controls enabled." : "Only the host can change channels.",
            new Vector2(0f, 65f), new Vector2(230f, 34f), 13, new Color(0.72f, 0.75f, 0.82f), TextAnchor.MiddleCenter);

        var viewport = MakeRect("ChannelViewport", root, new Vector2(570f, 270f), new Vector2(0f, -100f), new Color(0.04f, 0.05f, 0.07f, 0.96f));
        var mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        channelContent = MakeRect("ChannelContent", viewport, new Vector2(548f, 0f), Vector2.zero, null);
        channelContent.anchorMin = new Vector2(0f, 1f);
        channelContent.anchorMax = new Vector2(1f, 1f);
        channelContent.pivot = new Vector2(0.5f, 1f);
        scroll.viewport = viewport;
        scroll.content = channelContent;

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

    private string SelectedChannelName()
    {
        return string.IsNullOrWhiteSpace(selectedChannelId) ? string.Empty : QuantumNetwork.ChannelName(selectedChannelId);
    }

    private void RefreshChannelList()
    {
        if (channelContent == null) return;
        for (var i = channelContent.childCount - 1; i >= 0; i--) Destroy(channelContent.GetChild(i).gameObject);

        var summaries = QuantumNetwork.Summaries();
        var rowHeight = 42f;
        channelContent.sizeDelta = new Vector2(0f, Math.Max(42f, summaries.Count * rowHeight));
        if (summaries.Count == 0)
        {
            AddText(channelContent, "No channels yet. Create one for this adapter.", new Vector2(0f, -24f),
                new Vector2(520f, 34f), 16, new Color(0.72f, 0.75f, 0.82f), TextAnchor.MiddleCenter);
            return;
        }

        for (var i = 0; i < summaries.Count; i++)
        {
            var summary = summaries[i];
            var y = -(i * rowHeight + rowHeight * 0.5f);
            var row = MakeRect("ChannelRow", channelContent, new Vector2(540f, 36f), new Vector2(0f, y),
                new Color(0.10f, 0.12f, 0.16f, 1f));
            AddText(row, $"{summary.Name}  ({summary.Outputs} out / {summary.Inputs} in)",
                new Vector2(-118f, 0f), new Vector2(280f, 32f), 14, Color.white, TextAnchor.MiddleLeft);
            CreateButton(row, "Use", new Vector2(100f, 0f), new Vector2(72f, 28f),
                () => UseChannel(summary.Id));
            CreateButton(row, "Rename", new Vector2(185f, 0f), new Vector2(88f, 28f),
                () => SelectChannel(summary.Id));
            CreateButton(row, "Delete", new Vector2(285f, 0f), new Vector2(82f, 28f),
                () => DeleteChannel(summary.Id));
        }
    }

    private void SelectChannel(string channelId)
    {
        selectedChannelId = channelId;
        if (nameField != null) nameField.text = SelectedChannelName();
        if (menuStatus != null) menuStatus.text = $"Selected: {QuantumNetwork.ChannelName(channelId)}";
    }

    private void UseChannel(string channelId)
    {
        if (!Moon.IsAuthority)
        {
            if (menuStatus != null) menuStatus.text = "Only the host can change channels.";
            return;
        }

        if (QuantumNetwork.AssignEndpoint(menuEndpointId, channelId))
        {
            selectedChannelId = channelId;
            if (nameField != null) nameField.text = SelectedChannelName();
            if (menuStatus != null) menuStatus.text = $"Using: {QuantumNetwork.ChannelName(channelId)}";
            RefreshChannelList();
        }
    }

    private void CreateNewChannel()
    {
        if (!Moon.IsAuthority)
        {
            if (menuStatus != null) menuStatus.text = "Only the host can create channels.";
            return;
        }

        if (QuantumNetwork.CreateAndAssign(menuEndpointId, out var channelId))
        {
            selectedChannelId = channelId;
            if (nameField != null) nameField.text = SelectedChannelName();
            if (menuStatus != null) menuStatus.text = "New channel created.";
            RefreshChannelList();
        }
    }

    private void SaveName()
    {
        if (string.IsNullOrWhiteSpace(selectedChannelId))
        {
            if (menuStatus != null) menuStatus.text = "Select a channel first.";
            return;
        }

        if (!Moon.IsAuthority)
        {
            if (menuStatus != null) menuStatus.text = "Only the host can rename channels.";
            return;
        }

        if (QuantumNetwork.RenameChannel(selectedChannelId, nameField == null ? string.Empty : nameField.text))
        {
            if (menuStatus != null) menuStatus.text = "Channel renamed.";
            RefreshChannelList();
        }
    }

    private void DeleteChannel(string channelId)
    {
        if (!Moon.IsAuthority)
        {
            if (menuStatus != null) menuStatus.text = "Only the host can delete channels.";
            return;
        }

        if (QuantumNetwork.DeleteChannel(channelId))
        {
            if (selectedChannelId == channelId)
            {
                selectedChannelId = string.Empty;
                if (nameField != null) nameField.text = string.Empty;
            }
            if (menuStatus != null) menuStatus.text = "Channel deleted.";
            RefreshChannelList();
        }
    }

    private void CloseMenu()
    {
        if (menuCanvas != null) Destroy(menuCanvas);
        menuCanvas = null;
        menuRaycaster = null;
        nameField = null;
        menuStatus = null;
        channelContent = null;
        selectedChannelId = string.Empty;
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
            found.UIBlocked = true;
            found.enabled = false;
        }
        else QuantumNetwork.Diagnostic("Player controller was not found from the active camera");

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

    private static RectTransform MakeRect(string name, Transform parent, Vector2 size, Vector2 position, Color? background)
    {
        var objectRoot = new GameObject(name);
        var rect = objectRoot.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
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

    private static void EnsureEventSystem()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null) return;
        var eventSystemObject = new GameObject("Quantum Conveyors EventSystem");
        eventSystemObject.AddComponent<UnityEngine.EventSystems.EventSystem>();
        DontDestroyOnLoad(eventSystemObject);
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

    private static QuantumEndpointView? FindTarget(Camera camera, IReadOnlyList<QuantumEndpointView> endpoints)
    {
        var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        var bestPixels = 300f;
        QuantumEndpointView? best = null;

        foreach (var endpoint in endpoints)
        {
            if (endpoint.View == null) continue;
            var worldPoint = endpoint.View.transform.TransformPoint(new Vector3(0f, 1.0f, 0f));
            var toTarget = worldPoint - camera.transform.position;
            if (toTarget.magnitude > 6f || Vector3.Angle(camera.transform.forward, toTarget) > 18f) continue;
            var screenPoint = camera.WorldToScreenPoint(worldPoint);
            if (screenPoint.z <= 0f) continue;

            var guiPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            var pixels = Vector2.Distance(center, guiPoint);
            if (pixels > bestPixels) continue;
            bestPixels = pixels;
            best = endpoint;
        }

        return best;
    }
}
