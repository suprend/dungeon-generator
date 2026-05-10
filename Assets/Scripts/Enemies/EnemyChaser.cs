//DP
using UnityEngine;

/// <summary>
/// Противник для дебага
/// </summary>
public class EnemyChaser : EnemyTemplate
{
    [Header("Преследование")]
    [SerializeField] private PlayerCharacterSwitcher characterSwitcher;
    [SerializeField] private bool findSwitcherOnStart = true;
    [SerializeField, Min(0f)] private float stopDistance = 0.35f;

    private Rigidbody2D enemyRigidbody;

    private void Start()
    {
        CacheRigidbody();

        if (findSwitcherOnStart)
        {
            FindCharacterSwitcher();
        }
    }

    private void FixedUpdate()
    {
        if (!IsAlive || IsExternalMovementActive)
        {
            return;
        }

        if (characterSwitcher == null && findSwitcherOnStart)
        {
            FindCharacterSwitcher();
        }

        PlayerCharacterTemplate targetCharacter =
            characterSwitcher != null ? characterSwitcher.CurrentCharacter : null;

        if (targetCharacter == null || !targetCharacter.IsAlive)
        {
            return;
        }

        MoveToTarget(targetCharacter.transform.position);
    }

    private void FindCharacterSwitcher()
    {
        characterSwitcher = FindFirstObjectByType<PlayerCharacterSwitcher>();
    }

    private void MoveToTarget(Vector2 targetPosition)
    {
        if (enemyRigidbody == null)
        {
            CacheRigidbody();
        }

        if (enemyRigidbody == null)
        {
            return;
        }

        Vector2 directionToTarget = targetPosition - enemyRigidbody.position;

        if (directionToTarget.sqrMagnitude <= stopDistance * stopDistance)
        {
            return;
        }

        Vector2 moveDirection = directionToTarget.normalized;
        Vector2 nextPosition = enemyRigidbody.position + moveDirection * Speed * Time.fixedDeltaTime;
        enemyRigidbody.MovePosition(nextPosition);
    }

    private void CacheRigidbody()
    {
        enemyRigidbody = GetComponent<Rigidbody2D>();
    }
}
