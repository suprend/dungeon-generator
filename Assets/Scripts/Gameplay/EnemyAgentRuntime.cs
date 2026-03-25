using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Rigidbody2D))]
public sealed class EnemyAgentRuntime : MonoBehaviour
{
    private static readonly List<EnemyAgentRuntime> ActiveEnemyAgents = new();

    [SerializeField] private bool activeAI;
    [SerializeField] private float reacquireInterval = 0.2f;
    [SerializeField] private float stopDistance = 0.6f;

    private NavMeshAgent navMeshAgent;
    private Rigidbody2D body2D;
    private Health health;
    private Transform target;
    private float nextPathRefreshTime;
    private Coroutine activeKnockbackRoutine;

    public Transform Target => target;
    public bool ActiveAI => activeAI;
    public bool IsAlive => health == null || !health.IsDead;
    public static IReadOnlyList<EnemyAgentRuntime> ActiveAgents => ActiveEnemyAgents;

    private void Awake()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        body2D = GetComponent<Rigidbody2D>();
        health = GetComponent<Health>();

        if (navMeshAgent != null)
        {
            navMeshAgent.updateRotation = false;
            navMeshAgent.updateUpAxis = false;
            navMeshAgent.stoppingDistance = stopDistance;
        }

        if (body2D != null)
        {
            body2D.gravityScale = 0f;
            body2D.bodyType = RigidbodyType2D.Kinematic;
            body2D.constraints |= RigidbodyConstraints2D.FreezeRotation;
        }

        SetActiveAI(activeAI);
    }

    private void OnEnable()
    {
        if (!ActiveEnemyAgents.Contains(this))
            ActiveEnemyAgents.Add(this);
    }

    private void OnDisable()
    {
        ActiveEnemyAgents.Remove(this);
    }

    private void Update()
    {
        TickPathUpdate();
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        nextPathRefreshTime = 0f;
    }

    public void SetActiveAI(bool isActive)
    {
        activeAI = isActive;
        if (navMeshAgent == null)
            return;
        if (!navMeshAgent.isActiveAndEnabled || !navMeshAgent.isOnNavMesh)
            return;

        navMeshAgent.isStopped = !activeAI;
        if (!activeAI)
            navMeshAgent.ResetPath();
    }

    public void Configure(float newReacquireInterval, float newStopDistance)
    {
        reacquireInterval = Mathf.Max(0.05f, newReacquireInterval);
        stopDistance = Mathf.Max(0f, newStopDistance);

        if (navMeshAgent != null)
            navMeshAgent.stoppingDistance = stopDistance;
    }

    public void ApplyKnockback(Vector2 direction, float distance, float duration)
    {
        if (direction.sqrMagnitude <= 0.0001f || distance <= 0.001f || duration <= 0.001f)
            return;

        if (activeKnockbackRoutine != null)
            StopCoroutine(activeKnockbackRoutine);

        activeKnockbackRoutine = StartCoroutine(KnockbackRoutine(direction.normalized, distance, duration));
    }

    public void TickPathUpdate()
    {
        if (!activeAI || target == null || navMeshAgent == null || !navMeshAgent.isOnNavMesh)
            return;
        if (Time.time < nextPathRefreshTime)
            return;

        nextPathRefreshTime = Time.time + Mathf.Max(0.05f, reacquireInterval);
        navMeshAgent.stoppingDistance = stopDistance;
        navMeshAgent.SetDestination(target.position);
    }

    private IEnumerator KnockbackRoutine(Vector2 direction, float distance, float duration)
    {
        var wasActiveAI = activeAI;
        SetActiveAI(false);

        if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled)
            navMeshAgent.updatePosition = false;

        var startPosition = body2D != null ? body2D.position : (Vector2)transform.position;
        var targetPosition = startPosition + direction * distance;
        var elapsed = 0f;

        while (elapsed < duration)
        {
            yield return new WaitForFixedUpdate();
            elapsed += Time.fixedDeltaTime;

            var nextPosition = Vector2.Lerp(startPosition, targetPosition, Mathf.Clamp01(elapsed / duration));
            if (body2D != null)
                body2D.MovePosition(nextPosition);
            else
                transform.position = nextPosition;
        }

        if (navMeshAgent != null && navMeshAgent.isActiveAndEnabled)
        {
            navMeshAgent.updatePosition = true;
            if (navMeshAgent.isOnNavMesh)
                navMeshAgent.Warp(transform.position);
        }

        nextPathRefreshTime = 0f;
        SetActiveAI(wasActiveAI);
        activeKnockbackRoutine = null;
    }
}
