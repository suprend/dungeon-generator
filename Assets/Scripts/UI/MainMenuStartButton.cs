//DP
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Загружает выбранную в инспекторе сцену при нажатии кнопки СТАРТ
/// </summary>
public class MainMenuStartButton : MonoBehaviour
{
    [Header("Сцена для старта")]
#if UNITY_EDITOR
    [SerializeField] private SceneAsset targetScene;
#endif
    [SerializeField] private string targetSceneName;

    [Header("Кнопка")]
    [SerializeField] private string buttonText = "Старт";
    [SerializeField] private Vector2 buttonSize = new Vector2(240f, 80f);
    [SerializeField, Range(0.05f, 0.95f)] private float buttonCenterXRatio = 0.28f;
    [SerializeField] private string exitButtonText = "Выход";
    [SerializeField] private Vector2 exitButtonSize = new Vector2(240f, 60f);
    [SerializeField, Min(0f)] private float exitButtonSpacing = 16f;

    [Header("Плашка управления")]
    [SerializeField, TextArea(2, 4)] private string controlsDescription =
        "Управление: WASD - движение, ЛКМ - базовая атака, 1/2/3 - способности, Q/E - смена героя, R - поднять союзника";

    [Header("Описания героев")]
    [SerializeField, TextArea(3, 6)] private string warriorDescription =
        "Воин\n1: притягивает и замедляет врагов.\n2: бьет вокруг себя, расталкивает врагов и получает сопротивление урону.\n3: совершает рывок сквозь врагов.";
    [SerializeField, TextArea(3, 6)] private string rangerDescription =
        "Следопыт\nЛКМ: заряженный выстрел.\n1: выстрел снижает защиту цели.\n2: выпускает 5 снарядов веером.\n3: рывок и временное ускорение.";
    [SerializeField, TextArea(3, 6)] private string mageDescription =
        "Маг\n1: цепной снаряд с отскоками.\n2: большой удар по области с оглушением.\n3: проходящий снаряд с периодическим уроном и отталкиванием.";
    [SerializeField, TextArea(3, 6)] private string priestDescription =
        "Жрец\n1: лечит ближайшего к курсору героя после подготовки.\n2: лечит живых героев, дает щит и бонус урона.\n3: мощная ближняя атака.";

    [Header("Окно обучения")]
    [SerializeField] private Vector2 tutorialWindowSize = new Vector2(780f, 560f);
    [SerializeField, Min(8)] private int tutorialWindowFontSize = 18;
    [SerializeField, Min(0f)] private float tutorialWindowRightOffset = 40f;
    [SerializeField] private float tutorialWindowVerticalOffset;
    [SerializeField, Min(0f)] private float layoutPadding = 24f;

    /// <summary>
    /// Рисует простое главное меню
    /// </summary>
    private void OnGUI()
    {
        Font previousFont = GUI.skin.font;
        Font menuFont = GameTextFontProvider.LegacyFont;
        if (menuFont != null)
        {
            GUI.skin.font = menuFont;
        }

        DrawTutorialWindow();
        DrawStartButton();
        DrawExitButton();

        if (menuFont != null)
        {
            GUI.skin.font = previousFont;
        }
    }

    /// <summary>
    /// Рисует кнопку запуска игры
    /// </summary>
    private void DrawStartButton()
    {
        float buttonWidth = Mathf.Max(1f, buttonSize.x);
        float buttonHeight = Mathf.Max(1f, buttonSize.y);
        Rect buttonRect = new Rect(
            GetButtonX(buttonWidth),
            (Screen.height - buttonHeight) * 0.5f,
            buttonWidth,
            buttonHeight);

        if (GUI.Button(buttonRect, buttonText))
        {
            LoadTargetScene();
        }
    }

