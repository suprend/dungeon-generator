using System.Collections;
using System.Collections.Generic;
using DanverPlayground.Roguelike.Characters;
using DanverPlayground.Roguelike.Characters.Abilities;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TopDownPlayerController))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(PlayerBowAttack))]
public sealed class PlayerClassRuntime : MonoBehaviour
{
    public const int AbilitySlotCount = 3;

    [SerializeField] private CharacterDefinition defaultClassDefinition;
    [SerializeField] private TopDownPlayerController playerController;
    [SerializeField] private Rigidbody2D body2D;
    [SerializeField] private Health health;
    [SerializeField] private PlayerBowAttack bowAttack;
    [SerializeField] private KeyCode ability1Key = KeyCode.Alpha1;
    [SerializeField] private KeyCode ability2Key = KeyCode.Alpha2;
    [SerializeField] private KeyCode ability3Key = KeyCode.Alpha3;
    [SerializeField] private bool playerInputEnabled = true;

    private readonly float[] abilityReadyTimes = new float[3];
    private readonly HashSet<int> warnedUnsupportedSlots = new();

    private CharacterDefinition activeDefinition;
    private Coroutine activeDashRoutine;
    private Coroutine activeSpeedBoostRoutine;
    private float baseMoveSpeed = 5f;
    private float currentMoveSpeedMultiplier = 1f;

    public CharacterDefinition ActiveDefinition => activeDefinition;
    public KeyCode Ability1Key => ability1Key;
    public KeyCode Ability2Key => ability2Key;
    public KeyCode Ability3Key => ability3Key;
    public bool PlayerInputEnabled => playerInputEnabled;

    private void Reset()
    {
        CacheComponents();
    }

    private void Awake()
    {
        CacheComponents();
    }

    private void Start()
    {
        if (defaultClassDefinition != null)
            ApplyDefinition(defaultClassDefinition);
    }

    private void Update()
    {
        if (activeDefinition == null || !playerInputEnabled)
            return;

        if (IsAbilityKeyPressed(ability1Key, KeyCode.Keypad1))
            TryUseAbility(0);

        if (IsAbilityKeyPressed(ability2Key, KeyCode.Keypad2))
            TryUseAbility(1);

        if (IsAbilityKeyPressed(ability3Key, KeyCode.Keypad3))
            TryUseAbility(2);
    }

    public void ApplyDefinition(CharacterDefinition definition)
    {
        CacheComponents();

        activeDefinition = definition;
        defaultClassDefinition = definition;
        warnedUnsupportedSlots.Clear();
        for (var i = 0; i < abilityReadyTimes.Length; i++)
            abilityReadyTimes[i] = 0f;

        if (activeSpeedBoostRoutine != null)
        {
            StopCoroutine(activeSpeedBoostRoutine);
            activeSpeedBoostRoutine = null;
        }

        if (activeDashRoutine != null)
        {
            StopCoroutine(activeDashRoutine);
            activeDashRoutine = null;
            if (playerController != null)
                playerController.enabled = true;
        }

        currentMoveSpeedMultiplier = 1f;

        if (definition == null)
        {
            Debug.LogWarning("[PlayerClassRuntime] CharacterDefinition is not assigned.", this);
            return;
        }

        baseMoveSpeed = Mathf.Max(0.01f, definition.BaseStats.moveSpeed);
        ApplyCurrentMoveSpeed();

        if (health != null)
            health.Configure(Mathf.Max(1, Mathf.RoundToInt(definition.BaseStats.maxHealth)), false);

        if (bowAttack != null && definition.RangedAttack != null)
        {
            bowAttack.ApplyAttackProfile(
                1f / Mathf.Max(definition.RangedAttack.fireRate, 0.01f),
                definition.RangedAttack.projectileSpeed,
                Mathf.Max(1, Mathf.RoundToInt(definition.RangedAttack.damage)),
                definition.RangedAttack.projectileLifetime,
                definition.RangedAttack.projectileScale,
                definition.ProjectileColor);
        }
    }

