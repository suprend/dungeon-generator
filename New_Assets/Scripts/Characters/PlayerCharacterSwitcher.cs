//DP
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Переключает управление игрока между доступными персонажами
/// </summary>
public class PlayerCharacterSwitcher : MonoBehaviour
{
    [Header("Персонажи")]
    [SerializeField] private List<PlayerCharacterTemplate> characters = new List<PlayerCharacterTemplate>();
    [SerializeField, Min(0)] private int startCharacterIndex;
    [SerializeField] private bool findCharactersOnStart = true;

    [Header("Воскрешение")]
    [SerializeField, Min(0f)] private float resurrectionRange = 1.5f;
    [SerializeField, Min(0f)] private float resurrectionFillPerPress = 0.18f;
    [SerializeField, Min(0f)] private float resurrectionDecayPerSecond = 0.35f;
    [SerializeField, Range(0f, 1f)] private float revivedHealthPercent = 0.2f;

    [Header("Шкала воскрешения")]
    [SerializeField] private Vector2 resurrectionBarOffset = new Vector2(0f, 0.9f);
    [SerializeField, Min(0f)] private float resurrectionBarWidth = 1.1f;
    [SerializeField, Min(0f)] private float resurrectionBarHeight = 0.12f;
    [SerializeField] private Color resurrectionBarBackgroundColor = new Color(0f, 0f, 0f, 0.7f);
    [SerializeField] private Color resurrectionBarFillColor = new Color(0.2f, 0.9f, 1f, 0.95f);
    [SerializeField] private int resurrectionBarSortingOrderOffset = 30;

    [Header("Подпись воскрешения")]
    [SerializeField] private string resurrectionPromptText = "Spam R";
    [SerializeField] private Vector2 resurrectionPromptOffsetFromBar = new Vector2(0f, 0.24f);
    [SerializeField, Min(1)] private int resurrectionPromptFontSize = 32;
    [SerializeField, Min(0f)] private float resurrectionPromptCharacterSize = 0.035f;
    [SerializeField] private Color resurrectionPromptColor = Color.white;

    private static Sprite resurrectionBarSprite;

    private int currentCharacterIndex = -1;
    private PlayerCharacterTemplate resurrectionTarget;
    private Transform resurrectionBarRoot;
    private SpriteRenderer resurrectionBarBackgroundRenderer;
    private SpriteRenderer resurrectionBarFillRenderer;
    private TextMesh resurrectionPromptTextMesh;
    private MeshRenderer resurrectionPromptRenderer;
    private float resurrectionProgress;

    public PlayerCharacterTemplate CurrentCharacter =>
        IsCharacterIndexValid(currentCharacterIndex) ? characters[currentCharacterIndex] : null;

    private void Start()
    {
        PrepareCharacterList();
        CreateResurrectionBarVisual();
        SelectStartCharacter();
    }

    private void Update()
    {
        if (characters.Count == 0)
        {
            return;
        }

        EnsureCurrentCharacterIsAvailable();
        UpdateResurrection();

        if (Input.GetKeyDown(KeyCode.Q))
        {
            SwitchToPreviousCharacter();
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            SwitchToNextCharacter();
        }
    }

    /// <summary>
    /// Переключает управление на предыдущего персонажа 
    /// </summary>
    public void SwitchToPreviousCharacter()
    {
        SwitchCharacter(-1);
    }

    /// <summary>
    /// Переключает управление на следующего персонажа
    /// </summary>
    public void SwitchToNextCharacter()
    {
        SwitchCharacter(1);
    }

    /// <summary>
    /// Подготавливает список персонажей для переключения
    /// </summary>
    private void PrepareCharacterList()
    {
        characters.RemoveAll(character => character == null);

        if (characters.Count > 0 || !findCharactersOnStart)
        {
            return;
        }

        characters.AddRange(FindObjectsByType<PlayerCharacterTemplate>(FindObjectsSortMode.None));
        characters.Sort(CompareCharactersBySwitchOrder);
    }

