using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class InteractionPromptHud : MonoBehaviour
{
    private static InteractionPromptHud instance;
    private static Object promptOwner;

    [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);
    [SerializeField] private Vector2 anchoredPosition = new Vector2(0f, 128f);
    [SerializeField] private Vector2 panelSize = new Vector2(360f, 48f);
    [SerializeField] private float fontSize = 24f;
    [SerializeField] private Color textColor = new Color(0.95f, 0.97f, 1f, 1f);
    [SerializeField] private Color backgroundColor = new Color(0.04f, 0.05f, 0.08f, 0.78f);

    private GameObject canvasRoot;
    private GameObject panelRoot;
    private TextMeshProUGUI promptLabel;

    public static void Show(Object owner, string prompt)
    {
        if (owner == null)
            return;

        var hud = EnsureInstance();
        promptOwner = owner;
        hud.SetPrompt(prompt);
    }

    public static void Hide(Object owner)
    {
        if (instance == null || promptOwner != owner)
            return;

        promptOwner = null;
        instance.SetVisible(false);
    }

    private static InteractionPromptHud EnsureInstance()
    {
        if (instance != null)
            return instance;

        instance = FindObjectOfType<InteractionPromptHud>();
        if (instance != null)
            return instance;

        var host = new GameObject("InteractionPromptHud");
        instance = host.AddComponent<InteractionPromptHud>();
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
        EnsureUi();
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;

        if (canvasRoot != null && canvasRoot.transform.parent == transform)
            Destroy(canvasRoot);
    }

    private void SetPrompt(string prompt)
    {
        EnsureUi();

        promptLabel.text = string.IsNullOrWhiteSpace(prompt)
            ? "Press F to interact"
            : prompt;
        SetVisible(true);
    }

    private void EnsureUi()
    {
        if (panelRoot != null && promptLabel != null)
            return;

        var canvas = GetComponentInParent<Canvas>();
        Transform uiParent;
        if (canvas != null)
        {
            uiParent = canvas.transform;
        }
        else
        {
            canvasRoot = new GameObject(
                "InteractionPromptCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasRoot.transform.SetParent(transform, false);

            canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue - 20;

            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            uiParent = canvasRoot.transform;
        }

        panelRoot = CreateUiObject("InteractionPromptPanel", uiParent);
        var panelRect = panelRoot.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = anchoredPosition;
        panelRect.sizeDelta = panelSize;

        var panelImage = panelRoot.AddComponent<Image>();
        panelImage.color = backgroundColor;
        panelImage.raycastTarget = false;

        promptLabel = CreateText(panelRoot.transform);
    }

    private void SetVisible(bool visible)
    {
        if (panelRoot != null && panelRoot.activeSelf != visible)
            panelRoot.SetActive(visible);
    }

    private TextMeshProUGUI CreateText(Transform parent)
    {
        var labelObject = CreateUiObject("InteractionPromptLabel", parent);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(18f, 0f);
        labelRect.offsetMax = new Vector2(-18f, 0f);

        var label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = string.Empty;
        label.fontSize = fontSize;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        label.color = textColor;
        label.font = GetHudFont();
        return label;
    }

    private static GameObject CreateUiObject(string objectName, Transform parent)
    {
        var gameObject = new GameObject(objectName, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static TMP_FontAsset GetHudFont()
    {
        if (TMP_Settings.defaultFontAsset != null)
            return TMP_Settings.defaultFontAsset;

        return Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
    }
}