    public bool TryUseAbility(int slotIndex)
    {
        var ability = GetAbility(slotIndex);
        if (ability == null || slotIndex < 0 || slotIndex >= abilityReadyTimes.Length)
            return false;
        if (Time.time < abilityReadyTimes[slotIndex])
            return false;

        var activated = ability switch
        {
            DashAbilityDefinition dash => TryDash(dash),
            SpeedBoostAbilityDefinition speedBoost => TrySpeedBoost(speedBoost),
            ProjectileBurstAbilityDefinition projectileBurst => TryProjectileBurst(projectileBurst),
            HealPulseAbilityDefinition healPulse => TryHealPulse(healPulse),
            ShockwaveAbilityDefinition shockwave => TryShockwave(shockwave),
            _ => TryWarnUnsupportedAbility(slotIndex, ability)
        };

        if (activated)
            abilityReadyTimes[slotIndex] = Time.time + ability.Cooldown;

        return activated;
    }

    public void SetPlayerInputEnabled(bool enabled)
    {
        playerInputEnabled = enabled;
    }

    private bool TryDash(DashAbilityDefinition dash)
    {
        var direction = ResolveAimDirection();
        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        if (activeDashRoutine != null)
            StopCoroutine(activeDashRoutine);

        activeDashRoutine = StartCoroutine(DashRoutine(direction.normalized, dash.DashSpeed, dash.DashDuration));
        return true;
    }

    private bool TrySpeedBoost(SpeedBoostAbilityDefinition speedBoost)
    {
        if (activeSpeedBoostRoutine != null)
            StopCoroutine(activeSpeedBoostRoutine);

        activeSpeedBoostRoutine = StartCoroutine(SpeedBoostRoutine(speedBoost.SpeedMultiplier, speedBoost.Duration));
        return true;
    }

    private bool TryProjectileBurst(ProjectileBurstAbilityDefinition projectileBurst)
    {
        if (bowAttack == null)
            return false;

        var direction = ResolveAimDirection();
        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        var normalizedDirection = direction.normalized;
        var halfSpread = projectileBurst.SpreadAngle * 0.5f;
        var firedAny = false;
        for (var i = 0; i < projectileBurst.ProjectileCount; i++)
        {
            var t = projectileBurst.ProjectileCount == 1 ? 0.5f : i / (float)(projectileBurst.ProjectileCount - 1);
            var angle = Mathf.Lerp(-halfSpread, halfSpread, t);
            var rotatedDirection = (Vector2)(Quaternion.Euler(0f, 0f, angle) * normalizedDirection);
            firedAny |= bowAttack.FireOnce(rotatedDirection, true, projectileBurst.DamageMultiplier, projectileBurst.SpeedMultiplier);
        }

        return firedAny;
    }

    private bool TryHealPulse(HealPulseAbilityDefinition healPulse)
    {
        if (health == null)
            return false;

        var healedAny = false;
        var healAmount = Mathf.Max(1, Mathf.RoundToInt(healPulse.HealAmount));
        var targets = new HashSet<Health>();

        targets.Add(health);

        var hits = Physics2D.OverlapCircleAll(transform.position, healPulse.Radius);
        for (var i = 0; i < hits.Length; i++)
        {
            var targetHealth = hits[i].GetComponentInParent<Health>();
            if (targetHealth == null || targetHealth.IsDead)
                continue;
            if (targetHealth.GetComponentInParent<EnemyAuthoring>() != null)
                continue;
            if (targetHealth.GetComponentInParent<TopDownPlayerController>() == null)
                continue;

            targets.Add(targetHealth);
        }

        foreach (var target in targets)
        {
            if (target == null || target.IsDead)
                continue;

            var healthBefore = target.CurrentHealth;
            target.Heal(healAmount);
            healedAny |= target.CurrentHealth > healthBefore;
        }

        return healedAny;
    }

