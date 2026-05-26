//DP
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Персонаж воин
/// </summary>
public class PlayerCharacterWarrior : PlayerCharacterTemplate
{
    [Header("Ближняя атака")]
    [SerializeField, Min(0f)] private float meleeAttackRange = 1f;
    [SerializeField, Min(0f)] private float meleeAttackOffsetDistance = 0.75f;
    [SerializeField, Min(0f)] private float meleeKnockbackForce = 4f;
    [SerializeField] private LayerMask meleeAttackLayers = ~0;

    [Header("ИИ атака")]
    [SerializeField, Min(0f)] private float aiAttackSearchRadius = 2f;
    [SerializeField, Min(0.01f)] private float aiAttackMinInterval = 0.8f;
    [SerializeField, Min(0.01f)] private float aiAttackMaxInterval = 1.6f;
    [SerializeField] private LayerMask aiAttackLayers = ~0;

    [Header("Отображение ближней атаки")]
    [SerializeField, Min(0.01f)] private float meleeAttackVisualLifeTime = 0.15f;
    [SerializeField, Min(8)] private int meleeAttackCircleSegments = 40;
    [SerializeField, Min(0.01f)] private float meleeAttackCircleWidth = 0.07f;
    [SerializeField] private Color meleeAttackCircleColor = new Color(1f, 0.2f, 0.12f, 0.9f);

    [Header("Отображение первой способности")]
    [SerializeField, Min(0.01f)] private float firstAbilityVisualLifeTime = 0.25f;
    [SerializeField, Min(8)] private int firstAbilityCircleSegments = 56;
    [SerializeField, Min(0.01f)] private float firstAbilityCircleWidth = 0.08f;
    [SerializeField] private Color firstAbilityCircleColor = new Color(0.1f, 0.75f, 1f, 0.9f);

    [Header("Отображение второй способности")]
    [SerializeField, Min(0.01f)] private float secondAbilityVisualLifeTime = 0.22f;
    [SerializeField, Min(8)] private int secondAbilityCircleSegments = 48;
    [SerializeField, Min(0.01f)] private float secondAbilityCircleWidth = 0.1f;
    [SerializeField] private Color secondAbilityCircleColor = new Color(1f, 0.85f, 0.15f, 0.95f);

    [Header("Первая способность")]
    [SerializeField, Min(0f)] private float firstAbilityRadius = 3f;
    [SerializeField, Min(0f)] private float firstAbilityPullSpeed = 8f;
    [SerializeField, Min(0f)] private float firstAbilityPullDuration = 0.25f;
    [SerializeField, Range(0f, 1f)] private float firstAbilitySlowMultiplier = 0.5f;
    [SerializeField, Min(0f)] private float firstAbilitySlowDuration = 3f;
    [SerializeField] private LayerMask firstAbilityLayers = ~0;

    [Header("Вторая способность")]
    [SerializeField, Min(0f)] private float secondAbilityRadius = 2f;
    [SerializeField, Min(0f)] private float secondAbilityPushSpeed = 6f;
    [SerializeField, Min(0f)] private float secondAbilityPushDuration = 0.2f;
    [SerializeField, Min(0f)] private float secondAbilityDamageResistanceDuration = 3f;
    [SerializeField] private LayerMask secondAbilityLayers = ~0;

    [Header("Третья способность")]
    [SerializeField, Min(0f)] private float thirdAbilityDashDistance = 4f;
    [SerializeField, Min(0.01f)] private float thirdAbilityDashDuration = 0.25f;
    [SerializeField, Min(0f)] private float thirdAbilityHitRadius = 0.65f;
    [SerializeField, Min(0f)] private float thirdAbilitySidePushSpeed = 5f;
    [SerializeField, Min(0f)] private float thirdAbilitySidePushDuration = 0.15f;
    [SerializeField] private LayerMask thirdAbilityLayers = ~0;

    private bool isDashing;
    private float nextMeleeAttackTime;
    private float nextAiAttackTime;
    private Vector2 lastMeleeAttackDirection = Vector2.right;

    protected override void FixedUpdate()
    {
        if (isDashing)
        {
            return;
        }

        base.FixedUpdate();
    }

    /// <summary>
    /// Считывает ввод ближней атаки
    /// </summary>
    protected override void ReadCharacterSpecificInput()
    {
        if (isDashing)
        {
            return;
        }

        if (Input.GetMouseButton(0))
        {
            TryUseMeleeAttack();
        }
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

        Component target = FindClosestAliveDamageableComponent(aiAttackSearchRadius, aiAttackLayers);

        if (target != null)
        {
            Vector2 attackDirection = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
            UseMeleeAttack(attackDirection);
        }

        ScheduleNextAiAttack();
    }

