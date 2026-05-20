//DP
using UnityEngine;

/// <summary>
/// Общие методы для отбрасывания целей атаками
/// </summary>
public static class KnockbackUtility
{
    private const float TransformFallbackDistanceMultiplier = 0.1f;
    private const float MinDirectionSqrMagnitude = 0.0001f;

    /// <summary>
    /// Отбрасывает цель в указанном направлении
    /// </summary>
    public static void ApplyKnockback(IDamageable target, Vector2 direction, float force)
    {
        if (target == null || force <= 0f)
        {
            return;
        }

        Component targetComponent = target as Component;

        if (targetComponent == null)
        {
            return;
        }

        Vector2 knockbackDirection = GetNormalizedDirection(direction, Vector2.right);

        if (target is EnemyTemplate enemy)
        {
            enemy.ApplyKnockback(knockbackDirection, force);
            return;
        }

        if (target is PlayerCharacterTemplate playerCharacter)
        {
            playerCharacter.ApplyKnockback(knockbackDirection, force);
            return;
        }

        Rigidbody2D targetRigidbody = targetComponent.GetComponentInParent<Rigidbody2D>();

        if (targetRigidbody != null && targetRigidbody.bodyType == RigidbodyType2D.Static)
        {
            return;
        }

        if (targetRigidbody != null && targetRigidbody.simulated)
        {
            targetRigidbody.AddForce(knockbackDirection * force, ForceMode2D.Impulse);
            return;
        }

        // Запасной вариант для целей без Rigidbody2D
        targetComponent.transform.position += (Vector3)(knockbackDirection * force * TransformFallbackDistanceMultiplier);
    }

    /// <summary>
    /// Возвращает направление от центра атаки к коллайдеру цели
    /// </summary>
    public static Vector2 GetDirectionFromPoint(Vector2 center, Collider2D targetCollider, Vector2 fallbackDirection)
    {
        if (targetCollider == null)
        {
            return GetNormalizedDirection(fallbackDirection, Vector2.right);
        }

        Vector2 direction = (Vector2)targetCollider.bounds.center - center;
        return GetNormalizedDirection(direction, fallbackDirection);
    }

    /// <summary>
    /// Нормализует направление или возвращает запасное
    /// </summary>
    private static Vector2 GetNormalizedDirection(Vector2 direction, Vector2 fallbackDirection)
    {
        if (direction.sqrMagnitude > MinDirectionSqrMagnitude)
        {
            return direction.normalized;
        }

        if (fallbackDirection.sqrMagnitude > MinDirectionSqrMagnitude)
        {
            return fallbackDirection.normalized;
        }

        return Vector2.right;
    }
}