    private bool TryShockwave(ShockwaveAbilityDefinition shockwave)
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, shockwave.Radius);
        var affectedEnemyIds = new HashSet<int>();
        var affectedAny = false;

        for (var i = 0; i < hits.Length; i++)
        {
            var enemyAgent = hits[i].GetComponentInParent<EnemyAgentRuntime>();
            if (enemyAgent == null)
                continue;

            var enemyId = enemyAgent.GetInstanceID();
            if (!affectedEnemyIds.Add(enemyId))
                continue;

            var targetPosition = enemyAgent.transform.position;
            var direction = (Vector2)(targetPosition - transform.position);
            if (direction.sqrMagnitude <= 0.0001f)
                direction = Vector2.up;

            enemyAgent.ApplyKnockback(direction.normalized, shockwave.KnockbackDistance, shockwave.KnockbackDuration);
            affectedAny = true;
        }

        return affectedAny;
    }

    private bool TryWarnUnsupportedAbility(int slotIndex, ActiveAbilityDefinition ability)
    {
        if (ability == null)
            return false;

        if (warnedUnsupportedSlots.Add(slotIndex))
            Debug.LogWarning($"[PlayerClassRuntime] Ability '{ability.name}' in slot {slotIndex + 1} is not supported in the current runtime.", this);

        return false;
    }

    public ActiveAbilityDefinition GetAbility(int slotIndex)
    {
        if (activeDefinition == null || activeDefinition.ActiveAbilities == null)
            return null;
        if (slotIndex < 0 || slotIndex >= activeDefinition.ActiveAbilities.Length)
            return null;

        return activeDefinition.ActiveAbilities[slotIndex];
    }

    public float GetAbilityCooldownRemaining(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= abilityReadyTimes.Length)
            return 0f;

        return Mathf.Max(0f, abilityReadyTimes[slotIndex] - Time.time);
    }

    public float GetAbilityCooldownDuration(int slotIndex)
    {
        return GetAbility(slotIndex)?.Cooldown ?? 0f;
    }

    public bool IsAbilityReady(int slotIndex)
    {
        return GetAbility(slotIndex) != null && GetAbilityCooldownRemaining(slotIndex) <= 0.001f;
    }

    public KeyCode GetAbilityKey(int slotIndex)
    {
        return slotIndex switch
        {
            0 => ability1Key,
            1 => ability2Key,
            2 => ability3Key,
            _ => KeyCode.None
        };
    }

    private IEnumerator DashRoutine(Vector2 direction, float dashSpeed, float dashDuration)
    {
        CacheComponents();

        var disableController = playerController != null && playerController.enabled;
        if (disableController)
            playerController.enabled = false;

        if (body2D != null)
            body2D.velocity = Vector2.zero;

        var elapsed = 0f;
        while (elapsed < dashDuration)
        {
            yield return new WaitForFixedUpdate();
            elapsed += Time.fixedDeltaTime;

            if (body2D != null)
                body2D.MovePosition(body2D.position + direction * (dashSpeed * Time.fixedDeltaTime));
            else
                transform.position += (Vector3)(direction * (dashSpeed * Time.fixedDeltaTime));
        }

        if (body2D != null)
            body2D.velocity = Vector2.zero;

        if (playerController != null && disableController)
            playerController.enabled = true;

        activeDashRoutine = null;
    }

    private IEnumerator SpeedBoostRoutine(float speedMultiplier, float duration)
    {
        currentMoveSpeedMultiplier = Mathf.Max(0.01f, speedMultiplier);
        ApplyCurrentMoveSpeed();

        yield return new WaitForSeconds(duration);

        currentMoveSpeedMultiplier = 1f;
        ApplyCurrentMoveSpeed();
        activeSpeedBoostRoutine = null;
    }

    private void ApplyCurrentMoveSpeed()
    {
        if (playerController != null)
            playerController.MoveSpeed = baseMoveSpeed * currentMoveSpeedMultiplier;
    }

    private Vector2 ResolveAimDirection()
    {
        if (!playerInputEnabled)
        {
            var fallbackFacingSign = playerController != null ? playerController.FacingSign : 1;
            return Vector2.right * fallbackFacingSign;
        }

        var currentCamera = Camera.main;
        if (currentCamera != null)
        {
            var mousePosition = Input.mousePosition;
            mousePosition.z = Mathf.Abs(currentCamera.transform.position.z - transform.position.z);
            var mouseWorld = currentCamera.ScreenToWorldPoint(mousePosition);
            var directionToCursor = (Vector2)(mouseWorld - transform.position);
            if (directionToCursor.sqrMagnitude > 0.0001f)
                return directionToCursor.normalized;
        }

        var facingSign = playerController != null ? playerController.FacingSign : 1;
        return Vector2.right * facingSign;
    }

    private void CacheComponents()
    {
        if (playerController == null)
            playerController = GetComponent<TopDownPlayerController>();
        if (body2D == null)
            body2D = GetComponent<Rigidbody2D>();
        if (health == null)
            health = GetComponent<Health>();
        if (bowAttack == null)
            bowAttack = GetComponent<PlayerBowAttack>();
    }

    private static bool IsAbilityKeyPressed(KeyCode primary, KeyCode alternate)
    {
        return Input.GetKeyDown(primary) || Input.GetKeyDown(alternate);
    }
}
