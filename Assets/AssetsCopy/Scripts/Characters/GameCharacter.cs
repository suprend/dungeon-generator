using System.Collections;
using DanverPlayground.Roguelike.Characters.Abilities;
using DanverPlayground.Roguelike.Combat;
using UnityEngine;

namespace DanverPlayground.Roguelike.Characters
{
    [RequireComponent(typeof(Rigidbody2D))]
    // Главный runtime-компонент персонажа: движение, стрельба, способности, здоровье и переключение Player/AI.
    public class GameCharacter : MonoBehaviour, IDamageable
    {
        [Header("Config")]
        [SerializeField] private CharacterDefinition definition;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private PlayerCharacterBrain playerBrain;
        [SerializeField] private CompanionAIController aiBrain;

        [Header("Runtime")]
        [SerializeField] private CharacterControlMode controlMode = CharacterControlMode.AI;

        private Vector2 desiredMoveInput;
        private Vector2 desiredAimInput = Vector2.down;
        private float currentMaxHealth;
        private float currentMoveSpeedMultiplier = 1f;
        private float basicAttackReadyTime;
        private float[] abilityReadyTimes = new float[3];
        private Coroutine activeDashRoutine;
        private Coroutine activeSpeedBoostRoutine;
        private bool movementLocked;

        public CharacterDefinition Definition => definition;
        public CharacterControlMode ControlMode => controlMode;
        public float CurrentHealth { get; private set; }
        public float MaxHealth => currentMaxHealth;
        public Vector2 LastNonZeroMoveDirection { get; private set; } = Vector2.down;
        public Rigidbody2D Body => body;
        public PartyController PartyOwner { get; private set; }
        public TeamAlignment Team => TeamAlignment.Player;
        public Transform AimPoint => transform;
        public bool IsAlive => CurrentHealth > 0f;

        private void Reset()
        {
            body = GetComponent<Rigidbody2D>();
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            playerBrain = GetComponent<PlayerCharacterBrain>();
            aiBrain = GetComponent<CompanionAIController>();
        }

        private void Awake()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody2D>();
            }

            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            if (playerBrain == null)
            {
                playerBrain = GetComponent<PlayerCharacterBrain>();
            }

            if (aiBrain == null)
            {
                aiBrain = GetComponent<CompanionAIController>();
            }

