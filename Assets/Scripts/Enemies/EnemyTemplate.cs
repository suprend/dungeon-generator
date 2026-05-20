//DP
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Шаблон противника
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class EnemyTemplate : MonoBehaviour, IDamageable
{
    private enum AiMovementMode
    {
        Idle,
        ApproachTarget,
        RetreatFromTarget
    }

    private const float IncapacitatedSpriteRotationZ = 90f;
    private const float TransformKnockbackFallbackDistanceMultiplier = 0.1f;
    private const float MinKnockbackDirectionSqrMagnitude = 0.0001f;
    private const float MinAiMovementSqrMagnitude = 0.0001f;
    private const float KnockbackSlowdown = 20f;

    [Header("Статы противника")]
    [SerializeField, Min(1f)] private float maxHealth = 50f;
    [SerializeField, Min(0f)] private float currentHealth = 50f;
    [SerializeField, Min(0f)] private float damage = 5f;
    [SerializeField, Min(0f)] private float speed = 2f;
    [SerializeField] private float damageResistancePercent;

    [Header("Реакция на урон")]
    [SerializeField, Min(0f)] private float damageFlashDuration = 0.1f;
    [SerializeField] private Color damageFlashColor = Color.red;

    [Header("Визуал снижения защиты")]
    [SerializeField] private Sprite defenceDownStatusSprite;
    [SerializeField] private Vector2 defenceDownStatusOffset = new Vector2(0f, 0.85f);
    [SerializeField, Min(0f)] private float defenceDownStatusScale = 1f;
    [SerializeField] private int defenceDownStatusSortingOrderOffset = 5;

    [Header("Визуал замедления")]
    [SerializeField] private Sprite speedDownStatusSprite;
    [SerializeField] private Vector2 speedDownStatusOffset = new Vector2(0f, 0.85f);
    [SerializeField, Min(0f)] private float speedDownStatusScale = 1f;
    [SerializeField] private int speedDownStatusSortingOrderOffset = 5;

    [Header("Визуал оглушения")]
    [SerializeField] private Sprite stunStatusSprite;
    [SerializeField] private Vector2 stunStatusOffset = new Vector2(0f, 0.85f);
    [SerializeField, Min(0f)] private float stunStatusScale = 1f;
    [SerializeField] private int stunStatusSortingOrderOffset = 5;

    [Header("Смерть")]
    [SerializeField, Range(0f, 1f)] private float incapacitatedBrightnessMultiplier = 0.45f;
    [Tooltip("Multiplier applied to knockback from a lethal hit.")]
    [SerializeField, Min(1f)] private float deathKnockbackForceMultiplier = 2f;

    [Header("AI Movement")]
    [SerializeField, Min(0f)] private float aiApproachZone = 5f;
    [SerializeField, Min(0f)] private float aiRetreatZone = 2f;
    [SerializeField, Min(0f)] private float aiMovementZoneHysteresis = 0.35f;
    [SerializeField, Min(0f)] private float aiMovementSmoothing = 10f;
    [SerializeField] private Color aiApproachZoneGizmoColor = new Color(0.2f, 0.7f, 1f, 0.9f);
    [SerializeField] private Color aiRetreatZoneGizmoColor = new Color(1f, 0.35f, 0.15f, 0.9f);

    [Header("AI Attack")]
    [SerializeField, Min(0f)] private float aiAttackZone = 4f;
    [SerializeField] private Color aiAttackZoneGizmoColor = Color.green;

    private Rigidbody2D enemyRigidbody;
    private SpriteRenderer[] spriteRenderers;
    private Color[] defaultSpriteColors;
    private Transform[] spriteTransforms;
    private Quaternion[] defaultSpriteLocalRotations;
    private SpriteRenderer stunStatusRenderer;
    private SpriteRenderer defenceDownStatusRenderer;
    private SpriteRenderer speedDownStatusRenderer;
    private Coroutine speedSlowCoroutine;
    private Coroutine externalMovementCoroutine;
    private Coroutine stunCoroutine;
    private Coroutine enemyFlashCoroutine;
    private Coroutine damageResistanceReductionCoroutine;
    private Vector2 smoothedAiMovementInput;
    private Vector2 knockbackVelocity;
    private float currentSpeed;
    private float activeTemporaryDamageResistanceReductionPercent;
    private AiMovementMode aiMovementMode;
    private bool isStunned;
    private bool isSpeedSlowed;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public float Damage => damage;
    public float BaseSpeed => speed;
    public float Speed => isStunned ? 0f : currentSpeed;
    public float DamageResistancePercent => damageResistancePercent;
    public bool IsExternalMovementActive { get; private set; }
    public bool IsStunned => isStunned;
    public bool IsAlive => currentHealth > 0f;
    public event Action<EnemyTemplate> Died;

    private void Awake()
    {
        enemyRigidbody = GetComponent<Rigidbody2D>();
        enemyRigidbody.gravityScale = 0f;
        enemyRigidbody.freezeRotation = true;

        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        currentSpeed = speed;
        CacheSpriteRenderers();
        CreateStunStatusVisual();
        CreateDefenceDownStatusVisual();
        CreateSpeedDownStatusVisual();
        UpdateStunStatusVisual();
        UpdateDefenceDownStatusVisual();
        UpdateSpeedDownStatusVisual();

        if (!IsAlive)
        {
            BecomeIncapacitated();
        }
    }

    private void OnValidate()
    {
        if (stunStatusRenderer != null)
        {
            stunStatusRenderer.sprite = stunStatusSprite;
            RefreshStunStatusVisualTransform();
            RefreshStunStatusVisualSorting();
            UpdateStunStatusVisual();
        }

        if (defenceDownStatusRenderer != null)
        {
            defenceDownStatusRenderer.sprite = defenceDownStatusSprite;
            RefreshDefenceDownStatusVisualTransform();
            RefreshDefenceDownStatusVisualSorting();
            UpdateDefenceDownStatusVisual();
        }

        if (speedDownStatusRenderer != null)
        {
            speedDownStatusRenderer.sprite = speedDownStatusSprite;
            RefreshSpeedDownStatusVisualTransform();
            RefreshSpeedDownStatusVisualSorting();
            UpdateSpeedDownStatusVisual();
        }
    }

    protected void MoveWithAiDistancesToTarget(PlayerCharacterTemplate targetCharacter)
    {
        if (!TryCacheRigidbody())
        {
            ResetAiDistanceMovement();
            return;
        }

        if (IsExternalMovementActive)
        {
            ResetAiDistanceMovement();
            return;
        }

        if (!CanUseAiDistanceMovement(targetCharacter))
        {
            ResetAiDistanceMovement();
            MoveWithVelocity(Vector2.zero);
            return;
        }

        Vector2 movementInput = GetAiDistanceMovementInput(targetCharacter.transform.position);
        SetAiDistanceMovementInput(movementInput);

        if (smoothedAiMovementInput.sqrMagnitude <= MinAiMovementSqrMagnitude)
        {
            MoveWithVelocity(Vector2.zero);
            return;
        }

        MoveWithVelocity(smoothedAiMovementInput * Speed);
    }

    protected void ResetAiDistanceMovement()
    {
        aiMovementMode = AiMovementMode.Idle;
        smoothedAiMovementInput = Vector2.zero;
    }

    protected void DrawAiMovementGizmos()
    {
        Gizmos.color = aiApproachZoneGizmoColor;
        Gizmos.DrawWireSphere(transform.position, aiApproachZone);

        Gizmos.color = aiRetreatZoneGizmoColor;
        Gizmos.DrawWireSphere(transform.position, aiRetreatZone);
    }

    protected void DrawAiAttackGizmos()
    {
        Gizmos.color = aiAttackZoneGizmoColor;
        Gizmos.DrawWireSphere(transform.position, aiAttackZone);
    }

    protected PlayerCharacterTemplate FindClosestAlivePlayerCharacter()
    {
        return FindClosestAlivePlayerCharacter(float.PositiveInfinity);
    }

    protected PlayerCharacterTemplate FindClosestAlivePlayerCharacterInAiAttackZone()
    {
        return FindClosestAlivePlayerCharacter(aiAttackZone);
    }

    private PlayerCharacterTemplate FindClosestAlivePlayerCharacter(float maxDistance)
    {
        PlayerCharacterTemplate[] characters =
            FindObjectsByType<PlayerCharacterTemplate>(FindObjectsSortMode.None);
        PlayerCharacterTemplate closestCharacter = null;
        float closestSqrDistance = float.PositiveInfinity;
        float maxSqrDistance = Mathf.Max(0f, maxDistance) * Mathf.Max(0f, maxDistance);
        Vector2 currentPosition = transform.position;

        for (int i = 0; i < characters.Length; i++)
        {
            PlayerCharacterTemplate character = characters[i];

            if (character == null || !character.IsAlive)
            {
                continue;
            }

            float sqrDistance = ((Vector2)character.transform.position - currentPosition).sqrMagnitude;

            if (sqrDistance > maxSqrDistance || sqrDistance >= closestSqrDistance)
            {
                continue;
            }

            closestSqrDistance = sqrDistance;
            closestCharacter = character;
        }

        return closestCharacter;
    }

    private bool CanUseAiDistanceMovement(PlayerCharacterTemplate targetCharacter)
    {
        return targetCharacter != null
            && targetCharacter.IsAlive
            && IsAlive
            && !IsExternalMovementActive
            && !IsStunned
            && TryCacheRigidbody();
    }

    private Vector2 GetAiDistanceMovementInput(Vector2 targetPosition)
    {
        Vector2 directionToTarget = targetPosition - enemyRigidbody.position;
        float distanceToTarget = directionToTarget.magnitude;
        float retreatZone = Mathf.Max(0f, aiRetreatZone);
        float approachZone = Mathf.Max(retreatZone, aiApproachZone);
        float hysteresis = Mathf.Max(0f, aiMovementZoneHysteresis);
        float retreatStopDistance = Mathf.Min(approachZone, retreatZone + hysteresis);

        if (aiMovementMode == AiMovementMode.RetreatFromTarget
            && distanceToTarget >= retreatStopDistance)
        {
            aiMovementMode = AiMovementMode.Idle;
        }

        if (aiMovementMode == AiMovementMode.ApproachTarget
            && distanceToTarget <= approachZone)
        {
            aiMovementMode = AiMovementMode.Idle;
            smoothedAiMovementInput = Vector2.zero;
        }

        if (aiMovementMode == AiMovementMode.Idle)
        {
            if (distanceToTarget < retreatZone)
            {
                aiMovementMode = AiMovementMode.RetreatFromTarget;
            }
            else if (distanceToTarget > approachZone)
            {
                aiMovementMode = AiMovementMode.ApproachTarget;
            }
        }

        if (directionToTarget.sqrMagnitude <= MinAiMovementSqrMagnitude)
        {
            return Vector2.zero;
        }

        if (aiMovementMode == AiMovementMode.RetreatFromTarget)
        {
            return -directionToTarget.normalized;
        }

        if (aiMovementMode == AiMovementMode.ApproachTarget)
        {
            return directionToTarget.normalized;
        }

        return Vector2.zero;
    }

    private void SetAiDistanceMovementInput(Vector2 newMovementInput)
    {
        Vector2 targetMovementInput = newMovementInput.sqrMagnitude > MinAiMovementSqrMagnitude
            ? Vector2.ClampMagnitude(newMovementInput, 1f)
            : Vector2.zero;
        float smoothing = Mathf.Max(0f, aiMovementSmoothing);

        smoothedAiMovementInput = smoothing > 0f
            ? Vector2.MoveTowards(
                smoothedAiMovementInput,
                targetMovementInput,
                smoothing * Time.fixedDeltaTime)
            : targetMovementInput;

        if (smoothedAiMovementInput.sqrMagnitude <= MinAiMovementSqrMagnitude)
        {
            smoothedAiMovementInput = Vector2.zero;
            return;
        }

        smoothedAiMovementInput = Vector2.ClampMagnitude(smoothedAiMovementInput, 1f);
    }

    /// <summary>
    /// Получение урона
    /// </summary>
    public void TakeDamage(float damageAmount)
    {
        TakeDamage(damageAmount, Vector2.zero, 0f);
    }

    public void TakeDamage(float damageAmount, Vector2 knockbackDirection, float knockbackForce)
    {
        if (damageAmount <= 0f || !IsAlive)
        {
            return;
        }

        bool wasAlive = IsAlive;
        ApplyKnockback(knockbackDirection, knockbackForce, 1f);
        StartDamageFlash();

        float finalDamageAmount = GetDamageAfterResistance(damageAmount);
        currentHealth = Mathf.Max(currentHealth - finalDamageAmount, 0f);

        if (!IsAlive)
        {
            BecomeIncapacitated();
            ApplyDeathKnockback(knockbackDirection, knockbackForce);
            if (wasAlive)
                Died?.Invoke(this);
        }

        Debug.Log($"{name}: получил {finalDamageAmount:0.0} урона. Здоровье: {currentHealth}/{maxHealth}.");
    }

    /// <summary>
    /// Изменение сопротивления урону
    /// </summary>
    public void ChangeDamageResistance(float resistanceDeltaPercent)
    {
        damageResistancePercent += resistanceDeltaPercent;
        Debug.Log($"{name}: сопротивление урону изменено на {resistanceDeltaPercent:0.0}%. Сейчас: {damageResistancePercent:0.0}%.");
    }

    /// <summary>
    /// Временно снижает сопротивление урону и показывает статус снижения защиты
    /// </summary>
    public void ApplyTemporaryDamageResistanceReduction(float reductionPercent, float duration)
    {
        if (!IsAlive || reductionPercent <= 0f || duration <= 0f)
        {
            return;
        }

        if (damageResistanceReductionCoroutine != null)
        {
            StopCoroutine(damageResistanceReductionCoroutine);
            RestoreTemporaryDamageResistanceReduction();
        }

        damageResistanceReductionCoroutine = StartCoroutine(
            ApplyTemporaryDamageResistanceReductionCoroutine(reductionPercent, duration));
    }

    /// <summary>
    /// Восстанавливает здоровье противника
    /// </summary>
    public void Heal(float healAmount)
    {
        if (healAmount <= 0f || !IsAlive)
        {
            return;
        }

        currentHealth = Mathf.Min(currentHealth + healAmount, maxHealth);
    }

    /// <summary>
    /// Учитывает сопротивление урону
    /// </summary>
    private float GetDamageAfterResistance(float damageAmount)
    {
        float damageMultiplier = Mathf.Max(0f, 1f - damageResistancePercent / 100f);
        return damageAmount * damageMultiplier;
    }

    public void ApplyKnockback(Vector2 knockbackDirection, float knockbackForce)
    {
        float forceMultiplier = IsAlive ? 1f : deathKnockbackForceMultiplier;
        ApplyKnockback(knockbackDirection, knockbackForce, forceMultiplier);
    }

    private void ApplyDeathKnockback(Vector2 knockbackDirection, float knockbackForce)
    {
        ApplyKnockback(knockbackDirection, knockbackForce, deathKnockbackForceMultiplier);
    }

    private void ApplyKnockback(
        Vector2 knockbackDirection,
        float knockbackForce,
        float forceMultiplier)
    {
        if (knockbackForce <= 0f || knockbackDirection.sqrMagnitude <= MinKnockbackDirectionSqrMagnitude)
        {
            return;
        }

        Vector2 normalizedDirection = knockbackDirection.normalized;
        float finalKnockbackForce = knockbackForce * Mathf.Max(1f, forceMultiplier);

        if (!TryCacheRigidbody())
        {
            transform.position += (Vector3)(normalizedDirection
                * finalKnockbackForce
                * TransformKnockbackFallbackDistanceMultiplier);
            return;
        }

        if (enemyRigidbody.bodyType == RigidbodyType2D.Static)
        {
            return;
        }

        if (IsAlive && enemyRigidbody.simulated)
        {
            enemyRigidbody.velocity = Vector2.zero;
            knockbackVelocity = normalizedDirection * finalKnockbackForce;
            return;
        }

        if (enemyRigidbody.simulated)
        {
            enemyRigidbody.AddForce(normalizedDirection * finalKnockbackForce, ForceMode2D.Impulse);
            return;
        }

        transform.position += (Vector3)(normalizedDirection
            * finalKnockbackForce
            * TransformKnockbackFallbackDistanceMultiplier);
    }

    private void MoveWithVelocity(Vector2 movementVelocity)
    {
        if (!TryCacheRigidbody() || enemyRigidbody.bodyType == RigidbodyType2D.Static)
        {
            return;
        }

        Vector2 totalVelocity = movementVelocity + knockbackVelocity;

        if (totalVelocity.sqrMagnitude > MinAiMovementSqrMagnitude)
        {
            enemyRigidbody.MovePosition(enemyRigidbody.position + totalVelocity * Time.fixedDeltaTime);
        }

        enemyRigidbody.velocity = Vector2.zero;
        UpdateKnockbackVelocity();
    }

    private void MoveToPositionWithKnockback(Vector2 nextPosition)
    {
        if (!TryCacheRigidbody() || enemyRigidbody.bodyType == RigidbodyType2D.Static)
        {
            return;
        }

        enemyRigidbody.MovePosition(nextPosition + knockbackVelocity * Time.fixedDeltaTime);
        enemyRigidbody.velocity = Vector2.zero;
        UpdateKnockbackVelocity();
    }

    private void UpdateKnockbackVelocity()
    {
        if (knockbackVelocity.sqrMagnitude <= MinKnockbackDirectionSqrMagnitude)
        {
            knockbackVelocity = Vector2.zero;
            return;
        }

        knockbackVelocity = Vector2.MoveTowards(
            knockbackVelocity,
            Vector2.zero,
            KnockbackSlowdown * Time.fixedDeltaTime);
    }

    /// <summary>
    /// Сохраняет спрайты тела противника чтобы статусы могли правильно вставать поверх них
    /// </summary>
    private void CacheSpriteRenderers()
    {
        SpriteRenderer[] allSpriteRenderers = GetComponentsInChildren<SpriteRenderer>();
        int bodyRenderersCount = 0;

        for (int i = 0; i < allSpriteRenderers.Length; i++)
        {
            if (IsEnemyBodySpriteRenderer(allSpriteRenderers[i]))
            {
                bodyRenderersCount++;
            }
        }

        spriteRenderers = new SpriteRenderer[bodyRenderersCount];
        defaultSpriteColors = new Color[bodyRenderersCount];
        List<Transform> bodySpriteTransforms = new List<Transform>();
        List<Quaternion> bodySpriteRotations = new List<Quaternion>();
        int bodyRendererIndex = 0;

        for (int i = 0; i < allSpriteRenderers.Length; i++)
        {
            if (!IsEnemyBodySpriteRenderer(allSpriteRenderers[i]))
            {
                continue;
            }

            spriteRenderers[bodyRendererIndex] = allSpriteRenderers[i];
            defaultSpriteColors[bodyRendererIndex] = allSpriteRenderers[i].color;

            Transform spriteTransform = allSpriteRenderers[i].transform;

            if (!bodySpriteTransforms.Contains(spriteTransform))
            {
                bodySpriteTransforms.Add(spriteTransform);
                bodySpriteRotations.Add(spriteTransform.localRotation);
            }

            bodyRendererIndex++;
        }

        spriteTransforms = bodySpriteTransforms.ToArray();
        defaultSpriteLocalRotations = bodySpriteRotations.ToArray();
    }

    /// <summary>
    /// Проверяет что спрайт относится к телу противника а не UI
    /// </summary>
    private bool IsEnemyBodySpriteRenderer(SpriteRenderer spriteRenderer)
    {
        return spriteRenderer != null
            && !ReferenceEquals(spriteRenderer, stunStatusRenderer)
            && !ReferenceEquals(spriteRenderer, defenceDownStatusRenderer)
            && !ReferenceEquals(spriteRenderer, speedDownStatusRenderer);
    }

    /// <summary>
    /// Переводит противника в недееспособное состояние
    /// </summary>
    private void BecomeIncapacitated()
    {
        StopCoroutineIfRunning(ref speedSlowCoroutine);
        StopCoroutineIfRunning(ref stunCoroutine);

        if (damageResistanceReductionCoroutine != null)
        {
            StopCoroutine(damageResistanceReductionCoroutine);
            damageResistanceReductionCoroutine = null;
            RestoreTemporaryDamageResistanceReduction();
        }

        if (externalMovementCoroutine != null)
        {
            StopCoroutine(externalMovementCoroutine);
            FinishExternalMovement();
        }

        StopCoroutineIfRunning(ref enemyFlashCoroutine);
        ResetAiDistanceMovement();
        knockbackVelocity = Vector2.zero;
        currentSpeed = 0f;
        isStunned = false;
        isSpeedSlowed = false;

        if (enemyRigidbody != null)
        {
            enemyRigidbody.velocity = Vector2.zero;
        }

        RestoreEnemyBodyColors();
        SetIncapacitatedSpriteRotation(true);
        UpdateStunStatusVisual();
        UpdateDefenceDownStatusVisual();
        UpdateSpeedDownStatusVisual();
    }

    private void StopCoroutineIfRunning(ref Coroutine coroutine)
    {
        if (coroutine == null)
        {
            return;
        }

        StopCoroutine(coroutine);
        coroutine = null;
    }

    /// <summary>
    /// Перезапускает красную вспышку урона если противника ударили несколько раз подряд
    /// </summary>
    private void StartDamageFlash()
    {
        StartEnemyFlash(damageFlashColor, damageFlashDuration);
    }

    /// <summary>
    /// Запускает короткое окрашивание спрайтов противника указанным цветом
    /// </summary>
    private void StartEnemyFlash(Color flashColor, float flashDuration)
    {
        if (flashDuration <= 0f)
        {
            return;
        }

        if (enemyFlashCoroutine != null)
        {
            StopCoroutine(enemyFlashCoroutine);
        }

        enemyFlashCoroutine = StartCoroutine(EnemyFlashCoroutine(flashColor, flashDuration));
    }

    /// <summary>
    /// На мгновение окрашивает спрайты и возвращает исходные цвета
    /// </summary>
    private IEnumerator EnemyFlashCoroutine(Color flashColor, float flashDuration)
    {
        if (spriteRenderers == null || defaultSpriteColors == null)
        {
            CacheSpriteRenderers();
        }

        if (spriteRenderers != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] != null)
                {
                    spriteRenderers[i].color = flashColor;
                }
            }
        }

        yield return new WaitForSeconds(flashDuration);

        RestoreEnemyBodyColors();
        enemyFlashCoroutine = null;
    }

    private void RestoreEnemyBodyColors()
    {
        if (spriteRenderers == null || defaultSpriteColors == null)
        {
            CacheSpriteRenderers();
        }

        if (spriteRenderers == null || defaultSpriteColors == null)
        {
            return;
        }

        int renderersCount = Mathf.Min(spriteRenderers.Length, defaultSpriteColors.Length);

        for (int i = 0; i < renderersCount; i++)
        {
            if (spriteRenderers[i] == null)
            {
                continue;
            }

            spriteRenderers[i].color = IsAlive
                ? defaultSpriteColors[i]
                : GetIncapacitatedColor(defaultSpriteColors[i]);
        }
    }

    private Color GetIncapacitatedColor(Color sourceColor)
    {
        return new Color(
            sourceColor.r * incapacitatedBrightnessMultiplier,
            sourceColor.g * incapacitatedBrightnessMultiplier,
            sourceColor.b * incapacitatedBrightnessMultiplier,
            sourceColor.a);
    }

    private void SetIncapacitatedSpriteRotation(bool shouldLieDown)
    {
        if (spriteTransforms == null || defaultSpriteLocalRotations == null)
        {
            CacheSpriteRenderers();
        }

        if (spriteTransforms == null || defaultSpriteLocalRotations == null)
        {
            return;
        }

        int transformsCount = Mathf.Min(spriteTransforms.Length, defaultSpriteLocalRotations.Length);

        for (int i = 0; i < transformsCount; i++)
        {
            if (spriteTransforms[i] == null)
            {
                continue;
            }

            spriteTransforms[i].localRotation = shouldLieDown
                ? defaultSpriteLocalRotations[i] * Quaternion.Euler(0f, 0f, IncapacitatedSpriteRotationZ)
                : defaultSpriteLocalRotations[i];
        }
    }

    /// <summary>
    /// Создает дочерний спрайт статуса оглушения
    /// </summary>
    private void CreateStunStatusVisual()
    {
        if (stunStatusRenderer != null)
        {
            return;
        }

        GameObject stunStatusObject = new GameObject("StunStatusVisual");
        stunStatusObject.transform.SetParent(transform, false);

        stunStatusRenderer = stunStatusObject.AddComponent<SpriteRenderer>();
        stunStatusRenderer.sprite = stunStatusSprite;
        RefreshStunStatusVisualTransform();
        RefreshStunStatusVisualSorting();
        stunStatusObject.SetActive(false);
    }

    /// <summary>
    /// Показывает или скрывает спрайт оглушения над противником
    /// </summary>
    private void UpdateStunStatusVisual()
    {
        if (stunStatusRenderer == null)
        {
            CreateStunStatusVisual();
        }

        if (stunStatusRenderer == null)
        {
            return;
        }

        bool shouldShowStatus = isStunned && IsAlive && stunStatusSprite != null;
        stunStatusRenderer.gameObject.SetActive(shouldShowStatus);

        if (!shouldShowStatus)
        {
            return;
        }

        stunStatusRenderer.sprite = stunStatusSprite;
        RefreshStunStatusVisualTransform();
        RefreshStunStatusVisualSorting();
    }

    /// <summary>
    /// Расставляет спрайт оглушения над противником
    /// </summary>
    private void RefreshStunStatusVisualTransform()
    {
        if (stunStatusRenderer == null)
        {
            return;
        }

        stunStatusRenderer.transform.localPosition = new Vector3(stunStatusOffset.x, stunStatusOffset.y, 0f);
        stunStatusRenderer.transform.localScale = new Vector3(stunStatusScale, stunStatusScale, 1f);
    }

    /// <summary>
    /// Поднимает спрайт оглушения поверх тела противника
    /// </summary>
    private void RefreshStunStatusVisualSorting()
    {
        if (stunStatusRenderer == null)
        {
            return;
        }

        int highestSortingOrder = 0;
        int sortingLayerId = stunStatusRenderer.sortingLayerID;

        if (spriteRenderers != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] == null)
                {
                    continue;
                }

                if (i == 0 || spriteRenderers[i].sortingOrder >= highestSortingOrder)
                {
                    highestSortingOrder = spriteRenderers[i].sortingOrder;
                    sortingLayerId = spriteRenderers[i].sortingLayerID;
                }
            }
        }

        stunStatusRenderer.sortingLayerID = sortingLayerId;
        stunStatusRenderer.sortingOrder = highestSortingOrder + stunStatusSortingOrderOffset;
    }

    /// <summary>
    /// Создает дочерний спрайт статуса снижения защиты
    /// </summary>
    private void CreateDefenceDownStatusVisual()
    {
        if (defenceDownStatusRenderer != null)
        {
            return;
        }

        GameObject defenceDownStatusObject = new GameObject("DefenceDownStatusVisual");
        defenceDownStatusObject.transform.SetParent(transform, false);

        defenceDownStatusRenderer = defenceDownStatusObject.AddComponent<SpriteRenderer>();
        defenceDownStatusRenderer.sprite = defenceDownStatusSprite;
        RefreshDefenceDownStatusVisualTransform();
        RefreshDefenceDownStatusVisualSorting();
        defenceDownStatusObject.SetActive(false);
    }

    /// <summary>
    /// Показывает или скрывает спрайт снижения защиты над противником
    /// </summary>
    private void UpdateDefenceDownStatusVisual()
    {
        if (defenceDownStatusRenderer == null)
        {
            CreateDefenceDownStatusVisual();
        }

        if (defenceDownStatusRenderer == null)
        {
            return;
        }

        bool shouldShowStatus = activeTemporaryDamageResistanceReductionPercent > 0f
            && IsAlive
            && defenceDownStatusSprite != null;
        defenceDownStatusRenderer.gameObject.SetActive(shouldShowStatus);

        if (!shouldShowStatus)
        {
            return;
        }

        defenceDownStatusRenderer.sprite = defenceDownStatusSprite;
        RefreshDefenceDownStatusVisualTransform();
        RefreshDefenceDownStatusVisualSorting();
    }

    /// <summary>
    /// Расставляет спрайт снижения защиты над противником
    /// </summary>
    private void RefreshDefenceDownStatusVisualTransform()
    {
        if (defenceDownStatusRenderer == null)
        {
            return;
        }

        defenceDownStatusRenderer.transform.localPosition = new Vector3(
            defenceDownStatusOffset.x,
            defenceDownStatusOffset.y,
            0f);
        defenceDownStatusRenderer.transform.localScale = new Vector3(
            defenceDownStatusScale,
            defenceDownStatusScale,
            1f);
    }

    /// <summary>
    /// Поднимает спрайт снижения защиты поверх тела противника
    /// </summary>
    private void RefreshDefenceDownStatusVisualSorting()
    {
        if (defenceDownStatusRenderer == null)
        {
            return;
        }

        int highestSortingOrder = 0;
        int sortingLayerId = defenceDownStatusRenderer.sortingLayerID;

        if (spriteRenderers != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] == null)
                {
                    continue;
                }

                if (i == 0 || spriteRenderers[i].sortingOrder >= highestSortingOrder)
                {
                    highestSortingOrder = spriteRenderers[i].sortingOrder;
                    sortingLayerId = spriteRenderers[i].sortingLayerID;
                }
            }
        }

        defenceDownStatusRenderer.sortingLayerID = sortingLayerId;
        defenceDownStatusRenderer.sortingOrder = highestSortingOrder + defenceDownStatusSortingOrderOffset;
    }

    /// <summary>
    /// Создает дочерний спрайт статуса замедления
    /// </summary>
    private void CreateSpeedDownStatusVisual()
    {
        if (speedDownStatusRenderer != null)
        {
            return;
        }

        GameObject speedDownStatusObject = new GameObject("SpeedDownStatusVisual");
        speedDownStatusObject.transform.SetParent(transform, false);

        speedDownStatusRenderer = speedDownStatusObject.AddComponent<SpriteRenderer>();
        speedDownStatusRenderer.sprite = speedDownStatusSprite;
        RefreshSpeedDownStatusVisualTransform();
        RefreshSpeedDownStatusVisualSorting();
        speedDownStatusObject.SetActive(false);
    }

    /// <summary>
    /// Показывает или скрывает спрайт замедления над противником
    /// </summary>
    private void UpdateSpeedDownStatusVisual()
    {
        if (speedDownStatusRenderer == null)
        {
            CreateSpeedDownStatusVisual();
        }

        if (speedDownStatusRenderer == null)
        {
            return;
        }

        bool shouldShowStatus = isSpeedSlowed && IsAlive && speedDownStatusSprite != null;
        speedDownStatusRenderer.gameObject.SetActive(shouldShowStatus);

        if (!shouldShowStatus)
        {
            return;
        }

        speedDownStatusRenderer.sprite = speedDownStatusSprite;
        RefreshSpeedDownStatusVisualTransform();
        RefreshSpeedDownStatusVisualSorting();
    }

    /// <summary>
    /// Расставляет спрайт замедления над противником
    /// </summary>
    private void RefreshSpeedDownStatusVisualTransform()
    {
        if (speedDownStatusRenderer == null)
        {
            return;
        }

        speedDownStatusRenderer.transform.localPosition = new Vector3(
            speedDownStatusOffset.x,
            speedDownStatusOffset.y,
            0f);
        speedDownStatusRenderer.transform.localScale = new Vector3(
            speedDownStatusScale,
            speedDownStatusScale,
            1f);
    }

    /// <summary>
    /// Поднимает спрайт замедления поверх тела противника
    /// </summary>
    private void RefreshSpeedDownStatusVisualSorting()
    {
        if (speedDownStatusRenderer == null)
        {
            return;
        }

        int highestSortingOrder = 0;
        int sortingLayerId = speedDownStatusRenderer.sortingLayerID;

        if (spriteRenderers != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] == null)
                {
                    continue;
                }

                if (i == 0 || spriteRenderers[i].sortingOrder >= highestSortingOrder)
                {
                    highestSortingOrder = spriteRenderers[i].sortingOrder;
                    sortingLayerId = spriteRenderers[i].sortingLayerID;
                }
            }
        }

        speedDownStatusRenderer.sortingLayerID = sortingLayerId;
        speedDownStatusRenderer.sortingOrder = highestSortingOrder + speedDownStatusSortingOrderOffset;
    }

    /// <summary>
    /// Держит временное снижение сопротивления урону заданное время
    /// </summary>
    private IEnumerator ApplyTemporaryDamageResistanceReductionCoroutine(float reductionPercent, float duration)
    {
        activeTemporaryDamageResistanceReductionPercent = reductionPercent;
        damageResistancePercent -= activeTemporaryDamageResistanceReductionPercent;
        UpdateDefenceDownStatusVisual();

        Debug.Log(
            $"{name}: сопротивление урону снижено на {activeTemporaryDamageResistanceReductionPercent:0.0}% " +
            $"на {duration:0.0} сек. Сейчас: {damageResistancePercent:0.0}%."
        );

        yield return new WaitForSeconds(duration);

        RestoreTemporaryDamageResistanceReduction();
        damageResistanceReductionCoroutine = null;

        Debug.Log($"{name}: временное снижение сопротивления урону закончилось. Сейчас: {damageResistancePercent:0.0}%.");
    }

    /// <summary>
    /// Возвращает сопротивление и скрывает статус после временного снижения
    /// </summary>
    private void RestoreTemporaryDamageResistanceReduction()
    {
        if (activeTemporaryDamageResistanceReductionPercent > 0f)
        {
            damageResistancePercent += activeTemporaryDamageResistanceReductionPercent;
            activeTemporaryDamageResistanceReductionPercent = 0f;
        }

        UpdateDefenceDownStatusVisual();
    }

    /// <summary>
    /// Временно снижает текущую скорость противника
    /// </summary>
    public void ApplySpeedSlow(float speedMultiplier, float duration)
    {
        if (!IsAlive || duration <= 0f)
        {
            return;
        }

        if (speedSlowCoroutine != null)
        {
            StopCoroutine(speedSlowCoroutine);
        }

        speedSlowCoroutine = StartCoroutine(ApplySpeedSlowCoroutine(speedMultiplier, duration));
    }

    /// <summary>
    /// Временно оглушает противника и останавливает его обычное движение
    /// </summary>
    public void ApplyStun(float duration)
    {
        if (!IsAlive || duration <= 0f)
        {
            return;
        }

        if (stunCoroutine != null)
        {
            StopCoroutine(stunCoroutine);
        }

        stunCoroutine = StartCoroutine(ApplyStunCoroutine(duration));
    }

    /// <summary>
    /// Временно двигает противника к указанной точке не давая его обычному движению перебить эффект
    /// </summary>
    public void PullToPosition(Vector2 targetPosition, float pullSpeed, float duration)
    {
        if (!IsAlive || pullSpeed <= 0f || duration <= 0f)
        {
            return;
        }

        StartExternalMovement(PullToPositionCoroutine(targetPosition, pullSpeed, duration));
    }

    /// <summary>
    /// Временно двигает противника в указанном направлении не давая его обычному движению перебить эффект
    /// </summary>
    public void PushInDirection(Vector2 direction, float pushSpeed, float duration)
    {
        if (!IsAlive || pushSpeed <= 0f || duration <= 0f || direction.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        StartExternalMovement(PushInDirectionCoroutine(direction.normalized, pushSpeed, duration));
    }

    /// <summary>
    /// Держит замедление заданное время и возвращает базовую скорость
    /// </summary>
    private IEnumerator ApplySpeedSlowCoroutine(float speedMultiplier, float duration)
    {
        float clampedSpeedMultiplier = Mathf.Clamp01(speedMultiplier);
        currentSpeed = speed * clampedSpeedMultiplier;
        isSpeedSlowed = clampedSpeedMultiplier < 1f;
        UpdateSpeedDownStatusVisual();

        Debug.Log($"{name}: скорость снижена до {currentSpeed:0.0} на {duration:0.0} сек.");

        yield return new WaitForSeconds(duration);

        currentSpeed = speed;
        isSpeedSlowed = false;
        UpdateSpeedDownStatusVisual();
        speedSlowCoroutine = null;

        Debug.Log($"{name}: скорость восстановлена до {currentSpeed:0.0}.");
    }

    /// <summary>
    /// Держит противника в оглушении заданное время
    /// </summary>
    private IEnumerator ApplyStunCoroutine(float duration)
    {
        isStunned = true;
        UpdateStunStatusVisual();

        Debug.Log($"{name}: оглушен на {duration:0.0} сек.");

        yield return new WaitForSeconds(duration);

        isStunned = false;
        UpdateStunStatusVisual();
        stunCoroutine = null;

        Debug.Log($"{name}: оглушение закончилось.");
    }

    /// <summary>
    /// Двигает противника к точке притяжения в течение короткого времени
    /// </summary>
    private IEnumerator PullToPositionCoroutine(Vector2 targetPosition, float pullSpeed, float duration)
    {
        float endTime = Time.time + duration;

        while (Time.time < endTime && IsAlive)
        {
            yield return new WaitForFixedUpdate();

            if (enemyRigidbody == null)
            {
                enemyRigidbody = GetComponent<Rigidbody2D>();
            }

            if (enemyRigidbody == null)
            {
                break;
            }

            Vector2 nextPosition = Vector2.MoveTowards(
                enemyRigidbody.position,
                targetPosition,
                pullSpeed * Time.fixedDeltaTime
            );

            MoveToPositionWithKnockback(nextPosition);
        }

        FinishExternalMovement();
    }

    /// <summary>
    /// Двигает противника в сторону от источника эффекта в течение короткого времени
    /// </summary>
    private IEnumerator PushInDirectionCoroutine(Vector2 direction, float pushSpeed, float duration)
    {
        float endTime = Time.time + duration;

        while (Time.time < endTime && IsAlive)
        {
            yield return new WaitForFixedUpdate();

            if (!TryCacheRigidbody())
            {
                break;
            }

            MoveWithVelocity(direction * pushSpeed);
        }

        FinishExternalMovement();
    }

    /// <summary>
    /// Запускает внешнее перемещение противника
    /// </summary>
    private void StartExternalMovement(IEnumerator movementCoroutine)
    {
        if (externalMovementCoroutine != null)
        {
            StopCoroutine(externalMovementCoroutine);
        }

        IsExternalMovementActive = true;
        externalMovementCoroutine = StartCoroutine(movementCoroutine);
    }

    /// <summary>
    /// Завершает внешнее перемещение противника
    /// </summary>
    private void FinishExternalMovement()
    {
        IsExternalMovementActive = false;
        externalMovementCoroutine = null;
        ResetAiDistanceMovement();
    }

    /// <summary>
    /// Проверяет наличие Rigidbody 2D и пытается найти его при необходимости
    /// </summary>
    private bool TryCacheRigidbody()
    {
        if (enemyRigidbody == null)
        {
            enemyRigidbody = GetComponent<Rigidbody2D>();
        }

        return enemyRigidbody != null;
    }
}
