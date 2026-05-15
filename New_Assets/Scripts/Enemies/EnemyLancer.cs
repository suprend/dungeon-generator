//DP
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Противник-копейщик: преследует героя и атакует длинной прямой зоной перед собой.
/// </summary>
public class EnemyLancer : EnemyTemplate
{
    [Header("Длинная ближняя атака")]
    [SerializeField, Min(0f)] private float lineAttackLength = 3f;
    [SerializeField, Min(0f)] private float lineAttackWidth = 0.55f;
    [SerializeField, Min(0f)] private float lineAttackStartOffsetDistance = 0.55f;
    [SerializeField, Min(0f)] private float lineAttackKnockbackForce = 5f;
    [SerializeField] private LayerMask lineAttackLayers = ~0;

    [Header("ИИ атака")]
    [SerializeField, Min(0.01f)] private float aiAttackMinInterval = 1f;
    [SerializeField, Min(0.01f)] private float aiAttackMaxInterval = 1.8f;

    [Header("Отображение длинной атаки")]
    [SerializeField, Min(0.01f)] private float lineAttackVisualLifeTime = 0.16f;
    [SerializeField, Min(0.01f)] private float lineAttackVisualWidth = 0.07f;
    [SerializeField] private Color lineAttackVisualColor = new Color(1f, 0.25f, 0.08f, 0.95f);

    private static Material lineVisualMaterial;

    private float nextLineAttackTime;
    private Vector2 lastLineAttackDirection = Vector2.right;

    private void Update()
    {
        if (!IsAlive || IsStunned || IsExternalMovementActive)
        {
            return;
        }

        if (nextLineAttackTime <= 0f)
        {
            ScheduleNextLineAttack();
        }

        if (Time.time < nextLineAttackTime)
        {
            return;
        }

        PlayerCharacterTemplate targetCharacter = FindClosestAlivePlayerCharacterInAiAttackZone();

        if (targetCharacter != null)
        {
            Vector2 attackDirection = ((Vector2)targetCharacter.transform.position - (Vector2)transform.position).normalized;
            UseLineAttack(attackDirection);
        }

        ScheduleNextLineAttack();
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

    /// <summary>
    /// Выполняет длинную прямую атаку в заданном направлении.
    /// </summary>
    private void UseLineAttack(Vector2 attackDirection)
    {
        if (attackDirection.sqrMagnitude <= 0.0001f)
        {
            attackDirection = lastLineAttackDirection;
        }

        attackDirection = attackDirection.normalized;
        lastLineAttackDirection = attackDirection;

        Vector2 attackCenter = GetLineAttackCenter(attackDirection);
        CreateLineAttackVisual(attackCenter, attackDirection);

        int damagedTargetsCount = DamagePlayerCharactersInLine(attackCenter, attackDirection);
        Debug.Log($"{name} длинная атака нанесла {Damage} урона. Целей задето: {damagedTargetsCount}");
    }

    /// <summary>
    /// Наносит урон всем героям внутри прямоугольника атаки.
    /// </summary>
    private int DamagePlayerCharactersInLine(Vector2 attackCenter, Vector2 attackDirection)
    {
        float attackAngle = GetLineAttackAngle(attackDirection);
        Vector2 attackSize = new Vector2(lineAttackLength, lineAttackWidth);
        Collider2D[] hitColliders = Physics2D.OverlapBoxAll(attackCenter, attackSize, attackAngle, lineAttackLayers);
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

            character.TakeDamage(Damage, attackDirection, lineAttackKnockbackForce);
        }

        return damagedCharacters.Count;
    }

    /// <summary>
    /// Назначает случайное время следующей атаки.
    /// </summary>
    private void ScheduleNextLineAttack()
    {
        float finalMinInterval = Mathf.Min(aiAttackMinInterval, aiAttackMaxInterval);
        float finalMaxInterval = Mathf.Max(aiAttackMinInterval, aiAttackMaxInterval);
        nextLineAttackTime = Time.time + Random.Range(finalMinInterval, finalMaxInterval);
    }

