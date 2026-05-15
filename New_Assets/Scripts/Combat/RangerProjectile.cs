//DP
using UnityEngine;

/// <summary>
/// Снаряд следопыта который поражает только одну цель и исчезает при первом попадании
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class RangerProjectile : MonoBehaviour
{
    [Header("Параметры снаряда")]
    [SerializeField, Min(0f)] private float damage = 10f;
    [SerializeField, Min(0.01f)] private float speed = 10f;
    [SerializeField, Min(0f)] private float knockbackForce = 6f;
    [SerializeField, Min(0f)] private float damageResistanceReductionPercent;
    [SerializeField, Min(0f)] private float damageResistanceReductionDuration;
    [SerializeField, Min(0.1f)] private float lifeTime = 3f;
    [SerializeField] private LayerMask hitLayers = ~0;

    private Rigidbody2D projectileRigidbody;
    private IDamageable owner;
    private Vector2 moveDirection = Vector2.right;
    private float destroyTime;
    private bool isHit;

    private void Awake()
    {
        projectileRigidbody = GetComponent<Rigidbody2D>();

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
    /// Настраивает снаряд после создания следопытом
    /// </summary>
    public void Initialize(
        Vector2 direction,
        float projectileDamage,
        float projectileSpeed,
        float projectileKnockbackForce,
        float projectileLifeTime,
        LayerMask projectileHitLayers,
        IDamageable projectileOwner,
        float projectileDamageResistanceReductionPercent = 0f,
        float projectileDamageResistanceReductionDuration = 0f)
    {
        moveDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        damage = projectileDamage;
        speed = projectileSpeed;
        knockbackForce = projectileKnockbackForce;
        damageResistanceReductionPercent = projectileDamageResistanceReductionPercent;
        damageResistanceReductionDuration = projectileDamageResistanceReductionDuration;
        lifeTime = Mathf.Max(0.1f, projectileLifeTime);
        hitLayers = projectileHitLayers;
        owner = projectileOwner;
        destroyTime = Time.time + lifeTime;

        float angle = Mathf.Atan2(moveDirection.y, moveDirection.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isHit || IsFriendlyCollider(other) || !IsLayerAllowed(other.gameObject.layer))
        {
            return;
        }

        IDamageable target = other.GetComponentInParent<IDamageable>();

        if (!PlayerCharacterTemplate.CanPlayerAttackDamageTarget(target))
        {
            return;
        }

        isHit = true;
        target.TakeDamage(damage);
        KnockbackUtility.ApplyKnockback(target, moveDirection, knockbackForce);
        ReduceTargetDamageResistance(target);
        Debug.Log($"{name}: снаряд следопыта нанес {damage} урона одной цели.");

        Destroy(gameObject);
    }

    /// <summary>
    /// Проверяет снаряд на дружелюбие :)
    /// </summary>
    private bool IsFriendlyCollider(Collider2D other)
    {
        IDamageable damageable = other.GetComponentInParent<IDamageable>();
        return damageable != null && (ReferenceEquals(damageable, owner) || damageable is PlayerCharacterTemplate);
    }

    /// <summary>
    /// Снижает сопротивление урону цели
    /// </summary>
    private void ReduceTargetDamageResistance(IDamageable target)
    {
        if (damageResistanceReductionPercent <= 0f)
        {
            return;
        }

        EnemyTemplate enemy = target as EnemyTemplate;

        if (enemy == null)
        {
            return;
        }

        enemy.ApplyTemporaryDamageResistanceReduction(
            damageResistanceReductionPercent,
            damageResistanceReductionDuration);
    }

    /// <summary>
    /// Проверяет разрешен ли слой объекта для попадания
    /// </summary>
    private bool IsLayerAllowed(int layer)
    {
        return (hitLayers.value & (1 << layer)) != 0;
    }
}
