using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class DungeonRunHud : MonoBehaviour
{
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);
    [SerializeField] private Vector2 anchoredPosition = new Vector2(-28f, -28f);
    [SerializeField] private Vector2 panelSize = new Vector2(300f, 92f);
    [SerializeField] private float fontSize = 24f;
    [SerializeField] private Color textColor = new Color(0.95f, 0.97f, 1f, 1f);
    [SerializeField] private Color backgroundColor = new Color(0.04f, 0.05f, 0.08f, 0.78f);

    private GameObject canvasRoot;
    private GameObject panelRoot;
    private TextMeshProUGUI label;

    public void SetValues(int levelIndex, int score, float multiplier)
    {
        EnsureUi();
        label.text = $"Level {Mathf.Max(1, levelIndex)}\nScore {Mathf.Max(0, score)}  x{Mathf.Max(0f, multiplier):0.#}";
    }

    private void OnDestroy()
    {
        if (canvasRoot != null && canvasRoot.transform.parent == transform)
            Destroy(canvasRoot);
    }

    private void EnsureUi()
    {
        if (panelRoot != null && label != null)
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
                "DungeonRunCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasRoot.transform.SetParent(transform, false);

            canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue - 30;

            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            uiParent = canvasRoot.transform;
        }

        panelRoot = CreateUiObject("DungeonRunPanel", uiParent);
        var panelRect = panelRoot.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.anchoredPosition = anchoredPosition;
        panelRect.sizeDelta = panelSize;

        var panelImage = panelRoot.AddComponent<Image>();
        panelImage.color = backgroundColor;
        panelImage.raycastTarget = false;

        label = CreateText(panelRoot.transform);
    }

    private TextMeshProUGUI CreateText(Transform parent)
    {
        var labelObject = CreateUiObject("DungeonRunLabel", parent);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(16f, 8f);
        labelRect.offsetMax = new Vector2(-16f, -8f);

        var text = labelObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.MidlineRight;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        text.color = textColor;
        text.font = GetHudFont();
        return text;
    }

    private static GameObject CreateUiObject(string objectName, Transform parent)
    {
        var gameObject = new GameObject(objectName, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static TMP_FontAsset GetHudFont()
    {
        return GameTextFontProvider.TmpFont;
    }
}