    /// <summary>
    /// Выбирает стартового персонажа и переводит остальных под контроль ИИ
    /// </summary>
    private void SelectStartCharacter()
    {
        if (characters.Count == 0)
        {
            Debug.LogWarning($"{name} не найдены персонажи для переключения.");
            return;
        }

        int clampedStartIndex = Mathf.Clamp(startCharacterIndex, 0, characters.Count - 1);
        int availableCharacterIndex = FindAvailableCharacterIndex(clampedStartIndex, 1);

        if (!IsCharacterIndexValid(availableCharacterIndex))
        {
            ClearControlledCharacter(true);
            return;
        }

        SetControlledCharacter(availableCharacterIndex);
    }

    /// <summary>
    /// Переключает персонажа с учетом кругового списка
    /// </summary>
    private void SwitchCharacter(int direction)
    {
        if (characters.Count == 0)
        {
            return;
        }

        int searchStartIndex = IsCharacterIndexValid(currentCharacterIndex)
            ? currentCharacterIndex + direction
            : 0;
        int nextCharacterIndex = FindAvailableCharacterIndex(searchStartIndex, direction);

        if (!IsCharacterIndexValid(nextCharacterIndex))
        {
            ClearControlledCharacter(true);
            return;
        }

        SetControlledCharacter(nextCharacterIndex);
    }

    /// <summary>
    /// Назначает одного персонажа управляемым игроком, остальных передает ии
    /// </summary>
    private void SetControlledCharacter(int newCharacterIndex)
    {
        if (!IsCharacterAvailable(newCharacterIndex))
        {
            return;
        }

        for (int i = 0; i < characters.Count; i++)
        {
            PlayerCharacterControlState controlState =
                i == newCharacterIndex
                    ? PlayerCharacterControlState.PlayerControlled
                    : PlayerCharacterControlState.AiControlled;

            characters[i].SetControlState(controlState);
        }

        currentCharacterIndex = newCharacterIndex;
        Debug.Log($"{name}: управление переключено на {CurrentCharacter.name}.");
    }

    /// <summary>
    /// Обновляет поиск цели для воскрешения, заполнение шкалы и завершение подъема
    /// </summary>
    private void UpdateResurrection()
    {
        PlayerCharacterTemplate activeCharacter = CurrentCharacter;

        if (activeCharacter == null || !activeCharacter.IsAlive)
        {
            SetResurrectionTarget(null);
            return;
        }

        PlayerCharacterTemplate nearestTarget = FindClosestIncapacitatedCharacterInRange(activeCharacter);

        if (nearestTarget != resurrectionTarget)
        {
            SetResurrectionTarget(nearestTarget);
        }

        if (resurrectionTarget == null)
        {
            return;
        }

        resurrectionProgress = Mathf.Max(
            0f,
            resurrectionProgress - resurrectionDecayPerSecond * Time.deltaTime);

        if (Input.GetKeyDown(KeyCode.R))
        {
            resurrectionProgress = Mathf.Clamp01(resurrectionProgress + resurrectionFillPerPress);
        }

        UpdateResurrectionBarVisual();

        if (resurrectionProgress >= 1f)
        {
            CompleteResurrection();
        }
    }

    /// <summary>
    /// Находит ближайшего упавшего персонажа рядом с активным персонажем
    /// </summary>
    private PlayerCharacterTemplate FindClosestIncapacitatedCharacterInRange(PlayerCharacterTemplate activeCharacter)
    {
        PlayerCharacterTemplate closestCharacter = null;
        float rangeSqr = resurrectionRange * resurrectionRange;
        float closestSqrDistance = float.PositiveInfinity;

        for (int i = 0; i < characters.Count; i++)
        {
            PlayerCharacterTemplate character = characters[i];

            if (character == null || character == activeCharacter || !character.IsIncapacitated)
            {
                continue;
            }

            float sqrDistance =
                ((Vector2)character.transform.position - (Vector2)activeCharacter.transform.position).sqrMagnitude;

            if (sqrDistance > rangeSqr || sqrDistance >= closestSqrDistance)
            {
                continue;
            }

            closestSqrDistance = sqrDistance;
            closestCharacter = character;
        }

        return closestCharacter;
    }

