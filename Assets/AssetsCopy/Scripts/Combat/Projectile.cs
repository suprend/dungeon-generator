using UnityEngine;

namespace DanverPlayground.Roguelike.Combat
{
    [RequireComponent(typeof(CircleCollider2D))]
    [RequireComponent(typeof(Rigidbody2D))]
    // Универсальный снаряд для базовой атаки и некоторых способностей.
    public class Projectile : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private CircleCollider2D hitCollider;

        private TeamAlignment sourceTeam;
        private float damage;
        private float lifeUntil;
        private bool initialized;

        private void Reset()
        {
            body = GetComponent<Rigidbody2D>();
            hitCollider = GetComponent<CircleCollider2D>();
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        private void Awake()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody2D>();
            }

            if (hitCollider == null)
            {
                hitCollider = GetComponent<CircleCollider2D>();
            }

            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }
        }

        private void Update()
        {
            if (initialized && Time.time >= lifeUntil)
            {
                Destroy(gameObject);
            }
        }

        // Все параметры задаются при создании, чтобы не плодить отдельные prefab'ы под каждый тип снаряда.
        public void Initialize(
            TeamAlignment projectileTeam,
            float projectileDamage,
            float speed,
            float lifetime,
            Vector2 direction,
            Sprite sprite,
            Color color,
            float scale)
        {
            sourceTeam = projectileTeam;
            damage = projectileDamage;
            lifeUntil = Time.time + lifetime;
            initialized = true;

            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            if (spriteRenderer != null)
            {
                spriteRenderer.sprite = sprite;
                spriteRenderer.color = color;
                spriteRenderer.transform.localScale = Vector3.one * scale;
            }

            body.gravityScale = 0f;
            body.isKinematic = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.velocity = direction.normalized * speed;

            hitCollider.isTrigger = true;
            hitCollider.radius = 0.25f;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            IDamageable damageable = other.GetComponentInParent<IDamageable>();
            if (damageable == null || !damageable.IsAlive || damageable.Team == sourceTeam)
            {
                return;
            }

            // Снаряд исчезает после первого валидного попадания.
            damageable.ReceiveDamage(damage);
            Destroy(gameObject);
        }
    }
}
