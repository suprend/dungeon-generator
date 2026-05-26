//DP
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Снаряд мага: летит вперед, взрывается при попадании и наносит урон (и отталкивание) по области
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class MageProjectile : MonoBehaviour
{
    [Header("Параметры снаряда")]
    [SerializeField, Min(0f)] private float damage = 10f;
    [SerializeField, Min(0.01f)] private float speed = 8f;
    [SerializeField, Min(0f)] private float impactRadius = 1f;
    [SerializeField, Min(0f)] private float impactKnockbackForce = 5f;
    [SerializeField, Min(0.1f)] private float lifeTime = 3f;
    [SerializeField] private LayerMask impactLayers = ~0;

    private const int PiercingCircleSegments = 32;
    private const float PiercingCircleWidth = 0.05f;

    private static Material projectileCircleMaterial;

    private Rigidbody2D projectileRigidbody;
    private SpriteRenderer[] spriteRenderers;
    private IDamageable owner;
    private Vector2 moveDirection = Vector2.right;
    private float destroyTime;
    private bool isExploded;
    private bool isChainBounceProjectile;
    private bool isPiercingProjectile;
    private int remainingChainBounces;
    private float chainBounceRadius;
    private float piercingRadius;
    private float piercingDamageInterval = 0.25f;
    private IDamageable lastChainHitTarget;
    private Dictionary<IDamageable, float> nextPiercingDamageTimes = new Dictionary<IDamageable, float>();

    private void Awake()
    {
        projectileRigidbody = GetComponent<Rigidbody2D>();
        Rigidbody2DSmoothingUtility.EnableInterpolation(projectileRigidbody);
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>();

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

        if (isPiercingProjectile)
        {
            DamagePiercingTargetsAtPosition(nextPosition);
        }
    }

    /// <summary>
    /// Настраивает снаряд сразу после создания
    /// </summary>
    public void Initialize(
        Vector2 direction,
        float projectileDamage,
        float projectileSpeed,
        float projectileImpactRadius,
        float projectileImpactKnockbackForce,
        float projectileLifeTime,
        LayerMask projectileImpactLayers,
        IDamageable projectileOwner)
    {
        moveDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        damage = projectileDamage;
        speed = projectileSpeed;
        impactRadius = projectileImpactRadius;
        impactKnockbackForce = projectileImpactKnockbackForce;
        lifeTime = Mathf.Max(0.1f, projectileLifeTime);
        impactLayers = projectileImpactLayers;
        owner = projectileOwner;
        destroyTime = Time.time + lifeTime;
        isExploded = false;
        isChainBounceProjectile = false;
        isPiercingProjectile = false;
        remainingChainBounces = 0;
        chainBounceRadius = 0f;
        piercingRadius = 0f;
        piercingDamageInterval = 0.25f;
        lastChainHitTarget = null;
        nextPiercingDamageTimes.Clear();
        SetSpriteRenderersEnabled(true);

        RotateToMoveDirection();
    }

    /// <summary>
    /// Настраивает снаряд как первую способность мага
    /// </summary>
    public void InitializeChainBounce(
        Vector2 direction,
        float projectileDamage,
        float projectileSpeed,
        float projectileImpactKnockbackForce,
        float projectileLifeTime,
        LayerMask projectileImpactLayers,
        IDamageable projectileOwner,
        int maxBounces,
        float bounceRadius)
    {
        Initialize(
            direction,
            projectileDamage,
            projectileSpeed,
            0f,
            projectileImpactKnockbackForce,
            projectileLifeTime,
            projectileImpactLayers,
            projectileOwner);

        isChainBounceProjectile = true;
        remainingChainBounces = Mathf.Max(0, maxBounces);
        chainBounceRadius = Mathf.Max(0f, bounceRadius);
    }

    /// <summary>
    /// Настраивает снаряд как третью способность мага
    /// </summary>
    public void InitializePiercing(
        Vector2 direction,
        float projectileDamage,
        float projectileSpeed,
        float projectileRadius,
        float projectileKnockbackForce,
        float projectileDamageInterval,
        float projectileLifeTime,
        LayerMask projectileImpactLayers,
        IDamageable projectileOwner,
        Color projectileColor)
    {
        Initialize(
            direction,
            projectileDamage,
            projectileSpeed,
            projectileRadius,
            projectileKnockbackForce,
            projectileLifeTime,
            projectileImpactLayers,
            projectileOwner);

        isPiercingProjectile = true;
        piercingRadius = Mathf.Max(0.01f, projectileRadius);
        piercingDamageInterval = Mathf.Max(0.02f, projectileDamageInterval);
        SetSpriteRenderersEnabled(false);
        CreatePiercingCircleVisual(piercingRadius, projectileColor);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsFriendlyCollider(other))
        {
            return;
        }

        if (isChainBounceProjectile)
        {
            TryHitChainTarget(other);
            return;
        }

        if (isPiercingProjectile)
        {
            TryHitPiercingTarget(other);
            return;
        }

        if (!CanRegularProjectileExplodeOnCollider(other))
        {
            return;
        }

        Explode();
    }

    /// <summary>
    /// Проверяет снаряд на дружелюбие :)
    /// </summary>
    private bool IsFriendlyCollider(Collider2D other)
    {
        IDamageable damageable = other.GetComponentInParent<IDamageable>();
        return damageable != null && (ReferenceEquals(damageable, owner) || damageable is PlayerCharacterTemplate);
    }

    private bool CanRegularProjectileExplodeOnCollider(Collider2D other)
    {
        if (!IsLayerAllowed(other.gameObject.layer))
        {
            return false;
        }

        IDamageable target = other.GetComponentInParent<IDamageable>();
        if (target != null)
        {
            return PlayerCharacterTemplate.CanPlayerAttackDamageTarget(target);
        }

        return !other.isTrigger;
    }

    /// <summary>
    /// Наносит урон цели через которую пролетел пробивающий снаряд
    /// </summary>
    private void TryHitPiercingTarget(Collider2D other)
    {
        if (!IsLayerAllowed(other.gameObject.layer))
        {
            return;
        }

        IDamageable target = other.GetComponentInParent<IDamageable>();
        HitPiercingTarget(target);
    }

    /// <summary>
    /// Проверяет цели вокруг пробивающего снаряда и периодически наносит им урон
    /// </summary>
    private void DamagePiercingTargetsAtPosition(Vector2 projectilePosition)
    {
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(projectilePosition, piercingRadius, impactLayers);

        foreach (Collider2D hitCollider in hitColliders)
        {
            IDamageable target = hitCollider.GetComponentInParent<IDamageable>();
            HitPiercingTarget(target);
        }
    }

    /// <summary>
    /// Наносит периодический урон одной цели пробивающего снаряда
    /// </summary>
    private void HitPiercingTarget(IDamageable target)
    {
        if (!PlayerCharacterTemplate.CanPlayerAttackDamageTarget(target) || !CanApplyPiercingDamage(target))
        {
            return;
        }

        nextPiercingDamageTimes[target] = Time.time + piercingDamageInterval;
        target.TakeDamage(damage);
        KnockbackUtility.ApplyKnockback(target, moveDirection, impactKnockbackForce);

        Debug.Log($"{name} пробивающий снаряд мага нанес {damage:0.0} периодического урона и слегка оттолкнул цель");
    }

    /// <summary>
    /// Проверяет можно ли снова нанести урон цели пробивающим снарядом
    /// </summary>
    private bool CanApplyPiercingDamage(IDamageable target)
    {
        if (!nextPiercingDamageTimes.TryGetValue(target, out float nextDamageTime))
        {
            return true;
        }

        return Time.time >= nextDamageTime;
    }

    /// <summary>
    /// Наносит урон одной цели и пытается направить цепной снаряд к следующему противнику
    /// </summary>
    private void TryHitChainTarget(Collider2D other)
    {
        IDamageable target = other.GetComponentInParent<IDamageable>();

        if (!PlayerCharacterTemplate.CanPlayerAttackDamageTarget(target) || ReferenceEquals(target, lastChainHitTarget))
        {
            return;
        }

        target.TakeDamage(damage);
        KnockbackUtility.ApplyKnockback(target, moveDirection, impactKnockbackForce);
        lastChainHitTarget = target;

        if (remainingChainBounces <= 0)
        {
            Debug.Log($"{name} цепной снаряд мага закончил отскоки");
            Destroy(gameObject);
            return;
        }

        EnemyTemplate nextEnemy = FindRandomBounceTarget(target);

        if (nextEnemy == null)
        {
            Debug.Log($"{name} цепной снаряд мага не нашел следующую цель");
            Destroy(gameObject);
            return;
        }

        remainingChainBounces--;
        SetMoveDirection((Vector2)nextEnemy.transform.position - (Vector2)transform.position);

        Debug.Log($"{name} цепной снаряд мага отскочил к {nextEnemy.name}. Осталось отскоков: {remainingChainBounces}");
    }

    /// <summary>
    /// Выбирает случайного живого противника поблизости кроме только что пораженного
    /// </summary>
    private EnemyTemplate FindRandomBounceTarget(IDamageable excludedTarget)
    {
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(transform.position, chainBounceRadius, impactLayers);
        List<EnemyTemplate> possibleEnemies = new List<EnemyTemplate>();
        HashSet<EnemyTemplate> uniqueEnemies = new HashSet<EnemyTemplate>();

        foreach (Collider2D hitCollider in hitColliders)
        {
            EnemyTemplate enemy = hitCollider.GetComponentInParent<EnemyTemplate>();

            if (enemy == null ||
                !enemy.IsAlive ||
                ReferenceEquals(enemy, excludedTarget) ||
                !uniqueEnemies.Add(enemy))
            {
                continue;
            }

            possibleEnemies.Add(enemy);
        }

        if (possibleEnemies.Count == 0)
        {
            return null;
        }

        int randomIndex = Random.Range(0, possibleEnemies.Count);
        return possibleEnemies[randomIndex];
    }

    /// <summary>
    /// Меняет направление движения снаряда и поворачивает его в сторону полета
    /// </summary>
    private void SetMoveDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        moveDirection = direction.normalized;
        RotateToMoveDirection();
    }

    /// <summary>
    /// Поворачивает снаряд по текущему направлению движения
    /// </summary>
    private void RotateToMoveDirection()
    {
        float angle = Mathf.Atan2(moveDirection.y, moveDirection.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    /// <summary>
    /// Включает или выключает спрайт снаряда
    /// </summary>
    private void SetSpriteRenderersEnabled(bool isEnabled)
    {
        if (spriteRenderers == null)
        {
            return;
        }

        foreach (SpriteRenderer spriteRenderer in spriteRenderers)
        {
            spriteRenderer.enabled = isEnabled;
        }
    }

    /// <summary>
    /// Создает круглый визуал для пробивающего снаряда
    /// </summary>
    private void CreatePiercingCircleVisual(float radius, Color circleColor)
    {
        GameObject circleObject = new GameObject("MagePiercingProjectileCircle");
        circleObject.transform.SetParent(transform, false);

        LineRenderer circleRenderer = circleObject.AddComponent<LineRenderer>();
        circleRenderer.useWorldSpace = false;
        circleRenderer.loop = true;
        circleRenderer.positionCount = PiercingCircleSegments;
        circleRenderer.startWidth = PiercingCircleWidth;
        circleRenderer.endWidth = PiercingCircleWidth;
        circleRenderer.startColor = circleColor;
        circleRenderer.endColor = circleColor;
        circleRenderer.sortingOrder = 10;
        circleRenderer.sharedMaterial = GetProjectileCircleMaterial();

        for (int i = 0; i < PiercingCircleSegments; i++)
        {
            float angle = (float)i / PiercingCircleSegments * Mathf.PI * 2f;
            Vector3 point = new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius,
                0f
            );

            circleRenderer.SetPosition(i, point);
        }
    }

    /// <summary>
    /// Возвращает материал для круглого снаряда
    /// </summary>
    private static Material GetProjectileCircleMaterial()
    {
        if (projectileCircleMaterial != null)
        {
            return projectileCircleMaterial;
        }

        Shader spriteShader = Shader.Find("Sprites/Default");

        if (spriteShader == null)
        {
            return null;
        }

        projectileCircleMaterial = new Material(spriteShader);
        return projectileCircleMaterial;
    }

    /// <summary>
    /// Проверяет разрешен ли слой объекта для попадания снаряда
    /// </summary>
    private bool IsLayerAllowed(int layer)
    {
        return (impactLayers.value & (1 << layer)) != 0;
    }

    /// <summary>
    /// Наносит урон всем подходящим целям рядом с точкой попадания
    /// </summary>
    private void Explode()
    {
        if (isExploded)
        {
            return;
        }

        isExploded = true;
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(transform.position, impactRadius, impactLayers);
        HashSet<IDamageable> damagedTargets = new HashSet<IDamageable>();

        foreach (Collider2D hitCollider in hitColliders)
        {
            IDamageable target = hitCollider.GetComponentInParent<IDamageable>();

            if (!PlayerCharacterTemplate.CanPlayerAttackDamageTarget(target) || !damagedTargets.Add(target))
            {
                continue;
            }

            target.TakeDamage(damage);
            Vector2 knockbackDirection = KnockbackUtility.GetDirectionFromPoint(
                transform.position,
                hitCollider,
                moveDirection);
            KnockbackUtility.ApplyKnockback(target, knockbackDirection, impactKnockbackForce);
        }

        Debug.Log($"{name}: взрыв нанес {damage} урона. Целей задето: {damagedTargets.Count}.");
        Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, impactRadius);
    }
}