    /// <summary>
    /// Меняет текущую цель воскрешения и сбрасывает накопленный прогресс
    /// </summary>
    private void SetResurrectionTarget(PlayerCharacterTemplate newTarget)
    {
        resurrectionTarget = newTarget;
        resurrectionProgress = 0f;
        UpdateResurrectionBarVisual();
    }

    /// <summary>
    /// Завершает воскрешение не меняя активного персонажа
    /// </summary>
    private void CompleteResurrection()
    {
        PlayerCharacterTemplate revivedCharacter = resurrectionTarget;

        if (revivedCharacter == null)
        {
            SetResurrectionTarget(null);
            return;
        }

        revivedCharacter.ReviveWithHealthPercent(revivedHealthPercent);
        bool wasRevived = revivedCharacter.IsAlive;

        SetResurrectionTarget(null);

        if (!wasRevived)
        {
            return;
        }

        if (revivedCharacter != CurrentCharacter)
        {
            revivedCharacter.SetControlState(PlayerCharacterControlState.AiControlled);
        }

        Debug.Log($"{name}: {revivedCharacter.name} поднят с {revivedHealthPercent:P0} здоровья.");
    }

    /// <summary>
    /// Создает шкалу прогресса воскрешения
    /// </summary>
    private void CreateResurrectionBarVisual()
    {
        if (resurrectionBarRoot != null)
        {
            return;
        }

        GameObject barObject = new GameObject("ResurrectionProgressVisual");
        barObject.transform.SetParent(transform, false);
        resurrectionBarRoot = barObject.transform;
        resurrectionBarBackgroundRenderer = CreateResurrectionBarPart("Background", resurrectionBarRoot, resurrectionBarBackgroundColor);
        resurrectionBarFillRenderer = CreateResurrectionBarPart("Fill", resurrectionBarRoot, resurrectionBarFillColor);
        CreateResurrectionPrompt(resurrectionBarRoot);

        barObject.SetActive(false);
    }

    /// <summary>
    /// Создает надпись с подсказкой над шкалой воскрешения
    /// </summary>
    private void CreateResurrectionPrompt(Transform parent)
    {
        GameObject promptObject = new GameObject("Prompt");
        promptObject.transform.SetParent(parent, false);

        resurrectionPromptTextMesh = promptObject.AddComponent<TextMesh>();
        resurrectionPromptTextMesh.anchor = TextAnchor.MiddleCenter;
        resurrectionPromptTextMesh.alignment = TextAlignment.Center;
        resurrectionPromptTextMesh.richText = false;

        resurrectionPromptRenderer = promptObject.GetComponent<MeshRenderer>();
    }

    /// <summary>
    /// Создает слой шкалы воскрешения
    /// </summary>
    private SpriteRenderer CreateResurrectionBarPart(string partName, Transform parent, Color partColor)
    {
        GameObject partObject = new GameObject(partName);
        partObject.transform.SetParent(parent, false);

        SpriteRenderer partRenderer = partObject.AddComponent<SpriteRenderer>();
        partRenderer.sprite = GetResurrectionBarSprite();
        partRenderer.color = partColor;

        return partRenderer;
    }

    /// <summary>
    /// Возвращает общий пиксельный спрайт для шкалы воскрешения
    /// </summary>
    private static Sprite GetResurrectionBarSprite()
    {
        if (resurrectionBarSprite != null)
        {
            return resurrectionBarSprite;
        }

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "GeneratedResurrectionBarPixel",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        texture.SetPixel(0, 0, Color.white);
        texture.Apply();

        resurrectionBarSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
        resurrectionBarSprite.name = "GeneratedResurrectionBarPixel";

        return resurrectionBarSprite;
    }

