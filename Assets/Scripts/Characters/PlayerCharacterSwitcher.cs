//DP
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Переключает управление игрока между доступными персонажами.
/// </summary>
public class PlayerCharacterSwitcher : MonoBehaviour
{
    [Header("Персонажи")]
    [SerializeField] private List<PlayerCharacterTemplate> characters = new List<PlayerCharacterTemplate>();
    [SerializeField, Min(0)] private int startCharacterIndex;
    [SerializeField] private bool findCharactersOnStart = true;

    [Header("Следование партии")]
    [SerializeField, Min(0f)] private float companionTrailSpacing = 1.1f;
    [SerializeField, Min(0.01f)] private float trailPointSpacing = 0.25f;
    [SerializeField, Min(0f)] private float companionTeleportDistance = 8f;

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
    private readonly HashSet<string> exploredRoomNodeIds = new HashSet<string>();
    private readonly List<Vector2> activeTrailPoints = new List<Vector2>();
    private GeneratedLevelRuntime generatedLevelRuntime;
    private PlayerRoomTracker playerRoomTracker;
    private PlayerCharacterTemplate trailOwner;
    private PlayerCharacterTemplate lastActiveCharacter;
    private bool ownsSpawnedCharacters;
    private bool hasShownPartyDeathMenu;

    public PlayerCharacterTemplate CurrentCharacter =>
        IsCharacterIndexValid(currentCharacterIndex) ? characters[currentCharacterIndex] : null;
    public IReadOnlyList<PlayerCharacterTemplate> Characters => characters;

    private void Start()
    {
        PrepareCharacterList();
        CacheRuntimeReferences();
        CreateResurrectionBarVisual();
        SelectStartCharacter();
        SyncAnchorTransform();
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

        CacheRuntimeReferences();
        RefreshExploredRooms();
        TickCompanionFollow();
        SyncAnchorTransform();
    }

    private void LateUpdate()
    {
        SyncAnchorTransform();
    }

