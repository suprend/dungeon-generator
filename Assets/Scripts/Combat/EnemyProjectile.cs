//DP
using UnityEngine;

/// <summary>
/// Снаряд противника, который летит в заданном направлении и наносит урон персонажам игрока.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class EnemyProjectile : MonoBehaviour
{
    [Header("Параметры снаряда")]
    [SerializeField, Min(0f)] private float damage = 5f;
    [SerializeField, Min(0.01f)] private float speed = 6f;
    [SerializeField, Min(0f)] private float knockbackForce = 4f;
    [SerializeField, Min(0.1f)] private float lifeTime = 4f;
    [SerializeField] private LayerMask hitLayers = ~0;

    private Rigidbody2D projectileRigidbody;
    private EnemyTemplate owner;
    private Vector2 moveDirection = Vector2.right;
    private float destroyTime;
    private bool hasHit;

    private void Awake()
    {
        projectileRigidbody = GetComponent<Rigidbody2D>();
        Rigidbody2DSmoothingUtility.EnableInterpolation(projectileRigidbody);

        // Снаряд двигается только в плоскости 2D, поэтому гравитация и вращение ему не нужны.
        projectileRigidbody.gravityScale = 0f;
        projectileRigidbody.freezeRotation = true;
    }

    private void OnEnable()
    {
        destroyTime = Time.time + lifeTime;
    }

    private void Update()
    {
        if (Time.time >= destroyTime)
        {
            Destroy(gameObject);
        }
    }

    private void FixedUpdate()
    {
        Vector2 nextPosition = projectileRigidbody.position + moveDirection * speed * Time.fixedDeltaTime;
        projectileRigidbody.MovePosition(nextPosition);
    }

    /// <summary>
    /// Настраивает снаряд сразу после создания противником.
    /// </summary>
    public void Initialize(
        Vector2 direction,
        float projectileDamage,
        float projectileSpeed,
        float projectileKnockbackForce,
        float projectileLifeTime,
        LayerMask projectileHitLayers,
        EnemyTemplate projectileOwner)
    {
        moveDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        damage = projectileDamage;
        speed = Mathf.Max(0.01f, projectileSpeed);
        knockbackForce = Mathf.Max(0f, projectileKnockbackForce);
        lifeTime = Mathf.Max(0.1f, projectileLifeTime);
        hitLayers = projectileHitLayers;
        owner = projectileOwner;
        destroyTime = Time.time + lifeTime;
        hasHit = false;

        float angle = Mathf.Atan2(moveDirection.y, moveDirection.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hasHit || !IsLayerAllowed(other.gameObject.layer) || IsOwnerCollider(other))
        {
            return;
        }

        PlayerCharacterTemplate target = other.GetComponentInParent<PlayerCharacterTemplate>();

        if (target == null || !target.IsAlive)
        {
            return;
        }

        if (target.IsIgnoringIncomingAttacks)
        {
            return;
        }

        hasHit = true;
        target.TakeDamage(damage, moveDirection, knockbackForce);

        Debug.Log($"{name}: снаряд противника нанес {damage:0.0} урона персонажу {target.name}.");
        Destroy(gameObject);
    }

    /// <summary>
    /// Проверяет, принадлежит ли коллайдер противнику, который выпустил снаряд.
    /// </summary>
    private bool IsOwnerCollider(Collider2D other)
    {
        EnemyTemplate enemy = other.GetComponentInParent<EnemyTemplate>();
        return enemy != null && ReferenceEquals(enemy, owner);
    }

    /// <summary>
    /// Проверяет, разрешен ли слой объекта для попадания снаряда.
    /// </summary>
    private bool IsLayerAllowed(int layer)
    {
        return (hitLayers.value & (1 << layer)) != 0;
    }
}
