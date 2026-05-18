//DP
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Шаблон босса: использует базовую логику противника и по очереди запускает несколько атак.
/// </summary>
public class BossTemplate : EnemyTemplate
{
    private static Material chaserAttackCircleVisualMaterial;
    private static Sprite healthBarSprite;

    [Header("Отображение здоровья")]
    [SerializeField] private Vector2 healthBarOffset = new Vector2(0f, -0.75f);
    [SerializeField, Min(0f)] private float healthBarWidth = 2.2f;
    [SerializeField, Min(0f)] private float healthBarHeight = 0.16f;
    [SerializeField] private Color healthBarBackgroundColor = new Color(0f, 0f, 0f, 0.65f);
    [SerializeField] private Color healthBarFillColor = new Color(0.15f, 0.95f, 0.2f, 0.95f);
    [SerializeField] private int healthBarSortingOrderOffset = 20;

    [Header("Атаки босса")]
    [SerializeField, Min(1)] private int bossAttacksCount = 3;
    [SerializeField, Min(0.01f)] private float attackMinInterval = 1.5f;
    [SerializeField, Min(0.01f)] private float attackMaxInterval = 3f;
    [SerializeField, Min(0)] private int firstAttackIndex;
    [SerializeField] private bool attackOnlyInsideAttackZone = true;

    [Header("Спиральная атака")]
    [SerializeField] private EnemyProjectile spiralProjectilePrefab;
    [SerializeField, Min(1)] private int spiralWavesCount = 5;
    [SerializeField, Min(1)] private int spiralProjectilesPerWave = 12;
    [SerializeField, Min(0f)] private float spiralInitialPause = 0.35f;
    [SerializeField, Min(0f)] private float spiralPauseBetweenWaves = 0.45f;
    [SerializeField, Min(0f)] private float spiralProjectileSpawnOffsetDistance = 0.9f;
    [SerializeField, Min(0.01f)] private float spiralProjectileSpeed = 5f;
    [SerializeField, Min(0f)] private float spiralProjectileKnockbackForce = 3f;
    [SerializeField, Min(0.1f)] private float spiralProjectileLifeTime = 5f;
    [SerializeField] private LayerMask spiralProjectileHitLayers = ~0;
    [SerializeField] private float spiralStartAngle;
    [SerializeField] private float spiralAngleStepPerWave = 18f;

    [Header("Рывок в активного персонажа")]
    [SerializeField] private PlayerCharacterSwitcher characterSwitcher;
    [SerializeField, Min(0f)] private float chargePrepareDelay = 0.35f;
    [SerializeField, Min(0.01f)] private float chargeAcceleration = 18f;
    [SerializeField, Min(0.01f)] private float chargeMaxSpeed = 10f;
    [SerializeField, Min(0.05f)] private float chargeDuration = 1.2f;
    [SerializeField, Min(0f)] private float chargeStunDuration = 2.5f;

    [Header("Погоня с ближней атакой")]
    [SerializeField, Min(0.1f)] private float chaserAttackDuration = 5f;
    [SerializeField, Min(0f)] private float chaserMeleeAttackZone = 4f;
    [SerializeField, Min(0f)] private float chaserMeleeAttackRange = 1f;
    [SerializeField, Min(0f)] private float chaserMeleeAttackOffsetDistance = 0.75f;
    [SerializeField, Min(0f)] private float chaserMeleeKnockbackForce = 4f;
    [SerializeField] private LayerMask chaserMeleeAttackLayers = ~0;
    [SerializeField, Min(0.01f)] private float chaserMeleeAttackMinInterval = 0.8f;
    [SerializeField, Min(0.01f)] private float chaserMeleeAttackMaxInterval = 1.6f;

    [Header("Визуализация ближней атаки босса")]
    [SerializeField, Min(0.01f)] private float chaserMeleeAttackVisualLifeTime = 0.15f;
    [SerializeField, Min(8)] private int chaserMeleeAttackCircleSegments = 40;
    [SerializeField, Min(0.01f)] private float chaserMeleeAttackCircleWidth = 0.07f;
    [SerializeField] private Color chaserMeleeAttackCircleColor = new Color(1f, 0.2f, 0.12f, 0.9f);

    [Header("Смерть")]
    [SerializeField] private GameObject deathSpawnPrefab;
    [SerializeField] private Vector2 deathSpawnOffset = Vector2.zero;

