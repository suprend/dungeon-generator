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
    public float NormalizedHealth => MaxHealth > 0 ? currentHealth / (float)MaxHealth : 0f;
    public bool IsDead => isDead;
    public event Action<Health> Changed;
    public event Action<Health> Died;

    private void Awake()
    {
        currentHealth = MaxHealth;
        Changed?.Invoke(this);
    }

    public void ApplyDamage(int amount)
    {
        if (isDead)
            return;

        var newHealth = Mathf.Max(0, currentHealth - Mathf.Max(0, amount));
        if (newHealth == currentHealth)
            return;

        currentHealth = newHealth;
        Changed?.Invoke(this);
        if (currentHealth > 0)
            return;

        isDead = true;
        Died?.Invoke(this);
        if (GetComponentInParent<TopDownPlayerController>() != null)
        {
            Debug.Log($"[Health] Triggering death menu for player '{name}'.");
            PlayerDeathRestartMenu.Show();
        }

        if (destroyOnDeath)
            Destroy(gameObject);
        else
            Debug.Log($"[Health] {name} died.");
    }

    public void RestoreFull()
    {
        isDead = false;
        currentHealth = MaxHealth;
        Changed?.Invoke(this);
    }

    public void Configure(int newMaxHealth, bool newDestroyOnDeath)
    {
        maxHealth = Mathf.Max(1, newMaxHealth);
        destroyOnDeath = newDestroyOnDeath;
        RestoreFull();
    }
}
