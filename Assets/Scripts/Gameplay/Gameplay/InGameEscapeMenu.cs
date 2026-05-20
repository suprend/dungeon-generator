using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class InGameEscapeMenu : MonoBehaviour
{
    private const string DefaultMainMenuSceneName = "MainMenuScene";

    [SerializeField] private KeyCode toggleKey = KeyCode.Escape;
    [SerializeField] private string mainMenuSceneName = DefaultMainMenuSceneName;
    [SerializeField] private string titleText = "Меню";
    [SerializeField] private string continueButtonText = "Продолжить";
    [SerializeField] private string mainMenuButtonText = "В меню";
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);

    private static InGameEscapeMenu instance;

    private GameObject canvasRoot;
    private Button continueButton;
    private bool isVisible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    private static InGameEscapeMenu EnsureInstance()
    {
        if (instance != null)
        {
            return instance;
        }

        instance = FindObjectOfType<InGameEscapeMenu>();
        if (instance != null)
        {
            return instance;
        }

        GameObject host = new GameObject(nameof(InGameEscapeMenu));
        instance = host.AddComponent<InGameEscapeMenu>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += HandleSceneLoaded;

        EnsureUi();
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            instance = null;
        }

        if (canvasRoot != null)
        {
            Destroy(canvasRoot);
        }
    }

    private void Update()
    {
        if (!CanShowInCurrentScene())
        {
            if (isVisible)
            {
                SetVisible(false);
            }

            return;
        }

        if (Time.timeScale <= 0f && !isVisible)
        {
            return;
        }

        if (Input.GetKeyDown(toggleKey))
        {
            SetVisible(!isVisible);
        }
    }

    private void ContinueGame()
    {
        SetVisible(false);
    }

    private void ReturnToMainMenu()
    {
        SetVisible(false);

        if (!string.IsNullOrWhiteSpace(mainMenuSceneName)
            && Application.CanStreamedLevelBeLoaded(mainMenuSceneName))
        {
            SceneManager.LoadScene(mainMenuSceneName);
            return;
        }

        SceneManager.LoadScene(0);
    }

    private void SetVisible(bool visible)
    {
        isVisible = visible;

        if (canvasRoot != null)
        {
            canvasRoot.SetActive(visible);
        }

        if (visible && continueButton != null)
        {
            continueButton.Select();
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SetVisible(false);
    }

    private bool CanShowInCurrentScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        return activeScene.IsValid()
            && !string.Equals(activeScene.name, mainMenuSceneName, System.StringComparison.Ordinal);
    }

    private void EnsureUi()
    {
        if (canvasRoot == null)
        {
            BuildUi();
        }

        EnsureEventSystem();
    }

    private void BuildUi()
    {
        canvasRoot = new GameObject(
            "InGameEscapeMenuCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasRoot.transform.SetParent(transform, false);

        Canvas canvas = canvasRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue - 5;

        CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GameObject overlay = CreateUiObject("Overlay", canvasRoot.transform);
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        StretchToParent(overlayRect);

        Image overlayImage = overlay.AddComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.42f);

        GameObject panel = CreateUiObject("Panel", overlay.transform);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(360f, 220f);

        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.07f, 0.08f, 0.10f, 0.94f);

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 14f;
        layout.padding = new RectOffset(24, 24, 24, 24);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateTitle(panel.transform);
        continueButton = CreateButton(panel.transform, continueButtonText, ContinueGame);
        CreateButton(panel.transform, mainMenuButtonText, ReturnToMainMenu);
    }

    private void CreateTitle(Transform parent)
    {
        GameObject titleObject = CreateUiObject("Title", parent);
        LayoutElement layoutElement = titleObject.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = 42f;

        TextMeshProUGUI label = titleObject.AddComponent<TextMeshProUGUI>();
        label.text = titleText;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.fontSize = 30f;
        label.fontStyle = FontStyles.Bold;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        label.font = GetUiFont();
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        new GameObject(
            "InGameEscapeMenuEventSystem",
            typeof(EventSystem),
            typeof(StandaloneInputModule));
    }

    private static Button CreateButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = CreateUiObject(text + "Button", parent);
        LayoutElement layoutElement = buttonObject.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = 300f;
        layoutElement.preferredHeight = 54f;
        layoutElement.minHeight = 54f;

        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.16f, 0.18f, 0.22f, 0.98f);

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        ColorBlock colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = new Color(0.24f, 0.27f, 0.34f, 1f);
        colors.pressedColor = new Color(0.11f, 0.12f, 0.15f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.16f, 0.18f, 0.22f, 0.45f);
        button.colors = colors;

        GameObject labelObject = CreateUiObject("Label", buttonObject.transform);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        StretchToParent(labelRect);

        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.fontSize = 23f;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        label.font = GetUiFont();

        return button;
    }

    private static GameObject CreateUiObject(string objectName, Transform parent)
    {
        GameObject gameObject = new GameObject(objectName, typeof(RectTransform));
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

    private static TMP_FontAsset GetUiFont()
    {
        return GameTextFontProvider.TmpFont;
    }
}
