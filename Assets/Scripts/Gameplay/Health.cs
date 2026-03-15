using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class Health : MonoBehaviour
{
    [SerializeField] private int maxHealth = 10;
    [SerializeField] private bool destroyOnDeath = true;

    private int currentHealth;
    private bool isDead;

    public int MaxHealth => Mathf.Max(1, maxHealth);
    public int CurrentHealth => currentHealth;
    public bool IsDead => isDead;
    public event Action<Health> Died;

    private void Awake()
    {
        currentHealth = MaxHealth;
    }

    public void ApplyDamage(int amount)
    {
        if (isDead)
            return;

        currentHealth = Mathf.Max(0, currentHealth - Mathf.Max(0, amount));
        if (currentHealth > 0)
            return;

        isDead = true;
        Died?.Invoke(this);
        if (destroyOnDeath)
            Destroy(gameObject);
        else
            Debug.Log($"[Health] {name} died.");
    }

    public void RestoreFull()
    {
        isDead = false;
        currentHealth = MaxHealth;
    }

    public void Configure(int newMaxHealth, bool newDestroyOnDeath)
    {
        maxHealth = Mathf.Max(1, newMaxHealth);
        destroyOnDeath = newDestroyOnDeath;
        RestoreFull();
    }
}
