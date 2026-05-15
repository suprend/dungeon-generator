//DP
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

    [Header("Плашка управления")]
    [SerializeField, TextArea(2, 4)] private string controlsDescription =
        "Управление: WASD - движение, ЛКМ - базовая атака, 1/2/3 - способности, Q/E - смена героя, R - поднять союзника";
    [SerializeField] private Vector2 controlsBoxSize = new Vector2(720f, 70f);
    [SerializeField, Min(0f)] private float controlsBoxTopOffset = 24f;

    [Header("Описания героев")]
    [SerializeField, TextArea(3, 6)] private string warriorDescription =
        "Воин\n1: притягивает и замедляет врагов.\n2: бьет вокруг себя, расталкивает врагов и получает сопротивление урону.\n3: совершает рывок сквозь врагов.";
    [SerializeField, TextArea(3, 6)] private string rangerDescription =
        "Следопыт\nЛКМ: заряженный выстрел.\n1: выстрел снижает защиту цели.\n2: выпускает 5 снарядов веером.\n3: рывок и временное ускорение.";
    [SerializeField, TextArea(3, 6)] private string mageDescription =
        "Маг\n1: цепной снаряд с отскоками.\n2: большой удар по области с оглушением.\n3: проходящий снаряд с периодическим уроном и отталкиванием.";
    [SerializeField, TextArea(3, 6)] private string priestDescription =
        "Жрец\n1: лечит ближайшего к курсору героя после подготовки.\n2: лечит живых героев, дает щит и бонус урона.\n3: мощная ближняя атака.";
    [SerializeField] private Vector2 descriptionBoxSize = new Vector2(430f, 170f);
    [SerializeField, Min(0f)] private float descriptionBoxMargin = 30f;

    /// <summary>
    /// Рисует простое главное меню
    /// </summary>
    private void OnGUI()
    {
        DrawControlsDescription();
        DrawHeroDescriptions();
        DrawStartButton();
    }

    /// <summary>
    /// Рисует кнопку запуска игры
    /// </summary>
    private void DrawStartButton()
    {
        float buttonWidth = Mathf.Max(1f, buttonSize.x);
        float buttonHeight = Mathf.Max(1f, buttonSize.y);
        Rect buttonRect = new Rect(
            (Screen.width - buttonWidth) * 0.5f,
            (Screen.height - buttonHeight) * 0.5f,
            buttonWidth,
            buttonHeight);

        if (GUI.Button(buttonRect, buttonText))
        {
            LoadTargetScene();
        }
    }

    /// <summary>
    /// Рисует верхнюю плашку с управлением
    /// </summary>
    private void DrawControlsDescription()
    {
        GUIStyle controlsStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            fontSize = Mathf.Max(14, Mathf.RoundToInt(Screen.height / 48f)),
            padding = new RectOffset(16, 16, 8, 8)
        };

        float boxWidth = Mathf.Min(Mathf.Max(1f, controlsBoxSize.x), Screen.width - 40f);
        float boxHeight = Mathf.Max(1f, controlsBoxSize.y);
        Rect controlsRect = new Rect(
            (Screen.width - boxWidth) * 0.5f,
            Mathf.Max(0f, controlsBoxTopOffset),
            boxWidth,
            boxHeight);

        GUI.Box(controlsRect, controlsDescription, controlsStyle);
    }

    /// <summary>
    /// Рисует четыре текстовых блока с описаниями способностей героев
    /// </summary>
    private void DrawHeroDescriptions()
    {
        GUIStyle descriptionStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            fontSize = Mathf.Max(12, Mathf.RoundToInt(Screen.height / 54f)),
            padding = new RectOffset(14, 14, 12, 12)
        };

        Vector2 finalBoxSize = new Vector2(
            Mathf.Min(Mathf.Max(1f, descriptionBoxSize.x), Screen.width * 0.46f),
            Mathf.Min(Mathf.Max(1f, descriptionBoxSize.y), Screen.height * 0.28f));
        float margin = Mathf.Max(0f, descriptionBoxMargin);

        GUI.Box(new Rect(margin, margin, finalBoxSize.x, finalBoxSize.y), warriorDescription, descriptionStyle);
        GUI.Box(
            new Rect(Screen.width - finalBoxSize.x - margin, margin, finalBoxSize.x, finalBoxSize.y),
            priestDescription,
            descriptionStyle);
        GUI.Box(
            new Rect(margin, Screen.height - finalBoxSize.y - margin, finalBoxSize.x, finalBoxSize.y),
            mageDescription,
            descriptionStyle);
        GUI.Box(
            new Rect(
                Screen.width - finalBoxSize.x - margin,
                Screen.height - finalBoxSize.y - margin,
                finalBoxSize.x,
                finalBoxSize.y),
            rangerDescription,
            descriptionStyle);
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