    private Rigidbody2D bossRigidbody;
    private Transform healthBarRoot;
    private SpriteRenderer healthBarBackgroundRenderer;
    private SpriteRenderer healthBarFillRenderer;
    private Coroutine spiralAttackCoroutine;
    private Coroutine chargeAttackCoroutine;
    private Coroutine chaserAttackCoroutine;
    private int currentAttackIndex;
    private float nextAttackTime;
    private float nextChaserMeleeAttackTime;
    private float currentChargeSpeed;
    private bool isSpiralAttackActive;
    private bool isChargeAttackActive;
    private bool isChaserAttackActive;
    private bool wasAlive;
    private bool hasSpawnedDeathPrefab;
    private Vector2 lastChaserMeleeAttackDirection = Vector2.right;

    private void Start()
    {
        bossRigidbody = GetComponent<Rigidbody2D>();
        CreateHealthBarVisual();
        UpdateHealthBarVisual();
        wasAlive = IsAlive;
        currentAttackIndex = GetClampedAttackIndex(firstAttackIndex);
        ScheduleNextAttack();
    }

    private void Update()
    {
        UpdateHealthBarVisual();
        CheckDeathSpawn();

        if (!CanTryAttack())
        {
            return;
        }

        if (Time.time < nextAttackTime)
        {
            return;
        }

        PlayerCharacterTemplate targetCharacter = FindTargetForCurrentAttack();

        if (targetCharacter == null)
        {
            ScheduleNextAttack();
            return;
        }

        UseCurrentAttack(targetCharacter);

        if (!IsLongAttackActive())
        {
            FinishCurrentAttackCycle();
        }
    }

    private void FixedUpdate()
    {
        if (!IsAlive)
        {
            ResetAiDistanceMovement();
            return;
        }

        if (isSpiralAttackActive)
        {
            StopBossMovementForAttack();
            return;
        }

        if (isChargeAttackActive)
        {
            return;
        }

        if (isChaserAttackActive)
        {
            return;
        }

        MoveWithAiDistancesToTarget(FindClosestAlivePlayerCharacter());
    }

    /// <summary>
    /// Проверяет может ли босс сейчас пытаться атаковать.
    /// </summary>
    private bool CanTryAttack()
    {
        return IsAlive && !IsStunned && !IsExternalMovementActive && !IsLongAttackActive();
    }

    /// <summary>
    /// Запускает текущую атаку босса.
    /// </summary>
    private void UseCurrentAttack(PlayerCharacterTemplate targetCharacter)
    {
        switch (currentAttackIndex)
        {
            case 0:
                UseFirstBossAttack(targetCharacter);
                break;
            case 1:
                UseSecondBossAttack(targetCharacter);
                break;
            case 2:
                UseThirdBossAttack(targetCharacter);
                break;
            default:
                UseExtraBossAttack(currentAttackIndex, targetCharacter);
                break;
        }
    }

    /// <summary>
    /// Заглушка первой атаки босса.
    /// </summary>
    protected virtual void UseFirstBossAttack(PlayerCharacterTemplate targetCharacter)
    {
        if (spiralProjectilePrefab != null)
        {
            StartSpiralProjectileAttack();
            return;
        }

        Debug.Log($"{name}: первая атака босса пока не реализована. Цель: {targetCharacter.name}.");
    }

    /// <summary>
    /// Заглушка второй атаки босса.
    /// </summary>
    protected virtual void UseSecondBossAttack(PlayerCharacterTemplate targetCharacter)
    {
        if (StartChargeAttack(targetCharacter))
        {
            return;
        }
        Debug.Log($"{name}: вторая атака босса пока не реализована. Цель: {targetCharacter.name}.");
    }

    /// <summary>
    /// Заглушка третьей атаки босса.
    /// </summary>
    protected virtual void UseThirdBossAttack(PlayerCharacterTemplate targetCharacter)
    {
        if (StartChaserMeleeAttack(targetCharacter))
        {
            return;
        }

        Debug.Log($"{name}: третья атака босса пока не реализована. Цель: {targetCharacter.name}.");
    }

    /// <summary>
    /// Заглушка для дополнительных атак если в инспекторе указано больше трех атак.
    /// </summary>
    protected virtual void UseExtraBossAttack(int attackIndex, PlayerCharacterTemplate targetCharacter)
    {
        Debug.Log($"{name}: атака босса #{attackIndex + 1} пока не реализована. Цель: {targetCharacter.name}.");
    }

    /// <summary>
    /// Переключает босса на следующую атаку по кругу.
    /// </summary>
    private void SelectNextAttack()
    {
        currentAttackIndex = GetRandomNextAttackIndex();
    }

