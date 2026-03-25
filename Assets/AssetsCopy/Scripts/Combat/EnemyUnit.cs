using System.Collections.Generic;
using DanverPlayground.Roguelike.Characters;
using UnityEngine;

namespace DanverPlayground.Roguelike.Combat
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    // Базовый враг для прототипа: идёт к ближайшему герою, получает урон и умирает.
    public class EnemyUnit : MonoBehaviour, IDamageable
    {
        private static readonly List<EnemyUnit> ActiveEnemiesInternal = new List<EnemyUnit>();

        [SerializeField] private float maxHealth = 12f;
        [SerializeField] private float moveSpeed = 2.5f;
        [SerializeField] private float contactDamage = 1f;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Rigidbody2D body;

        public static IReadOnlyList<EnemyUnit> ActiveEnemies => ActiveEnemiesInternal;

        public TeamAlignment Team => TeamAlignment.Enemy;
        public Transform AimPoint => transform;
        public bool IsAlive => CurrentHealth > 0f;
        public float CurrentHealth { get; private set; }

        private void Reset()
        {
            body = GetComponent<Rigidbody2D>();
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        private void Awake()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody2D>();
            }

            CurrentHealth = maxHealth;
        }

        private void OnEnable()
        {
            // Глобальный список нужен союзному ИИ, чтобы быстро искать цели.
            if (!ActiveEnemiesInternal.Contains(this))
            {
                ActiveEnemiesInternal.Add(this);
            }
        }

        private void OnDisable()
        {
            ActiveEnemiesInternal.Remove(this);
        }

        private void FixedUpdate()
        {
            if (!IsAlive)
            {
                body.velocity = Vector2.zero;
                return;
            }

            // Для простоты враг всегда выбирает ближайшего живого героя.
            GameCharacter target = FindClosestPlayer();
            if (target == null)
            {
                body.velocity = Vector2.zero;
                return;
            }

            Vector2 direction = ((Vector2)target.transform.position - body.position).normalized;
            body.velocity = direction * moveSpeed;
        }

        public void ReceiveDamage(float amount)
        {
            if (!IsAlive)
            {
                return;
            }

            CurrentHealth = Mathf.Max(CurrentHealth - amount, 0f);
            if (CurrentHealth <= 0f)
            {
                Destroy(gameObject);
            }
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            if (!IsAlive)
            {
                return;
            }

            // Контактный урон наносится постепенно, пока враг касается героя.
            GameCharacter character = collision.collider.GetComponentInParent<GameCharacter>();
            if (character != null && character.IsAlive)
            {
                character.ReceiveDamage(contactDamage * Time.fixedDeltaTime);
            }
        }

        private GameCharacter FindClosestPlayer()
        {
            GameCharacter[] characters = Object.FindObjectsOfType<GameCharacter>();
            float bestDistance = float.MaxValue;
            GameCharacter bestTarget = null;

            // Пока врагов мало, полного перебора по всем персонажам достаточно.
            for (int i = 0; i < characters.Length; i++)
            {
                if (!characters[i].IsAlive)
                {
                    continue;
                }

                float sqrDistance = ((Vector2)characters[i].transform.position - body.position).sqrMagnitude;
                if (sqrDistance < bestDistance)
                {
                    bestDistance = sqrDistance;
                    bestTarget = characters[i];
                }
            }

            return bestTarget;
        }
    }
}
