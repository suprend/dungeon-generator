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
    private Vector2 direction = Vector2.right;
    private float despawnTime;
    private bool launched;

    private void Reset()
    {
        CacheComponents();
        ApplyComponentDefaults();
    }

    private void Awake()
    {
        CacheComponents();
        ApplyComponentDefaults();
    }

    private void OnValidate()
    {
        CacheComponents();
        ApplyComponentDefaults();
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
        launched = true;

        transform.right = new Vector3(direction.x, direction.y, 0f);
        IgnoreOwnerColliders();
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

        var targetHealth = other.GetComponentInParent<Health>();
        if (targetHealth != null)
        {
            if (targetHealth == ownerHealth)
                return;

            targetHealth.ApplyDamage(damage);
            Destroy(gameObject);
            return;
        }

        if (destroyOnAnySolidHit && other is Collider2D collider2D && !collider2D.isTrigger)
            Destroy(gameObject);
    }
}