    /// <summary>
    /// Выбирает случайную следующую атаку, не повторяя текущую два раза подряд.
    /// </summary>
    private int GetRandomNextAttackIndex()
    {
        int finalAttacksCount = Mathf.Max(1, bossAttacksCount);

        if (finalAttacksCount <= 1)
        {
            return 0;
        }

        int currentClampedAttackIndex = GetClampedAttackIndex(currentAttackIndex);
        int randomAttackIndex = Random.Range(0, finalAttacksCount - 1);

        if (randomAttackIndex >= currentClampedAttackIndex)
        {
            randomAttackIndex++;
        }

        return randomAttackIndex;
    }

    /// <summary>
    /// Назначает случайное время следующей атаки.
    /// </summary>
    private void ScheduleNextAttack()
    {
        float finalMinInterval = Mathf.Min(attackMinInterval, attackMaxInterval);
        float finalMaxInterval = Mathf.Max(attackMinInterval, attackMaxInterval);
        nextAttackTime = Time.time + Random.Range(finalMinInterval, finalMaxInterval);
    }

    /// <summary>
    /// Возвращает индекс атаки внутри доступного количества атак.
    /// </summary>
    private int GetClampedAttackIndex(int attackIndex)
    {
        int finalAttacksCount = Mathf.Max(1, bossAttacksCount);
        return (attackIndex % finalAttacksCount + finalAttacksCount) % finalAttacksCount;
    }

    /// <summary>
    /// Запускает атаку босса спиральными волнами снарядов.
    /// </summary>
    private PlayerCharacterTemplate FindTargetForCurrentAttack()
    {
        if (currentAttackIndex == 1 || currentAttackIndex == 2)
        {
            PlayerCharacterTemplate activeCharacter = FindActivePlayerCharacter();

            if (activeCharacter != null)
            {
                return activeCharacter;
            }
        }

        return attackOnlyInsideAttackZone
            ? FindClosestAlivePlayerCharacterInAiAttackZone()
            : FindClosestAlivePlayerCharacter();
    }

    /// <summary>
    /// Возвращает персонажа, который сейчас находится под контролем игрока.
    /// </summary>
    private PlayerCharacterTemplate FindActivePlayerCharacter()
    {
        if (characterSwitcher == null)
        {
            characterSwitcher = FindFirstObjectByType<PlayerCharacterSwitcher>();
        }

        PlayerCharacterTemplate activeCharacter =
            characterSwitcher != null ? characterSwitcher.CurrentCharacter : null;

        if (activeCharacter == null || !activeCharacter.IsAlive)
        {
            return null;
        }

        return activeCharacter;
    }

    /// <summary>
    /// Проверяет, занят ли босс длительной атакой.
    /// </summary>
    private bool IsLongAttackActive()
    {
        return isSpiralAttackActive || isChargeAttackActive || isChaserAttackActive;
    }

    /// <summary>
    /// Проверяет переход босса из живого состояния в смерть и один раз создает заданный префаб.
    /// </summary>
    private void CheckDeathSpawn()
    {
        if (hasSpawnedDeathPrefab)
        {
            return;
        }

        if (wasAlive && !IsAlive)
        {
            SpawnDeathPrefab();
            hasSpawnedDeathPrefab = true;
        }

        wasAlive = IsAlive;
    }

    /// <summary>
    /// Создает префаб, который должен появиться после смерти босса.
    /// </summary>
    private void SpawnDeathPrefab()
    {
        if (deathSpawnPrefab == null)
        {
            return;
        }

        Vector3 spawnPosition = transform.position + new Vector3(deathSpawnOffset.x, deathSpawnOffset.y, 0f);
        Instantiate(deathSpawnPrefab, spawnPosition, Quaternion.identity);

        Debug.Log($"{name}: после смерти создан префаб {deathSpawnPrefab.name}.");
    }

    /// <summary>
    /// Запускает рывок босса в сторону активного персонажа.
    /// </summary>
    private bool StartChargeAttack(PlayerCharacterTemplate fallbackTargetCharacter)
    {
        PlayerCharacterTemplate targetCharacter = FindActivePlayerCharacter();

        if (targetCharacter == null)
        {
            targetCharacter = fallbackTargetCharacter;
        }

        if (targetCharacter == null || !targetCharacter.IsAlive)
        {
            Debug.LogWarning($"{name}: рывок босса не нашел живую цель.");
            return false;
        }

        Vector2 chargeDirection = (Vector2)targetCharacter.transform.position - (Vector2)transform.position;

        if (chargeDirection.sqrMagnitude <= 0.0001f)
        {
            Debug.LogWarning($"{name}: рывок босса не запущен, цель слишком близко.");
            return false;
        }

        if (chargeAttackCoroutine != null)
        {
            StopCoroutine(chargeAttackCoroutine);
        }

        isChargeAttackActive = true;
        chargeAttackCoroutine = StartCoroutine(ChargeAttackCoroutine(chargeDirection.normalized, targetCharacter));
        return true;
    }

