using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(TopDownPlayerController))]
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class PartyMemberRuntime : MonoBehaviour
{
    [Header("Follow")]
    [SerializeField] private float followDistance = 1.8f;
    [SerializeField] private float teleportDistance = 10f;
    [SerializeField] private float navMeshRepathInterval = 0.15f;
    [SerializeField] private float navMeshSampleRadius = 2f;

    [Header("Combat")]
    [SerializeField] private float attackDistance = 8f;

    [Header("References")]
    [SerializeField] private TopDownPlayerController playerController;
    [SerializeField] private PlayerBowAttack bowAttack;
    [SerializeField] private PlayerClassRuntime playerClassRuntime;
    [SerializeField] private Health health;
    [SerializeField] private Rigidbody2D body2D;
    [SerializeField] private PlayerRoomTracker playerRoomTracker;
    [SerializeField] private NavMeshAgent navMeshAgent;

    private PlayerPartyController partyOwner;
    private bool navMeshControlActive;
    private float nextRepathTime;
    private NavMeshPath navMeshPath;

    public bool IsAlive => health == null || !health.IsDead;

    private void Reset()
    {
        CacheComponents();
    }

    private void Awake()
    {
        CacheComponents();
        if (navMeshPath == null)
            navMeshPath = new NavMeshPath();
    }

    private void OnDestroy()
    {
        if (health != null)
            health.Died -= HandleDeath;
    }

    public void Initialize(PlayerPartyController owner)
    {
        CacheComponents();

        partyOwner = owner;

        if (health != null)
        {
            health.Died -= HandleDeath;
            health.Died += HandleDeath;
        }

        if (playerRoomTracker != null)
            playerRoomTracker.enabled = false;

        if (playerController != null)
            playerController.SuppressAutoDeathMenu = true;

        SetNavMeshActive(false);
    }

    public void SetPlayerControlled(bool isPlayerControlled)
    {
        CacheComponents();

        if (playerController != null)
        {
            playerController.SuppressAutoDeathMenu = true;
            playerController.SetPlayerInputEnabled(isPlayerControlled);
            playerController.SetExternalMoveInput(Vector2.zero);
            playerController.enabled = true;
        }

        if (bowAttack != null)
            bowAttack.SetPlayerInputEnabled(isPlayerControlled);

        if (playerClassRuntime != null)
            playerClassRuntime.SetPlayerInputEnabled(isPlayerControlled);

        SetNavMeshActive(!isPlayerControlled && IsAlive);
    }

    public void TickCompanionAI(Transform leader, Vector2 formationOffset)
    {
        var targetPosition = leader != null ? (Vector2)leader.position + formationOffset : (Vector2)transform.position;
        TickCompanionAI(targetPosition);
    }

    public void TickCompanionAI(Vector2 targetPosition)
    {
        CacheComponents();

        if (!IsAlive)
        {
            SetNavMeshActive(false);
            if (playerController != null)
                playerController.SetExternalMoveInput(Vector2.zero);

            return;
        }

        var toTarget = targetPosition - (Vector2)transform.position;

        if (partyOwner != null &&
            partyOwner.AllowCompanionTeleportToExploredRooms &&
            toTarget.magnitude > teleportDistance)
        {
            if (partyOwner != null && partyOwner.TryGetSafeTeleportPosition(targetPosition, out var safeTeleportPosition))
            {
                if (body2D != null)
                {
                    body2D.position = safeTeleportPosition;
                    body2D.velocity = Vector2.zero;
                }
                else
                {
                    transform.position = safeTeleportPosition;
                }

                WarpAgentToCurrentPosition();
                toTarget = Vector2.zero;
            }
        }

        SyncAgentToTransform();
        UpdateNavMeshDestination(targetPosition);
        var moveInput = ResolveNavMeshMoveInput(toTarget);

        if (playerController != null)
            playerController.SetExternalMoveInput(moveInput);

        var nearestEnemy = FindNearestEnemy();
        if (nearestEnemy != null)
        {
            var enemyDirection = (Vector2)(nearestEnemy.transform.position - transform.position);
            if (enemyDirection.sqrMagnitude > 0.0001f)
            {
                if (playerController != null)
                    playerController.SetFacingX(enemyDirection.x);

                if (enemyDirection.magnitude <= attackDistance && bowAttack != null)
                    bowAttack.FireOnce(enemyDirection.normalized);
            }
        }
        else if (playerController != null && moveInput.sqrMagnitude > 0.0001f)
        {
            playerController.SetFacingX(moveInput.x);
        }
    }

    private EnemyAgentRuntime FindNearestEnemy()
    {
        EnemyAgentRuntime bestEnemy = null;
        var bestSqrDistance = float.MaxValue;
        var activeEnemies = EnemyAgentRuntime.ActiveAgents;

        for (var i = 0; i < activeEnemies.Count; i++)
        {
            var enemy = activeEnemies[i];
            if (enemy == null || !enemy.IsAlive)
                continue;

            var sqrDistance = ((Vector2)enemy.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (sqrDistance >= bestSqrDistance)
                continue;

            bestSqrDistance = sqrDistance;
            bestEnemy = enemy;
        }

        return bestEnemy;
    }

    private void HandleDeath(Health _)
    {
        SetNavMeshActive(false);

        if (playerController != null)
            playerController.SetExternalMoveInput(Vector2.zero);

        partyOwner?.HandleMemberDeath(this);
    }

    private void CacheComponents()
    {
        if (playerController == null)
            playerController = GetComponent<TopDownPlayerController>();
        if (bowAttack == null)
            bowAttack = GetComponent<PlayerBowAttack>();
        if (playerClassRuntime == null)
            playerClassRuntime = GetComponent<PlayerClassRuntime>();
        if (health == null)
            health = GetComponent<Health>();
        if (body2D == null)
            body2D = GetComponent<Rigidbody2D>();
        if (playerRoomTracker == null)
            playerRoomTracker = GetComponent<PlayerRoomTracker>();
        if (navMeshAgent == null)
            navMeshAgent = GetComponent<NavMeshAgent>();

        if (navMeshAgent != null)
        {
            if (navMeshPath == null)
                navMeshPath = new NavMeshPath();

            navMeshAgent.updatePosition = false;
            navMeshAgent.updateRotation = false;
            navMeshAgent.updateUpAxis = false;
            navMeshAgent.stoppingDistance = followDistance;
            navMeshAgent.speed = playerController != null ? Mathf.Max(0.01f, playerController.MoveSpeed) : 5f;
            navMeshAgent.acceleration = Mathf.Max(navMeshAgent.speed * 3f, 8f);
            navMeshAgent.angularSpeed = 120f;
        }
    }

    private void SetNavMeshActive(bool isActive)
    {
        if (navMeshAgent == null)
            return;

        if (navMeshControlActive == isActive)
        {
            if (isActive)
            {
                navMeshAgent.speed = playerController != null ? Mathf.Max(0.01f, playerController.MoveSpeed) : navMeshAgent.speed;
                navMeshAgent.stoppingDistance = followDistance;
            }

            return;
        }

        navMeshControlActive = isActive;

        if (isActive)
        {
            navMeshAgent.enabled = true;
            navMeshAgent.speed = playerController != null ? Mathf.Max(0.01f, playerController.MoveSpeed) : navMeshAgent.speed;
            nextRepathTime = 0f;
            WarpAgentToCurrentPosition();
            StopNavMeshMovement();
            return;
        }

        if (navMeshAgent.enabled)
        {
            StopNavMeshMovement();
            navMeshAgent.enabled = false;
        }
    }

    private void StopNavMeshMovement()
    {
        if (navMeshAgent == null || !navMeshAgent.enabled)
            return;

        if (navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.isStopped = true;
            navMeshAgent.ResetPath();
        }
    }

    private void UpdateNavMeshDestination(Vector2 targetPosition)
    {
        if (navMeshAgent == null || !navMeshAgent.enabled)
            return;

        if (!EnsureAgentOnNavMesh())
            return;

        if (Time.time < nextRepathTime)
            return;

        nextRepathTime = Time.time + Mathf.Max(0.05f, navMeshRepathInterval);
        navMeshAgent.stoppingDistance = followDistance;
        navMeshAgent.isStopped = false;

        var destination = ResolveFollowDestination(targetPosition);
        navMeshAgent.SetDestination(destination);
    }

    private bool EnsureAgentOnNavMesh()
    {
        if (navMeshAgent == null || !navMeshAgent.enabled)
            return false;

        if (navMeshAgent.isOnNavMesh)
            return true;

        if (!TrySampleNavMeshPosition(transform.position, GetAgentSnapSampleRadius(), out var sampledPosition))
            return false;

        return navMeshAgent.Warp(sampledPosition);
    }

    private void WarpAgentToCurrentPosition()
    {
        if (navMeshAgent == null || !navMeshAgent.enabled)
            return;

        if (TrySampleNavMeshPosition(transform.position, GetAgentSnapSampleRadius(), out var sampledPosition))
            navMeshAgent.Warp(sampledPosition);
    }

    private void SyncAgentToTransform()
    {
        if (navMeshAgent == null || !navMeshAgent.enabled)
            return;

        navMeshAgent.nextPosition = transform.position;
    }

    private bool TrySampleNavMeshPosition(Vector3 sourcePosition, out Vector3 sampledPosition)
    {
        return TrySampleNavMeshPosition(sourcePosition, navMeshSampleRadius, out sampledPosition);
    }

    private bool TrySampleNavMeshPosition(Vector3 sourcePosition, float sampleRadius, out Vector3 sampledPosition)
    {
        if (NavMesh.SamplePosition(sourcePosition, out var hit, Mathf.Max(0.05f, sampleRadius), NavMesh.AllAreas))
        {
            sampledPosition = hit.position;
            return true;
        }

        sampledPosition = sourcePosition;
        return false;
    }

    private Vector2 ResolveNavMeshMoveInput(Vector2 directFallback)
    {
        if (navMeshAgent != null && navMeshAgent.enabled)
        {
            if (!navMeshAgent.isOnNavMesh || navMeshAgent.pathPending)
                return Vector2.zero;

            if (navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance)
                return Vector2.zero;

            if (TryGetNavMeshPathDirection(out var navMeshDirection))
                return navMeshDirection;

            return Vector2.zero;
        }

        return directFallback.magnitude > followDistance ? directFallback.normalized : Vector2.zero;
    }

    private bool TryGetNavMeshPathDirection(out Vector2 direction)
    {
        direction = Vector2.zero;

        if (navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.hasPath)
            return false;

        var corners = navMeshAgent.path.corners;
        if (corners == null || corners.Length == 0)
            return false;

        var currentPosition = (Vector2)transform.position;
        var cornerReachDistance = Mathf.Max(navMeshAgent.radius * 0.5f, 0.1f);

        for (var i = 0; i < corners.Length; i++)
        {
            var toCorner = (Vector2)corners[i] - currentPosition;
            if (toCorner.magnitude <= cornerReachDistance)
                continue;

            direction = toCorner.normalized;
            return true;
        }

        return false;
    }

    private Vector3 ResolveFollowDestination(Vector2 preferredTargetPosition)
    {
        var targetSampleRadius = GetTargetSampleRadius();

        if (TrySampleNavMeshPosition(preferredTargetPosition, targetSampleRadius, out var sampledPreferredPosition) &&
            HasCompletePathTo(sampledPreferredPosition))
        {
            return sampledPreferredPosition;
        }

        // In narrow corridors formation offsets often point into walls or bad partial paths.
        // Falling back to the leader cell keeps companions moving instead of sticking on corners.
        var leaderPosition = partyOwner != null && partyOwner.ActiveMember != null
            ? partyOwner.ActiveMember.transform.position
            : (Vector3)preferredTargetPosition;

        if (TrySampleNavMeshPosition(leaderPosition, targetSampleRadius, out var sampledLeaderPosition) &&
            HasCompletePathTo(sampledLeaderPosition))
            return sampledLeaderPosition;

        return transform.position;
    }

    private bool HasCompletePathTo(Vector3 destination)
    {
        if (navMeshAgent == null || !navMeshAgent.enabled)
            return false;

        if (navMeshPath == null)
            navMeshPath = new NavMeshPath();

        if (!navMeshAgent.CalculatePath(destination, navMeshPath))
            return false;

        return navMeshPath.status == NavMeshPathStatus.PathComplete;
    }

    private float GetAgentSnapSampleRadius()
    {
        var agentRadius = navMeshAgent != null ? navMeshAgent.radius : 0.35f;
        return Mathf.Min(navMeshSampleRadius, Mathf.Max(0.15f, agentRadius * 0.75f));
    }

    private float GetTargetSampleRadius()
    {
        var agentRadius = navMeshAgent != null ? navMeshAgent.radius : 0.35f;
        var minRadius = Mathf.Max(0.2f, agentRadius);
        var maxRadius = Mathf.Max(minRadius, followDistance);
        return Mathf.Min(navMeshSampleRadius, maxRadius);
    }
}
