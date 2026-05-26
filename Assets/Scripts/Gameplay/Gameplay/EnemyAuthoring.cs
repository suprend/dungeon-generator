using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyAgentRuntime))]
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(ContactDamageDealer))]
public sealed class EnemyAuthoring : MonoBehaviour
{
    [SerializeField] private int maxHealth = 3;
    [SerializeField] private bool destroyOnDeath = true;
    [SerializeField] private int contactDamage = 1;
    [SerializeField] private float contactHitCooldownSeconds = 0.5f;
    [SerializeField] private float reacquireInterval = 0.2f;
    [SerializeField] private float stopDistance = 0.6f;

    private float runtimeStatsMultiplier = 1f;

    public int MaxHealth => Mathf.Max(1, maxHealth);
    public bool DestroyOnDeath => destroyOnDeath;
    public int ContactDamage => Mathf.Max(0, contactDamage);
    public float ContactHitCooldownSeconds => Mathf.Max(0.01f, contactHitCooldownSeconds);
    public float ReacquireInterval => Mathf.Max(0.05f, reacquireInterval);
    public float StopDistance => Mathf.Max(0f, stopDistance);

    private void Reset()
    {
        ApplyAuthoringDefaults();
    }

    private void OnValidate()
    {
        ApplyAuthoringDefaults();
    }

    public void ApplyRuntimeConfiguration(LayerMask targetLayers)
    {
        ApplyRuntimeConfiguration(targetLayers, 1f);
    }

    public void ApplyRuntimeConfiguration(LayerMask targetLayers, float statsMultiplier)
    {
        ApplyAuthoringDefaults();
        runtimeStatsMultiplier = Mathf.Max(0f, statsMultiplier);

        if (TryGetComponent<Health>(out var health))
            health.Configure(GetScaledMaxHealth(), DestroyOnDeath);

        ApplyTargetLayers(targetLayers);
    }

    public void ApplyTargetLayers(LayerMask targetLayers)
    {
        if (TryGetComponent<ContactDamageDealer>(out var damageDealer))
            damageDealer.Configure(GetScaledContactDamage(), ContactHitCooldownSeconds, targetLayers);
    }

    private int GetScaledMaxHealth()
    {
        return Mathf.Max(1, Mathf.RoundToInt(MaxHealth * Mathf.Max(0f, runtimeStatsMultiplier)));
    }

    private int GetScaledContactDamage()
    {
        return Mathf.Max(0, Mathf.RoundToInt(ContactDamage * Mathf.Max(0f, runtimeStatsMultiplier)));
    }

    private void ApplyAuthoringDefaults()
    {
        if (TryGetComponent<Rigidbody2D>(out var body2D))
        {
            body2D.gravityScale = 0f;
            body2D.bodyType = RigidbodyType2D.Kinematic;
            body2D.constraints |= RigidbodyConstraints2D.FreezeRotation;
            Rigidbody2DSmoothingUtility.EnableInterpolation(body2D);
        }

        if (TryGetComponent<NavMeshAgent>(out var navMeshAgent))
        {
            navMeshAgent.updateRotation = false;
            navMeshAgent.updateUpAxis = false;
            navMeshAgent.stoppingDistance = StopDistance;
        }

        if (TryGetComponent<EnemyAgentRuntime>(out var enemyAgent))
            enemyAgent.Configure(ReacquireInterval, StopDistance);

        if (TryGetComponent<ContactDamageDealer>(out var damageDealer))
            damageDealer.ConfigureStats(ContactDamage, ContactHitCooldownSeconds);

        if (GetComponent<Collider2D>() == null)
            Debug.LogWarning($"[EnemyAuthoring] Enemy '{name}' has no Collider2D.", this);
    }
}
