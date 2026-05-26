//DP
using UnityEngine;

public sealed class SceneTextWindowOverlay : MonoBehaviour
{
    [SerializeField] private bool visible = true;
    [SerializeField, TextArea(3, 8)] private string windowText = string.Empty;
    [SerializeField] private Vector2 windowSize = new Vector2(430f, 170f);
    [SerializeField] private Vector2 bottomLeftOffset = new Vector2(30f, 30f);
    [SerializeField, Min(8)] private int fontSize = 18;
    [SerializeField, Min(0f)] private float screenPadding = 12f;

    private void OnGUI()
    {
        if (!visible)
        {
            return;
        }

        Font previousFont = GUI.skin.font;
        Font overlayFont = GameTextFontProvider.LegacyFont;
        if (overlayFont != null)
        {
            GUI.skin.font = overlayFont;
        }

        GUIStyle windowStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            fontSize = Mathf.Max(1, fontSize),
            padding = new RectOffset(18, 18, 14, 14)
        };

        GUI.Box(GetWindowRect(), windowText ?? string.Empty, windowStyle);

        if (overlayFont != null)
        {
            GUI.skin.font = previousFont;
        }
    }

    private Rect GetWindowRect()
    {
        float padding = Mathf.Max(0f, screenPadding);
        float width = Mathf.Min(Mathf.Max(1f, windowSize.x), Mathf.Max(1f, Screen.width - padding * 2f));
        float height = Mathf.Min(Mathf.Max(1f, windowSize.y), Mathf.Max(1f, Screen.height - padding * 2f));
        float maxX = Mathf.Max(padding, Screen.width - width - padding);
        float maxY = Mathf.Max(padding, Screen.height - height - padding);
        float x = Mathf.Clamp(bottomLeftOffset.x, padding, maxX);
        float y = Mathf.Clamp(Screen.height - height - bottomLeftOffset.y, padding, maxY);

        return new Rect(x, y, width, height);
    }
}