    /// <summary>
    /// Настраивает случайный таймер атаки при передаче персонажа под контроль ии
    /// </summary>
    protected override void OnControlStateChanged(
        PlayerCharacterControlState previousControlState,
        PlayerCharacterControlState newControlState)
    {
        if (newControlState == PlayerCharacterControlState.AiControlled)
        {
            ScheduleNextAiAttack();
        }
    }

    /// <summary>
    /// Проверяет может ли персонаж атаковать
    /// </summary>
    private void TryUseMeleeAttack()
    {
        if (!IsAlive || Time.time < nextMeleeAttackTime)
        {
            return;
        }

        UseMeleeAttack();
        nextMeleeAttackTime = Time.time + GetAttackDelayFromAttackSpeed();
    }

    /// <summary>
    /// Совершает ближнюю атаку на лкм
    /// </summary>
    private void UseMeleeAttack()
    {
        UseMeleeAttack(GetDirectionToCursor(ref lastMeleeAttackDirection));
    }

    /// <summary>
    /// Наносит урон всем подходящим целям в заданном направлении
    /// </summary>
    private void UseMeleeAttack(Vector2 attackDirection)
    {
        if (attackDirection.sqrMagnitude <= 0.0001f)
        {
            attackDirection = lastMeleeAttackDirection;
        }

        attackDirection = attackDirection.normalized;
        Vector2 attackCenter = GetMeleeAttackCenter(attackDirection);

        CreateMeleeAttackCircle(attackCenter);

        int damagedTargetsCount = ForEachUniqueDamageableInCircle(
            attackCenter,
            meleeAttackRange,
            meleeAttackLayers,
            (target, hitCollider) =>
            {
                target.TakeDamage(Damage);
                KnockbackUtility.ApplyKnockback(target, attackDirection, meleeKnockbackForce);
            });

        Debug.Log($"{name} ближняя атака нанесла {Damage} урона. Целей задето: {damagedTargetsCount}");
    }

    /// <summary>
    /// Назначает следующее время атаки ии
    /// </summary>
    private void ScheduleNextAiAttack()
    {
        ScheduleNextRandomActionTime(ref nextAiAttackTime, aiAttackMinInterval, aiAttackMaxInterval);
    }

    /// <summary>
    /// Создает круг который показывает область ближней атаки
    /// </summary>
    private void CreateMeleeAttackCircle(Vector2 attackCenter)
    {
        GameObject attackCircle = CreateCircleVisual(
            "WarriorMeleeAttackCircle",
            attackCenter,
            meleeAttackRange,
            meleeAttackCircleSegments,
            meleeAttackCircleWidth,
            meleeAttackCircleColor);

        Destroy(attackCircle, meleeAttackVisualLifeTime);
    }

    /// <summary>
    /// Возвращает центр атаки
    /// </summary>
    private Vector2 GetMeleeAttackCenter(Vector2 attackDirection)
    {
        return (Vector2)transform.position + attackDirection * meleeAttackOffsetDistance;
    }

