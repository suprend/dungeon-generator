using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public sealed class ArrowProjectile : MonoBehaviour
{
    [SerializeField] private Rigidbody2D body2D;
    [SerializeField] private Collider2D hitCollider;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private float speed = 12f;
    [SerializeField] private int damage = 1;
    [SerializeField] private float lifetimeSeconds = 1.5f;
    [SerializeField] private bool destroyOnAnySolidHit = true;

    private Health ownerHealth;
    private Transform ownerRoot;
    private Vector2 direction = Vector2.right;
    private float despawnTime;
    private bool launched;
    private Vector3 initialVisualScale = Vector3.one;

    public float Speed => Mathf.Max(0.01f, speed);
    public int Damage => Mathf.Max(1, damage);
    public float LifetimeSeconds => Mathf.Max(0.1f, lifetimeSeconds);
    public float VisualScale
    {
        get
        {
            CacheComponents();
            CacheVisualDefaults();
            return spriteRenderer != null
                ? Mathf.Max(0.01f, spriteRenderer.transform.localScale.x / Mathf.Max(0.0001f, initialVisualScale.x))
                : 1f;
        }
    }

    private void Reset()
    {
        CacheComponents();
        ApplyComponentDefaults();
    }

    private void Awake()
    {
        CacheComponents();
        ApplyComponentDefaults();
        CacheVisualDefaults();
    }

    private void OnValidate()
    {
        CacheComponents();
        ApplyComponentDefaults();
        CacheVisualDefaults();
    }

    private void Update()
    {
        if (!launched)
            return;

        if (Time.time >= despawnTime)
        {
            Destroy(gameObject);
        }
    }

    private void FixedUpdate()
    {
        if (!launched)
            return;

        if (body2D != null)
        {
            body2D.MovePosition(body2D.position + direction * (speed * Time.fixedDeltaTime));
            return;
        }

        transform.position += (Vector3)(direction * (speed * Time.fixedDeltaTime));
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryHit(other);
    }

    public void Launch(Vector2 newDirection, Health newOwnerHealth)
    {
        CacheComponents();
        ApplyComponentDefaults();

        direction = newDirection.sqrMagnitude > 0.0001f ? newDirection.normalized : Vector2.right;
        despawnTime = Time.time + Mathf.Max(0.1f, lifetimeSeconds);
        ownerHealth = newOwnerHealth;
        ownerRoot = newOwnerHealth != null ? newOwnerHealth.transform : null;
        launched = true;

        transform.right = new Vector3(direction.x, direction.y, 0f);
        IgnoreOwnerColliders();
    }

    public void ConfigureRuntime(float newSpeed, int newDamage, float newLifetimeSeconds, float newScale, Color newTint)
    {
        CacheComponents();
        CacheVisualDefaults();

        speed = Mathf.Max(0.01f, newSpeed);
        damage = Mathf.Max(1, newDamage);
        lifetimeSeconds = Mathf.Max(0.1f, newLifetimeSeconds);

        if (spriteRenderer != null)
        {
            spriteRenderer.color = newTint;
            spriteRenderer.transform.localScale = initialVisualScale * Mathf.Max(0.01f, newScale);
        }
    }

    private void CacheComponents()
    {
        if (body2D == null)
            body2D = GetComponent<Rigidbody2D>();

        if (hitCollider == null)
            hitCollider = GetComponent<Collider2D>();

        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    private void CacheVisualDefaults()
    {
        if (spriteRenderer == null)
            return;

        if (initialVisualScale == Vector3.one && spriteRenderer.transform != null)
            initialVisualScale = spriteRenderer.transform.localScale;
    }

    private void ApplyComponentDefaults()
    {
        if (body2D != null)
        {
            body2D.gravityScale = 0f;
            body2D.bodyType = RigidbodyType2D.Kinematic;
            body2D.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body2D.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        if (hitCollider != null)
            hitCollider.isTrigger = true;

        if (spriteRenderer != null)
            spriteRenderer.sortingOrder = Mathf.Max(spriteRenderer.sortingOrder, 400);
    }

    private void IgnoreOwnerColliders()
    {
        if (ownerHealth == null || hitCollider == null)
            return;

        var ownerColliders = ownerHealth.GetComponentsInChildren<Collider2D>(true);
        for (var i = 0; i < ownerColliders.Length; i++)
        {
            var ownerCollider = ownerColliders[i];
            if (ownerCollider != null)
                Physics2D.IgnoreCollision(hitCollider, ownerCollider, true);
        }
    }

    private void TryHit(Component other)
    {
        if (other == null)
            return;
        if (!launched)
            return;
        if (ownerRoot != null && other.transform != null && other.transform.IsChildOf(ownerRoot))
            return;

        var targetHealth = other.GetComponentInParent<Health>();
        if (targetHealth != null)
        {
            if (targetHealth == ownerHealth)
                return;
            if (AreFriendly(targetHealth))
                return;

            targetHealth.ApplyDamage(damage);
            Destroy(gameObject);
            return;
        }

        if (destroyOnAnySolidHit && other is Collider2D collider2D && !collider2D.isTrigger)
            Destroy(gameObject);
    }

    private bool AreFriendly(Health targetHealth)
    {
        if (targetHealth == null || ownerHealth == null)
            return false;

        var ownerIsEnemy = ownerHealth.GetComponentInParent<EnemyAuthoring>() != null;
        var targetIsEnemy = targetHealth.GetComponentInParent<EnemyAuthoring>() != null;
        return ownerIsEnemy == targetIsEnemy;
    }
}
