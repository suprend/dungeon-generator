//DP
using System.Collections;
using UnityEngine;

/// <summary>
/// Персонаж следопыт с дальней заряжаемой атакой
/// </summary>
public class PlayerCharacterRanger : PlayerCharacterTemplate
{
    [Header("Заряжаемая дальняя атака")]
    [SerializeField] private RangerProjectile rangerProjectilePrefab;
    [SerializeField, Min(0.01f)] private float maxChargeValue = 1f;
    [SerializeField, Min(0f)] private float minChargeValue = 0.25f;
    [SerializeField, Min(0f)] private float projectileSpawnOffsetDistance = 0.75f;
    [SerializeField, Min(0.01f)] private float minProjectileSpeed = 5f;
    [SerializeField, Min(0.01f)] private float maxProjectileSpeed = 12f;
    [SerializeField, Min(0f)] private float maxProjectileKnockbackForce = 6f;
    [SerializeField, Min(0.1f)] private float projectileLifeTime = 3f;
    [SerializeField] private LayerMask projectileHitLayers = ~0;
    [SerializeField, Min(0f)] private float maxChargeFlashDuration = 0.08f;
    [SerializeField] private Color maxChargeFlashColor = Color.white;

    [Header("ИИ атака")]
    [SerializeField, Min(0f)] private float aiAttackSearchRadius = 8f;
    [SerializeField, Min(0.01f)] private float aiAttackMinInterval = 1f;
    [SerializeField, Min(0.01f)] private float aiAttackMaxInterval = 2f;
    [SerializeField] private LayerMask aiAttackLayers = ~0;

    [Header("Первая способность")]
    [SerializeField, Min(0f)] private float firstAbilityDamage = 10f;
    [SerializeField, Min(0.01f)] private float firstAbilityProjectileSpeed = 12f;
    [SerializeField, Min(0.1f)] private float firstAbilityProjectileLifeTime = 3f;
    [SerializeField, Min(0f)] private float firstAbilityDamageResistanceReductionPercent = 50f;
    [SerializeField, Min(0f)] private float firstAbilityDamageResistanceReductionDuration = 5f;

    [Header("Вторая способность")]
    [SerializeField, Min(0f)] private float secondAbilityDamage = 8f;
    [SerializeField, Min(0.01f)] private float secondAbilityProjectileSpeed = 10f;
    [SerializeField, Min(0f)] private float secondAbilityKnockbackForce = 6f;
    [SerializeField, Min(0.1f)] private float secondAbilityProjectileLifeTime = 3f;
    [SerializeField, Min(0f)] private float secondAbilitySpreadAngle = 45f;

    [Header("Третья способность")]
    [SerializeField, Min(0f)] private float thirdAbilityDashDistance = 4f;
    [SerializeField, Min(0.01f)] private float thirdAbilityDashDuration = 0.25f;
    [SerializeField, Min(1f)] private float thirdAbilitySpeedBonusMultiplier = 1.35f;
    [SerializeField, Min(0f)] private float thirdAbilitySpeedBonusDuration = 2f;

    private const int SecondAbilityProjectileCount = 5;

    private float currentChargeValue;
    private bool isChargingAttack;
    private bool hasFlashedOnMaxCharge;
    private bool isDashing;
    private float nextAiAttackTime;
    private Vector2 lastRangedAttackDirection = Vector2.right;

    protected override void FixedUpdate()
    {
        if (isDashing)
        {
            return;
        }

        base.FixedUpdate();
    }

    /// <summary>
    /// Сбрасывает заряд при передаче следопыта под контроль ИИ
    /// </summary>
    protected override void OnControlStateChanged(
        PlayerCharacterControlState previousControlState,
        PlayerCharacterControlState newControlState)
    {
        if (newControlState == PlayerCharacterControlState.AiControlled)
        {
            isChargingAttack = false;
            currentChargeValue = 0f;
            hasFlashedOnMaxCharge = false;
            ScheduleNextAiAttack();
            return;
        }

        nextAiAttackTime = 0f;
    }

    /// <summary>
    /// (ИИ) атака ближайшего врага в зоне досягаемости со случайным интервалом
    /// </summary>
    protected override void UpdateAiControl()
    {
        if (isDashing || !IsAlive)
        {
            return;
        }

        if (nextAiAttackTime <= 0f)
        {
            ScheduleNextAiAttack();
        }

        if (Time.time < nextAiAttackTime)
        {
            return;
        }

        EnemyTemplate target = FindClosestAliveEnemy(aiAttackSearchRadius, aiAttackLayers);

        if (target != null)
        {
            Vector2 attackDirection = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
            UseFullyChargedAiAttack(attackDirection);
        }

        ScheduleNextAiAttack();
    }

    /// <summary>
    /// Считывает ввод заряжаемой атаки следопыта
    /// </summary>
    protected override void ReadCharacterSpecificInput()
    {
        if (isDashing)
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            StartChargingAttack();
        }

        if (Input.GetMouseButton(0) && isChargingAttack)
        {
            ChargeAttack();
        }