    /// <summary>
    /// Создает прямоугольную визуализацию атаки.
    /// </summary>
    private void CreateLineAttackVisual(Vector2 attackCenter, Vector2 attackDirection)
    {
        GameObject attackVisual = new GameObject("EnemyLancerLineAttackVisual");
        LineRenderer lineRenderer = attackVisual.AddComponent<LineRenderer>();
        Vector3[] corners = GetLineAttackCorners(attackCenter, attackDirection);

        lineRenderer.useWorldSpace = true;
        lineRenderer.loop = true;
        lineRenderer.positionCount = corners.Length;
        lineRenderer.startWidth = lineAttackVisualWidth;
        lineRenderer.endWidth = lineAttackVisualWidth;
        lineRenderer.startColor = lineAttackVisualColor;
        lineRenderer.endColor = lineAttackVisualColor;
        lineRenderer.sortingOrder = 10;
        lineRenderer.sharedMaterial = GetLineVisualMaterial();

        for (int i = 0; i < corners.Length; i++)
        {
            lineRenderer.SetPosition(i, corners[i]);
        }

        Destroy(attackVisual, lineAttackVisualLifeTime);
    }

    /// <summary>
    /// Возвращает четыре угла прямоугольника атаки.
    /// </summary>
    private Vector3[] GetLineAttackCorners(Vector2 attackCenter, Vector2 attackDirection)
    {
        Vector2 forwardDirection = attackDirection.normalized;
        Vector2 sideDirection = new Vector2(-forwardDirection.y, forwardDirection.x);
        float halfLength = lineAttackLength * 0.5f;
        float halfWidth = lineAttackWidth * 0.5f;

        return new[]
        {
            (Vector3)(attackCenter - forwardDirection * halfLength - sideDirection * halfWidth),
            (Vector3)(attackCenter + forwardDirection * halfLength - sideDirection * halfWidth),
            (Vector3)(attackCenter + forwardDirection * halfLength + sideDirection * halfWidth),
            (Vector3)(attackCenter - forwardDirection * halfLength + sideDirection * halfWidth)
        };
    }

    /// <summary>
    /// Общий материал для прямоугольника атаки.
    /// </summary>
    private static Material GetLineVisualMaterial()
    {
        if (lineVisualMaterial != null)
        {
            return lineVisualMaterial;
        }

        Shader spriteShader = Shader.Find("Sprites/Default");

        if (spriteShader == null)
        {
            return null;
        }

        lineVisualMaterial = new Material(spriteShader);
        return lineVisualMaterial;
    }

    /// <summary>
    /// Возвращает центр прямоугольника атаки перед копейщиком.
    /// </summary>
    private Vector2 GetLineAttackCenter(Vector2 attackDirection)
    {
        float centerOffset = lineAttackStartOffsetDistance + lineAttackLength * 0.5f;
        return (Vector2)transform.position + attackDirection.normalized * centerOffset;
    }

    /// <summary>
    /// Возвращает угол прямоугольника атаки для Physics2D и Gizmos.
    /// </summary>
    private float GetLineAttackAngle(Vector2 attackDirection)
    {
        return Mathf.Atan2(attackDirection.y, attackDirection.x) * Mathf.Rad2Deg;
    }

    private void OnDrawGizmosSelected()
    {
        Vector2 attackDirection = lastLineAttackDirection.sqrMagnitude > 0f ? lastLineAttackDirection.normalized : Vector2.right;
        Vector2 attackCenter = (Vector2)transform.position + attackDirection * (lineAttackStartOffsetDistance + lineAttackLength * 0.5f);
        Matrix4x4 previousMatrix = Gizmos.matrix;

        Gizmos.color = Color.red;
        Gizmos.matrix = Matrix4x4.TRS(
            attackCenter,
            Quaternion.Euler(0f, 0f, GetLineAttackAngle(attackDirection)),
            Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(lineAttackLength, lineAttackWidth, 0f));
        Gizmos.matrix = previousMatrix;

        DrawAiMovementGizmos();
        DrawAiAttackGizmos();
    }
}