    /// <summary>
    /// Показывает зону ближней атаки в редакторе
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Vector2 attackDirection = lastMeleeAttackDirection.sqrMagnitude > 0f ? lastMeleeAttackDirection.normalized : Vector2.right;
        Vector2 attackCenter = (Vector2)transform.position + attackDirection * meleeAttackOffsetDistance;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackCenter, meleeAttackRange);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, firstAbilityRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, secondAbilityRadius);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, aiAttackSearchRadius);

        DrawAiMovementGizmos();
    }

    /// <summary>
    /// Притягивает ближайших противников и временно снижает их скорость
    /// </summary>
    protected override void UseFirstAbility()
    {
        base.UseFirstAbility();
        CreateFirstAbilityCircle();

        int affectedEnemiesCount = ForEachUniqueEnemyInCircle(
            transform.position,
            firstAbilityRadius,
            firstAbilityLayers,
            (enemy, hitCollider) =>
            {
                enemy.PullToPosition(transform.position, firstAbilityPullSpeed, firstAbilityPullDuration);
                enemy.ApplySpeedSlow(firstAbilitySlowMultiplier, firstAbilitySlowDuration);
            });

        Debug.Log($"{name} первая способность притянула и замедлила {affectedEnemiesCount} противников");
    }

    /// <summary>
    /// Наносит урон вокруг воина, расталкивает противников и дает сопротивление урону за каждого задетого врага по формуле
    /// </summary>
    protected override void UseSecondAbility()
    {
        base.UseSecondAbility();
        CreateSecondAbilityCircle();

        int affectedEnemiesCount = ForEachUniqueEnemyInCircle(
            transform.position,
            secondAbilityRadius,
            secondAbilityLayers,
            (enemy, hitCollider) =>
            {
                Vector2 pushDirection = GetDirectionFromSelfTo(enemy.transform.position, LastMovementInputDirection);
                enemy.TakeDamage(Damage);
                enemy.PushInDirection(pushDirection, secondAbilityPushSpeed, secondAbilityPushDuration);
            });

        if (affectedEnemiesCount > 0)
        {
            float resistancePercent = 20f + Mathf.Sqrt(affectedEnemiesCount);
            ApplyDamageResistance(resistancePercent, secondAbilityDamageResistanceDuration);
        }

        Debug.Log(
            $"{name} вторая способность воина задела {affectedEnemiesCount} противников" +
            $"Сопротивление урону {(affectedEnemiesCount > 0 ? 20f + Mathf.Sqrt(affectedEnemiesCount) : 0f):0.0}%"
        );
    }

    /// <summary>
    /// Создает круг который показывает радиус притягивания первой способности
    /// </summary>
    private void CreateFirstAbilityCircle()
    {
        GameObject abilityCircle = CreateCircleVisual(
            "WarriorFirstAbilityCircle",
            transform.position,
            firstAbilityRadius,
            firstAbilityCircleSegments,
            firstAbilityCircleWidth,
            firstAbilityCircleColor);

        Destroy(abilityCircle, firstAbilityVisualLifeTime);
    }

    /// <summary>
    /// Создает круг который показывает радиус ударной волны второй способности
    /// </summary>
    private void CreateSecondAbilityCircle()
    {
        GameObject abilityCircle = CreateCircleVisual(
            "WarriorSecondAbilityCircle",
            transform.position,
            secondAbilityRadius,
            secondAbilityCircleSegments,
            secondAbilityCircleWidth,
            secondAbilityCircleColor);

        Destroy(abilityCircle, secondAbilityVisualLifeTime);
    }

    /// <summary>
    /// Совершает рывок вперед игнорируя урон и расталкивая противников на пути
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

        Vector2 dashDirection = GetMovementDashDirection();
        StartCoroutine(UseThirdAbilityDash(dashDirection));
    }

    /// <summary>
    /// Выполняет движение рывка и обрабатывает противников на пути
    /// </summary>
    private IEnumerator UseThirdAbilityDash(Vector2 dashDirection)
    {
        isDashing = true;
        SetIncomingAttacksIgnored(true);
        SaveCharacterColliderTriggerStates();
        SetCharacterCollidersTriggerState(true);

        HashSet<EnemyTemplate> damagedEnemies = new HashSet<EnemyTemplate>();
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

            Vector2 desiredNextPosition = Vector2.MoveTowards(
                CharacterRigidbody.position,
                dashEndPosition,
                dashSpeed * Time.fixedDeltaTime
            );

            if (!TryGetSafeDashStep(CharacterRigidbody.position, desiredNextPosition, out Vector2 nextPosition))
            {
                break;
            }

            CharacterRigidbody.MovePosition(nextPosition);
            DamageEnemiesDuringDash(nextPosition, dashDirection, damagedEnemies);
        }

        SetCharacterCollidersTriggerState(false);
        RestoreCharacterColliderTriggerStates();
        SetIncomingAttacksIgnored(false);
        isDashing = false;

        Debug.Log($"{name} рывок воина задел {damagedEnemies.Count} противников");
    }

    private bool TryGetSafeDashStep(Vector2 currentPosition, Vector2 desiredNextPosition, out Vector2 safeNextPosition)
    {
        return TryGetStaticRigidbodySafeMovementStep(
            currentPosition,
            desiredNextPosition,
            out safeNextPosition);
    }

    /// <summary>
    /// Наносит урон противникам во время рывка
    /// </summary>
    private void DamageEnemiesDuringDash(
        Vector2 dashPosition,
        Vector2 dashDirection,
        HashSet<EnemyTemplate> damagedEnemies)
    {
        ForEachUniqueEnemyInCircle(
            dashPosition,
            thirdAbilityHitRadius,
            thirdAbilityLayers,
            (enemy, hitCollider) =>
            {
                if (!damagedEnemies.Add(enemy))
                {
                    return;
                }

                Vector2 sidePushDirection = GetSidePushDirection(dashDirection, enemy.transform.position);

                enemy.TakeDamage(Damage);
                enemy.PushInDirection(sidePushDirection, thirdAbilitySidePushSpeed, thirdAbilitySidePushDuration);
            });
    }

    /// <summary>
    /// Возвращает боковое направление отталкивания относительно направления рывка
    /// </summary>
    private Vector2 GetSidePushDirection(Vector2 dashDirection, Vector2 enemyPosition)
    {
        Vector2 sideDirection = new Vector2(-dashDirection.y, dashDirection.x);
        Vector2 directionToEnemy = enemyPosition - (Vector2)transform.position;
        float sideSign = Mathf.Sign(Vector2.Dot(directionToEnemy, sideDirection));

        if (Mathf.Approximately(sideSign, 0f))
        {
            sideSign = 1f;
        }

        return sideDirection * sideSign;
    }

}