            ApplyDefinition();
            SetControlMode(controlMode);
        }

        private void FixedUpdate()
        {
            if (definition == null || movementLocked)
            {
                body.velocity = Vector2.zero;
                return;
            }

            // Переводим вход движения в реальную физическую скорость через плавный разгон/торможение.
            Vector2 targetVelocity = desiredMoveInput * EffectiveMoveSpeed;
            float acceleration = desiredMoveInput.sqrMagnitude > 0.01f
                ? definition.BaseStats.acceleration
                : definition.BaseStats.deceleration;

            body.velocity = Vector2.MoveTowards(body.velocity, targetVelocity, acceleration * Time.fixedDeltaTime);
        }

        public void Initialize(PartyController partyOwner)
        {
            PartyOwner = partyOwner;

            if (aiBrain != null)
            {
                aiBrain.Initialize(this, partyOwner);
            }
        }

        public void SetDefinition(CharacterDefinition newDefinition)
        {
            definition = newDefinition;
            ApplyDefinition();
        }

        public void SetControlMode(CharacterControlMode newMode)
        {
            controlMode = newMode;
            desiredMoveInput = Vector2.zero;

            // Одновременно активен только один источник управления: либо игрок, либо ИИ.
            if (playerBrain != null)
            {
                playerBrain.enabled = newMode == CharacterControlMode.Player;
                if (playerBrain.enabled)
                {
                    playerBrain.Initialize(this);
                }
            }

            if (aiBrain != null)
            {
                aiBrain.enabled = newMode == CharacterControlMode.AI;
            }
        }

        public void SetMovementInput(Vector2 moveInput)
        {
            desiredMoveInput = Vector2.ClampMagnitude(moveInput, 1f);
            if (desiredMoveInput.sqrMagnitude > 0.01f)
            {
                LastNonZeroMoveDirection = desiredMoveInput.normalized;
            }
        }

        public void SetAimInput(Vector2 aimInput)
        {
            if (aimInput.sqrMagnitude > 0.01f)
            {
                desiredAimInput = aimInput.normalized;
            }
        }

        public bool TryUseAbility()
        {
            return TryUseAbility(0);
        }

        public bool IsAbilityReady(int slotIndex)
        {
            ActiveAbilityDefinition ability = GetAbility(slotIndex);
            return ability != null && slotIndex >= 0 && slotIndex < abilityReadyTimes.Length && Time.time >= abilityReadyTimes[slotIndex];
        }

        public bool TryUseAbility(int slotIndex)
        {
            ActiveAbilityDefinition ability = GetAbility(slotIndex);
            if (ability == null || !IsAbilityReady(slotIndex) || !ability.CanActivate(this))
            {
                return false;
            }

            // Кулдаун хранится отдельно для каждого слота способности.
            bool activated = ability.Activate(this, desiredAimInput);
            if (activated)
            {
                abilityReadyTimes[slotIndex] = Time.time + ability.Cooldown;
            }

            return activated;
        }

        public bool TryBasicAttack()
        {
            if (!IsAlive || definition == null || definition.RangedAttack == null || Time.time < basicAttackReadyTime)
            {
                return false;
            }

            // Если игрок не целится мышью, используем последнее направление движения.
            Vector2 direction = desiredAimInput.sqrMagnitude > 0.01f ? desiredAimInput : LastNonZeroMoveDirection;
            if (direction.sqrMagnitude < 0.01f)
            {
                direction = Vector2.right;
            }

            FireProjectile(direction, 1f, 1f);
            basicAttackReadyTime = Time.time + 1f / Mathf.Max(definition.RangedAttack.fireRate, 0.01f);
            return true;
        }

        public void FireProjectile(Vector2 direction, float damageMultiplier, float speedMultiplier)
        {
            if (definition == null || definition.RangedAttack == null)
            {
                return;
            }

            // Снаряд создаётся полностью кодом, чтобы не требовать отдельный prefab на каждую атаку.
            GameObject projectileObject = new GameObject($"{definition.DisplayName}_Projectile");
            projectileObject.transform.position = transform.position + (Vector3)(direction.normalized * 0.55f);

            Rigidbody2D projectileBody = projectileObject.AddComponent<Rigidbody2D>();
            CircleCollider2D projectileCollider = projectileObject.AddComponent<CircleCollider2D>();

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(projectileObject.transform, false);
            SpriteRenderer projectileRenderer = visual.AddComponent<SpriteRenderer>();
            projectileRenderer.sortingOrder = 20;

            Projectile projectile = projectileObject.AddComponent<Projectile>();

            projectile.Initialize(
                Team,
                definition.RangedAttack.damage * damageMultiplier,
                definition.RangedAttack.projectileSpeed * speedMultiplier,
                definition.RangedAttack.projectileLifetime,
                direction,
                definition.Sprite,
                definition.ProjectileColor,
                definition.RangedAttack.projectileScale);
        }

        public void Dash(Vector2 direction, float speed, float duration)
        {
            if (activeDashRoutine != null)
            {
                StopCoroutine(activeDashRoutine);
            }

            activeDashRoutine = StartCoroutine(DashRoutine(direction, speed, duration));
        }

        public void ApplySpeedBoost(float multiplier, float duration)
        {
            if (activeSpeedBoostRoutine != null)
            {
                StopCoroutine(activeSpeedBoostRoutine);
            }

            activeSpeedBoostRoutine = StartCoroutine(SpeedBoostRoutine(multiplier, duration));
        }

        public void Heal(float value)
        {
            CurrentHealth = Mathf.Min(CurrentHealth + value, currentMaxHealth);
        }

        public void ReceiveDamage(float value)
        {
            if (!IsAlive)
            {
                return;
            }

            CurrentHealth = Mathf.Max(CurrentHealth - value, 0f);
            if (CurrentHealth <= 0f)
            {
                // В прототипе смерть игрока пока только отключает активность и визуально "гасит" персонажа.
                desiredMoveInput = Vector2.zero;
                body.velocity = Vector2.zero;
                if (spriteRenderer != null)
                {
                    spriteRenderer.color = new Color(0.4f, 0.4f, 0.4f, 0.7f);
                }
            }
        }

        private float EffectiveMoveSpeed => definition.BaseStats.moveSpeed * currentMoveSpeedMultiplier;

        private void ApplyDefinition()
        {
            if (definition == null)
            {
                return;
            }

            // При смене CharacterDefinition полностью пересобираем runtime-статы и кулдауны.
            currentMaxHealth = definition.BaseStats.maxHealth;
            CurrentHealth = currentMaxHealth;
            basicAttackReadyTime = 0f;
            abilityReadyTimes = new float[Mathf.Max(definition.ActiveAbilities != null ? definition.ActiveAbilities.Length : 0, 3)];

            if (spriteRenderer != null)
            {
                spriteRenderer.sprite = definition.Sprite;
                spriteRenderer.color = Color.white;
            }
        }

        private ActiveAbilityDefinition GetAbility(int slotIndex)
        {
            if (definition == null || definition.ActiveAbilities == null || slotIndex < 0 || slotIndex >= definition.ActiveAbilities.Length)
            {
                return null;
            }

            return definition.ActiveAbilities[slotIndex];
        }

        private IEnumerator DashRoutine(Vector2 direction, float speed, float duration)
        {
            movementLocked = true;

            if (direction.sqrMagnitude < 0.01f)
            {
                direction = LastNonZeroMoveDirection;
            }

            // Во время рывка обычное управление временно блокируется.
            body.velocity = direction.normalized * speed;
            yield return new WaitForSeconds(duration);

            movementLocked = false;
            activeDashRoutine = null;
        }

        private IEnumerator SpeedBoostRoutine(float multiplier, float duration)
        {
            currentMoveSpeedMultiplier = multiplier;
            yield return new WaitForSeconds(duration);
            currentMoveSpeedMultiplier = 1f;
            activeSpeedBoostRoutine = null;
        }
    }
}
