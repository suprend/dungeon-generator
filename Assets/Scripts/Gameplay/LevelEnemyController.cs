using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(GeneratedLevelRuntime))]
[RequireComponent(typeof(GeneratedLevelNavMeshRuntime))]
public sealed class LevelEnemyController : MonoBehaviour
{
    [SerializeField] private GeneratedLevelRuntime generatedLevelRuntime;
    [SerializeField] private GeneratedLevelNavMeshRuntime navMeshRuntime;
    [SerializeField] private Transform enemiesRoot;
    [SerializeField] private bool spawnOnBuild = true;
    [SerializeField] private bool destroyOldEnemiesOnRebuild = true;

    private readonly Dictionary<string, List<EnemyAgentRuntime>> enemiesByRoomNodeId = new();
    private readonly List<GameObject> spawnedEnemies = new();
    private PlayerRoomTracker playerRoomTracker;
    private Transform playerTarget;
    private bool pendingSpawnRebuild;

    private void Awake()
    {
        if (generatedLevelRuntime == null)
            generatedLevelRuntime = GetComponent<GeneratedLevelRuntime>();
        if (navMeshRuntime == null)
            navMeshRuntime = GetComponent<GeneratedLevelNavMeshRuntime>();
    }

    private void Reset()
    {
        if (generatedLevelRuntime == null)
            generatedLevelRuntime = GetComponent<GeneratedLevelRuntime>();
        if (navMeshRuntime == null)
            navMeshRuntime = GetComponent<GeneratedLevelNavMeshRuntime>();
    }

    private void OnEnable()
    {
        if (generatedLevelRuntime != null)
        {
            generatedLevelRuntime.GeneratedRoomsChanged += HandleGeneratedRoomsChanged;
            generatedLevelRuntime.PlayerSpawned += HandlePlayerSpawned;
        }
        if (navMeshRuntime != null)
            navMeshRuntime.NavMeshBuilt += HandleNavMeshBuilt;

        TryAttachCurrentPlayer();
        if (spawnOnBuild && generatedLevelRuntime != null && generatedLevelRuntime.LastGeneratedRooms.Count > 0)
        {
            if (navMeshRuntime != null && navMeshRuntime.IsNavMeshReady)
                RebuildEnemies();
            else
                pendingSpawnRebuild = true;
        }
    }

    private void OnDisable()
    {
        if (generatedLevelRuntime != null)
        {
            generatedLevelRuntime.GeneratedRoomsChanged -= HandleGeneratedRoomsChanged;
            generatedLevelRuntime.PlayerSpawned -= HandlePlayerSpawned;
        }
        if (navMeshRuntime != null)
            navMeshRuntime.NavMeshBuilt -= HandleNavMeshBuilt;

        AttachPlayerTracker(null);
    }

    private void HandleGeneratedRoomsChanged()
    {
        if (!spawnOnBuild)
            return;

        pendingSpawnRebuild = true;
        if (destroyOldEnemiesOnRebuild)
            ClearEnemies();

        if (navMeshRuntime != null && navMeshRuntime.IsNavMeshReady)
        {
            RebuildEnemies();
            pendingSpawnRebuild = false;
        }
    }

    private void HandlePlayerSpawned(GameObject playerInstance)
    {
        playerTarget = playerInstance != null ? playerInstance.transform : null;
        if (playerInstance == null)
        {
            AttachPlayerTracker(null);
            return;
        }

        playerInstance.TryGetComponent<PlayerRoomTracker>(out var tracker);
        AttachPlayerTracker(tracker);
        ApplyTargetToAllEnemies();
        ApplyDamageTargetsToAllEnemies();
        RefreshRoomActivation(tracker != null ? tracker.CurrentRoom : null);
    }

    private void HandleNavMeshBuilt()
    {
        if (pendingSpawnRebuild)
        {
            pendingSpawnRebuild = false;
            RebuildEnemies();
            return;
        }

        RefreshRoomActivation(playerRoomTracker != null ? playerRoomTracker.CurrentRoom : null);
    }

