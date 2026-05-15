//DP
using UnityEngine;

/// <summary>
/// Шаблон декорации, которую нельзя сдвинуть, но можно сломать уроном.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CapsuleCollider2D))]
public class PropTemplate : MonoBehaviour, IDamageable
{
    [Header("Прочность декорации")]
    [SerializeField, Min(1f)] private float maxHealth = 30f;
    [SerializeField, Min(0f)] private float currentHealth = 30f;
    [SerializeField] private bool destroyOnBreak = true;

    private Rigidbody2D propRigidbody;
    private CapsuleCollider2D propCollider;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public bool IsAlive => currentHealth > 0f;

    private void Awake()
    {
        ConfigureStaticPhysics();
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        if (!IsAlive)
        {
            Break();
        }
    }

    private void Reset()
    {
        currentHealth = maxHealth;
        ConfigureStaticPhysics();
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
    }

    public void TakeDamage(float damageAmount)
    {
        if (damageAmount <= 0f || !IsAlive)
        {
            return;
        }

        currentHealth = Mathf.Max(currentHealth - damageAmount, 0f);
        Debug.Log($"{name}: декорация получила {damageAmount:0.0} урона. Прочность: {currentHealth:0.0}/{maxHealth:0.0}.");

        if (!IsAlive)
        {
            Break();
        }
    }

    private void ConfigureStaticPhysics()
    {
        propRigidbody = GetComponent<Rigidbody2D>();
        propCollider = GetComponent<CapsuleCollider2D>();

        if (propRigidbody != null)
        {
            propRigidbody.bodyType = RigidbodyType2D.Static;
            propRigidbody.gravityScale = 0f;
            propRigidbody.simulated = true;
        }

        if (propCollider != null)
        {
            propCollider.isTrigger = false;
            propCollider.direction = CapsuleDirection2D.Vertical;
        }
    }

    private void Break()
    {
        Debug.Log($"{name}: декорация сломана.");

        if (destroyOnBreak)
        {
            Destroy(gameObject);
            return;
        }

        gameObject.SetActive(false);
    }
}