    /// <summary>
    /// Разгоняет босса в выбранном направлении и дает игроку короткое предупреждение перед стартом.
    /// </summary>
    private IEnumerator ChargeAttackCoroutine(Vector2 chargeDirection, PlayerCharacterTemplate targetCharacter)
    {
        isChargeAttackActive = true;
        StopBossMovementForAttack();

        if (chargePrepareDelay > 0f)
        {
            yield return new WaitForSeconds(chargePrepareDelay);
        }

        float endTime = Time.time + chargeDuration;
        currentChargeSpeed = 0f;

        Debug.Log($"{name}: начал рывок в сторону {targetCharacter.name}.");

        while (Time.time < endTime && isChargeAttackActive && IsAlive && !IsStunned)
        {
            yield return new WaitForFixedUpdate();

            if (bossRigidbody == null)
            {
                bossRigidbody = GetComponent<Rigidbody2D>();
            }

            if (bossRigidbody == null)
            {
                break;
            }

            currentChargeSpeed = Mathf.MoveTowards(
                currentChargeSpeed,
                chargeMaxSpeed,
                chargeAcceleration * Time.fixedDeltaTime);

            Vector2 nextPosition = bossRigidbody.position
                + chargeDirection * currentChargeSpeed * Time.fixedDeltaTime;
            bossRigidbody.MovePosition(nextPosition);
        }

        if (isChargeAttackActive)
        {
            CompleteChargeAttack();
        }
    }

    /// <summary>
    /// Завершает рывок без оглушения, если босс никуда не врезался.
    /// </summary>
    private void CompleteChargeAttack()
    {
        isChargeAttackActive = false;
        currentChargeSpeed = 0f;
        chargeAttackCoroutine = null;
        StopBossMovementForAttack();

        if (IsAlive)
        {
            FinishCurrentAttackCycle();
        }
    }

    /// <summary>
    /// Прерывает рывок и оглушает босса после столкновения со статичным Rigidbody2D.
    /// </summary>
    private void InterruptChargeAttackWithStun()
    {
        if (!isChargeAttackActive)
        {
            return;
        }

        isChargeAttackActive = false;
        currentChargeSpeed = 0f;

        if (chargeAttackCoroutine != null)
        {
            StopCoroutine(chargeAttackCoroutine);
            chargeAttackCoroutine = null;
        }

        StopBossMovementForAttack();
        ApplyStun(chargeStunDuration);

        if (IsAlive)
        {
            FinishCurrentAttackCycle();
        }

        Debug.Log($"{name}: врезался в статичный объект и оглушен на {chargeStunDuration:0.0} сек.");
    }

    /// <summary>
    /// Запускает атаку босса спиральными волнами снарядов.
    /// </summary>
    /// <summary>
    /// Запускает фазу погони, в которой босс идет к активному персонажу и бьет как ChaserEnemy.
    /// </summary>
    private bool StartChaserMeleeAttack(PlayerCharacterTemplate fallbackTargetCharacter)
    {
        PlayerCharacterTemplate targetCharacter = FindActivePlayerCharacter();

        if (targetCharacter == null)
        {
            targetCharacter = fallbackTargetCharacter;
        }

        if (targetCharacter == null || !targetCharacter.IsAlive)
        {
            Debug.LogWarning($"{name}: погоня босса не нашла живую цель.");
            return false;
        }

        if (chaserAttackCoroutine != null)
        {
            StopCoroutine(chaserAttackCoroutine);
        }

        isChaserAttackActive = true;
        ScheduleNextChaserMeleeAttack();
        chaserAttackCoroutine = StartCoroutine(ChaserMeleeAttackCoroutine(targetCharacter));
        return true;
    }

    /// <summary>
    /// Двигает босса к активному персонажу и периодически запускает ближнюю атаку.
    /// </summary>
    private IEnumerator ChaserMeleeAttackCoroutine(PlayerCharacterTemplate fallbackTargetCharacter)
    {
        StopBossMovementForAttack();
        float endTime = Time.time + chaserAttackDuration;

        while (Time.time < endTime && isChaserAttackActive && IsAlive && !IsStunned)
        {
            yield return new WaitForFixedUpdate();

            PlayerCharacterTemplate targetCharacter = FindActivePlayerCharacter();

            if (targetCharacter == null)
            {
                targetCharacter = fallbackTargetCharacter != null && fallbackTargetCharacter.IsAlive
                    ? fallbackTargetCharacter
                    : FindClosestAlivePlayerCharacter();
            }

            if (targetCharacter == null || !targetCharacter.IsAlive)
            {
                break;
            }

            MoveBossDirectlyToTarget(targetCharacter);
            TryUseChaserMeleeAttack(targetCharacter);
        }

        if (isChaserAttackActive)
        {
            CompleteChaserMeleeAttack();
        }
    }

