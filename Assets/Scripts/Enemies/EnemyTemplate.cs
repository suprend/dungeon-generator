//DP
using System.Collections;
using UnityEngine;

/// <summary>
/// Шаблон противника
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class EnemyTemplate : MonoBehaviour, IDamageable
{
    [Header("Статы противника")]
    [SerializeField, Min(1f)] private float maxHealth = 50f;
    [SerializeField, Min(0f)] private float currentHealth = 50f;
    [SerializeField, Min(0f)] private float damage = 5f;
    [SerializeField, Min(0f)] private float speed = 2f;
    [SerializeField] private float damageResistancePercent;

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

    private Rigidbody2D enemyRigidbody;
    private SpriteRenderer[] spriteRenderers;
    private SpriteRenderer stunStatusRenderer;
    private SpriteRenderer defenceDownStatusRenderer;
    private SpriteRenderer speedDownStatusRenderer;
    private Coroutine speedSlowCoroutine;
    private Coroutine externalMovementCoroutine;
    private Coroutine stunCoroutine;
    private Coroutine damageResistanceReductionCoroutine;
    private float currentSpeed;
    private float activeTemporaryDamageResistanceReductionPercent;
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

    /// <summary>
    /// Получение урона
    /// </summary>
    public void TakeDamage(float damageAmount)
    {
        if (damageAmount <= 0f || !IsAlive)
        {
            return;
        }

        float finalDamageAmount = GetDamageAfterResistance(damageAmount);
        currentHealth = Mathf.Max(currentHealth - finalDamageAmount, 0f);

        if (!IsAlive)
        {
            isStunned = false;
            isSpeedSlowed = false;
            UpdateStunStatusVisual();
            UpdateDefenceDownStatusVisual();
            UpdateSpeedDownStatusVisual();
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
        int bodyRendererIndex = 0;

        for (int i = 0; i < allSpriteRenderers.Length; i++)
        {
            if (!IsEnemyBodySpriteRenderer(allSpriteRenderers[i]))
            {
                continue;
            }

            spriteRenderers[bodyRendererIndex] = allSpriteRenderers[i];
            bodyRendererIndex++;
        }
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

            enemyRigidbody.MovePosition(nextPosition);
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

            Vector2 nextPosition = enemyRigidbody.position + direction * pushSpeed * Time.fixedDeltaTime;
            enemyRigidbody.MovePosition(nextPosition);
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
