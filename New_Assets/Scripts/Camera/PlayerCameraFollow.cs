//DP
using UnityEngine;

/// <summary>
/// Двигает камеру за активным персонажем
/// </summary>
[RequireComponent(typeof(Camera))]
public class PlayerCameraFollow : MonoBehaviour
{
    [Header("Цель")]
    [SerializeField] private PlayerCharacterSwitcher characterSwitcher;
    [SerializeField] private Transform target;

    [Header("Движение камеры")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);
    [SerializeField, Min(0f)] private float followSmoothTime = 0.12f;

    private Transform currentTarget;
    private Vector3 followVelocity;

    private void Awake()
    {
        if (characterSwitcher == null)
        {
            characterSwitcher = FindFirstObjectByType<PlayerCharacterSwitcher>();
        }

        currentTarget = target;
    }

    private void LateUpdate()
    {
        RefreshTarget();

        if (currentTarget == null)
        {
            return;
        }

        Vector3 targetPosition = currentTarget.position + offset;

        if (followSmoothTime <= 0f)
        {
            transform.position = targetPosition;
            return;
        }

        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPosition,
            ref followVelocity,
            followSmoothTime
        );
    }

    /// <summary>
    /// Обновляет цель камеры
    /// </summary>
    private void RefreshTarget()
    {
        PlayerCharacterTemplate activeCharacter =
            characterSwitcher != null ? characterSwitcher.CurrentCharacter : null;

        Transform nextTarget = activeCharacter != null ? activeCharacter.transform : target;

        if (currentTarget == nextTarget)
        {
            return;
        }

        currentTarget = nextTarget;
        followVelocity = Vector3.zero;
    }
}