    /// <summary>
    /// Двигает босса прямо к цели без остановки на дистанции обычного AI.
    /// </summary>
    private void MoveBossDirectlyToTarget(PlayerCharacterTemplate targetCharacter)
    {
        if (targetCharacter == null)
        {
            return;
        }

        if (bossRigidbody == null)
        {
            bossRigidbody = GetComponent<Rigidbody2D>();
        }

        if (bossRigidbody == null)
        {
            return;
        }

        Vector2 directionToTarget = (Vector2)targetCharacter.transform.position - bossRigidbody.position;

        if (directionToTarget.sqrMagnitude <= 0.0001f)
        {
            bossRigidbody.velocity = Vector2.zero;
            return;
        }

        Vector2 nextPosition = bossRigidbody.position
            + directionToTarget.normalized * Speed * Time.fixedDeltaTime;
        bossRigidbody.MovePosition(nextPosition);
    }

    /// <summary>
    /// Проверяет таймер и расстояние до цели, после чего выполняет ближнюю атаку.
    /// </summary>
    private void TryUseChaserMeleeAttack(PlayerCharacterTemplate targetCharacter)
    {
        if (targetCharacter == null || Time.time < nextChaserMeleeAttackTime)
        {
            return;
        }

        Vector2 attackDirection = (Vector2)targetCharacter.transform.position - (Vector2)transform.position;
        float attackZone = Mathf.Max(0f, chaserMeleeAttackZone);

        if (attackDirection.sqrMagnitude <= attackZone * attackZone)
        {
            UseChaserMeleeAttack(attackDirection);
        }

        ScheduleNextChaserMeleeAttack();
    }

    /// <summary>
    /// Выполняет ближнюю круговую атаку перед боссом по логике ChaserEnemy.
    /// </summary>
    private void UseChaserMeleeAttack(Vector2 attackDirection)
    {
        if (attackDirection.sqrMagnitude <= 0.0001f)
        {
            attackDirection = lastChaserMeleeAttackDirection;
        }

        attackDirection = attackDirection.normalized;
        lastChaserMeleeAttackDirection = attackDirection;

        Vector2 attackCenter = GetChaserMeleeAttackCenter(attackDirection);
        CreateChaserMeleeAttackCircle(attackCenter);

        int damagedTargetsCount = DamagePlayerCharactersInChaserCircle(attackCenter, attackDirection);
        Debug.Log($"{name}: ближняя атака босса нанесла {Damage:0.0} урона. Целей задето: {damagedTargetsCount}.");
    }

    /// <summary>
    /// Наносит урон всем живым персонажам в круге ближней атаки.
    /// </summary>
    private int DamagePlayerCharactersInChaserCircle(Vector2 attackCenter, Vector2 attackDirection)
    {
        Collider2D[] hitColliders =
            Physics2D.OverlapCircleAll(attackCenter, chaserMeleeAttackRange, chaserMeleeAttackLayers);
        HashSet<PlayerCharacterTemplate> damagedCharacters = new HashSet<PlayerCharacterTemplate>();

        foreach (Collider2D hitCollider in hitColliders)
        {
            if (hitCollider == null)
            {
                continue;
            }

            PlayerCharacterTemplate character = hitCollider.GetComponentInParent<PlayerCharacterTemplate>();

            if (character == null || !character.IsAlive || !damagedCharacters.Add(character))
            {
                continue;
            }

            character.TakeDamage(Damage, attackDirection, chaserMeleeKnockbackForce);
        }

        return damagedCharacters.Count;
    }

    /// <summary>
    /// Назначает случайное время следующей ближней атаки во время погони.
    /// </summary>
    private void ScheduleNextChaserMeleeAttack()
    {
        float finalMinInterval = Mathf.Min(chaserMeleeAttackMinInterval, chaserMeleeAttackMaxInterval);
        float finalMaxInterval = Mathf.Max(chaserMeleeAttackMinInterval, chaserMeleeAttackMaxInterval);
        nextChaserMeleeAttackTime = Time.time + Random.Range(finalMinInterval, finalMaxInterval);
    }