        if (Input.GetMouseButtonUp(0) && isChargingAttack)
        {
            ReleaseChargedAttack();
        }
    }

    /// <summary>
    /// Начинает накопление заряда атаки
    /// </summary>
    private void StartChargingAttack()
    {
        if (!IsAlive)
        {
            return;
        }

        isChargingAttack = true;
        currentChargeValue = 0f;
        hasFlashedOnMaxCharge = false;
    }

    /// <summary>
    /// Увеличивает заряд атаки с учетом скорости атаки
    /// </summary>
    private void ChargeAttack()
    {
        if (!IsAlive)
        {
            isChargingAttack = false;
            currentChargeValue = 0f;
            hasFlashedOnMaxCharge = false;
            return;
        }

        float previousChargeValue = currentChargeValue;
        currentChargeValue = Mathf.Min(currentChargeValue + AttackSpeed * Time.deltaTime, maxChargeValue);

        if (!hasFlashedOnMaxCharge && previousChargeValue < maxChargeValue && currentChargeValue >= maxChargeValue)
        {
            hasFlashedOnMaxCharge = true;
            StartCharacterFlash(maxChargeFlashColor, maxChargeFlashDuration);
        }
    }

    /// <summary>
    /// Выпускает снаряд с уроном и скоростью зависящими от накопленного заряда
    /// </summary>
    private void ReleaseChargedAttack()
    {
        isChargingAttack = false;

        if (!IsAlive)
        {
            currentChargeValue = 0f;
            hasFlashedOnMaxCharge = false;
            return;
        }

        float requiredChargeValue = Mathf.Min(minChargeValue, maxChargeValue);

        if (currentChargeValue < requiredChargeValue)
        {
            Debug.Log($"{name} заряд слишком маленький, снаряд не выпущен");
            currentChargeValue = 0f;
            hasFlashedOnMaxCharge = false;
            return;
        }

        if (rangerProjectilePrefab == null)
        {
            Debug.LogWarning($"{name} не назначен префаб снаряда следопыта");
            currentChargeValue = 0f;
            hasFlashedOnMaxCharge = false;
            return;
        }

        float chargeProgress = Mathf.Clamp01(currentChargeValue / maxChargeValue);
        Vector2 attackDirection = GetDirectionToCursor(ref lastRangedAttackDirection);
        Vector2 spawnPosition = (Vector2)transform.position + attackDirection * projectileSpawnOffsetDistance;
        RangerProjectile projectile = Instantiate(rangerProjectilePrefab, spawnPosition, Quaternion.identity);

        projectile.Initialize(
            attackDirection,
            Damage * chargeProgress,
            Mathf.Lerp(minProjectileSpeed, maxProjectileSpeed, chargeProgress),
            maxProjectileKnockbackForce * chargeProgress,
            projectileLifeTime,
            projectileHitLayers,
            this);

        Debug.Log($"{name} выпущен снаряд. Заряд {chargeProgress:P0}, урон {Damage * chargeProgress:0.0}");
        currentChargeValue = 0f;
        hasFlashedOnMaxCharge = false;
    }

    /// <summary>
    /// (ИИ) Выпускает со свойствами полностью заряженной обычной атаки
    /// </summary>
    private void UseFullyChargedAiAttack(Vector2 attackDirection)
    {
        if (rangerProjectilePrefab == null || attackDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        attackDirection = attackDirection.normalized;
        Vector2 spawnPosition = (Vector2)transform.position + attackDirection * projectileSpawnOffsetDistance;
        RangerProjectile projectile = Instantiate(rangerProjectilePrefab, spawnPosition, Quaternion.identity);

        projectile.Initialize(
            attackDirection,
            Damage,
            maxProjectileSpeed,
            maxProjectileKnockbackForce,
            projectileLifeTime,
            projectileHitLayers,
            this);

        Debug.Log($"{name}: ии следопыта выпустил полностью заряженный снаряд с уроном {Damage:0.0}");
    }

    /// <summary>
    /// Назначает следующее время атаки ии
    /// </summary>
    private void ScheduleNextAiAttack()
    {
        ScheduleNextRandomActionTime(ref nextAiAttackTime, aiAttackMinInterval, aiAttackMaxInterval);
    }

    /// <summary>
    /// Выпускает снаряд снижающий сопротивление урону цели
    /// </summary>
    protected override void UseFirstAbility()
    {
        base.UseFirstAbility();

        if (rangerProjectilePrefab == null)
        {
            Debug.LogWarning($"{name}: не назначен префаб снаряда следопыта");
            return;
        }

        Vector2 attackDirection = GetDirectionToCursor(ref lastRangedAttackDirection);
        Vector2 spawnPosition = (Vector2)transform.position + attackDirection * projectileSpawnOffsetDistance;
        RangerProjectile projectile = Instantiate(rangerProjectilePrefab, spawnPosition, Quaternion.identity);

        projectile.Initialize(
            attackDirection,
            firstAbilityDamage,
            firstAbilityProjectileSpeed,
            maxProjectileKnockbackForce,
            firstAbilityProjectileLifeTime,
            projectileHitLayers,
            this,
            firstAbilityDamageResistanceReductionPercent,
            firstAbilityDamageResistanceReductionDuration);

        Debug.Log(
            $"{name} первая способность выпустила снаряд" +
            $"Урон {firstAbilityDamage:0.0}, скорость: {firstAbilityProjectileSpeed:0.0}, " +
            $"снижение сопротивления {firstAbilityDamageResistanceReductionPercent:0.0}% " +
            $"на {firstAbilityDamageResistanceReductionDuration:0.0} сек"
        );
    }

    /// <summary>
    /// Выпускает несколько снарядов веером в сторону курсора
    /// </summary>
    protected override void UseSecondAbility()
    {
        base.UseSecondAbility();

        if (rangerProjectilePrefab == null)
        {
            Debug.LogWarning($"{name}: не назначен префаб снаряда следопыта");
            return;
        }

        Vector2 centerDirection = GetDirectionToCursor(ref lastRangedAttackDirection);
        float angleStep = GetSecondAbilityAngleStep();
        float startAngle = -secondAbilitySpreadAngle * 0.5f;

        for (int i = 0; i < SecondAbilityProjectileCount; i++)
        {
            float projectileAngle = startAngle + angleStep * i;
            Vector2 projectileDirection = RotateDirection(centerDirection, projectileAngle);
            SpawnSecondAbilityProjectile(projectileDirection);
        }

        Debug.Log(
            $"{name}: вторая способность выпустила снаряды" +
            $"Урон {secondAbilityDamage:0.0}, скорость {secondAbilityProjectileSpeed:0.0}, " +
            $"отбрасывание {secondAbilityKnockbackForce:0.0}, " +
            $"разброс {secondAbilitySpreadAngle:0.0} град"
        );
    }

    /// <summary>
    /// Возвращает шаг угла между снарядами второй способности
    /// </summary>
    private float GetSecondAbilityAngleStep()
    {
        return secondAbilitySpreadAngle / (SecondAbilityProjectileCount - 1);
    }

    /// <summary>
    /// Создает один снаряд второй способности
    /// </summary>
    private void SpawnSecondAbilityProjectile(Vector2 projectileDirection)
    {
        Vector2 spawnPosition = (Vector2)transform.position + projectileDirection * projectileSpawnOffsetDistance;
        RangerProjectile projectile = Instantiate(rangerProjectilePrefab, spawnPosition, Quaternion.identity);

        projectile.Initialize(
            projectileDirection,
            secondAbilityDamage,
            secondAbilityProjectileSpeed,
            secondAbilityKnockbackForce,
            secondAbilityProjectileLifeTime,
            projectileHitLayers,
            this);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, aiAttackSearchRadius);

        DrawAiMovementGizmos();
    }

    /// <summary>
    /// Совершает рывок вперед и дает короткий бонус скорости
    /// </summary>
    protected override void UseThirdAbility()
    {
        base.UseThirdAbility();
        TryStartDashAbility();
    }

    /// <summary>
    /// Проверяет возможность начать рывок
    /// </summary>
    private void TryStartDashAbility()
    {
        if (isDashing)
        {
            return;
        }

        isChargingAttack = false;
        currentChargeValue = 0f;

        Vector2 dashDirection = GetMovementDashDirection();
        StartCoroutine(UseThirdAbilityDash(dashDirection));
    }

    /// <summary>
    /// Выполняет рывок, временно игнорирует урон и включает прохождение сквозь противников
    /// </summary>
    private IEnumerator UseThirdAbilityDash(Vector2 dashDirection)
    {
        isDashing = true;
        SetIncomingAttacksIgnored(true);
        SaveCharacterColliderTriggerStates();
        SetCharacterCollidersTriggerState(true);

        float dashSpeed = thirdAbilityDashDistance / Mathf.Max(thirdAbilityDashDuration, 0.01f);
        float endTime = Time.time + thirdAbilityDashDuration;
        Vector2 dashEndPosition = CharacterRigidbody.position + dashDirection * thirdAbilityDashDistance;

        while (Time.time < endTime && IsAlive)
        {
            yield return new WaitForFixedUpdate();

            if (CharacterRigidbody == null)
            {
                break;
            }

            Vector2 nextPosition = Vector2.MoveTowards(
                CharacterRigidbody.position,
                dashEndPosition,
                dashSpeed * Time.fixedDeltaTime
            );

            CharacterRigidbody.MovePosition(nextPosition);
        }

        SetCharacterCollidersTriggerState(false);
        RestoreCharacterColliderTriggerStates();
        SetIncomingAttacksIgnored(false);
        isDashing = false;
        ApplyMovementSpeedMultiplier(thirdAbilitySpeedBonusMultiplier, thirdAbilitySpeedBonusDuration);

        Debug.Log(
            $"{name} третья способность выполнила рывок " +
            $"Бонус скорости x{thirdAbilitySpeedBonusMultiplier:0.00} на {thirdAbilitySpeedBonusDuration:0.0} сек"
        );
    }

}