    /// <summary>
    /// Обновляет видимость и заполнение шкалы воскрешения
    /// </summary>
    private void UpdateResurrectionBarVisual()
    {
        if (resurrectionBarRoot == null
            || resurrectionBarBackgroundRenderer == null
            || resurrectionBarFillRenderer == null
            || resurrectionPromptTextMesh == null)
        {
            return;
        }

        bool shouldShowBar = resurrectionTarget != null
            && resurrectionBarWidth > 0f
            && resurrectionBarHeight > 0f;
        resurrectionBarRoot.gameObject.SetActive(shouldShowBar);

        if (!shouldShowBar)
        {
            resurrectionBarRoot.SetParent(transform, false);
            return;
        }

        resurrectionBarRoot.SetParent(resurrectionTarget.transform, false);
        resurrectionBarRoot.localPosition = new Vector3(resurrectionBarOffset.x, resurrectionBarOffset.y, 0f);
        resurrectionBarRoot.localScale = Vector3.one;

        float barWidth = Mathf.Max(0f, resurrectionBarWidth);
        float barHeight = Mathf.Max(0f, resurrectionBarHeight);
        float fillWidth = barWidth * Mathf.Clamp01(resurrectionProgress);

        resurrectionBarBackgroundRenderer.color = resurrectionBarBackgroundColor;
        resurrectionBarFillRenderer.color = resurrectionBarFillColor;
        resurrectionBarBackgroundRenderer.transform.localPosition = Vector3.zero;
        resurrectionBarBackgroundRenderer.transform.localScale = new Vector3(barWidth, barHeight, 1f);
        resurrectionBarFillRenderer.transform.localPosition = new Vector3((fillWidth - barWidth) * 0.5f, 0f, 0f);
        resurrectionBarFillRenderer.transform.localScale = new Vector3(fillWidth, barHeight, 1f);
        resurrectionPromptTextMesh.text = resurrectionPromptText;
        resurrectionPromptTextMesh.fontSize = resurrectionPromptFontSize;
        resurrectionPromptTextMesh.characterSize = resurrectionPromptCharacterSize;
        resurrectionPromptTextMesh.color = resurrectionPromptColor;
        resurrectionPromptTextMesh.transform.localPosition = new Vector3(
            resurrectionPromptOffsetFromBar.x,
            barHeight * 0.5f + resurrectionPromptOffsetFromBar.y,
            0f);

        RefreshResurrectionBarSorting();
    }

    /// <summary>
    /// Поднимает шкалу воскрешения поверх визуалов упавшего персонажа
    /// </summary>
    private void RefreshResurrectionBarSorting()
    {
        if (resurrectionTarget == null
            || resurrectionBarBackgroundRenderer == null
            || resurrectionBarFillRenderer == null
            || resurrectionPromptRenderer == null)
        {
            return;
        }

        int highestSortingOrder = 0;
        int sortingLayerId = resurrectionBarBackgroundRenderer.sortingLayerID;
        SpriteRenderer[] targetRenderers = resurrectionTarget.GetComponentsInChildren<SpriteRenderer>();

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            SpriteRenderer targetRenderer = targetRenderers[i];

            if (targetRenderer == null
                || ReferenceEquals(targetRenderer, resurrectionBarBackgroundRenderer)
                || ReferenceEquals(targetRenderer, resurrectionBarFillRenderer))
            {
                continue;
            }

            if (i == 0 || targetRenderer.sortingOrder >= highestSortingOrder)
            {
                highestSortingOrder = targetRenderer.sortingOrder;
                sortingLayerId = targetRenderer.sortingLayerID;
            }
        }

