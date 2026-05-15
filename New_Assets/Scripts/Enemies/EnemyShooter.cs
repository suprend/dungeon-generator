//DP
using UnityEngine;

/// <summary>
/// Противник для дебага
/// </summary>
public class EnemyShooter : EnemyTemplate
{
    [Header("Стрельба")]
    [SerializeField] private EnemyProjectile projectilePrefab;
    [SerializeField] private Vector2 shootDirection = Vector2.right;
    [SerializeField, Min(0.01f)] private float fireInterval = 1f;
    [SerializeField, Min(0.01f)] private float projectileSpeed = 6f;
    [SerializeField, Min(0f)] private float projectileKnockbackForce = 4f;
    [SerializeField, Min(0.1f)] private float projectileLifeTime = 4f;
    [SerializeField, Min(0f)] private float projectileSpawnOffsetDistance = 0.75f;
    [SerializeField] private LayerMask projectileHitLayers = ~0;

    private float nextShotTime;

    private void Start()
    {
        nextShotTime = Time.time;
    }

    private void FixedUpdate()
    {
        if (!IsAlive)
        {
            ResetAiDistanceMovement();
            return;
        }

        MoveWithAiDistancesToTarget(FindClosestAlivePlayerCharacter());
    }

    private void Update()
    {
        if (!IsAlive || IsStunned || projectilePrefab == null || Time.time < nextShotTime)
        {
            return;
        }

        PlayerCharacterTemplate targetCharacter = FindClosestAlivePlayerCharacterInAiAttackZone();

        if (targetCharacter == null)
        {
            return;
        }

        Shoot(targetCharacter);
        nextShotTime = Time.time + fireInterval;
    }

    private void Shoot(PlayerCharacterTemplate targetCharacter)
    {
        Vector2 direction = GetShootDirection(targetCharacter);
        Vector2 spawnPosition = (Vector2)transform.position + direction * projectileSpawnOffsetDistance;
        EnemyProjectile projectile = Instantiate(projectilePrefab, spawnPosition, Quaternion.identity);

        projectile.Initialize(
            direction,
            Damage,
            projectileSpeed,
            projectileKnockbackForce,
            projectileLifeTime,
            projectileHitLayers,
            this);

        Debug.Log($"{name}: противник выстрелил в направлении {direction}.");
    }

    private Vector2 GetShootDirection(PlayerCharacterTemplate targetCharacter)
    {
        if (targetCharacter != null)
        {
            Vector2 directionToTarget = (Vector2)targetCharacter.transform.position - (Vector2)transform.position;

            if (directionToTarget.sqrMagnitude > 0.0001f)
            {
                return directionToTarget.normalized;
            }
        }

        if (shootDirection.sqrMagnitude <= 0.0001f)
        {
            return Vector2.right;
        }

        return shootDirection.normalized;
    }

    private void OnDrawGizmosSelected()
    {
        Vector2 direction = shootDirection.sqrMagnitude > 0.0001f ? shootDirection.normalized : Vector2.right;
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, (Vector2)transform.position + direction * 1.5f);
        DrawAiMovementGizmos();
        DrawAiAttackGizmos();
    }
}
