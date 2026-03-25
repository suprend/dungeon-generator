using TMPro;
using DanverPlayground.Roguelike.Characters.Abilities;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerClassRuntime))]
public sealed class PlayerAbilityCooldownHud : MonoBehaviour
{
    [SerializeField] private PlayerClassRuntime playerClassRuntime;
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);
    [SerializeField] private Vector2 panelAnchorPosition = new Vector2(28f, -28f);

    private GameObject canvasRoot;
    private readonly TextMeshProUGUI[] slotLabels = new TextMeshProUGUI[PlayerClassRuntime.AbilitySlotCount];

    private void Reset()
    {
        if (playerClassRuntime == null)
            playerClassRuntime = GetComponent<PlayerClassRuntime>();
    }

    private void Awake()
    {
        if (playerClassRuntime == null)
            playerClassRuntime = GetComponent<PlayerClassRuntime>();
    }

    private void Update()
    {
        if (playerClassRuntime == null)
            return;

        if (!playerClassRuntime.PlayerInputEnabled)
        {
            if (canvasRoot != null)
                canvasRoot.SetActive(false);

            return;
        }

        EnsureUi();
        canvasRoot.SetActive(true);
        RefreshAll();
    }

    private void OnDestroy()
    {
        if (canvasRoot != null)
            Destroy(canvasRoot);
    }

    private void EnsureUi()
    {
        if (canvasRoot != null)
            return;

        canvasRoot = new GameObject(
            "PlayerAbilityCooldownCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        var canvas = canvasRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue - 10;

        var scaler = canvasRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var panel = CreateUiObject("CooldownPanel", canvasRoot.transform);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = panelAnchorPosition;
        panelRect.sizeDelta = new Vector2(340f, 156f);

        var panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.06f, 0.07f, 0.10f, 0.82f);

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 12, 12);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = CreateText(panel.transform, "Abilities", 26f, FontStyles.Bold);
        header.color = new Color(0.95f, 0.96f, 0.98f, 1f);

        for (var i = 0; i < slotLabels.Length; i++)
        {
            slotLabels[i] = CreateText(panel.transform, string.Empty, 22f, FontStyles.Normal);
            slotLabels[i].color = new Color(0.86f, 0.90f, 0.96f, 1f);
        }
    }

    private void RefreshAll()
    {
        if (playerClassRuntime == null)
            return;

        EnsureUi();

        for (var i = 0; i < slotLabels.Length; i++)
        {
            if (slotLabels[i] == null)
                continue;

            var ability = playerClassRuntime.GetAbility(i);
            var shouldShow = !ShouldHideFromHud(ability);
            if (slotLabels[i].gameObject.activeSelf != shouldShow)
                slotLabels[i].gameObject.SetActive(shouldShow);

            if (!shouldShow)
                continue;

            var key = playerClassRuntime.GetAbilityKey(i);
            if (ability == null)
            {
                slotLabels[i].text = $"{FormatKey(key)}: Empty";
                slotLabels[i].color = new Color(0.60f, 0.64f, 0.70f, 1f);
                continue;
            }

            var remaining = playerClassRuntime.GetAbilityCooldownRemaining(i);
            if (remaining <= 0.001f)
            {
                slotLabels[i].text = $"{FormatKey(key)}: {ability.AbilityName}  READY";
                slotLabels[i].color = new Color(0.56f, 0.95f, 0.68f, 1f);
            }
            else
            {
                slotLabels[i].text = $"{FormatKey(key)}: {ability.AbilityName}  {remaining:0.0}s";
                slotLabels[i].color = new Color(0.98f, 0.82f, 0.45f, 1f);
            }
        }
    }

    private static string FormatKey(KeyCode key)
    {
        return key switch
        {
            KeyCode.Alpha1 => "1",
            KeyCode.Alpha2 => "2",
            KeyCode.Alpha3 => "3",
            KeyCode.Keypad1 => "Num1",
            KeyCode.Keypad2 => "Num2",
            KeyCode.Keypad3 => "Num3",
            _ => key == KeyCode.None ? "-" : key.ToString()
        };
    }

    private static bool ShouldHideFromHud(ActiveAbilityDefinition ability)
    {
        return ability is DashAbilityDefinition;
    }

    private static GameObject CreateUiObject(string objectName, Transform parent)
    {
        var gameObject = new GameObject(objectName, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string text, float fontSize, FontStyles fontStyle)
    {
        var labelObject = CreateUiObject("Label", parent);
        var label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = fontStyle;
        label.alignment = TextAlignmentOptions.Left;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        label.font = GetHudFont();
        return label;
    }

    private static TMP_FontAsset GetHudFont()
    {
        if (TMP_Settings.defaultFontAsset != null)
            return TMP_Settings.defaultFontAsset;

        return Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
    }
}