        resurrectionBarBackgroundRenderer.sortingLayerID = sortingLayerId;
        resurrectionBarFillRenderer.sortingLayerID = sortingLayerId;
        resurrectionBarBackgroundRenderer.sortingOrder = highestSortingOrder + resurrectionBarSortingOrderOffset;
        resurrectionBarFillRenderer.sortingOrder = resurrectionBarBackgroundRenderer.sortingOrder + 1;
        resurrectionPromptRenderer.sortingLayerID = sortingLayerId;
        resurrectionPromptRenderer.sortingOrder = resurrectionBarFillRenderer.sortingOrder + 1;
    }

    /// <summary>
    /// Передает управление следующему доступному персонажу если текущий упал
    /// </summary>
    private void EnsureCurrentCharacterIsAvailable()
    {
        if (IsCharacterAvailable(currentCharacterIndex))
        {
            return;
        }

        int searchStartIndex = IsCharacterIndexValid(currentCharacterIndex)
            ? currentCharacterIndex + 1
            : 0;
        int availableCharacterIndex = FindAvailableCharacterIndex(searchStartIndex, 1);

        if (IsCharacterIndexValid(availableCharacterIndex))
        {
            SetControlledCharacter(availableCharacterIndex);
            return;
        }

        if (IsCharacterIndexValid(currentCharacterIndex))
        {
            ClearControlledCharacter(true);
        }
    }

    /// <summary>
    /// Снимает управление игрока со всех персонажей
    /// </summary>
    private void ClearControlledCharacter(bool showWarning)
    {
        for (int i = 0; i < characters.Count; i++)
        {
            if (characters[i] == null)
            {
                continue;
            }

            characters[i].SetControlState(PlayerCharacterControlState.AiControlled);
        }

        currentCharacterIndex = -1;

        if (showWarning)
        {
            Debug.LogWarning($"{name} нет дееспособных персонажей для управления.");
        }
    }

    /// <summary>
    /// Ищет ближайшего не упавшего персонажа в выбранном направлении
    /// </summary>
    private int FindAvailableCharacterIndex(int startIndex, int direction)
    {
        if (characters.Count == 0)
        {
            return -1;
        }

        int normalizedDirection = direction >= 0 ? 1 : -1;
        int checkedCharacterIndex = GetWrappedCharacterIndex(startIndex);

        // Проверяем каждого персонажа один раз чтобы не зациклиться если все упали
        for (int checkedCharacters = 0; checkedCharacters < characters.Count; checkedCharacters++)
        {
            if (IsCharacterAvailable(checkedCharacterIndex))
            {
                return checkedCharacterIndex;
            }

            checkedCharacterIndex = GetWrappedCharacterIndex(checkedCharacterIndex + normalizedDirection);
        }

        return -1;
    }

    /// <summary>
    /// Возвращает индекс списка персонажей
    /// </summary>
    private int GetWrappedCharacterIndex(int characterIndex)
    {
        int charactersCount = characters.Count;
        return (characterIndex % charactersCount + charactersCount) % charactersCount;
    }

    /// <summary>
    /// Сравнивает персонажей по ожидаемому порядку переключения
    /// </summary>
    private int CompareCharactersBySwitchOrder(PlayerCharacterTemplate leftCharacter, PlayerCharacterTemplate rightCharacter)
    {
        int leftOrder = GetCharacterSwitchOrder(leftCharacter);
        int rightOrder = GetCharacterSwitchOrder(rightCharacter);

        if (leftOrder != rightOrder)
        {
            return leftOrder.CompareTo(rightOrder);
        }

        return string.Compare(leftCharacter.name, rightCharacter.name, StringComparison.Ordinal);
    }

    /// <summary>
    /// Возвращает порядок персонажа в круговом переключении
    /// </summary>
    private int GetCharacterSwitchOrder(PlayerCharacterTemplate character)
    {
        if (character is PlayerCharacterWarrior)
        {
            return 0;
        }

        if (character is PlayerCharacterRanger)
        {
            return 1;
        }

        if (character is PlayerCharacterMage)
        {
            return 2;
        }

        if (character is PlayerCharacterPriest)
        {
            return 3;
        }

        return 100;
    }

    /// <summary>
    /// Проверяет можно ли использовать индекс персонажа
    /// </summary>
    private bool IsCharacterIndexValid(int characterIndex)
    {
        return characterIndex >= 0 && characterIndex < characters.Count;
    }

    /// <summary>
    /// Проверяет можно ли переключиться на персонажа
    /// </summary>
    private bool IsCharacterAvailable(int characterIndex)
    {
        return IsCharacterIndexValid(characterIndex)
               && characters[characterIndex] != null
               && characters[characterIndex].IsAlive;
    }
}
