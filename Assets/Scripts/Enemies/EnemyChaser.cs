//DP
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Противник для дебага
/// </summary>
public class EnemyChaser : EnemyTemplate
{
    [Header("Melee Attack")]
    [SerializeField, Min(0f)] private float meleeAttackRange = 1f;
    [SerializeField, Min(0f)] private float meleeAttackOffsetDistance = 0.75f;
    [SerializeField, Min(0f)] private float meleeKnockbackForce = 4f;
    [SerializeField] private LayerMask meleeAttackLayers = ~0;

    [Header("AI Attack")]
    [SerializeField, Min(0.01f)] private float aiAttackMinInterval = 0.8f;
    [SerializeField, Min(0.01f)] private float aiAttackMaxInterval = 1.6f;

    [Header("Melee Attack Visual")]
    [SerializeField, Min(0.01f)] private float meleeAttackVisualLifeTime = 0.15f;
    [SerializeField, Min(8)] private int meleeAttackCircleSegments = 40;
    [SerializeField, Min(0.01f)] private float meleeAttackCircleWidth = 0.07f;
    [SerializeField] private Color meleeAttackCircleColor = new Color(1f, 0.2f, 0.12f, 0.9f);

    private static Material circleVisualMaterial;

    private float nextMeleeAttackTime;
    private Vector2 lastMeleeAttackDirection = Vector2.right;

    private void Update()
    {
        if (!IsAlive || IsStunned || IsExternalMovementActive)
        {
            return;
        }

        if (nextMeleeAttackTime <= 0f)
        {
            ScheduleNextMeleeAttack();
        }

        if (Time.time < nextMeleeAttackTime)
        {
            return;
        }

        PlayerCharacterTemplate targetCharacter = FindClosestAlivePlayerCharacterInAiAttackZone();

        if (targetCharacter != null)
        {
            Vector2 attackDirection = ((Vector2)targetCharacter.transform.position - (Vector2)transform.position).normalized;
            UseMeleeAttack(attackDirection);
        }

        ScheduleNextMeleeAttack();
    }

    private void FixedUpdate()
    {
        if (!IsAlive)
        {
            ResetAiDistanceMovement();
            return;
        }

        MoveWithAiDistancesToTarget(FindClosestAlivePlayerCharacter());
    }

    private void UseMeleeAttack(Vector2 attackDirection)
    {
        if (attackDirection.sqrMagnitude <= 0.0001f)
        {
            attackDirection = lastMeleeAttackDirection;
        }

        attackDirection = attackDirection.normalized;
        lastMeleeAttackDirection = attackDirection;

        Vector2 attackCenter = GetMeleeAttackCenter(attackDirection);
        CreateMeleeAttackCircle(attackCenter);

        int damagedTargetsCount = DamagePlayerCharactersInCircle(attackCenter, attackDirection);
        Debug.Log($"{name} melee attack dealt {Damage} damage. Targets hit: {damagedTargetsCount}");
    }

    private int DamagePlayerCharactersInCircle(Vector2 attackCenter, Vector2 attackDirection)
    {
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(attackCenter, meleeAttackRange, meleeAttackLayers);
        HashSet<PlayerCharacterTemplate> damagedCharacters = new HashSet<PlayerCharacterTemplate>();

        foreach (Collider2D hitCollider in hitColliders)
        {
            if (hitCollider == null)
            {
                continue;
            }

            PlayerCharacterTemplate character = hitCollider.GetComponentInParent<PlayerCharacterTemplate>();

            if (character == null || !character.IsAlive || !damagedCharacters.Add(character))
            {
                continue;
            }

            character.TakeDamage(Damage, attackDirection, meleeKnockbackForce);
        }

        return damagedCharacters.Count;
    }

    private void ScheduleNextMeleeAttack()
    {
        float finalMinInterval = Mathf.Min(aiAttackMinInterval, aiAttackMaxInterval);
        float finalMaxInterval = Mathf.Max(aiAttackMinInterval, aiAttackMaxInterval);
        nextMeleeAttackTime = Time.time + UnityEngine.Random.Range(finalMinInterval, finalMaxInterval);
    }

    private void CreateMeleeAttackCircle(Vector2 attackCenter)
    {
        GameObject attackCircle = CreateCircleVisual(
            "EnemyChaserMeleeAttackCircle",
            attackCenter,
            meleeAttackRange,
            meleeAttackCircleSegments,
            meleeAttackCircleWidth,
            meleeAttackCircleColor);

        Destroy(attackCircle, meleeAttackVisualLifeTime);
    }

    private GameObject CreateCircleVisual(
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
        LineRenderer lineRenderer = circleObject.AddComponent<LineRenderer>();

        lineRenderer.useWorldSpace = false;
        lineRenderer.loop = true;
        lineRenderer.positionCount = finalSegments;
        lineRenderer.startWidth = circleWidth;
        lineRenderer.endWidth = circleWidth;
        lineRenderer.startColor = circleColor;
        lineRenderer.endColor = circleColor;
        lineRenderer.sortingOrder = sortingOrder;
        lineRenderer.sharedMaterial = GetCircleVisualMaterial();

        for (int i = 0; i < finalSegments; i++)
        {
            float angle = i / (float)finalSegments * Mathf.PI * 2f;
            Vector3 point = new Vector3(
                Mathf.Cos(angle) * finalRadius,
                Mathf.Sin(angle) * finalRadius,
                0f);

            lineRenderer.SetPosition(i, point);
        }

        return circleObject;
    }

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

    private Vector2 GetMeleeAttackCenter(Vector2 attackDirection)
    {
        return (Vector2)transform.position + attackDirection * meleeAttackOffsetDistance;
    }

    private void OnDrawGizmosSelected()
    {
        Vector2 attackDirection = lastMeleeAttackDirection.sqrMagnitude > 0f ? lastMeleeAttackDirection.normalized : Vector2.right;
        Vector2 attackCenter = (Vector2)transform.position + attackDirection * meleeAttackOffsetDistance;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackCenter, meleeAttackRange);

        DrawAiMovementGizmos();
        DrawAiAttackGizmos();
    }
}
