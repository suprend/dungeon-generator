using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public sealed class PlayerDeathRestartMenu : MonoBehaviour
{
    [SerializeField] private string restartButtonText = "Restart";
    [SerializeField] private string exitButtonText = "Exit";
    [SerializeField] private KeyCode restartKey = KeyCode.R;
    [SerializeField] private KeyCode exitKey = KeyCode.Escape;

    private static PlayerDeathRestartMenu instance;

    private bool isVisible;
    private float previousTimeScale = 1f;

    private GameObject canvasRoot;
    private Button restartButton;

    public static void Show()
    {
        var menu = EnsureInstance();
        Debug.Log($"[PlayerDeathRestartMenu] Show requested. Instance='{menu.gameObject.name}'.");
        menu.ShowMenu();
    }

    private static PlayerDeathRestartMenu EnsureInstance()
    {
        if (instance != null)
        {
            Debug.Log($"[PlayerDeathRestartMenu] Using cached instance on '{instance.gameObject.name}'.");
            return instance;
        }

        instance = FindObjectOfType<PlayerDeathRestartMenu>();
        if (instance != null)
        {
            Debug.Log($"[PlayerDeathRestartMenu] Found scene instance on '{instance.gameObject.name}'.");
            return instance;
        }

        var host = new GameObject("PlayerDeathRestartMenu");
        instance = host.AddComponent<PlayerDeathRestartMenu>();
        Debug.Log("[PlayerDeathRestartMenu] Created runtime host because scene instance was not found.");
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.Log($"[PlayerDeathRestartMenu] Destroying duplicate instance on '{gameObject.name}'.");
            Destroy(gameObject);
            return;
        }

        instance = this;
        SceneManager.sceneLoaded += HandleSceneLoaded;

        Debug.Log($"[PlayerDeathRestartMenu] Awake on '{gameObject.name}' in scene '{SceneManager.GetActiveScene().name}'.");
        EnsureUi();
        SetVisible(false, restoreTimeScale: true);
    }

    private void OnDestroy()
    {
        if (instance == this)
            SceneManager.sceneLoaded -= HandleSceneLoaded;

        if (canvasRoot != null)
            Destroy(canvasRoot);
    }

    private void OnDisable()
    {
        if (instance == this)
            SetVisible(false, restoreTimeScale: true);
    }

    private void Update()
    {
        if (!isVisible)
            return;

        if (Input.GetKeyDown(restartKey) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            Debug.Log("[PlayerDeathRestartMenu] Restart hotkey pressed.");
            RestartScene();
            return;
        }

        if (Input.GetKeyDown(exitKey))
        {
            Debug.Log("[PlayerDeathRestartMenu] Exit hotkey pressed.");
            ExitGame();
        }
    }

    private void ShowMenu()
    {
        if (isVisible)
        {
            Debug.Log("[PlayerDeathRestartMenu] Show ignored because menu is already visible.");
            return;
        }

        EnsureUi();
        SetVisible(true, restoreTimeScale: false);

        if (restartButton != null)
            restartButton.Select();
    }

    private void RestartScene()
    {
        SetVisible(false, restoreTimeScale: true);

        var activeScene = SceneManager.GetActiveScene();
        Debug.Log($"[PlayerDeathRestartMenu] Restarting scene '{activeScene.name}'.");

        if (activeScene.buildIndex >= 0)
        {
            SceneManager.LoadScene(activeScene.buildIndex);
            return;
        }

        SceneManager.LoadScene(activeScene.path);
    }

    private void ExitGame()
    {
        SetVisible(false, restoreTimeScale: true);
        Debug.Log("[PlayerDeathRestartMenu] Exit requested.");

#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void SetVisible(bool visible, bool restoreTimeScale)
    {
        isVisible = visible;

        if (visible)
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }
        else if (restoreTimeScale)
        {
            Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;
        }

        if (canvasRoot != null)
            canvasRoot.SetActive(visible);

        Debug.Log($"[PlayerDeathRestartMenu] SetVisible({visible}) canvasRoot={(canvasRoot != null ? canvasRoot.name : "null")} timeScale={Time.timeScale}.");
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"[PlayerDeathRestartMenu] Scene loaded: '{scene.name}' mode={mode}.");
        EnsureUi();
        SetVisible(false, restoreTimeScale: true);
    }

    private void EnsureUi()
    {
        if (canvasRoot == null)
        {
            Debug.Log("[PlayerDeathRestartMenu] Creating DeathMenuCanvas.");
            BuildUi();
        }

        EnsureEventSystem();
    }

    private void BuildUi()
    {
        canvasRoot = new GameObject(
            "DeathMenuCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        var canvas = canvasRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        var scaler = canvasRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var overlay = CreateUiObject("Overlay", canvasRoot.transform);
        var overlayRect = overlay.GetComponent<RectTransform>();
        StretchToParent(overlayRect);

        var overlayImage = overlay.AddComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.55f);

        var panel = CreateUiObject("Panel", overlay.transform);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(320f, 140f);

        var panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.08f, 0.08f, 0.08f, 0.92f);

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 16f;
        layout.padding = new RectOffset(20, 20, 20, 20);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        restartButton = CreateButton(panel.transform, restartButtonText, RestartScene);
        CreateButton(panel.transform, exitButtonText, ExitGame);

        Debug.Log("[PlayerDeathRestartMenu] DeathMenuCanvas created successfully.");
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            Debug.Log($"[PlayerDeathRestartMenu] Reusing EventSystem '{EventSystem.current.gameObject.name}'.");
            return;
        }

        var eventSystemObject = new GameObject(
            "DeathMenuEventSystem",
            typeof(EventSystem),
            typeof(StandaloneInputModule));

        Debug.Log("[PlayerDeathRestartMenu] Created DeathMenuEventSystem.");
    }

    private static GameObject CreateUiObject(string objectName, Transform parent)
    {
        var gameObject = new GameObject(objectName, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static void StretchToParent(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }

    private static TMP_FontAsset GetButtonFont()
    {
        if (TMP_Settings.defaultFontAsset != null)
            return TMP_Settings.defaultFontAsset;

        return Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
    }

    private static Button CreateButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick)
    {
        var buttonObject = CreateUiObject(text + "Button", parent);
        var layoutElement = buttonObject.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = 280f;
        layoutElement.preferredHeight = 56f;
        layoutElement.minHeight = 56f;

        var image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.16f, 0.16f, 0.16f, 0.96f);

        var button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        var colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = new Color(0.22f, 0.22f, 0.22f, 1f);
        colors.pressedColor = new Color(0.10f, 0.10f, 0.10f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.16f, 0.16f, 0.16f, 0.5f);
        button.colors = colors;

        var labelObject = CreateUiObject("Label", buttonObject.transform);
        var labelRect = labelObject.GetComponent<RectTransform>();
        StretchToParent(labelRect);

        var label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.fontSize = 24f;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        label.font = GetButtonFont();

        Debug.Log($"[PlayerDeathRestartMenu] Created TMP label '{text}' with font='{(label.font != null ? label.font.name : "null")}'.");

        return button;
    }
}
