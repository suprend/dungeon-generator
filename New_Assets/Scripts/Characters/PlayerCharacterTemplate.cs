//DP
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Определяет, кто сейчас управляет персонажем
/// </summary>
public enum PlayerCharacterControlState
{
    PlayerControlled,
    AiControlled
}

/// <summary>
/// Шаблон персонажа игрока
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerCharacterTemplate : MonoBehaviour, IDamageable
{
    private enum AiMovementMode
    {
        Idle,
        ApproachEnemy,
        RetreatFromEnemy,
        FollowActiveCharacter
    }

    private const float DamageKnockbackSlowdown = 20f;
    private const float MinDamageKnockbackSqrMagnitude = 0.0001f;
    private const float MinAiMovementSqrMagnitude = 0.0001f;
    private const float IncapacitatedSpriteRotationZ = 90f;
    private const int ShieldVisualTextureSize = 64;
    private const int AbilityCooldownSlotsCount = 3;

    private static readonly List<PlayerCharacterTemplate> ActivePlayerCharacters = new List<PlayerCharacterTemplate>();

    private static Material circleVisualMaterial;
    [Header("Управление")]
    [SerializeField] private PlayerCharacterControlState controlState = PlayerCharacterControlState.PlayerControlled;

    [Header("Статы персонажа")]
    [SerializeField, Min(1f)] private float maxHealth = 100f;
    [SerializeField, Min(0f)] private float currentHealth = 100f;
    [SerializeField, Min(0.01f)] private float attackSpeed = 1f;
    [SerializeField, Min(0f)] private float damage = 10f;
    [SerializeField, Min(0f)] private float movementSpeed = 5f;

    [Header("Реакция на урон")]
    [SerializeField, Min(0f)] private float damageKnockbackForce = 4f;
    [Tooltip("Multiplier applied to knockback from a lethal hit.")]
    [SerializeField, Min(1f)] private float deathKnockbackForceMultiplier = 2f;
    [SerializeField, Min(0f)] private float damageFlashDuration = 0.1f;
    [SerializeField] private Color damageFlashColor = Color.red;
    [SerializeField, Min(0f)] private float damageInvulnerabilityDuration = 1f;

    [Header("Отображение щита")]
    [SerializeField, Min(0f)] private float shieldVisualDiameter = 1.6f;
    [SerializeField] private Vector2 shieldVisualOffset = Vector2.zero;
    [SerializeField] private Color shieldVisualColor = new Color(1f, 0.85f, 0f, 0.35f);
    [SerializeField] private int shieldVisualSortingOrderOffset = 10;

    [Header("Отображение усиления урона")]
    [SerializeField] private Sprite attackBonusStatusSprite;
    [SerializeField] private Vector2 attackBonusStatusOffset = new Vector2(0f, 1.05f);
    [SerializeField, Min(0f)] private float attackBonusStatusScale = 1f;
    [SerializeField] private int attackBonusStatusSortingOrderOffset = 25;

    [Header("Отображение сопротивления урону")]
    [SerializeField] private Sprite defenceBonusStatusSprite;
    [SerializeField] private Vector2 defenceBonusStatusOffset = new Vector2(-0.3f, 1.05f);
    [SerializeField, Min(0f)] private float defenceBonusStatusScale = 1f;
    [SerializeField] private int defenceBonusStatusSortingOrderOffset = 25;

    [Header("Отображение ускорения")]
    [SerializeField] private Sprite speedBonusStatusSprite;
    [SerializeField] private Vector2 speedBonusStatusOffset = new Vector2(0.3f, 1.05f);
    [SerializeField, Min(0f)] private float speedBonusStatusScale = 1f;
    [SerializeField] private int speedBonusStatusSortingOrderOffset = 25;

    [Header("Реакция на лечение")]
    [SerializeField, Min(0f)] private float healingFlashDuration = 0.12f;
    [SerializeField] private Color healingFlashColor = Color.green;

    [Header("Отображение здоровья")]
    [SerializeField] private Vector2 healthBarOffset = new Vector2(0f, -0.75f);
    [SerializeField, Min(0f)] private float healthBarWidth = 1.2f;
    [SerializeField, Min(0f)] private float healthBarHeight = 0.12f;
    [SerializeField] private Color healthBarBackgroundColor = new Color(0f, 0f, 0f, 0.65f);
    [SerializeField] private Color healthBarFillColor = new Color(0.15f, 0.95f, 0.2f, 0.95f);
    [SerializeField] private int healthBarSortingOrderOffset = 20;

    [Header("Отображение откатов способностей")]
    [SerializeField] private Vector2 abilityCooldownOffsetFromHealthBar = new Vector2(0f, -0.22f);
    [SerializeField, Min(0f)] private float abilityCooldownSquareSize = 0.18f;
    [SerializeField, Min(0f)] private float abilityCooldownSquareSpacing = 0.08f;
    [SerializeField] private Color abilityCooldownBackgroundColor = new Color(0f, 0f, 0f, 0.7f);
    [SerializeField] private Color abilityCooldownReadyColor = new Color(1f, 1f, 1f, 0.95f);
    [SerializeField] private Color abilityCooldownRechargingColor = new Color(0.25f, 0.55f, 1f, 0.95f);
    [SerializeField] private int abilityCooldownSortingOrderOffset = 22;

    [Header("Недееспособность")]
    [SerializeField, Range(0f, 1f)] private float incapacitatedBrightnessMultiplier = 0.45f;

    [Header("AI Movement")]
    [SerializeField, Min(0f)] private float aiApproachZone = 5f;
    [SerializeField, Min(0f)] private float aiRetreatZone = 2f;
    [SerializeField, Min(0f)] private float aiMovementZoneHysteresis = 0.35f;
    [SerializeField, Min(0f)] private float aiMovementSmoothing = 10f;
    [SerializeField, Min(0f)] private float aiFollowActiveCharacterStopDistance = 1.1f;
    [SerializeField] private Color aiApproachZoneGizmoColor = new Color(0.2f, 0.7f, 1f, 0.9f);
    [SerializeField] private Color aiRetreatZoneGizmoColor = new Color(1f, 0.35f, 0.15f, 0.9f);

    [Header("Откаты способностей")]
    [SerializeField, Min(0f)] private float firstAbilityCooldownTime = 3f;
    [SerializeField, Min(0f)] private float secondAbilityCooldownTime = 5f;
    [SerializeField, Min(0f)] private float thirdAbilityCooldownTime = 8f;

    private static Sprite shieldVisualSprite;
    private static Sprite healthBarSprite;

    private Rigidbody2D characterRigidbody;
    private Collider2D[] characterColliders;
    private bool[] savedCharacterColliderTriggerStates;
    private SpriteRenderer[] spriteRenderers;
    private Color[] defaultSpriteColors;
    private Transform[] spriteTransforms;
    private Quaternion[] defaultSpriteLocalRotations;
    private SpriteRenderer shieldVisualRenderer;
    private SpriteRenderer attackBonusStatusRenderer;
    private SpriteRenderer defenceBonusStatusRenderer;
    private SpriteRenderer speedBonusStatusRenderer;
    private Transform healthBarRoot;
    private SpriteRenderer healthBarBackgroundRenderer;
    private SpriteRenderer healthBarFillRenderer;
    private Transform abilityCooldownRoot;
    private SpriteRenderer[] abilityCooldownBackgroundRenderers;
    private SpriteRenderer[] abilityCooldownFillRenderers;
    private Vector2 movementInput;
    private Vector2 smoothedAiMovementInput;
    private Vector2 lastMovementInputDirection = Vector2.right;
    private Vector2 damageKnockbackVelocity;
    private Coroutine damageResistanceCoroutine;
    private Coroutine movementSpeedMultiplierCoroutine;
    private Coroutine damageBonusCoroutine;
    private Coroutine shieldCoroutine;
    private Coroutine characterFlashCoroutine;
    private Coroutine damageInvulnerabilityCoroutine;
    private float firstAbilityNextUseTime;
    private float secondAbilityNextUseTime;
    private float thirdAbilityNextUseTime;
    private float currentDamageResistancePercent;
    private float currentMovementSpeedMultiplier = 1f;
    private float currentDamageBonusPercent;
    private float currentShieldAmount;
    private AiMovementMode aiMovementMode;
    private bool isDamageInvulnerable;
    private bool isIgnoringIncomingAttacks;

    public PlayerCharacterControlState ControlState => controlState;
    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public float AttackSpeed => attackSpeed;
    public float Damage => Mathf.Max(0f, damage * (1f + currentDamageBonusPercent / 100f));
    public float MovementSpeed => movementSpeed * currentMovementSpeedMultiplier;
    public float FirstAbilityCooldownTime => firstAbilityCooldownTime;
    public float SecondAbilityCooldownTime => secondAbilityCooldownTime;
    public float ThirdAbilityCooldownTime => thirdAbilityCooldownTime;
    public float CurrentDamageResistancePercent => currentDamageResistancePercent;
    public float CurrentDamageBonusPercent => currentDamageBonusPercent;
    public float CurrentShieldAmount => currentShieldAmount;
    public float DamageInvulnerabilityDuration => damageInvulnerabilityDuration;
    public bool IsDamageInvulnerable => isDamageInvulnerable;
    public bool IsIgnoringIncomingAttacks => isIgnoringIncomingAttacks;
    public bool IsPlayerControlled => controlState == PlayerCharacterControlState.PlayerControlled;
    public bool IsAiControlled => controlState == PlayerCharacterControlState.AiControlled;
    public bool IsAlive => currentHealth > 0f;
    public bool IsIncapacitated => !IsAlive;
    protected Rigidbody2D CharacterRigidbody => characterRigidbody;
    protected Vector2 CurrentMovementInput => movementInput;
    protected Vector2 LastMovementInputDirection => lastMovementInputDirection;

    /// <summary>
    /// Проверяет, может ли атака персонажа игрока наносить урон цели
    /// </summary>
    public static bool CanPlayerAttackDamageTarget(IDamageable target)
    {
        return target != null && target.IsAlive && !(target is PlayerCharacterTemplate);
    }

    protected virtual void Awake()
    {
        characterRigidbody = GetComponent<Rigidbody2D>();

        characterRigidbody.gravityScale = 0f;
        characterRigidbody.freezeRotation = true;
        CacheCharacterColliders();
        RegisterPlayerCharacterCollisionIgnores();

        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        CreateShieldVisual();
        CreateAttackBonusStatusVisual();
        CreateDefenceBonusStatusVisual();
        CreateSpeedBonusStatusVisual();
        CreateHealthBarVisual();
        CreateAbilityCooldownVisual();
        CacheSpriteRenderers();
        UpdateShieldVisual();
        UpdateAttackBonusStatusVisual();
        UpdateDefenceBonusStatusVisual();
        UpdateSpeedBonusStatusVisual();
        UpdateHealthBarVisual();
        UpdateAbilityCooldownVisual();

        if (!IsAlive)
        {
            BecomeIncapacitated();
        }
    }

    protected virtual void OnDestroy()
    {
        ActivePlayerCharacters.Remove(this);
    }

    protected virtual void Update()
    {
        UpdateAbilityCooldownVisual();

        if (!IsAlive)
        {
            movementInput = Vector2.zero;
            return;
        }

        if (IsAiControlled)
        {
            movementInput = Vector2.zero;
            UpdateAiControl();
            return;
        }

        ReadMovementInput();
        ReadAbilityInput();
        ReadCharacterSpecificInput();
    }

    protected virtual void FixedUpdate()
    {
        if (IsAiControlled)
        {
            FixedUpdateAiControl();
        }

        MoveCharacter();
    }

    /// <summary>
    /// Меняет состояние управления персонажем (ИИ/Игрок)
    /// </summary>
    public void SetControlState(PlayerCharacterControlState newControlState)
    {
        if (controlState == newControlState)
        {
            return;
        }

        PlayerCharacterControlState previousControlState = controlState;
        controlState = newControlState;
        movementInput = Vector2.zero;
        smoothedAiMovementInput = Vector2.zero;
        aiMovementMode = AiMovementMode.Idle;

        OnControlStateChanged(previousControlState, controlState);
    }

    /// <summary>
    /// Считывает направление движения с WASD через стандартные оси Unity
    /// </summary>
    private void ReadMovementInput()
    {
        movementInput = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical")
        ).normalized;

        if (movementInput.sqrMagnitude > 0.0001f)
        {
            lastMovementInputDirection = movementInput;
        }
    }

    /// <summary>
    /// Считывает нажатия кнопок способностей
    /// </summary>
    private void ReadAbilityInput()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
        {
            TryUseFirstAbility();
        }

        if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
        {
            TryUseSecondAbility();
        }

        if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
        {
            TryUseThirdAbility();
        }
    }

    /// <summary>
    /// Считывает ввод, который нужен конкретному наследнику персонажа
    /// </summary>
    protected virtual void ReadCharacterSpecificInput()
    {
    }

    /// <summary>
    /// Заглушка логики ИИ
    /// </summary>
    protected virtual void UpdateAiControl()
    {
    }

    /// <summary>
    /// Заглушка физики ИИ
    /// </summary>
    protected virtual void FixedUpdateAiControl()
    {
        SetAiMovementInput(GetAiMovementInput());
    }

    /// <summary>
    /// Выбирает направление движения персонажа под управлением ии
    /// </summary>
    private Vector2 GetAiMovementInput()
    {
        if (!IsAlive)
        {
            aiMovementMode = AiMovementMode.Idle;
            return Vector2.zero;
        }

        if (Input.GetKey(KeyCode.Space))
        {
            PlayerCharacterTemplate activeCharacter = FindActivePlayerControlledCharacter();

            if (activeCharacter != null)
            {
                aiMovementMode = AiMovementMode.FollowActiveCharacter;
                return GetDirectionToPosition(
                    activeCharacter.transform.position,
                    aiFollowActiveCharacterStopDistance);
            }

            aiMovementMode = AiMovementMode.Idle;
        }
        else if (aiMovementMode == AiMovementMode.FollowActiveCharacter)
        {
            aiMovementMode = AiMovementMode.Idle;
        }

        EnemyTemplate closestEnemy = FindClosestAliveEnemyInScene();

        if (closestEnemy == null)
        {
            aiMovementMode = AiMovementMode.Idle;
            return Vector2.zero;
        }

        Vector2 directionToEnemy = (Vector2)closestEnemy.transform.position - (Vector2)transform.position;
        float distanceToEnemy = directionToEnemy.magnitude;
        float retreatZone = Mathf.Max(0f, aiRetreatZone);
        float approachZone = Mathf.Max(retreatZone, aiApproachZone);
        float hysteresis = Mathf.Max(0f, aiMovementZoneHysteresis);
        float retreatStopDistance = Mathf.Min(approachZone, retreatZone + hysteresis);
        float approachStopDistance = Mathf.Max(retreatZone, approachZone - hysteresis);

        if (aiMovementMode == AiMovementMode.RetreatFromEnemy
            && distanceToEnemy >= retreatStopDistance)
        {
            aiMovementMode = AiMovementMode.Idle;
        }

        if (aiMovementMode == AiMovementMode.ApproachEnemy
            && distanceToEnemy <= approachStopDistance)
        {
            aiMovementMode = AiMovementMode.Idle;
        }

        if (aiMovementMode == AiMovementMode.Idle)
        {
            if (distanceToEnemy < retreatZone)
            {
                aiMovementMode = AiMovementMode.RetreatFromEnemy;
            }
            else if (distanceToEnemy > approachZone)
            {
                aiMovementMode = AiMovementMode.ApproachEnemy;
            }
        }

        if (directionToEnemy.sqrMagnitude <= MinAiMovementSqrMagnitude)
        {
            return Vector2.zero;
        }

        if (aiMovementMode == AiMovementMode.RetreatFromEnemy)
        {
            return -directionToEnemy.normalized;
        }

        if (aiMovementMode == AiMovementMode.ApproachEnemy)
        {
            return directionToEnemy.normalized;
        }

        return Vector2.zero;
    }

    private void SetAiMovementInput(Vector2 newMovementInput)
    {
        Vector2 targetMovementInput = newMovementInput.sqrMagnitude > MinAiMovementSqrMagnitude
            ? Vector2.ClampMagnitude(newMovementInput, 1f)
            : Vector2.zero;
        float smoothing = Mathf.Max(0f, aiMovementSmoothing);

        smoothedAiMovementInput = smoothing > 0f
            ? Vector2.MoveTowards(
                smoothedAiMovementInput,
                targetMovementInput,
                smoothing * Time.fixedDeltaTime)
            : targetMovementInput;

        movementInput = smoothedAiMovementInput.sqrMagnitude > MinAiMovementSqrMagnitude
            ? Vector2.ClampMagnitude(smoothedAiMovementInput, 1f)
            : Vector2.zero;

        if (movementInput.sqrMagnitude > MinAiMovementSqrMagnitude)
        {
            lastMovementInputDirection = movementInput.normalized;
        }
    }

    private Vector2 GetDirectionToPosition(Vector2 targetPosition, float stopDistance = 0f)
    {
        Vector2 direction = targetPosition - (Vector2)transform.position;
        float stopDistanceSqr = Mathf.Max(0f, stopDistance) * Mathf.Max(0f, stopDistance);
        float distance = direction.magnitude;

        if (direction.sqrMagnitude <= Mathf.Max(MinAiMovementSqrMagnitude, stopDistanceSqr))
        {
            return Vector2.zero;
        }

        float slowdownDistance = Mathf.Max(0.01f, stopDistance);
        float speedScale = Mathf.Clamp01((distance - stopDistance) / slowdownDistance);

        return direction.normalized * speedScale;
    }

    private PlayerCharacterTemplate FindActivePlayerControlledCharacter()
    {
        ActivePlayerCharacters.RemoveAll(character => character == null);

        for (int i = 0; i < ActivePlayerCharacters.Count; i++)
        {
            PlayerCharacterTemplate character = ActivePlayerCharacters[i];

            if (character != null && character != this && character.IsAlive && character.IsPlayerControlled)
            {
                return character;
            }
        }

        return null;
    }

    private EnemyTemplate FindClosestAliveEnemyInScene()
    {
        EnemyTemplate[] enemies = FindObjectsByType<EnemyTemplate>(FindObjectsSortMode.None);
        EnemyTemplate closestEnemy = null;
        float closestSqrDistance = float.PositiveInfinity;

        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyTemplate enemy = enemies[i];

            if (enemy == null || !enemy.IsAlive)
            {
                continue;
            }

            float sqrDistance = ((Vector2)enemy.transform.position - (Vector2)transform.position).sqrMagnitude;

            if (sqrDistance >= closestSqrDistance)
            {
                continue;
            }

            closestSqrDistance = sqrDistance;
            closestEnemy = enemy;
        }

        return closestEnemy;
    }

    protected void DrawAiMovementGizmos()
    {
        Gizmos.color = aiApproachZoneGizmoColor;
        Gizmos.DrawWireSphere(transform.position, aiApproachZone);

        Gizmos.color = aiRetreatZoneGizmoColor;
        Gizmos.DrawWireSphere(transform.position, aiRetreatZone);
    }

    /// <summary>
    /// Вызывается после смены состояния управления персонажем
    /// </summary>
    protected virtual void OnControlStateChanged(
        PlayerCharacterControlState previousControlState,
        PlayerCharacterControlState newControlState)
    {
    }

    /// <summary>
    /// Передвигает персонажа с постоянной скоростью
    /// </summary>
    private void MoveCharacter()
    {
        Vector2 inputVelocity = IsAlive && !HasActiveDamageKnockback()
            ? movementInput * MovementSpeed
            : Vector2.zero;
        Vector2 nextPosition = characterRigidbody.position + (inputVelocity + damageKnockbackVelocity) * Time.fixedDeltaTime;

        characterRigidbody.MovePosition(nextPosition);
        characterRigidbody.velocity = Vector2.zero;

        UpdateDamageKnockbackVelocity();
    }

    /// <summary>
    /// Проверяет двигает ли персонажа сейчас отталкивание
    /// </summary>
    private bool HasActiveDamageKnockback()
    {
        return damageKnockbackVelocity.sqrMagnitude > MinDamageKnockbackSqrMagnitude;
    }

    /// <summary>
    /// Постепенно гасит скорость отталкивания чтобы она не копилась в Rigidbody2D (костыль)
    /// </summary>
    private void UpdateDamageKnockbackVelocity()
    {
        if (!HasActiveDamageKnockback())
        {
            damageKnockbackVelocity = Vector2.zero;
            return;
        }

        damageKnockbackVelocity = Vector2.MoveTowards(
            damageKnockbackVelocity,
            Vector2.zero,
            DamageKnockbackSlowdown * Time.fixedDeltaTime);
    }

    /// <summary>
    /// Запоминает коллайдеры героя чтобы отключить столкновения с другими героями и только с ними
    /// </summary>
    private void CacheCharacterColliders()
    {
        characterColliders = GetComponentsInChildren<Collider2D>();
    }

    /// <summary>
    /// Отключает столкновения между героями
    /// </summary>
    private void RegisterPlayerCharacterCollisionIgnores()
    {
        ActivePlayerCharacters.RemoveAll(character => character == null);

        for (int i = 0; i < ActivePlayerCharacters.Count; i++)
        {
            IgnoreCollisionsWithCharacter(ActivePlayerCharacters[i], true);
        }

        if (!ActivePlayerCharacters.Contains(this))
        {
            ActivePlayerCharacters.Add(this);
        }
    }

    /// <summary>
    /// Включает или выключает столкновения между коллайдерами героя и врагов
    /// </summary>
    private void IgnoreCollisionsWithCharacter(PlayerCharacterTemplate otherCharacter, bool shouldIgnoreCollision)
    {
        if (otherCharacter == null || otherCharacter == this)
        {
            return;
        }

        if (characterColliders == null)
        {
            CacheCharacterColliders();
        }

        if (otherCharacter.characterColliders == null)
        {
            otherCharacter.CacheCharacterColliders();
        }

        for (int i = 0; i < characterColliders.Length; i++)
        {
            Collider2D ownCollider = characterColliders[i];

            if (ownCollider == null)
            {
                continue;
            }

            for (int j = 0; j < otherCharacter.characterColliders.Length; j++)
            {
                Collider2D otherCollider = otherCharacter.characterColliders[j];

                if (otherCollider == null || ReferenceEquals(ownCollider, otherCollider))
                {
                    continue;
                }

                Physics2D.IgnoreCollision(ownCollider, otherCollider, shouldIgnoreCollision);
            }
        }
    }

    /// <summary>
    /// Возвращает задержку между атаками на основе скорости атаки
    /// </summary>
    protected float GetAttackDelayFromAttackSpeed()
    {
        return 1f / Mathf.Max(AttackSpeed, 0.01f);
    }

    /// <summary>
    /// Возвращает направление от персонажа к курсору (+обновляет запасное направление)
    /// </summary>
    protected Vector2 GetDirectionToCursor(ref Vector2 cachedDirection)
    {
        Camera mainCamera = Camera.main;

        if (mainCamera == null)
        {
            return GetNormalizedDirectionOrFallback(cachedDirection, Vector2.right);
        }

        Vector3 mouseWorldPosition = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 direction = (Vector2)mouseWorldPosition - (Vector2)transform.position;

        if (direction.sqrMagnitude <= MinDamageKnockbackSqrMagnitude)
        {
            return GetNormalizedDirectionOrFallback(cachedDirection, Vector2.right);
        }

        cachedDirection = direction.normalized;
        return cachedDirection;
    }

    /// <summary>
    /// Возвращает позицию курсора/персонажа если камеры нет
    /// </summary>
    protected Vector2 GetCursorWorldPosition()
    {
        Camera mainCamera = Camera.main;

        if (mainCamera == null)
        {
            return transform.position;
        }

        Vector3 mouseWorldPosition = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        return new Vector2(mouseWorldPosition.x, mouseWorldPosition.y);
    }

    /// <summary>
    /// Возвращает направление рывка по направлению движения или берёт последнеее направление
    /// </summary>
    protected Vector2 GetMovementDashDirection()
    {
        if (CurrentMovementInput.sqrMagnitude > MinDamageKnockbackSqrMagnitude)
        {
            return CurrentMovementInput.normalized;
        }

        return GetNormalizedDirectionOrFallback(LastMovementInputDirection, Vector2.right);
    }

    /// <summary>
    /// Возвращает направление от персонажа к точке
    /// </summary>
    protected Vector2 GetDirectionFromSelfTo(Vector2 targetPosition, Vector2 fallbackDirection)
    {
        Vector2 direction = targetPosition - (Vector2)transform.position;
        return GetNormalizedDirectionOrFallback(direction, fallbackDirection);
    }

    /// <summary>
    /// Нормализует направление (или возвращает запасное)
    /// </summary>
    protected Vector2 GetNormalizedDirectionOrFallback(Vector2 direction, Vector2 fallbackDirection)
    {
        if (direction.sqrMagnitude > MinDamageKnockbackSqrMagnitude)
        {
            return direction.normalized;
        }

        if (fallbackDirection.sqrMagnitude > MinDamageKnockbackSqrMagnitude)
        {
            return fallbackDirection.normalized;
        }

        return Vector2.right;
    }

    /// <summary>
    /// Поворачивает направление на заданное количество градусов (чтобы вторая способность Ranger была по красоте и легко настраивалась)
    /// </summary>
    protected Vector2 RotateDirection(Vector2 direction, float angleDegrees)
    {
        float angleRadians = angleDegrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(angleRadians);
        float cos = Mathf.Cos(angleRadians);

        return GetNormalizedDirectionOrFallback(
            new Vector2(
                direction.x * cos - direction.y * sin,
                direction.x * sin + direction.y * cos),
            direction);
    }

    /// <summary>
    /// Находит ближайшего живого противника в радиусе
    /// </summary>
    protected EnemyTemplate FindClosestAliveEnemy(float searchRadius, LayerMask searchLayers)
    {
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(transform.position, searchRadius, searchLayers);
        HashSet<EnemyTemplate> checkedEnemies = new HashSet<EnemyTemplate>();
        EnemyTemplate closestEnemy = null;
        float closestSqrDistance = float.PositiveInfinity;

        foreach (Collider2D hitCollider in hitColliders)
        {
            EnemyTemplate enemy = hitCollider.GetComponentInParent<EnemyTemplate>();

            if (enemy == null || !enemy.IsAlive || !checkedEnemies.Add(enemy))
            {
                continue;
            }

            float sqrDistance = ((Vector2)enemy.transform.position - (Vector2)transform.position).sqrMagnitude;

            if (sqrDistance >= closestSqrDistance)
            {
                continue;
            }

            closestSqrDistance = sqrDistance;
            closestEnemy = enemy;
        }

        return closestEnemy;
    }

    /// <summary>
    /// Назначает следующее время действия со случайным интервалом
    /// </summary>
    protected void ScheduleNextRandomActionTime(ref float nextActionTime, float minInterval, float maxInterval)
    {
        float finalMinInterval = Mathf.Min(minInterval, maxInterval);
        float finalMaxInterval = Mathf.Max(minInterval, maxInterval);
        nextActionTime = Time.time + UnityEngine.Random.Range(finalMinInterval, finalMaxInterval);
    }

    /// <summary>
    /// Перебор уникальных живых целей которым персонажи игрока могут нанести урон 
    /// </summary>
    protected int ForEachUniqueDamageableInCircle(
        Vector2 circleCenter,
        float circleRadius,
        LayerMask circleLayers,
        Action<IDamageable, Collider2D> handleTarget)
    {
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(circleCenter, circleRadius, circleLayers);
        HashSet<IDamageable> handledTargets = new HashSet<IDamageable>();
        int handledTargetsCount = 0;

        foreach (Collider2D hitCollider in hitColliders)
        {
            IDamageable target = hitCollider.GetComponentInParent<IDamageable>();

            if (!CanPlayerAttackDamageTarget(target) || !handledTargets.Add(target))
            {
                continue;
            }

            handleTarget?.Invoke(target, hitCollider);
            handledTargetsCount++;
        }

        return handledTargetsCount;
    }

    /// <summary>
    /// Перебирает уникальных живых противников в радиусе (круге)
    /// </summary>
    protected int ForEachUniqueEnemyInCircle(
        Vector2 circleCenter,
        float circleRadius,
        LayerMask circleLayers,
        Action<EnemyTemplate, Collider2D> handleEnemy)
    {
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(circleCenter, circleRadius, circleLayers);
        HashSet<EnemyTemplate> handledEnemies = new HashSet<EnemyTemplate>();
        int handledEnemiesCount = 0;

        foreach (Collider2D hitCollider in hitColliders)
        {
            EnemyTemplate enemy = hitCollider.GetComponentInParent<EnemyTemplate>();

            if (enemy == null || !enemy.IsAlive || !handledEnemies.Add(enemy))
            {
                continue;
            }

            handleEnemy?.Invoke(enemy, hitCollider);
            handledEnemiesCount++;
        }

        return handledEnemiesCount;
    }

    /// <summary>
    /// Создание круго через LineRenderer для визуализации атак и способностей
    /// </summary>
    protected GameObject CreateCircleVisual(
        string circleName,
        Vector2 circleCenter,
        float circleRadius,
        int circleSegments,
        float circleWidth,
        Color circleColor,
        int sortingOrder = 10)
    {
        GameObject circleObject = new GameObject(circleName);
        circleObject.transform.position = circleCenter;

        int finalSegments = Mathf.Max(8, circleSegments);
        float finalRadius = Mathf.Max(0f, circleRadius);
        LineRenderer circleRenderer = circleObject.AddComponent<LineRenderer>();
        circleRenderer.useWorldSpace = false;
        circleRenderer.loop = true;
        circleRenderer.positionCount = finalSegments;
        circleRenderer.startWidth = circleWidth;
        circleRenderer.endWidth = circleWidth;
        circleRenderer.startColor = circleColor;
        circleRenderer.endColor = circleColor;
        circleRenderer.sortingOrder = sortingOrder;
        circleRenderer.sharedMaterial = GetCircleVisualMaterial();

        for (int i = 0; i < finalSegments; i++)
        {
            float angle = (float)i / finalSegments * Mathf.PI * 2f;
            Vector3 point = new Vector3(
                Mathf.Cos(angle) * finalRadius,
                Mathf.Sin(angle) * finalRadius,
                0f);

            circleRenderer.SetPosition(i, point);
        }

        return circleObject;
    }

    /// <summary>
    /// Общий материал для кругов для визуализации
    /// </summary>
    private static Material GetCircleVisualMaterial()
    {
        if (circleVisualMaterial != null)
        {
            return circleVisualMaterial;
        }

        Shader spriteShader = Shader.Find("Sprites/Default");

        if (spriteShader == null)
        {
            return null;
        }

        circleVisualMaterial = new Material(spriteShader);
        return circleVisualMaterial;
    }

    /// <summary>
    /// Запоминает исходные состояния коллайдеров персонажа
    /// </summary>
    protected void SaveCharacterColliderTriggerStates()
    {
        if (characterColliders == null)
        {
            CacheCharacterColliders();
        }

        savedCharacterColliderTriggerStates = new bool[characterColliders.Length];

        for (int i = 0; i < characterColliders.Length; i++)
        {
            savedCharacterColliderTriggerStates[i] = characterColliders[i] != null && characterColliders[i].isTrigger;
        }
    }

    /// <summary>
    /// Меняет все коллайдеры персонажа на триггеры или возвращает их как было
    /// </summary>
    protected void SetCharacterCollidersTriggerState(bool isTrigger)
    {
        if (characterColliders == null)
        {
            CacheCharacterColliders();
        }

        foreach (Collider2D characterCollider in characterColliders)
        {
            if (characterCollider != null)
            {
                characterCollider.isTrigger = isTrigger;
            }
        }
    }

    /// <summary>
    /// Возвращает коллайдерам персонажа сохраненные состояния триггеров
    /// </summary>
    protected void RestoreCharacterColliderTriggerStates()
    {
        if (characterColliders == null || savedCharacterColliderTriggerStates == null)
        {
            return;
        }

        int collidersCount = Mathf.Min(characterColliders.Length, savedCharacterColliderTriggerStates.Length);

        for (int i = 0; i < collidersCount; i++)
        {
            if (characterColliders[i] != null)
            {
                characterColliders[i].isTrigger = savedCharacterColliderTriggerStates[i];
            }
        }
    }

    /// <summary>
    /// Проверяет возможность применить первую способность
    /// </summary>
    private void TryUseFirstAbility()
    {
        if (!IsAlive || !CanUseAbility(firstAbilityNextUseTime, "первая способность"))
        {
            return;
        }

        UseFirstAbility();
        firstAbilityNextUseTime = Time.time + firstAbilityCooldownTime;
        UpdateAbilityCooldownVisual();
    }

    /// <summary>
    /// Проверяет возможность применить вторую способность
    /// </summary>
    private void TryUseSecondAbility()
    {
        if (!IsAlive || !CanUseAbility(secondAbilityNextUseTime, "вторая способность"))
        {
            return;
        }

        UseSecondAbility();
        secondAbilityNextUseTime = Time.time + secondAbilityCooldownTime;
        UpdateAbilityCooldownVisual();
    }

    /// <summary>
    /// Проверяет возможность применить третью способность
    /// </summary>
    private void TryUseThirdAbility()
    {
        if (!IsAlive || !CanUseAbility(thirdAbilityNextUseTime, "третья способность"))
        {
            return;
        }

        UseThirdAbility();
        thirdAbilityNextUseTime = Time.time + thirdAbilityCooldownTime;
        UpdateAbilityCooldownVisual();
    }

    /// <summary>
    /// Проверяет, закончился ли откат способности
    /// </summary>
    private bool CanUseAbility(float nextUseTime, string abilityName)
    {
        if (Time.time >= nextUseTime)
        {
            return true;
        }

        float remainingCooldownTime = nextUseTime - Time.time;
        Debug.Log($"{name} {abilityName} на откате еще {remainingCooldownTime:0.0} сек");
        return false;
    }

    /// <summary>
    /// Заготовка первой способности
    /// </summary>
    protected virtual void UseFirstAbility()
    {
        Debug.Log($"{name} применена первая способность");
    }

    /// <summary>
    /// Заготовка второй способности
    /// </summary>
    protected virtual void UseSecondAbility()
    {
        Debug.Log($"{name} применена вторая способность");
    }

    /// <summary>
    /// Заготовка третьей способности
    /// </summary>
    protected virtual void UseThirdAbility()
    {
        Debug.Log($"{name} применена третья способность");
    }

    /// <summary>
    /// Включает или выключает полное игнорирование входящих атак
    /// </summary>
    protected void SetIncomingAttacksIgnored(bool shouldIgnoreIncomingAttacks)
    {
        isIgnoringIncomingAttacks = shouldIgnoreIncomingAttacks;
    }

    /// <summary>
    /// Получение урона
    /// </summary>
    public void TakeDamage(float damageAmount)
    {
        TakeDamage(damageAmount, Vector2.zero, 0f);
    }

    /// <summary>
    /// Изменяет здоровье персонажа и применяет визуальную и физическую реакции на удар
    /// </summary>
    public void TakeDamage(float damageAmount, Vector2 knockbackDirection, float knockbackForce)
    {
        if (damageAmount <= 0f || !IsAlive || isIgnoringIncomingAttacks || isDamageInvulnerable)
        {
            return;
        }

        ApplyDamageFeedback(knockbackDirection, knockbackForce);
        StartDamageInvulnerability();

        if (currentShieldAmount > 0f)
        {
            AbsorbDamageWithShield(damageAmount);
            return;
        }

        float finalDamageAmount = GetDamageAfterResistance(damageAmount);
        currentHealth = Mathf.Max(currentHealth - finalDamageAmount, 0f);
        UpdateHealthBarVisual();

        if (!IsAlive)
        {
            ApplyDeathKnockback(knockbackDirection, knockbackForce);
            BecomeIncapacitated();
            return;
        }

    }

    /// <summary>
    /// Запускает отталкивание и короткую красную вспышку после получения урона
    /// </summary>
    private void ApplyDamageFeedback(Vector2 knockbackDirection, float knockbackForce)
    {
        ApplyDamageKnockback(knockbackDirection, knockbackForce);
        StartDamageFlash();
    }

    private void ApplyDeathKnockback(Vector2 knockbackDirection, float knockbackForce)
    {
        ApplyDamageKnockback(knockbackDirection, knockbackForce, deathKnockbackForceMultiplier);
    }

    /// <summary>
    /// Отталкивает персонажа в направлении удара
    /// </summary>
    private void ApplyDamageKnockback(
        Vector2 knockbackDirection,
        float knockbackForce,
        float forceMultiplier = 1f)
    {
        if (characterRigidbody == null || knockbackDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        float finalKnockbackForce = knockbackForce > 0f ? knockbackForce : damageKnockbackForce;

        if (finalKnockbackForce <= 0f)
        {
            return;
        }

        characterRigidbody.velocity = Vector2.zero;
        damageKnockbackVelocity = knockbackDirection.normalized
            * finalKnockbackForce
            * Mathf.Max(1f, forceMultiplier);
    }

    /// <summary>
    /// Сохраняет все спрайты персонажа чтобы можно было быстро менять их цвет при ударе
    /// </summary>
    private void CacheSpriteRenderers()
    {
        SpriteRenderer[] allSpriteRenderers = GetComponentsInChildren<SpriteRenderer>();
        int visibleSpriteCount = 0;

        for (int i = 0; i < allSpriteRenderers.Length; i++)
        {
            if (IsCharacterBodySpriteRenderer(allSpriteRenderers[i]))
            {
                visibleSpriteCount++;
            }
        }

        spriteRenderers = new SpriteRenderer[visibleSpriteCount];
        defaultSpriteColors = new Color[spriteRenderers.Length];
        List<Transform> visibleSpriteTransforms = new List<Transform>();
        List<Quaternion> visibleSpriteRotations = new List<Quaternion>();
        int spriteIndex = 0;

        for (int i = 0; i < allSpriteRenderers.Length; i++)
        {
            if (!IsCharacterBodySpriteRenderer(allSpriteRenderers[i]))
            {
                continue;
            }

            spriteRenderers[spriteIndex] = allSpriteRenderers[i];
            defaultSpriteColors[spriteIndex] = allSpriteRenderers[i].color;

            Transform spriteTransform = allSpriteRenderers[i].transform;

            if (!visibleSpriteTransforms.Contains(spriteTransform))
            {
                visibleSpriteTransforms.Add(spriteTransform);
                visibleSpriteRotations.Add(spriteTransform.localRotation);
            }

            spriteIndex++;
        }

        spriteTransforms = visibleSpriteTransforms.ToArray();
        defaultSpriteLocalRotations = visibleSpriteRotations.ToArray();
    }

    /// <summary>
    /// Проверяет относится ли спрайт к телу персонажа а не интерфейсу
    /// </summary>
    protected bool IsCharacterBodySpriteRenderer(SpriteRenderer spriteRenderer)
    {
        return spriteRenderer != null
            && !ReferenceEquals(spriteRenderer, shieldVisualRenderer)
            && !ReferenceEquals(spriteRenderer, attackBonusStatusRenderer)
            && !ReferenceEquals(spriteRenderer, defenceBonusStatusRenderer)
            && !ReferenceEquals(spriteRenderer, speedBonusStatusRenderer)
            && !ReferenceEquals(spriteRenderer, healthBarBackgroundRenderer)
            && !ReferenceEquals(spriteRenderer, healthBarFillRenderer)
            && !ContainsSpriteRenderer(abilityCooldownBackgroundRenderers, spriteRenderer)
            && !ContainsSpriteRenderer(abilityCooldownFillRenderers, spriteRenderer);
    }

    /// <summary>
    /// Проверяет не является ли спрайт частью интерфейса
    /// </summary>
    private bool ContainsSpriteRenderer(SpriteRenderer[] renderers, SpriteRenderer targetRenderer)
    {
        if (renderers == null || targetRenderer == null)
        {
            return false;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            if (ReferenceEquals(renderers[i], targetRenderer))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Перезапускает вспышку урона если персонажа ударили несколько раз подряд
    /// </summary>
    private void StartDamageFlash()
    {
        StartCharacterFlash(damageFlashColor, damageFlashDuration);
    }

    /// <summary>
    /// Запускает короткую зеленую вспышку после лечения
    /// </summary>
    private void StartHealingFlash()
    {
        StartCharacterFlash(healingFlashColor, healingFlashDuration);
    }

    /// <summary>
    /// Запускает короткое окрашивание спрайтов персонажа указанным цветом
    /// </summary>
    protected void StartCharacterFlash(Color flashColor, float flashDuration)
    {
        if (flashDuration <= 0f)
        {
            return;
        }

        if (characterFlashCoroutine != null)
        {
            StopCoroutine(characterFlashCoroutine);
        }

        characterFlashCoroutine = StartCoroutine(CharacterFlashCoroutine(flashColor, flashDuration));
    }

    /// <summary>
    /// На мгновение окрашивает спрайты и возвращает исходные цвета
    /// </summary>
    private IEnumerator CharacterFlashCoroutine(Color flashColor, float flashDuration)
    {
        if (spriteRenderers == null || defaultSpriteColors == null)
        {
            CacheSpriteRenderers();
        }

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null)
            {
                spriteRenderers[i].color = flashColor;
            }
        }

        yield return new WaitForSeconds(flashDuration);

        RestoreCharacterBodyColors();

        characterFlashCoroutine = null;
    }

    /// <summary>
    /// Возвращает спрайтам обычный цвет или цвет недееспособности если персонаж лежит без здоровья
    /// </summary>
    private void RestoreCharacterBodyColors()
    {
        if (spriteRenderers == null || defaultSpriteColors == null)
        {
            return;
        }

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] == null)
            {
                continue;
            }

            spriteRenderers[i].color = IsAlive
                ? defaultSpriteColors[i]
                : GetIncapacitatedColor(defaultSpriteColors[i]);
        }
    }

    /// <summary>
    /// Возвращает затемненную версию исходного цвета
    /// </summary>
    private Color GetIncapacitatedColor(Color sourceColor)
    {
        return new Color(
            sourceColor.r * incapacitatedBrightnessMultiplier,
            sourceColor.g * incapacitatedBrightnessMultiplier,
            sourceColor.b * incapacitatedBrightnessMultiplier,
            sourceColor.a);
    }

    /// <summary>
    /// Переводит персонажа в недееспособное состояние
    /// </summary>
    private void BecomeIncapacitated()
    {
        movementInput = Vector2.zero;
        isIgnoringIncomingAttacks = false;

        if (characterFlashCoroutine != null)
        {
            StopCoroutine(characterFlashCoroutine);
            characterFlashCoroutine = null;
        }

        if (characterRigidbody != null)
        {
            characterRigidbody.velocity = Vector2.zero;
        }

        RestoreCharacterBodyColors();
        SetIncapacitatedSpriteRotation(true);
        UpdateAttackBonusStatusVisual();
        UpdateDefenceBonusStatusVisual();
        UpdateSpeedBonusStatusVisual();
    }

    /// <summary>
    /// Возвращает персонажа из недееспособного состояния
    /// </summary>
    private void RecoverFromIncapacitated()
    {
        SetIncapacitatedSpriteRotation(false);
        RestoreCharacterBodyColors();
        UpdateAttackBonusStatusVisual();
        UpdateDefenceBonusStatusVisual();
        UpdateSpeedBonusStatusVisual();
    }

    /// <summary>
    /// Поворачивает только визуальные спрайты персонажа не трогая коллайдер, полоску здоровья и UI
    /// </summary>
    private void SetIncapacitatedSpriteRotation(bool shouldLieDown)
    {
        if (spriteTransforms == null || defaultSpriteLocalRotations == null)
        {
            CacheSpriteRenderers();
        }

        if (spriteTransforms == null || defaultSpriteLocalRotations == null)
        {
            return;
        }

        int transformsCount = Mathf.Min(spriteTransforms.Length, defaultSpriteLocalRotations.Length);

        for (int i = 0; i < transformsCount; i++)
        {
            if (spriteTransforms[i] == null)
            {
                continue;
            }

            spriteTransforms[i].localRotation = shouldLieDown
                ? defaultSpriteLocalRotations[i] * Quaternion.Euler(0f, 0f, IncapacitatedSpriteRotationZ)
                : defaultSpriteLocalRotations[i];
        }
    }

    /// <summary>
    /// Даёт короткую неуязвимость после получения урона
    /// </summary>
    private void StartDamageInvulnerability()
    {
        if (damageInvulnerabilityDuration <= 0f)
        {
            return;
        }

        isDamageInvulnerable = true;

        if (damageInvulnerabilityCoroutine != null)
        {
            StopCoroutine(damageInvulnerabilityCoroutine);
        }

        damageInvulnerabilityCoroutine = StartCoroutine(DamageInvulnerabilityCoroutine());
    }

    /// <summary>
    /// Держит персонажа неуязвимым заданное время
    /// </summary>
    private IEnumerator DamageInvulnerabilityCoroutine()
    {
        yield return new WaitForSeconds(damageInvulnerabilityDuration);

        isDamageInvulnerable = false;
        damageInvulnerabilityCoroutine = null;
    }

    /// <summary>
    /// Поглощает входящий урон щитом и не переносит остаточный урон на здоровье
    /// </summary>
    private void AbsorbDamageWithShield(float damageAmount)
    {
        currentShieldAmount = Mathf.Max(currentShieldAmount - damageAmount, 0f);

        Debug.Log($"{name} щит поглотил {damageAmount:0.0} урона. Щит: {currentShieldAmount:0.0}");

        UpdateShieldVisual();

        if (currentShieldAmount > 0f)
        {
            return;
        }

        if (shieldCoroutine != null)
        {
            StopCoroutine(shieldCoroutine);
            shieldCoroutine = null;
        }

        Debug.Log($"{name} щит разрушен");
    }

    /// <summary>
    /// Временно дает персонажу процентное сопротивление урону
    /// </summary>
    public void ApplyDamageResistance(float resistancePercent, float duration)
    {
        if (duration <= 0f)
        {
            return;
        }

        if (damageResistanceCoroutine != null)
        {
            StopCoroutine(damageResistanceCoroutine);
        }

        damageResistanceCoroutine = StartCoroutine(ApplyDamageResistanceCoroutine(resistancePercent, duration));
    }

    /// <summary>
    /// Временно меняет скорость передвижения персонажа на множитель
    /// </summary>
    protected void ApplyMovementSpeedMultiplier(float speedMultiplier, float duration)
    {
        if (duration <= 0f || speedMultiplier <= 0f)
        {
            return;
        }

        if (movementSpeedMultiplierCoroutine != null)
        {
            StopCoroutine(movementSpeedMultiplierCoroutine);
        }

        movementSpeedMultiplierCoroutine = StartCoroutine(ApplyMovementSpeedMultiplierCoroutine(speedMultiplier, duration));
    }

    /// <summary>
    /// Временно усиливает урон персонажа на множитель
    /// </summary>
    public void ApplyDamageBonus(float damageBonusPercent, float duration)
    {
        if (duration <= 0f)
        {
            return;
        }

        if (damageBonusCoroutine != null)
        {
            StopCoroutine(damageBonusCoroutine);
        }

        damageBonusCoroutine = StartCoroutine(ApplyDamageBonusCoroutine(damageBonusPercent, duration));
    }

    /// <summary>
    /// Временно дает щит, который поглощает урон отдельно от здоровья
    /// </summary>
    public void ApplyShield(float shieldAmount, float duration)
    {
        if (duration <= 0f || shieldAmount <= 0f || !IsAlive)
        {
            return;
        }

        if (shieldCoroutine != null)
        {
            StopCoroutine(shieldCoroutine);
        }

        currentShieldAmount = shieldAmount;
        UpdateShieldVisual();
        shieldCoroutine = StartCoroutine(ApplyShieldCoroutine(duration));
    }

    /// <summary>
    /// Создает дочерний спрайт круга который показывается поверх персонажа при активном щите
    /// </summary>
    private void CreateShieldVisual()
    {
        if (shieldVisualRenderer != null)
        {
            return;
        }

        GameObject shieldVisualObject = new GameObject("ShieldVisual");
        shieldVisualObject.transform.SetParent(transform, false);
        shieldVisualObject.transform.localPosition = new Vector3(shieldVisualOffset.x, shieldVisualOffset.y, 0f);

        shieldVisualRenderer = shieldVisualObject.AddComponent<SpriteRenderer>();
        shieldVisualRenderer.sprite = GetShieldVisualSprite();
        shieldVisualRenderer.color = shieldVisualColor;
        RefreshShieldVisualTransform();
        RefreshShieldVisualSorting();

        shieldVisualObject.SetActive(false);
    }

    /// <summary>
    /// Возвращает общий круглый спрайт для отображения щита
    /// </summary>
    private static Sprite GetShieldVisualSprite()
    {
        if (shieldVisualSprite != null)
        {
            return shieldVisualSprite;
        }

        Texture2D texture = new Texture2D(ShieldVisualTextureSize, ShieldVisualTextureSize, TextureFormat.RGBA32, false)
        {
            name = "GeneratedShieldCircle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color[] pixels = new Color[ShieldVisualTextureSize * ShieldVisualTextureSize];
        float center = (ShieldVisualTextureSize - 1) * 0.5f;
        float radius = center;

        for (int y = 0; y < ShieldVisualTextureSize; y++)
        {
            for (int x = 0; x < ShieldVisualTextureSize; x++)
            {
                float xOffset = x - center;
                float yOffset = y - center;
                float distanceFromCenter = Mathf.Sqrt(xOffset * xOffset + yOffset * yOffset);
                float alpha = Mathf.Clamp01(radius - distanceFromCenter + 1f);
                pixels[y * ShieldVisualTextureSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        shieldVisualSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, ShieldVisualTextureSize, ShieldVisualTextureSize),
            new Vector2(0.5f, 0.5f),
            ShieldVisualTextureSize);
        shieldVisualSprite.name = "GeneratedShieldCircle";

        return shieldVisualSprite;
    }

    /// <summary>
    /// Обновляет видимость и параметры щита
    /// </summary>
    private void UpdateShieldVisual()
    {
        if (shieldVisualRenderer == null)
        {
            return;
        }

        bool shouldShowShield = currentShieldAmount > 0f;
        shieldVisualRenderer.gameObject.SetActive(shouldShowShield);

        if (!shouldShowShield)
        {
            return;
        }

        shieldVisualRenderer.color = shieldVisualColor;
        RefreshShieldVisualTransform();
        RefreshShieldVisualSorting();
    }

    /// <summary>
    /// Настраивает размер щита
    /// </summary>
    private void RefreshShieldVisualTransform()
    {
        if (shieldVisualRenderer == null)
        {
            return;
        }

        float visualDiameter = Mathf.Max(0f, shieldVisualDiameter);
        shieldVisualRenderer.transform.localPosition = new Vector3(shieldVisualOffset.x, shieldVisualOffset.y, 0f);
        shieldVisualRenderer.transform.localScale = new Vector3(visualDiameter, visualDiameter, 1f);
    }

    /// <summary>
    /// Поднимает круг щита поверх остальных спрайтов
    /// </summary>
    private void RefreshShieldVisualSorting()
    {
        if (shieldVisualRenderer == null)
        {
            return;
        }

        int highestSortingOrder = 0;
        int sortingLayerId = shieldVisualRenderer.sortingLayerID;

        if (spriteRenderers != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] == null || ReferenceEquals(spriteRenderers[i], shieldVisualRenderer))
                {
                    continue;
                }

                if (i == 0 || spriteRenderers[i].sortingOrder >= highestSortingOrder)
                {
                    highestSortingOrder = spriteRenderers[i].sortingOrder;
                    sortingLayerId = spriteRenderers[i].sortingLayerID;
                }
            }
        }

        shieldVisualRenderer.sortingLayerID = sortingLayerId;
        shieldVisualRenderer.sortingOrder = highestSortingOrder + shieldVisualSortingOrderOffset;
    }

    /// <summary>
    /// Создает дочерний спрайт статуса усиления урона
    /// </summary>
    private void CreateAttackBonusStatusVisual()
    {
        if (attackBonusStatusRenderer != null)
        {
            return;
        }

        GameObject attackBonusStatusObject = new GameObject("AttackBonusStatusVisual");
        attackBonusStatusObject.transform.SetParent(transform, false);

        attackBonusStatusRenderer = attackBonusStatusObject.AddComponent<SpriteRenderer>();
        attackBonusStatusRenderer.sprite = attackBonusStatusSprite;
        RefreshAttackBonusStatusVisualTransform();
        RefreshAttackBonusStatusVisualSorting();
        attackBonusStatusObject.SetActive(false);
    }

    /// <summary>
    /// Показывает или скрывает спрайт усиления урона над персонажем
    /// </summary>
    private void UpdateAttackBonusStatusVisual()
    {
        if (attackBonusStatusRenderer == null)
        {
            CreateAttackBonusStatusVisual();
        }

        if (attackBonusStatusRenderer == null)
        {
            return;
        }

        bool shouldShowStatus = IsAlive && currentDamageBonusPercent > 0f && attackBonusStatusSprite != null;
        attackBonusStatusRenderer.gameObject.SetActive(shouldShowStatus);

        if (!shouldShowStatus)
        {
            return;
        }

        attackBonusStatusRenderer.sprite = attackBonusStatusSprite;
        RefreshAttackBonusStatusVisualTransform();
        RefreshAttackBonusStatusVisualSorting();
    }

    /// <summary>
    /// Расставляет спрайт усиления урона над персонажем
    /// </summary>
    private void RefreshAttackBonusStatusVisualTransform()
    {
        if (attackBonusStatusRenderer == null)
        {
            return;
        }

        attackBonusStatusRenderer.transform.localPosition = new Vector3(
            attackBonusStatusOffset.x,
            attackBonusStatusOffset.y,
            0f);
        attackBonusStatusRenderer.transform.localScale = new Vector3(
            attackBonusStatusScale,
            attackBonusStatusScale,
            1f);
    }

    /// <summary>
    /// Поднимает спрайт усиления урона поверх тела и интерфейса персонажа
    /// </summary>
    private void RefreshAttackBonusStatusVisualSorting()
    {
        if (attackBonusStatusRenderer == null)
        {
            return;
        }

        int highestSortingOrder = 0;
        int sortingLayerId = attackBonusStatusRenderer.sortingLayerID;

        if (spriteRenderers != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] == null)
                {
                    continue;
                }

                if (i == 0 || spriteRenderers[i].sortingOrder >= highestSortingOrder)
                {
                    highestSortingOrder = spriteRenderers[i].sortingOrder;
                    sortingLayerId = spriteRenderers[i].sortingLayerID;
                }
            }
        }

        attackBonusStatusRenderer.sortingLayerID = sortingLayerId;
        attackBonusStatusRenderer.sortingOrder = highestSortingOrder + attackBonusStatusSortingOrderOffset;
    }

    /// <summary>
    /// Создает дочерний спрайт статуса сопротивления урону
    /// </summary>
    private void CreateDefenceBonusStatusVisual()
    {
        if (defenceBonusStatusRenderer != null)
        {
            return;
        }

        GameObject defenceBonusStatusObject = new GameObject("DefenceBonusStatusVisual");
        defenceBonusStatusObject.transform.SetParent(transform, false);

        defenceBonusStatusRenderer = defenceBonusStatusObject.AddComponent<SpriteRenderer>();
        defenceBonusStatusRenderer.sprite = defenceBonusStatusSprite;
        RefreshDefenceBonusStatusVisualTransform();
        RefreshDefenceBonusStatusVisualSorting();
        defenceBonusStatusObject.SetActive(false);
    }

    /// <summary>
    /// Показывает или скрывает спрайт сопротивления урону над персонажем
    /// </summary>
    private void UpdateDefenceBonusStatusVisual()
    {
        if (defenceBonusStatusRenderer == null)
        {
            CreateDefenceBonusStatusVisual();
        }

        if (defenceBonusStatusRenderer == null)
        {
            return;
        }

        bool shouldShowStatus = IsAlive && currentDamageResistancePercent > 0f && defenceBonusStatusSprite != null;
        defenceBonusStatusRenderer.gameObject.SetActive(shouldShowStatus);

        if (!shouldShowStatus)
        {
            return;
        }

        defenceBonusStatusRenderer.sprite = defenceBonusStatusSprite;
        RefreshDefenceBonusStatusVisualTransform();
        RefreshDefenceBonusStatusVisualSorting();
    }

    /// <summary>
    /// Расставляет спрайт сопротивления урону над персонажем
    /// </summary>
    private void RefreshDefenceBonusStatusVisualTransform()
    {
        if (defenceBonusStatusRenderer == null)
        {
            return;
        }

        defenceBonusStatusRenderer.transform.localPosition = new Vector3(
            defenceBonusStatusOffset.x,
            defenceBonusStatusOffset.y,
            0f);
        defenceBonusStatusRenderer.transform.localScale = new Vector3(
            defenceBonusStatusScale,
            defenceBonusStatusScale,
            1f);
    }

    /// <summary>
    /// Поднимает спрайт сопротивления урону поверх тела и интерфейса персонажа
    /// </summary>
    private void RefreshDefenceBonusStatusVisualSorting()
    {
        if (defenceBonusStatusRenderer == null)
        {
            return;
        }

        int highestSortingOrder = 0;
        int sortingLayerId = defenceBonusStatusRenderer.sortingLayerID;

        if (spriteRenderers != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] == null)
                {
                    continue;
                }

                if (i == 0 || spriteRenderers[i].sortingOrder >= highestSortingOrder)
                {
                    highestSortingOrder = spriteRenderers[i].sortingOrder;
                    sortingLayerId = spriteRenderers[i].sortingLayerID;
                }
            }
        }

        defenceBonusStatusRenderer.sortingLayerID = sortingLayerId;
        defenceBonusStatusRenderer.sortingOrder = highestSortingOrder + defenceBonusStatusSortingOrderOffset;
    }

    /// <summary>
    /// Создает дочерний спрайт статуса ускорения
    /// </summary>
    private void CreateSpeedBonusStatusVisual()
    {
        if (speedBonusStatusRenderer != null)
        {
            return;
        }

        GameObject speedBonusStatusObject = new GameObject("SpeedBonusStatusVisual");
        speedBonusStatusObject.transform.SetParent(transform, false);

        speedBonusStatusRenderer = speedBonusStatusObject.AddComponent<SpriteRenderer>();
        speedBonusStatusRenderer.sprite = speedBonusStatusSprite;
        RefreshSpeedBonusStatusVisualTransform();
        RefreshSpeedBonusStatusVisualSorting();
        speedBonusStatusObject.SetActive(false);
    }

    /// <summary>
    /// Показывает или скрывает спрайт ускорения над персонажем
    /// </summary>
    private void UpdateSpeedBonusStatusVisual()
    {
        if (speedBonusStatusRenderer == null)
        {
            CreateSpeedBonusStatusVisual();
        }

        if (speedBonusStatusRenderer == null)
        {
            return;
        }

        bool shouldShowStatus = IsAlive && currentMovementSpeedMultiplier > 1f && speedBonusStatusSprite != null;
        speedBonusStatusRenderer.gameObject.SetActive(shouldShowStatus);

        if (!shouldShowStatus)
        {
            return;
        }

        speedBonusStatusRenderer.sprite = speedBonusStatusSprite;
        RefreshSpeedBonusStatusVisualTransform();
        RefreshSpeedBonusStatusVisualSorting();
    }

    /// <summary>
    /// Расставляет спрайт ускорения над персонажем
    /// </summary>
    private void RefreshSpeedBonusStatusVisualTransform()
    {
        if (speedBonusStatusRenderer == null)
        {
            return;
        }

        speedBonusStatusRenderer.transform.localPosition = new Vector3(
            speedBonusStatusOffset.x,
            speedBonusStatusOffset.y,
            0f);
        speedBonusStatusRenderer.transform.localScale = new Vector3(
            speedBonusStatusScale,
            speedBonusStatusScale,
            1f);
    }

    /// <summary>
    /// Поднимает спрайт ускорения поверх тела и интерфейса персонажа
    /// </summary>
    private void RefreshSpeedBonusStatusVisualSorting()
    {
        if (speedBonusStatusRenderer == null)
        {
            return;
        }

        int highestSortingOrder = 0;
        int sortingLayerId = speedBonusStatusRenderer.sortingLayerID;

        if (spriteRenderers != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] == null)
                {
                    continue;
                }

                if (i == 0 || spriteRenderers[i].sortingOrder >= highestSortingOrder)
                {
                    highestSortingOrder = spriteRenderers[i].sortingOrder;
                    sortingLayerId = spriteRenderers[i].sortingLayerID;
                }
            }
        }

        speedBonusStatusRenderer.sortingLayerID = sortingLayerId;
        speedBonusStatusRenderer.sortingOrder = highestSortingOrder + speedBonusStatusSortingOrderOffset;
    }

    /// <summary>
    /// Создает полоску здоровья под персонажем
    /// </summary>
    private void CreateHealthBarVisual()
    {
        if (healthBarRoot != null)
        {
            return;
        }

        GameObject healthBarObject = new GameObject("HealthBarVisual");
        healthBarObject.transform.SetParent(transform, false);
        healthBarRoot = healthBarObject.transform;

        healthBarBackgroundRenderer = CreateHealthBarPart("Background", healthBarRoot, healthBarBackgroundColor);
        healthBarFillRenderer = CreateHealthBarPart("Fill", healthBarRoot, healthBarFillColor);

        RefreshHealthBarVisualTransform();
        RefreshHealthBarVisualSorting();
    }

    /// <summary>
    /// Создает слой полоски здоровья
    /// </summary>
    private SpriteRenderer CreateHealthBarPart(string partName, Transform parent, Color partColor)
    {
        GameObject partObject = new GameObject(partName);
        partObject.transform.SetParent(parent, false);

        SpriteRenderer partRenderer = partObject.AddComponent<SpriteRenderer>();
        partRenderer.sprite = GetHealthBarSprite();
        partRenderer.color = partColor;

        return partRenderer;
    }

    /// <summary>
    /// Создает три квадрата откатов под полоской здоровья
    /// </summary>
    private void CreateAbilityCooldownVisual()
    {
        if (abilityCooldownRoot != null)
        {
            return;
        }

        GameObject cooldownObject = new GameObject("AbilityCooldownVisual");
        cooldownObject.transform.SetParent(transform, false);
        abilityCooldownRoot = cooldownObject.transform;
        abilityCooldownBackgroundRenderers = new SpriteRenderer[AbilityCooldownSlotsCount];
        abilityCooldownFillRenderers = new SpriteRenderer[AbilityCooldownSlotsCount];

        for (int i = 0; i < AbilityCooldownSlotsCount; i++)
        {
            abilityCooldownBackgroundRenderers[i] = CreateAbilityCooldownPart(
                $"Ability{i + 1}Background",
                abilityCooldownRoot,
                abilityCooldownBackgroundColor);
            abilityCooldownFillRenderers[i] = CreateAbilityCooldownPart(
                $"Ability{i + 1}Fill",
                abilityCooldownRoot,
                abilityCooldownReadyColor);
        }

        RefreshAbilityCooldownVisualTransform();
        RefreshAbilityCooldownVisualSorting();
    }

    /// <summary>
    /// Создает слой квадрата отката способности
    /// </summary>
    private SpriteRenderer CreateAbilityCooldownPart(string partName, Transform parent, Color partColor)
    {
        GameObject partObject = new GameObject(partName);
        partObject.transform.SetParent(parent, false);

        SpriteRenderer partRenderer = partObject.AddComponent<SpriteRenderer>();
        partRenderer.sprite = GetHealthBarSprite();
        partRenderer.color = partColor;

        return partRenderer;
    }

    /// <summary>
    /// Возвращает общий прямоугольный спрайт для здоровья и откатов
    /// </summary>
    private static Sprite GetHealthBarSprite()
    {
        if (healthBarSprite != null)
        {
            return healthBarSprite;
        }

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "GeneratedHealthBarPixel",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        texture.SetPixel(0, 0, Color.white);
        texture.Apply();

        healthBarSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
        healthBarSprite.name = "GeneratedHealthBarPixel";

        return healthBarSprite;
    }

    /// <summary>
    /// Обновляет размер и заполнение полоски здоровья
    /// </summary>
    private void UpdateHealthBarVisual()
    {
        if (healthBarRoot == null || healthBarBackgroundRenderer == null || healthBarFillRenderer == null)
        {
            return;
        }

        healthBarBackgroundRenderer.color = healthBarBackgroundColor;
        healthBarFillRenderer.color = healthBarFillColor;
        RefreshHealthBarVisualTransform();
        RefreshHealthBarVisualSorting();
    }

    /// <summary>
    /// Расставляет фон и заполнение полоски здоровья под персонажем
    /// </summary>
    private void RefreshHealthBarVisualTransform()
    {
        if (healthBarRoot == null || healthBarBackgroundRenderer == null || healthBarFillRenderer == null)
        {
            return;
        }

        float barWidth = Mathf.Max(0f, healthBarWidth);
        float barHeight = Mathf.Max(0f, healthBarHeight);
        float healthPercent = GetHealthPercent();
        float fillWidth = barWidth * healthPercent;

        healthBarRoot.localPosition = new Vector3(healthBarOffset.x, healthBarOffset.y, 0f);
        healthBarRoot.localScale = Vector3.one;

        healthBarBackgroundRenderer.transform.localPosition = Vector3.zero;
        healthBarBackgroundRenderer.transform.localScale = new Vector3(barWidth, barHeight, 1f);

        healthBarFillRenderer.transform.localPosition = new Vector3((fillWidth - barWidth) * 0.5f, 0f, 0f);
        healthBarFillRenderer.transform.localScale = new Vector3(fillWidth, barHeight, 1f);
    }

    /// <summary>
    /// Поднимает полоску здоровья поверх остальных спрайтов
    /// </summary>
    private void RefreshHealthBarVisualSorting()
    {
        if (healthBarBackgroundRenderer == null || healthBarFillRenderer == null)
        {
            return;
        }

        int highestSortingOrder = 0;
        int sortingLayerId = healthBarBackgroundRenderer.sortingLayerID;

        if (spriteRenderers != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] == null)
                {
                    continue;
                }

                if (i == 0 || spriteRenderers[i].sortingOrder >= highestSortingOrder)
                {
                    highestSortingOrder = spriteRenderers[i].sortingOrder;
                    sortingLayerId = spriteRenderers[i].sortingLayerID;
                }
            }
        }

        healthBarBackgroundRenderer.sortingLayerID = sortingLayerId;
        healthBarFillRenderer.sortingLayerID = sortingLayerId;
        healthBarBackgroundRenderer.sortingOrder = highestSortingOrder + healthBarSortingOrderOffset;
        healthBarFillRenderer.sortingOrder = healthBarBackgroundRenderer.sortingOrder + 1;
    }

    /// <summary>
    /// Обновляет заполнение квадратов отката способностей
    /// </summary>
    private void UpdateAbilityCooldownVisual()
    {
        if (abilityCooldownRoot == null
            || abilityCooldownBackgroundRenderers == null
            || abilityCooldownFillRenderers == null)
        {
            return;
        }

        bool shouldShowCooldowns = abilityCooldownSquareSize > 0f;

        if (abilityCooldownRoot.gameObject.activeSelf != shouldShowCooldowns)
        {
            abilityCooldownRoot.gameObject.SetActive(shouldShowCooldowns);
        }

        if (!shouldShowCooldowns)
        {
            return;
        }

        RefreshAbilityCooldownVisualTransform();
        RefreshAbilityCooldownVisualSorting();

        for (int i = 0; i < AbilityCooldownSlotsCount; i++)
        {
            float cooldownProgress = GetAbilityCooldownProgress(i);
            UpdateAbilityCooldownSlot(i, cooldownProgress);
        }
    }

    /// <summary>
    /// Расставляет квадраты откатов под полоской здоровья
    /// </summary>
    private void RefreshAbilityCooldownVisualTransform()
    {
        if (abilityCooldownRoot == null
            || abilityCooldownBackgroundRenderers == null
            || abilityCooldownFillRenderers == null)
        {
            return;
        }

        float squareSize = Mathf.Max(0f, abilityCooldownSquareSize);
        float squareSpacing = Mathf.Max(0f, abilityCooldownSquareSpacing);
        float totalWidth = AbilityCooldownSlotsCount * squareSize
            + (AbilityCooldownSlotsCount - 1) * squareSpacing;
        float startX = -totalWidth * 0.5f + squareSize * 0.5f;
        Vector2 cooldownOffset = healthBarOffset + abilityCooldownOffsetFromHealthBar;

        abilityCooldownRoot.localPosition = new Vector3(cooldownOffset.x, cooldownOffset.y, 0f);
        abilityCooldownRoot.localScale = Vector3.one;

        for (int i = 0; i < AbilityCooldownSlotsCount; i++)
        {
            float slotX = startX + i * (squareSize + squareSpacing);
            SetAbilityCooldownSlotTransform(i, slotX, squareSize, GetAbilityCooldownProgress(i));
        }
    }

    /// <summary>
    /// Поднимает квадраты откатов поверх других спрайтов
    /// </summary>
    private void RefreshAbilityCooldownVisualSorting()
    {
        if (abilityCooldownBackgroundRenderers == null || abilityCooldownFillRenderers == null)
        {
            return;
        }

        int highestSortingOrder = 0;
        int sortingLayerId = 0;

        if (spriteRenderers != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] == null)
                {
                    continue;
                }

                if (i == 0 || spriteRenderers[i].sortingOrder >= highestSortingOrder)
                {
                    highestSortingOrder = spriteRenderers[i].sortingOrder;
                    sortingLayerId = spriteRenderers[i].sortingLayerID;
                }
            }
        }

        for (int i = 0; i < AbilityCooldownSlotsCount; i++)
        {
            if (abilityCooldownBackgroundRenderers[i] != null)
            {
                abilityCooldownBackgroundRenderers[i].sortingLayerID = sortingLayerId;
                abilityCooldownBackgroundRenderers[i].sortingOrder =
                    highestSortingOrder + abilityCooldownSortingOrderOffset;
            }

            if (abilityCooldownFillRenderers[i] != null)
            {
                abilityCooldownFillRenderers[i].sortingLayerID = sortingLayerId;
                abilityCooldownFillRenderers[i].sortingOrder =
                    highestSortingOrder + abilityCooldownSortingOrderOffset + 1;
            }
        }
    }

    /// <summary>
    /// Обновляет квадрат отката способности
    /// </summary>
    private void UpdateAbilityCooldownSlot(int slotIndex, float cooldownProgress)
    {
        if (!IsAbilityCooldownSlotValid(slotIndex))
        {
            return;
        }

        float squareSize = Mathf.Max(0f, abilityCooldownSquareSize);
        float squareSpacing = Mathf.Max(0f, abilityCooldownSquareSpacing);
        float totalWidth = AbilityCooldownSlotsCount * squareSize
            + (AbilityCooldownSlotsCount - 1) * squareSpacing;
        float slotX = -totalWidth * 0.5f
            + squareSize * 0.5f
            + slotIndex * (squareSize + squareSpacing);

        abilityCooldownBackgroundRenderers[slotIndex].color = abilityCooldownBackgroundColor;
        abilityCooldownFillRenderers[slotIndex].color = cooldownProgress >= 1f
            ? abilityCooldownReadyColor
            : abilityCooldownRechargingColor;

        SetAbilityCooldownSlotTransform(slotIndex, slotX, squareSize, cooldownProgress);
    }

    /// <summary>
    /// Применяет размер и заполнение к квадрату отката
    /// </summary>
    private void SetAbilityCooldownSlotTransform(
        int slotIndex,
        float slotX,
        float squareSize,
        float cooldownProgress)
    {
        if (!IsAbilityCooldownSlotValid(slotIndex))
        {
            return;
        }

        float fillHeight = squareSize * Mathf.Clamp01(cooldownProgress);

        abilityCooldownBackgroundRenderers[slotIndex].transform.localPosition = new Vector3(slotX, 0f, 0f);
        abilityCooldownBackgroundRenderers[slotIndex].transform.localScale =
            new Vector3(squareSize, squareSize, 1f);

        abilityCooldownFillRenderers[slotIndex].transform.localPosition =
            new Vector3(slotX, (-squareSize + fillHeight) * 0.5f, 0f);
        abilityCooldownFillRenderers[slotIndex].transform.localScale =
            new Vector3(squareSize, fillHeight, 1f);
    }

    /// <summary>
    /// Возвращает заполненность квадрата отката способности
    /// </summary>
    private float GetAbilityCooldownProgress(int abilityIndex)
    {
        float cooldownTime = GetAbilityCooldownTime(abilityIndex);

        if (cooldownTime <= 0f)
        {
            return 1f;
        }

        float remainingCooldownTime = Mathf.Max(0f, GetAbilityNextUseTime(abilityIndex) - Time.time);
        return Mathf.Clamp01(1f - remainingCooldownTime / cooldownTime);
    }

    /// <summary>
    /// Возвращает длительность отката способности по номеру слота
    /// </summary>
    private float GetAbilityCooldownTime(int abilityIndex)
    {
        switch (abilityIndex)
        {
            case 0:
                return firstAbilityCooldownTime;
            case 1:
                return secondAbilityCooldownTime;
            case 2:
                return thirdAbilityCooldownTime;
            default:
                return 0f;
        }
    }

    /// <summary>
    /// Возвращает время когда способность снова будет доступна
    /// </summary>
    private float GetAbilityNextUseTime(int abilityIndex)
    {
        switch (abilityIndex)
        {
            case 0:
                return firstAbilityNextUseTime;
            case 1:
                return secondAbilityNextUseTime;
            case 2:
                return thirdAbilityNextUseTime;
            default:
                return 0f;
        }
    }

    /// <summary>
    /// Проверяет можно ли обратиться к визуальному слоту отката
    /// </summary>
    private bool IsAbilityCooldownSlotValid(int slotIndex)
    {
        return abilityCooldownBackgroundRenderers != null
            && abilityCooldownFillRenderers != null
            && slotIndex >= 0
            && slotIndex < AbilityCooldownSlotsCount
            && slotIndex < abilityCooldownBackgroundRenderers.Length
            && slotIndex < abilityCooldownFillRenderers.Length
            && abilityCooldownBackgroundRenderers[slotIndex] != null
            && abilityCooldownFillRenderers[slotIndex] != null;
    }

    /// <summary>
    /// Возвращает процент текущего здоровья (для заполнения полоски)
    /// </summary>
    private float GetHealthPercent()
    {
        return Mathf.Clamp01(currentHealth / Mathf.Max(maxHealth, 0.01f));
    }

    /// <summary>
    /// Учитывает текущее сопротивление урону
    /// </summary>
    private float GetDamageAfterResistance(float damageAmount)
    {
        float damageMultiplier = 1f - currentDamageResistancePercent / 100f;
        return damageAmount * Mathf.Clamp01(damageMultiplier);
    }

    /// <summary>
    /// Держит сопротивление урону заданное время и убирает его
    /// </summary>
    private IEnumerator ApplyDamageResistanceCoroutine(float resistancePercent, float duration)
    {
        currentDamageResistancePercent = Mathf.Clamp(resistancePercent, 0f, 100f);
        UpdateDefenceBonusStatusVisual();

        Debug.Log($"{name} сопротивление урону {currentDamageResistancePercent:0.0}% на {duration:0.0} сек");

        yield return new WaitForSeconds(duration);

        currentDamageResistancePercent = 0f;
        UpdateDefenceBonusStatusVisual();
        damageResistanceCoroutine = null;

        Debug.Log($"{name} сопротивление урону закончилось");
    }

    /// <summary>
    /// Держит множитель скорости заданное время и убирает его
    /// </summary>
    private IEnumerator ApplyMovementSpeedMultiplierCoroutine(float speedMultiplier, float duration)
    {
        currentMovementSpeedMultiplier = speedMultiplier;
        UpdateSpeedBonusStatusVisual();
        Debug.Log($"{name} скорость передвижения x{currentMovementSpeedMultiplier:0.00} на {duration:0.0} сек");

        yield return new WaitForSeconds(duration);

        currentMovementSpeedMultiplier = 1f;
        UpdateSpeedBonusStatusVisual();
        movementSpeedMultiplierCoroutine = null;

        Debug.Log($"{name} бонус скорости закончился");
    }

    /// <summary>
    /// Держит бонус урона заданное время и убирает его
    /// </summary>
    private IEnumerator ApplyDamageBonusCoroutine(float damageBonusPercent, float duration)
    {
        currentDamageBonusPercent = damageBonusPercent;
        UpdateAttackBonusStatusVisual();

        Debug.Log($"{name} урон усилен на {currentDamageBonusPercent:0.0}% на {duration:0.0} сек");

        yield return new WaitForSeconds(duration);

        currentDamageBonusPercent = 0f;
        UpdateAttackBonusStatusVisual();
        damageBonusCoroutine = null;

        Debug.Log($"{name} усиление урона закончилось");
    }

    /// <summary>
    /// Держит щит заданное время и убирает его
    /// </summary>
    private IEnumerator ApplyShieldCoroutine(float duration)
    {
        Debug.Log($"{name} получил щит {currentShieldAmount:0.0} на {duration:0.0} сек");

        yield return new WaitForSeconds(duration);

        currentShieldAmount = 0f;
        UpdateShieldVisual();
        shieldCoroutine = null;

        Debug.Log($"{name} щит закончился.");
    }

    /// <summary>
    /// Восстанавливает здоровье персонажа
    /// </summary>
    public void Heal(float healAmount)
    {
        if (healAmount <= 0f)
        {
            return;
        }

        bool wasIncapacitated = IsIncapacitated;
        currentHealth = Mathf.Min(currentHealth + healAmount, maxHealth);
        UpdateHealthBarVisual();

        if (wasIncapacitated && IsAlive)
        {
            RecoverFromIncapacitated();
        }

        StartHealingFlash();
    }

    /// <summary>
    /// Поднимает упавшего персонажа спроцентом максимального здоровья
    /// </summary>
    public void ReviveWithHealthPercent(float healthPercent)
    {
        if (IsAlive)
        {
            return;
        }

        currentHealth = Mathf.Clamp(maxHealth * Mathf.Clamp01(healthPercent), 0f, maxHealth);
        UpdateHealthBarVisual();

        if (!IsAlive)
        {
            return;
        }

        RecoverFromIncapacitated();
        StartHealingFlash();
    }
}