    /// <summary>
    /// Завершает фазу погони и возвращает босса к циклу атак.
    /// </summary>
    private void CompleteChaserMeleeAttack()
    {
        isChaserAttackActive = false;
        chaserAttackCoroutine = null;
        StopBossMovementForAttack();

        if (IsAlive)
        {
            FinishCurrentAttackCycle();
        }
    }

    /// <summary>
    /// Создает визуальный круг ближней атаки босса.
    /// </summary>
    private void CreateChaserMeleeAttackCircle(Vector2 attackCenter)
    {
        GameObject attackCircle = CreateCircleVisual(
            "BossChaserMeleeAttackCircle",
            attackCenter,
            chaserMeleeAttackRange,
            chaserMeleeAttackCircleSegments,
            chaserMeleeAttackCircleWidth,
            chaserMeleeAttackCircleColor);

        Destroy(attackCircle, chaserMeleeAttackVisualLifeTime);
    }

    /// <summary>
    /// Создает кольцо через LineRenderer.
    /// </summary>
    private GameObject CreateCircleVisual(
        string circleName,
        Vector2 circleCenter,
        float circleRadius,
        int circleSegments,
        float circleWidth,
        Color circleColor,
        int sortingOrder = 10)
    {
        GameObject circleObject = new GameObject(circleName);
        circleObject.transform.position = circleCenter;

        int finalSegments = Mathf.Max(8, circleSegments);
        float finalRadius = Mathf.Max(0f, circleRadius);
        LineRenderer lineRenderer = circleObject.AddComponent<LineRenderer>();

        lineRenderer.useWorldSpace = false;
        lineRenderer.loop = true;
        lineRenderer.positionCount = finalSegments;
        lineRenderer.startWidth = circleWidth;
        lineRenderer.endWidth = circleWidth;
        lineRenderer.startColor = circleColor;
        lineRenderer.endColor = circleColor;
        lineRenderer.sortingOrder = sortingOrder;
        lineRenderer.sharedMaterial = GetChaserAttackCircleVisualMaterial();

        for (int i = 0; i < finalSegments; i++)
        {
            float angle = i / (float)finalSegments * Mathf.PI * 2f;
            Vector3 point = new Vector3(
                Mathf.Cos(angle) * finalRadius,
                Mathf.Sin(angle) * finalRadius,
                0f);

            lineRenderer.SetPosition(i, point);
        }

        return circleObject;
    }

    /// <summary>
    /// Возвращает общий материал для круговой визуализации ближней атаки.
    /// </summary>
    private static Material GetChaserAttackCircleVisualMaterial()
    {
        if (chaserAttackCircleVisualMaterial != null)
        {
            return chaserAttackCircleVisualMaterial;
        }

        Shader spriteShader = Shader.Find("Sprites/Default");

        if (spriteShader == null)
        {
            return null;
        }

        chaserAttackCircleVisualMaterial = new Material(spriteShader);
        return chaserAttackCircleVisualMaterial;
    }

    /// <summary>
    /// Возвращает центр ближней атаки перед боссом.
    /// </summary>
    private Vector2 GetChaserMeleeAttackCenter(Vector2 attackDirection)
    {
        return (Vector2)transform.position + attackDirection.normalized * chaserMeleeAttackOffsetDistance;
    }

    private void StartSpiralProjectileAttack()
    {
        if (spiralProjectilePrefab == null)
        {
            Debug.LogWarning($"{name}: не назначен префаб снаряда для спиральной атаки босса.");
            return;
        }

        if (spiralAttackCoroutine != null)
        {
            StopCoroutine(spiralAttackCoroutine);
        }

        spiralAttackCoroutine = StartCoroutine(SpiralProjectileAttackCoroutine());
    }

    /// <summary>
    /// Выпускает несколько круговых волн с поворотом угла между волнами и паузами для уклонения.
    /// </summary>
    private IEnumerator SpiralProjectileAttackCoroutine()
    {
        isSpiralAttackActive = true;
        StopBossMovementForAttack();

        if (spiralInitialPause > 0f)
        {
            yield return new WaitForSeconds(spiralInitialPause);
        }

        int finalWavesCount = Mathf.Max(1, spiralWavesCount);

        for (int waveIndex = 0; waveIndex < finalWavesCount; waveIndex++)
        {
            if (!CanContinueSpiralAttack())
            {
                break;
            }

            ShootSpiralWave(waveIndex);

            if (waveIndex < finalWavesCount - 1 && spiralPauseBetweenWaves > 0f)
            {
                yield return new WaitForSeconds(spiralPauseBetweenWaves);
            }
        }

        isSpiralAttackActive = false;
        spiralAttackCoroutine = null;

        if (IsAlive)
        {
            FinishCurrentAttackCycle();
        }
    }

