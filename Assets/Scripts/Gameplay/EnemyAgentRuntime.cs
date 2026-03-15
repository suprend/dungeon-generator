using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Rigidbody2D))]
public sealed class EnemyAgentRuntime : MonoBehaviour
{
    [SerializeField] private bool activeAI;
    [SerializeField] private float reacquireInterval = 0.2f;
    [SerializeField] private float stopDistance = 0.6f;

    private NavMeshAgent navMeshAgent;
    private Rigidbody2D body2D;
    private Transform target;
    private float nextPathRefreshTime;

    public Transform Target => target;
    public bool ActiveAI => activeAI;

    private void Awake()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        body2D = GetComponent<Rigidbody2D>();

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
}
