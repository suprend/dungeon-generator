//DP
using System.Collections;
using UnityEngine;

/// <summary>
/// Персонаж маг со взрывющейся дальней атакой 
/// </summary>
public class PlayerCharacterMage : PlayerCharacterTemplate
{
    [Header("Дальняя атака")]
    [SerializeField] private MageProjectile mageProjectilePrefab;
    [SerializeField, Min(0f)] private float projectileSpawnOffsetDistance = 0.75f;
    [SerializeField, Min(0.01f)] private float projectileSpeed = 8f;
    [SerializeField, Min(0f)] private float projectileImpactRadius = 1f;
    [SerializeField, Min(0f)] private float projectileImpactKnockbackForce = 5f;
    [SerializeField, Min(0.1f)] private float projectileLifeTime = 3f;
    [SerializeField] private LayerMask projectileImpactLayers = ~0;

    [Header("ИИ атака")]
    [SerializeField, Min(0f)] private float aiAttackSearchRadius = 8f;
    [SerializeField, Min(0.01f)] private float aiAttackMinInterval = 1f;
    [SerializeField, Min(0.01f)] private float aiAttackMaxInterval = 2f;
    [SerializeField] private LayerMask aiAttackLayers = ~0;

    [Header("Первая способность")]
    [SerializeField, Min(0.01f)] private float firstAbilityProjectileSpeed = 9f;
    [SerializeField, Min(0.1f)] private float firstAbilityProjectileLifeTime = 5f;
    [SerializeField, Min(0f)] private float firstAbilityBounceRadius = 4f;
    [SerializeField, Min(0)] private int firstAbilityMaxBounces = 5;

    [Header("Вторая способность")]
    [SerializeField, Min(0f)] private float secondAbilityRadius = 2.25f;
    [SerializeField, Min(0f)] private float secondAbilityDelay = 0.35f;
    [SerializeField, Min(0f)] private float secondAbilityDamage = 20f;
    [SerializeField, Min(0f)] private float secondAbilityKnockbackForce = 7f;
    [SerializeField, Min(0f)] private float secondAbilityStunDuration = 2f;
    [SerializeField, Min(0.01f)] private float secondAbilityLifeTime = 0.3f;
    [SerializeField] private LayerMask secondAbilityLayers = ~0;

    [Header("Отображение второй способности")]
    [SerializeField, Min(8)] private int secondAbilityCircleSegments = 56;
    [SerializeField, Min(0.01f)] private float secondAbilityCircleWidth = 0.08f;
    [SerializeField] private Color secondAbilityCircleColor = new Color(0.45f, 0.75f, 1f, 0.9f);

    [Header("Третья способность")]
    [SerializeField, Min(0f)] private float thirdAbilityDamage = 2f;
    [SerializeField, Min(0.01f)] private float thirdAbilityProjectileSpeed = 7f;
    [SerializeField, Min(0.01f)] private float thirdAbilityProjectileRadius = 0.45f;
    [SerializeField, Min(0f)] private float thirdAbilityKnockbackForce = 1.5f;
    [SerializeField, Min(0.02f)] private float thirdAbilityDamageInterval = 0.25f;
    [SerializeField, Min(0.1f)] private float thirdAbilityProjectileLifeTime = 3f;
    [SerializeField] private LayerMask thirdAbilityLayers = ~0;
    [SerializeField] private Color thirdAbilityProjectileColor = new Color(0.7f, 0.25f, 1f, 0.95f);

    private float nextRangedAttackTime;
    private float nextAiAttackTime;
    private Vector2 lastRangedAttackDirection = Vector2.right;

    /// <summary>
    /// Считывает ввод атаки
    /// </summary>
    protected override void ReadCharacterSpecificInput()
    {
        if (Input.GetMouseButton(0))
        {
            TryUseRangedAttack();
        }
    }

    /// <summary>
    /// Настраивает случайный таймер атаки
    /// </summary>
    protected override void OnControlStateChanged(
        PlayerCharacterControlState previousControlState,
        PlayerCharacterControlState newControlState)
    {
        if (newControlState == PlayerCharacterControlState.AiControlled)
        {
            ScheduleNextAiAttack();
            return;
        }

        nextAiAttackTime = 0f;
    }

    /// <summary>
    /// (ИИ) выстрел обычной атакой по ближайшему врагу в зоне досягаемости
    /// </summary>
    protected override void UpdateAiControl()
    {
        if (!IsAlive)
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
            UseRangedAttack(attackDirection);
        }