    public void RebuildEnemies()
    {
        if (destroyOldEnemiesOnRebuild)
            ClearEnemies();

        if (generatedLevelRuntime == null || generatedLevelRuntime.LastGeneratedRooms == null)
            return;

        var parent = enemiesRoot != null ? enemiesRoot : transform;
        var rooms = generatedLevelRuntime.LastGeneratedRooms;
        for (var i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            if (room == null || room.IsConnector || !room.HasEnemySpawns || room.EnemySpawns == null)
                continue;

            for (var j = 0; j < room.EnemySpawns.Count; j++)
            {
                var spawnInfo = room.EnemySpawns[j];
                if (spawnInfo == null || !spawnInfo.Enabled || spawnInfo.EnemyPrefab == null)
                    continue;
                if (!spawnInfo.EnemyPrefab.TryGetComponent<EnemyAuthoring>(out _))
                {
                    Debug.LogWarning($"[LevelEnemyController] Enemy prefab '{spawnInfo.EnemyPrefab.name}' has no EnemyAuthoring and was skipped.");
                    continue;
                }

                var enemyInstance = Instantiate(
                    spawnInfo.EnemyPrefab,
                    spawnInfo.SpawnWorldPosition,
                    Quaternion.Euler(0f, 0f, spawnInfo.FacingDegrees),
                    parent);

                spawnedEnemies.Add(enemyInstance);

                if (enemyInstance.TryGetComponent<TopDownPlayerController>(out var playerController))
                    playerController.enabled = false;
                if (enemyInstance.TryGetComponent<PlayerRoomTracker>(out var roomTracker))
                    roomTracker.enabled = false;
                if (!enemyInstance.TryGetComponent<EnemyAuthoring>(out var enemyAuthoring))
                {
                    Debug.LogWarning($"[LevelEnemyController] Spawned enemy '{enemyInstance.name}' has no EnemyAuthoring and was destroyed.");
                    Destroy(enemyInstance);
                    spawnedEnemies.RemoveAt(spawnedEnemies.Count - 1);
                    continue;
                }

                enemyAuthoring.ApplyRuntimeConfiguration(BuildPlayerLayerMask());

                var agent = enemyInstance.GetComponent<EnemyAgentRuntime>();
                agent.SetTarget(playerTarget);
                agent.SetActiveAI(false);

                if (!enemiesByRoomNodeId.TryGetValue(room.NodeId, out var list))
                {
                    list = new List<EnemyAgentRuntime>();
                    enemiesByRoomNodeId[room.NodeId] = list;
                }

                list.Add(agent);
            }
        }

        ApplyTargetToAllEnemies();
        ApplyDamageTargetsToAllEnemies();
        RefreshRoomActivation(playerRoomTracker != null ? playerRoomTracker.CurrentRoom : null);
    }

    private void HandleEnteredRoom(GeneratedRoomInfo roomInfo)
    {
        RefreshRoomActivation(roomInfo);
    }

    private void RefreshRoomActivation(GeneratedRoomInfo activeRoom)
    {
        var canActivate = navMeshRuntime != null && navMeshRuntime.IsNavMeshReady && playerTarget != null && activeRoom != null && !activeRoom.IsConnector;

        foreach (var pair in enemiesByRoomNodeId)
        {
            var shouldBeActive = canActivate && string.Equals(pair.Key, activeRoom.NodeId, System.StringComparison.Ordinal);
            var enemies = pair.Value;
            for (var i = 0; i < enemies.Count; i++)
            {
                if (enemies[i] == null)
                    continue;
                enemies[i].SetActiveAI(shouldBeActive);
            }
        }
    }

    private void TryAttachCurrentPlayer()
    {
        if (generatedLevelRuntime == null || generatedLevelRuntime.SpawnedPlayerInstance == null)
            return;

        HandlePlayerSpawned(generatedLevelRuntime.SpawnedPlayerInstance);
    }

    private void ClearEnemies()
    {
        for (var i = 0; i < spawnedEnemies.Count; i++)
        {
            if (spawnedEnemies[i] != null)
                Destroy(spawnedEnemies[i]);
        }

        spawnedEnemies.Clear();
        enemiesByRoomNodeId.Clear();
    }

    private void AttachPlayerTracker(PlayerRoomTracker tracker)
    {
        if (playerRoomTracker != null)
            playerRoomTracker.EnteredRoom -= HandleEnteredRoom;

        playerRoomTracker = tracker;

        if (playerRoomTracker != null)
            playerRoomTracker.EnteredRoom += HandleEnteredRoom;
    }

    private void ApplyTargetToAllEnemies()
    {
        foreach (var pair in enemiesByRoomNodeId)
        {
            var enemies = pair.Value;
            for (var i = 0; i < enemies.Count; i++)
            {
                if (enemies[i] == null)
                    continue;
                enemies[i].SetTarget(playerTarget);
            }
        }
    }

    private void ApplyDamageTargetsToAllEnemies()
    {
        var targetLayers = BuildPlayerLayerMask();
        for (var i = 0; i < spawnedEnemies.Count; i++)
        {
            var enemy = spawnedEnemies[i];
            if (enemy == null || !enemy.TryGetComponent<EnemyAuthoring>(out var enemyAuthoring))
                continue;

            enemyAuthoring.ApplyTargetLayers(targetLayers);
        }
    }

    private LayerMask BuildPlayerLayerMask()
    {
        if (playerTarget == null)
            return 0;

        return 1 << playerTarget.gameObject.layer;
    }
}
