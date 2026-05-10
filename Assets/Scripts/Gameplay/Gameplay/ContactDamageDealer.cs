using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public sealed class ContactDamageDealer : MonoBehaviour
{
    [SerializeField] private int damage = 1;
    [SerializeField] private float hitCooldownSeconds = 0.5f;
    [SerializeField] private LayerMask targetLayers = ~0;

    private readonly Dictionary<int, float> nextHitTimeByTarget = new();

    public void Configure(int newDamage, float newHitCooldownSeconds, LayerMask newTargetLayers)
    {
        damage = Mathf.Max(0, newDamage);
        hitCooldownSeconds = Mathf.Max(0.01f, newHitCooldownSeconds);
        targetLayers = newTargetLayers;
    }

    public void ConfigureStats(int newDamage, float newHitCooldownSeconds)
    {
        damage = Mathf.Max(0, newDamage);
        hitCooldownSeconds = Mathf.Max(0.01f, newHitCooldownSeconds);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryDealDamage(collision.collider);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        TryDealDamage(collision.collider);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryDealDamage(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryDealDamage(other);
    }

    private void TryDealDamage(Component other)
    {
        if (other == null)
            return;
        if (((1 << other.gameObject.layer) & targetLayers.value) == 0)
            return;

        var health = other.GetComponentInParent<Health>();
        if (health == null || health == GetComponentInParent<Health>())
            return;

        var targetId = health.GetInstanceID();
        if (nextHitTimeByTarget.TryGetValue(targetId, out var nextAllowedTime) && Time.time < nextAllowedTime)
            return;

        nextHitTimeByTarget[targetId] = Time.time + Mathf.Max(0.01f, hitCooldownSeconds);
        health.ApplyDamage(Mathf.Max(0, damage));
    }
}