        ScheduleNextAiAttack();
    }

    /// <summary>
    /// Проверяет можно ли выстрелить
    /// </summary>
    private void TryUseRangedAttack()
    {
        if (!IsAlive || Time.time < nextRangedAttackTime)
        {
            return;
        }

        if (mageProjectilePrefab == null)
        {
            Debug.LogWarning($"{name} не назначен префаб снаряда мага");
            return;
        }

        UseRangedAttack();
        nextRangedAttackTime = Time.time + GetAttackDelayFromAttackSpeed();
    }

    /// <summary>
    /// Стреляет в сторону курсора
    /// </summary>
    private void UseRangedAttack()
    {
        UseRangedAttack(GetDirectionToCursor(ref lastRangedAttackDirection));
    }

    /// <summary>
    /// Создает снаряд обычной атаки и отправляет его в заданном направлении
    /// </summary>
    private void UseRangedAttack(Vector2 attackDirection)
    {
        if (mageProjectilePrefab == null || attackDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        attackDirection = attackDirection.normalized;
        Vector2 spawnPosition = (Vector2)transform.position + attackDirection * projectileSpawnOffsetDistance;
        MageProjectile projectile = Instantiate(mageProjectilePrefab, spawnPosition, Quaternion.identity);

        projectile.Initialize(
            attackDirection,
            Damage,
            projectileSpeed,
            projectileImpactRadius,
            projectileImpactKnockbackForce,
            projectileLifeTime,
            projectileImpactLayers,
            this);

        Debug.Log($"{name} выпущен снаряд с уроном {Damage}");
    }

    /// <summary>
    /// Назначает следующее время атаки ии
    /// </summary>
    private void ScheduleNextAiAttack()
    {
        ScheduleNextRandomActionTime(ref nextAiAttackTime, aiAttackMinInterval, aiAttackMaxInterval);
    }

    /// <summary>
    /// Запускает снаряд отскакивающий между противниками
    /// </summary>
    protected override void UseFirstAbility()
    {
        base.UseFirstAbility();

        if (mageProjectilePrefab == null)
        {
            Debug.LogWarning($"{name} не назначен префаб снаряда мага");
            return;
        }

        Vector2 attackDirection = GetDirectionToCursor(ref lastRangedAttackDirection);
        Vector2 spawnPosition = (Vector2)transform.position + attackDirection * projectileSpawnOffsetDistance;
        MageProjectile projectile = Instantiate(mageProjectilePrefab, spawnPosition, Quaternion.identity);

        projectile.InitializeChainBounce(
            attackDirection,
            Damage,
            firstAbilityProjectileSpeed,
            projectileImpactKnockbackForce,
            firstAbilityProjectileLifeTime,
            projectileImpactLayers,
            this,
            firstAbilityMaxBounces,
            firstAbilityBounceRadius);

        Debug.Log(
            $"{name} первая способность выпустила снаряд" +
            $"Урон {Damage:0.0}, отскоки {firstAbilityMaxBounces}, радиус поиска {firstAbilityBounceRadius:0.0}"
        );
    }

    /// <summary>
    /// В месте курсора после задержки наносит урон по областии оглушает противников
    /// </summary>
    protected override void UseSecondAbility()
    {
        base.UseSecondAbility();

        Vector2 attackPosition = GetCursorWorldPosition();
        StartCoroutine(UseSecondAbilityAfterDelay(attackPosition));

        Debug.Log($"{name} начал подготовку второй способности");
    }

    /// <summary>
    /// Наносит урон по области после задержки второй способности
    /// </summary>
    private IEnumerator UseSecondAbilityAfterDelay(Vector2 attackPosition)
    {
        if (secondAbilityDelay > 0f)
        {
            yield return new WaitForSeconds(secondAbilityDelay);
        }

        GameObject attackCircle = CreateSecondAbilityCircle(attackPosition);
        int affectedEnemiesCount = DamageAndStunEnemiesInSecondAbility(attackPosition);

        Debug.Log(
            $"{name} вторая способность мага нанесла {secondAbilityDamage:0.0} урона " +
            $"Оглушено {affectedEnemiesCount} противников"
        );

        if (secondAbilityLifeTime > 0f)
        {
            yield return new WaitForSeconds(secondAbilityLifeTime);
        }

        Destroy(attackCircle);
    }

    /// <summary>
    /// Наносит урон противникам в области, отбрасывает их и накладывает оглушение
    /// </summary>
    private int DamageAndStunEnemiesInSecondAbility(Vector2 attackPosition)
    {
        return ForEachUniqueEnemyInCircle(
            attackPosition,
            secondAbilityRadius,
            secondAbilityLayers,
            (enemy, hitCollider) =>
            {
                enemy.TakeDamage(secondAbilityDamage);

                Vector2 knockbackDirection = KnockbackUtility.GetDirectionFromPoint(
                    attackPosition,
                    hitCollider,
                    Vector2.right);

                KnockbackUtility.ApplyKnockback(enemy, knockbackDirection, secondAbilityKnockbackForce);
                enemy.ApplyStun(secondAbilityStunDuration);
            });
    }

    /// <summary>
    /// Создает временный круг для визуализации второй способности
    /// </summary>
    private GameObject CreateSecondAbilityCircle(Vector2 attackPosition)
    {
        return CreateCircleVisual(
            "MageSecondAbilityCircle",
            attackPosition,
            secondAbilityRadius,
            secondAbilityCircleSegments,
            secondAbilityCircleWidth,
            secondAbilityCircleColor);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, aiAttackSearchRadius);
    }

    /// <summary>
    /// Выпускает толкающий снаряд в сторону курсора
    /// </summary>
    protected override void UseThirdAbility()
    {
        base.UseThirdAbility();

        if (mageProjectilePrefab == null)
        {
            Debug.LogWarning($"{name}: не назначен префаб снаряда мага");
            return;
        }

        Vector2 attackDirection = GetDirectionToCursor(ref lastRangedAttackDirection);
        Vector2 spawnPosition = (Vector2)transform.position + attackDirection * projectileSpawnOffsetDistance;
        MageProjectile projectile = Instantiate(mageProjectilePrefab, spawnPosition, Quaternion.identity);

        projectile.InitializePiercing(
            attackDirection,
            thirdAbilityDamage,
            thirdAbilityProjectileSpeed,
            thirdAbilityProjectileRadius,
            thirdAbilityKnockbackForce,
            thirdAbilityDamageInterval,
            thirdAbilityProjectileLifeTime,
            thirdAbilityLayers,
            this,
            thirdAbilityProjectileColor);

        Debug.Log(
            $"{name} третья способность выпустила пробивающий круглый снаряд " +
            $"Периодический урон {thirdAbilityDamage:0.0}, интервал {thirdAbilityDamageInterval:0.00} сек"
        );
    }
}