    private void OnDestroy()
    {
        AttachTracker(null);

        if (!ownsSpawnedCharacters)
        {
            return;
        }

        for (int i = 0; i < characters.Count; i++)
        {
            if (characters[i] != null)
            {
                Destroy(characters[i].gameObject);
            }
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

    public static Vector2 GetSpawnOffset(int index)
    {
        return index switch
        {
            0 => new Vector2(0f, 0f),
            1 => new Vector2(-0.8f, -0.65f),
            2 => new Vector2(0.8f, -0.65f),
            3 => new Vector2(0f, -1.2f),
            _ => Vector2.zero
        };
    }

    public static Vector2 GetFormationOffset(int index)
    {
        return index switch
        {
            0 => new Vector2(0f, 0f),
            1 => new Vector2(-1.5f, -1.25f),
            2 => new Vector2(1.5f, -1.25f),
            3 => new Vector2(0f, -2.2f),
            _ => Vector2.zero
        };
    }

    public void Initialize(IReadOnlyList<GameObject> memberInstances)
    {
        characters.Clear();
        exploredRoomNodeIds.Clear();
        ResetActiveTrail();
        lastActiveCharacter = null;
        ownsSpawnedCharacters = true;
        findCharactersOnStart = false;

        if (memberInstances != null)
        {
            for (int i = 0; i < memberInstances.Count; i++)
            {
                GameObject memberObject = memberInstances[i];
                if (memberObject == null)
                {
                    continue;
                }

                PlayerCharacterTemplate character = memberObject.GetComponent<PlayerCharacterTemplate>();
                if (character == null)
                {
                    Debug.LogError($"[PlayerCharacterSwitcher] Party member prefab '{memberObject.name}' is missing {nameof(PlayerCharacterTemplate)}.", memberObject);
                    continue;
                }

                characters.Add(character);
            }
        }

        characters.Sort(CompareCharactersBySwitchOrder);
        CacheRuntimeReferences();
        SelectStartCharacter();
        SyncAnchorTransform();
        RefreshExploredRooms();
    }

    public void SetGeneratedLevelRuntime(GeneratedLevelRuntime runtime)
    {
        generatedLevelRuntime = runtime;
        CacheRuntimeReferences();
        RefreshExploredRooms();
    }

    public void TeleportParty(Vector3 spawnPosition)
    {
        for (int i = 0; i < characters.Count; i++)
        {
            PlayerCharacterTemplate character = characters[i];
            if (character == null)
            {
                continue;
            }

            character.TeleportTo(spawnPosition + (Vector3)GetSpawnOffset(i));
        }

        ResetActiveTrail();
        SyncAnchorTransform();
        RefreshExploredRooms();
    }

    public int TeleportCompanionsToActiveCharacter(GeneratedRoomInfo requiredRoom = null)
    {
        PlayerCharacterTemplate activeCharacter = CurrentCharacter;
        if (activeCharacter == null)
        {
            return 0;
        }

        Vector3 anchor = activeCharacter.transform.position;
        int teleportedCount = 0;

        for (int i = 0; i < characters.Count; i++)
        {
            PlayerCharacterTemplate character = characters[i];
            if (character == null || character == activeCharacter || !character.IsAlive)
            {
                continue;
            }

            Vector3 desiredPosition = anchor + (Vector3)GetSpawnOffset(i);
            if (requiredRoom != null
                && generatedLevelRuntime != null
                && generatedLevelRuntime.TryGetNearestFloorWorldPosition(requiredRoom, desiredPosition, out Vector3 safePosition))
            {
                desiredPosition = safePosition;
            }

            character.TeleportTo(desiredPosition);
            teleportedCount++;
        }

        ResetActiveTrail();
        SyncAnchorTransform();
        RefreshExploredRooms();
        return teleportedCount;
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
        hasShownPartyDeathMenu = false;
        ResetActiveTrail();
        Debug.Log($"{name}: управление переключено на {CurrentCharacter.name}.");
    }

    private void TickCompanionFollow()
    {
        PlayerCharacterTemplate activeCharacter = CurrentCharacter;
        if (activeCharacter == null)
        {
            return;
        }

        if (activeCharacter != lastActiveCharacter)
        {
            lastActiveCharacter = activeCharacter;
            ResetActiveTrail();
        }

        UpdateActiveTrail(activeCharacter);

        for (int step = 1; step < characters.Count; step++)
        {
            int characterIndex = GetWrappedCharacterIndex(currentCharacterIndex + step);
            PlayerCharacterTemplate character = characters[characterIndex];
            if (character == null || character == activeCharacter || !character.IsAlive)
            {
                continue;
            }

            Vector2 targetPosition = TryGetTrailPosition(step * companionTrailSpacing, out Vector2 trailPosition)
                ? trailPosition
                : (Vector2)activeCharacter.transform.position + GetFormationOffset(step);
            Vector2 toTarget = targetPosition - (Vector2)character.transform.position;

            if (toTarget.magnitude > companionTeleportDistance
                && TryGetSafeTeleportPosition(targetPosition, out Vector3 safeTeleportPosition))
            {
                character.TeleportTo(safeTeleportPosition);
                character.SetExternalAiMovementInput(Vector2.zero);
                continue;
            }

            character.SetExternalAiMovementInput(toTarget.magnitude > 0.15f ? toTarget.normalized : Vector2.zero);
        }
    }

    private bool TryGetSafeTeleportPosition(Vector3 desiredWorldPosition, out Vector3 safeWorldPosition)
    {
        safeWorldPosition = desiredWorldPosition;

        if (generatedLevelRuntime == null)
        {
            return false;
        }

        if (!generatedLevelRuntime.TryGetRoomAtWorldPosition(desiredWorldPosition, out GeneratedRoomInfo roomInfo)
            || roomInfo == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(roomInfo.NodeId) && !exploredRoomNodeIds.Contains(roomInfo.NodeId))
        {
            return false;
        }

        return generatedLevelRuntime.TryGetNearestFloorWorldPosition(roomInfo, desiredWorldPosition, out safeWorldPosition);
    }

    private void CacheRuntimeReferences()
    {
        if (playerRoomTracker == null)
        {
            AttachTracker(GetComponent<PlayerRoomTracker>());
        }
    }

    private void AttachTracker(PlayerRoomTracker tracker)
    {
        if (playerRoomTracker != null)
        {
            playerRoomTracker.EnteredRoom -= HandleEnteredRoom;
        }

        playerRoomTracker = tracker;

        if (playerRoomTracker != null)
        {
            playerRoomTracker.EnteredRoom += HandleEnteredRoom;
        }
    }

    private void HandleEnteredRoom(GeneratedRoomInfo roomInfo)
    {
        RegisterExploredRoom(roomInfo);
    }

    private void RefreshExploredRooms()
    {
        if (playerRoomTracker == null)
        {
            return;
        }

        RegisterExploredRoom(playerRoomTracker.CurrentRoom);
        RegisterExploredRoom(playerRoomTracker.LastKnownRoom);
    }

    private void RegisterExploredRoom(GeneratedRoomInfo roomInfo)
    {
        if (roomInfo == null || string.IsNullOrEmpty(roomInfo.NodeId))
        {
            return;
        }

        exploredRoomNodeIds.Add(roomInfo.NodeId);
    }

    private void SyncAnchorTransform()
    {
        PlayerCharacterTemplate activeCharacter = CurrentCharacter;
        if (activeCharacter == null)
        {
            return;
        }

        transform.position = activeCharacter.transform.position;

        if (playerRoomTracker != null)
        {
            playerRoomTracker.RefreshRoomNow();
        }
    }

    private void ResetActiveTrail()
    {
        trailOwner = null;
        activeTrailPoints.Clear();
    }

    private void UpdateActiveTrail(PlayerCharacterTemplate activeCharacter)
    {
        if (activeCharacter == null)
        {
            ResetActiveTrail();
            return;
        }

        Vector2 activePosition = activeCharacter.transform.position;
        if (trailOwner != activeCharacter)
        {
            trailOwner = activeCharacter;
            activeTrailPoints.Clear();
            activeTrailPoints.Add(activePosition);
            return;
        }

        if (activeTrailPoints.Count == 0)
        {
            activeTrailPoints.Add(activePosition);
            return;
        }

        float minPointSpacing = Mathf.Max(0.05f, trailPointSpacing);
        if (Vector2.Distance(activeTrailPoints[0], activePosition) >= minPointSpacing)
        {
            activeTrailPoints.Insert(0, activePosition);
        }
        else
        {
            activeTrailPoints[0] = activePosition;
        }

        float requiredTrailLength = Mathf.Max(minPointSpacing, companionTrailSpacing) * Mathf.Max(2, characters.Count + 1);
        float accumulatedDistance = 0f;

        for (int i = 1; i < activeTrailPoints.Count; i++)
        {
            accumulatedDistance += Vector2.Distance(activeTrailPoints[i - 1], activeTrailPoints[i]);
            if (accumulatedDistance <= requiredTrailLength)
            {
                continue;
            }

            activeTrailPoints.RemoveRange(i, activeTrailPoints.Count - i);
            break;
        }
    }

    private bool TryGetTrailPosition(float trailingDistance, out Vector2 trailPosition)
    {
        PlayerCharacterTemplate activeCharacter = CurrentCharacter;
        if (activeCharacter == null)
        {
            trailPosition = Vector2.zero;
            return false;
        }

        if (activeTrailPoints.Count == 0)
        {
            trailPosition = activeCharacter.transform.position;
            return false;
        }

        float remainingDistance = Mathf.Max(0f, trailingDistance);

        for (int i = 1; i < activeTrailPoints.Count; i++)
        {
            Vector2 from = activeTrailPoints[i - 1];
            Vector2 to = activeTrailPoints[i];
            float segmentLength = Vector2.Distance(from, to);

            if (segmentLength <= 0.0001f)
            {
                continue;
            }

            if (remainingDistance <= segmentLength)
            {
                trailPosition = Vector2.Lerp(from, to, remainingDistance / segmentLength);
                return true;
            }

            remainingDistance -= segmentLength;
        }

        trailPosition = activeTrailPoints[activeTrailPoints.Count - 1];
        return false;
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

        hasShownPartyDeathMenu = false;

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
            ShowPartyDeathMenuOnce();
        }
    }

    private void ShowPartyDeathMenuOnce()
    {
        if (hasShownPartyDeathMenu)
        {
            return;
        }

        hasShownPartyDeathMenu = true;
        PlayerDeathRestartMenu.Show();
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