    /// <summary>
    /// Выпускает одну волну снарядов по кругу вокруг босса.
    /// </summary>
    private void ShootSpiralWave(int waveIndex)
    {
        int finalProjectilesCount = Mathf.Max(1, spiralProjectilesPerWave);
        float angleStep = 360f / finalProjectilesCount;
        float baseAngle = spiralStartAngle + spiralAngleStepPerWave * waveIndex;

        for (int projectileIndex = 0; projectileIndex < finalProjectilesCount; projectileIndex++)
        {
            float projectileAngle = baseAngle + angleStep * projectileIndex;
            Vector2 projectileDirection = GetDirectionFromAngle(projectileAngle);

            SpawnSpiralProjectile(projectileDirection);
        }

        Debug.Log($"{name}: спиральная атака выпустила волну {waveIndex + 1}/{spiralWavesCount}.");
    }

    /// <summary>
    /// Создает один снаряд спиральной атаки.
    /// </summary>
    private void SpawnSpiralProjectile(Vector2 projectileDirection)
    {
        Vector2 spawnPosition = (Vector2)transform.position
            + projectileDirection.normalized * spiralProjectileSpawnOffsetDistance;
        EnemyProjectile projectile = Instantiate(spiralProjectilePrefab, spawnPosition, Quaternion.identity);

        projectile.Initialize(
            projectileDirection,
            Damage,
            spiralProjectileSpeed,
            spiralProjectileKnockbackForce,
            spiralProjectileLifeTime,
            spiralProjectileHitLayers,
            this);
    }

    /// <summary>
    /// Останавливает движение босса пока он выполняет неподвижную атаку.
    /// </summary>
    private void StopBossMovementForAttack()
    {
        ResetAiDistanceMovement();

        if (bossRigidbody == null)
        {
            bossRigidbody = GetComponent<Rigidbody2D>();
        }

        if (bossRigidbody == null)
        {
            return;
        }

        bossRigidbody.velocity = Vector2.zero;
        bossRigidbody.angularVelocity = 0f;
    }

    /// <summary>
    /// Завершает текущую атаку и назначает следующую по очереди.
    /// </summary>
    private void FinishCurrentAttackCycle()
    {
        SelectNextAttack();
        ScheduleNextAttack();
    }

    /// <summary>
    /// Проверяет, может ли босс продолжать длительную спиральную атаку.
    /// </summary>
    private bool CanContinueSpiralAttack()
    {
        return IsAlive && !IsStunned;
    }