    private void DrawExitButton()
    {
        float startButtonHeight = Mathf.Max(1f, buttonSize.y);
        float buttonWidth = Mathf.Max(1f, exitButtonSize.x);
        float buttonHeight = Mathf.Max(1f, exitButtonSize.y);
        float spacing = Mathf.Max(0f, exitButtonSpacing);
        float buttonY = (Screen.height - startButtonHeight) * 0.5f + startButtonHeight + spacing;

        Rect buttonRect = new Rect(
            GetButtonX(buttonWidth),
            Mathf.Min(buttonY, Screen.height - buttonHeight - 10f),
            buttonWidth,
            buttonHeight);

        if (GUI.Button(buttonRect, exitButtonText))
        {
            ExitGame();
        }
    }

    private void DrawTutorialWindow()
    {
        GUIStyle descriptionStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            fontSize = Mathf.Max(1, tutorialWindowFontSize),
            padding = new RectOffset(18, 18, 14, 14)
        };

        GUI.Box(GetTutorialWindowRect(), BuildTutorialText(), descriptionStyle);
    }

    private Rect GetTutorialWindowRect()
    {
        float padding = Mathf.Max(0f, layoutPadding);
        float maxButtonWidth = Mathf.Max(Mathf.Max(1f, buttonSize.x), Mathf.Max(1f, exitButtonSize.x));
        float minimumX = GetButtonX(maxButtonWidth) + maxButtonWidth + padding;
        float maxWindowWidth = Screen.width - minimumX - padding;

        if (maxWindowWidth < 280f)
        {
            minimumX = padding;
            maxWindowWidth = Screen.width - padding * 2f;
        }

        float windowWidth = Mathf.Min(Mathf.Max(1f, tutorialWindowSize.x), Mathf.Max(1f, maxWindowWidth));
        float windowHeight = Mathf.Min(
            Mathf.Max(1f, tutorialWindowSize.y),
            Mathf.Max(1f, Screen.height - padding * 2f));
        float maxX = Mathf.Max(minimumX, Screen.width - windowWidth - padding);
        float windowX = Mathf.Clamp(
            Screen.width - windowWidth - Mathf.Max(0f, tutorialWindowRightOffset),
            minimumX,
            maxX);
        float maxY = Mathf.Max(padding, Screen.height - windowHeight - padding);
        float windowY = Mathf.Clamp(
            (Screen.height - windowHeight) * 0.5f + tutorialWindowVerticalOffset,
            padding,
            maxY);

        return new Rect(windowX, windowY, windowWidth, windowHeight);
    }

    private float GetButtonX(float buttonWidth)
    {
        float padding = Mathf.Max(0f, layoutPadding);
        float maxX = Mathf.Max(padding, Screen.width - buttonWidth - padding);
        float preferredX = Screen.width * Mathf.Clamp01(buttonCenterXRatio) - buttonWidth * 0.5f;
        return Mathf.Clamp(preferredX, padding, maxX);
    }

    private string BuildTutorialText()
    {
        var builder = new StringBuilder();
        AppendTutorialSection(builder, controlsDescription);
        AppendTutorialSection(builder, warriorDescription);
        AppendTutorialSection(builder, rangerDescription);
        AppendTutorialSection(builder, mageDescription);
        AppendTutorialSection(builder, priestDescription);
        return builder.ToString();
    }

    private static void AppendTutorialSection(StringBuilder builder, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine();
        }

        builder.Append(text.Trim());
    }

    /// <summary>
    /// Загружает сцену имя которой сохранено в инспекторе
    /// </summary>
    public void LoadTargetScene()
    {
        if (string.IsNullOrWhiteSpace(targetSceneName))
        {
            Debug.LogError($"{name}: не задана сцена для кнопки старта.");
            return;
        }

        SceneManager.LoadScene(targetSceneName);
    }

    public void ExitGame()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// Сохраняет имя выбранного ассета сцены для загрузки во время игры
    /// </summary>
    private void OnValidate()
    {
        if (targetScene == null)
        {
            return;
        }

        targetSceneName = targetScene.name;
    }
#endif
}