    /// <summary>
    /// Возвращает направление по углу в градусах.
    /// </summary>
    private Vector2 GetDirectionFromAngle(float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)).normalized;
    }

    /// <summary>
    /// Создает полоску здоровья под боссом.
    /// </summary>
    private void CreateHealthBarVisual()
    {
        if (healthBarRoot != null)
        {
            return;
        }

        GameObject healthBarObject = new GameObject("HealthBarVisual");
        healthBarObject.transform.SetParent(transform, false);
        healthBarRoot = healthBarObject.transform;

        healthBarBackgroundRenderer = CreateHealthBarPart("Background", healthBarRoot, healthBarBackgroundColor);
        healthBarFillRenderer = CreateHealthBarPart("Fill", healthBarRoot, healthBarFillColor);

        RefreshHealthBarVisualTransform();
        RefreshHealthBarVisualSorting();
    }

    /// <summary>
    /// Создает отдельный слой полоски здоровья.
    /// </summary>
    private SpriteRenderer CreateHealthBarPart(string partName, Transform parent, Color partColor)
    {
        GameObject partObject = new GameObject(partName);
        partObject.transform.SetParent(parent, false);

        SpriteRenderer partRenderer = partObject.AddComponent<SpriteRenderer>();
        partRenderer.sprite = GetHealthBarSprite();
        partRenderer.color = partColor;

        return partRenderer;
    }

    /// <summary>
    /// Возвращает общий пиксельный спрайт для прямоугольников полоски здоровья.
    /// </summary>
    private static Sprite GetHealthBarSprite()
    {
        if (healthBarSprite != null)
        {
            return healthBarSprite;
        }

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "GeneratedBossHealthBarPixel",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        texture.SetPixel(0, 0, Color.white);
        texture.Apply();

        healthBarSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
        healthBarSprite.name = "GeneratedBossHealthBarPixel";

        return healthBarSprite;
    }

    /// <summary>
    /// Обновляет цвет, размер и заполнение полоски здоровья босса.
    /// </summary>
    private void UpdateHealthBarVisual()
    {
        if (healthBarRoot == null || healthBarBackgroundRenderer == null || healthBarFillRenderer == null)
        {
            return;
        }

        healthBarBackgroundRenderer.color = healthBarBackgroundColor;
        healthBarFillRenderer.color = healthBarFillColor;
        RefreshHealthBarVisualTransform();
        RefreshHealthBarVisualSorting();
    }

    /// <summary>
    /// Расставляет фон и заполнение полоски здоровья под боссом.
    /// </summary>
    private void RefreshHealthBarVisualTransform()
    {
        if (healthBarRoot == null || healthBarBackgroundRenderer == null || healthBarFillRenderer == null)
        {
            return;
        }

        float barWidth = Mathf.Max(0f, healthBarWidth);
        float barHeight = Mathf.Max(0f, healthBarHeight);
        float healthPercent = GetHealthPercent();
        float fillWidth = barWidth * healthPercent;

        healthBarRoot.localPosition = new Vector3(healthBarOffset.x, healthBarOffset.y, 0f);
        healthBarRoot.localScale = Vector3.one;

        healthBarBackgroundRenderer.transform.localPosition = Vector3.zero;
        healthBarBackgroundRenderer.transform.localScale = new Vector3(barWidth, barHeight, 1f);

        healthBarFillRenderer.transform.localPosition = new Vector3((fillWidth - barWidth) * 0.5f, 0f, 0f);
        healthBarFillRenderer.transform.localScale = new Vector3(fillWidth, barHeight, 1f);
    }

    /// <summary>
    /// Поднимает полоску здоровья поверх остальных спрайтов босса.
    /// </summary>
    private void RefreshHealthBarVisualSorting()
    {
        if (healthBarBackgroundRenderer == null || healthBarFillRenderer == null)
        {
            return;
        }

        int highestSortingOrder = 0;
        int sortingLayerId = healthBarBackgroundRenderer.sortingLayerID;
        SpriteRenderer[] spriteRenderers = GetComponentsInChildren<SpriteRenderer>();
        bool foundBaseRenderer = false;

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            SpriteRenderer spriteRenderer = spriteRenderers[i];

            if (spriteRenderer == null
                || ReferenceEquals(spriteRenderer, healthBarBackgroundRenderer)
                || ReferenceEquals(spriteRenderer, healthBarFillRenderer))
            {
                continue;
            }

            if (!foundBaseRenderer || spriteRenderer.sortingOrder >= highestSortingOrder)
            {
                foundBaseRenderer = true;
                highestSortingOrder = spriteRenderer.sortingOrder;
                sortingLayerId = spriteRenderer.sortingLayerID;
            }
        }

        healthBarBackgroundRenderer.sortingLayerID = sortingLayerId;
        healthBarFillRenderer.sortingLayerID = sortingLayerId;
        healthBarBackgroundRenderer.sortingOrder = highestSortingOrder + healthBarSortingOrderOffset;
        healthBarFillRenderer.sortingOrder = healthBarBackgroundRenderer.sortingOrder + 1;
    }

    /// <summary>
    /// Возвращает процент текущего здоровья босса для заполнения полоски.
    /// </summary>
    private float GetHealthPercent()
    {
        return Mathf.Clamp01(CurrentHealth / Mathf.Max(MaxHealth, 0.01f));
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!isChargeAttackActive || !IsStaticRigidbodyCollision(collision))
        {
            return;
        }

        InterruptChargeAttackWithStun();
    }

    /// <summary>
    /// Проверяет, что босс во время рывка врезался именно в статичный Rigidbody2D.
    /// </summary>
    private bool IsStaticRigidbodyCollision(Collision2D collision)
    {
        if (collision == null)
        {
            return false;
        }

        Rigidbody2D collidedRigidbody = collision.rigidbody;

        if (collidedRigidbody == null && collision.collider != null)
        {
            collidedRigidbody = collision.collider.attachedRigidbody;
        }

        return collidedRigidbody != null && collidedRigidbody.bodyType == RigidbodyType2D.Static;
    }

    private void OnDrawGizmosSelected()
    {
        Vector2 chaserAttackDirection = lastChaserMeleeAttackDirection.sqrMagnitude > 0f
            ? lastChaserMeleeAttackDirection.normalized
            : Vector2.right;
        Vector2 chaserAttackCenter =
            (Vector2)transform.position + chaserAttackDirection * chaserMeleeAttackOffsetDistance;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(chaserAttackCenter, chaserMeleeAttackRange);

        DrawAiMovementGizmos();
        DrawAiAttackGizmos();
    }
}
