using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(GeneratedLevelRuntime))]
[RequireComponent(typeof(GeneratedLevelNavMeshRuntime))]
public sealed class LevelEnemyController : MonoBehaviour
{
    private static readonly Vector3Int[] CardinalCellOffsets =
    {
        Vector3Int.up,
        Vector3Int.down,
        Vector3Int.left,
        Vector3Int.right
    };

    [SerializeField] private GeneratedLevelRuntime generatedLevelRuntime;
    [SerializeField] private GeneratedLevelNavMeshRuntime navMeshRuntime;
    [SerializeField] private DungeonRunController runController;
    [SerializeField] private Transform enemiesRoot;
    [SerializeField] private bool spawnOnBuild = true;
    [SerializeField] private bool destroyOldEnemiesOnRebuild = true;
    [SerializeField] private int defaultEnemyScore = 10;
    [SerializeField] private bool lockUnclearedEnemyRooms = true;
    [SerializeField] private Color roomLockBlockerColor = new Color(0.15f, 0.15f, 0.18f, 0.95f);
    [SerializeField] private bool verboseRoomLockLogs = true;
    [SerializeField] private float repeatedLogIntervalSeconds = 0.75f;

    private readonly Dictionary<string, List<EnemyAgentRuntime>> enemiesByRoomNodeId = new();
    private readonly List<GameObject> spawnedEnemies = new();
    private readonly Dictionary<Health, GameObject> enemiesByHealth = new();
    private readonly Dictionary<Health, string> roomNodeIdByHealth = new();
    private readonly HashSet<string> clearedRoomNodeIds = new();
    private readonly List<GameObject> roomLockBlockers = new();
    private PlayerRoomTracker playerRoomTracker;
    private Transform playerTarget;
    private bool pendingSpawnRebuild;
    private string lockedRoomNodeId;
    private Sprite roomLockBlockerSprite;
    private Coroutine companionTeleportRoutine;
    private float nextRepeatedLogTime;
    private string lastObservedRoomNodeId;

    public event Action<GameObject, int> EnemyKilled;

    private void Update()
    {
        TryRefreshCurrentPlayerRoom();
    }

    private void Awake()
    {
        if (generatedLevelRuntime == null)
            generatedLevelRuntime = GetComponent<GeneratedLevelRuntime>();
        if (navMeshRuntime == null)
            navMeshRuntime = GetComponent<GeneratedLevelNavMeshRuntime>();
        if (runController == null)
            runController = GetComponent<DungeonRunController>();
    }

    private void Reset()
    {
        if (generatedLevelRuntime == null)
            generatedLevelRuntime = GetComponent<GeneratedLevelRuntime>();
        if (navMeshRuntime == null)
            navMeshRuntime = GetComponent<GeneratedLevelNavMeshRuntime>();
        if (runController == null)
            runController = GetComponent<DungeonRunController>();
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
        UnlockCurrentRoom();
    }

    private void HandleGeneratedRoomsChanged()
    {
        LogRoomLock("Generated rooms changed; clearing lock state.", repeated: false);
        UnlockCurrentRoom();
        clearedRoomNodeIds.Clear();

        if (!spawnOnBuild)
        {
            LogRoomLock("Spawn on build is disabled; enemies will not rebuild.", repeated: false);
            return;
        }

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
        LogRoomLock($"Player spawned event. player={(playerInstance != null ? playerInstance.name : "null")}", repeated: false);
        if (playerInstance == null)
        {
            AttachPlayerTracker(null);
            return;
        }

        playerInstance.TryGetComponent<PlayerRoomTracker>(out var tracker);
        LogRoomLock($"Player tracker={(tracker != null ? "found" : "missing")} currentRoom={FormatRoom(tracker != null ? tracker.CurrentRoom : null)}", repeated: false);
        AttachPlayerTracker(tracker);
        ApplyTargetToAllEnemies();
        ApplyDamageTargetsToAllEnemies();
        RefreshRoomActivation(tracker != null ? tracker.CurrentRoom : null);
        TryStartRoomLock(tracker != null ? tracker.CurrentRoom : null);
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
        LogRoomLock("RebuildEnemies started.", repeated: false);
        if (destroyOldEnemiesOnRebuild)
            ClearEnemies();

        if (generatedLevelRuntime == null || generatedLevelRuntime.LastGeneratedRooms == null)
            return;

        var parent = enemiesRoot != null ? enemiesRoot : transform;
        var spawnedCount = 0;
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
                spawnedCount++;

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

                enemyAuthoring.ApplyRuntimeConfiguration(BuildPlayerLayerMask(), GetEnemyStatsMultiplier());

                var agent = enemyInstance.GetComponent<EnemyAgentRuntime>();
                agent.SetTarget(playerTarget);
                agent.SetActiveAI(false);
                SubscribeEnemyDeath(enemyInstance, room.NodeId);

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
        LogRoomLock($"RebuildEnemies finished. spawned={spawnedCount} roomsWithEnemies={enemiesByRoomNodeId.Count} trackerRoom={FormatRoom(playerRoomTracker != null ? playerRoomTracker.CurrentRoom : null)}", repeated: false);
        TryStartRoomLock(playerRoomTracker != null ? playerRoomTracker.CurrentRoom : null);
    }

    private void HandleEnteredRoom(GeneratedRoomInfo roomInfo)
    {
        LogRoomLock($"Entered room event: {FormatRoom(roomInfo)}", repeated: false);
        RefreshRoomActivation(roomInfo);
        TryStartRoomLock(roomInfo);
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
        foreach (var pair in enemiesByHealth)
        {
            if (pair.Key != null)
                pair.Key.Died -= HandleEnemyDied;
        }
        enemiesByHealth.Clear();
        roomNodeIdByHealth.Clear();

        for (var i = 0; i < spawnedEnemies.Count; i++)
        {
            if (spawnedEnemies[i] != null)
                Destroy(spawnedEnemies[i]);
        }

        spawnedEnemies.Clear();
        enemiesByRoomNodeId.Clear();
        UnlockCurrentRoom();
    }

    private void SubscribeEnemyDeath(GameObject enemyInstance, string roomNodeId = null)
    {
        if (enemyInstance == null || !enemyInstance.TryGetComponent<Health>(out var health) || health == null)
            return;

        health.Died -= HandleEnemyDied;
        health.Died += HandleEnemyDied;
        enemiesByHealth[health] = enemyInstance;
        roomNodeIdByHealth[health] = roomNodeId ?? string.Empty;
    }

    private void HandleEnemyDied(Health health)
    {
        if (health == null)
            return;

        health.Died -= HandleEnemyDied;
        enemiesByHealth.TryGetValue(health, out var enemyInstance);
        enemiesByHealth.Remove(health);
        roomNodeIdByHealth.TryGetValue(health, out var roomNodeId);
        roomNodeIdByHealth.Remove(health);

        var score = Mathf.Max(0, defaultEnemyScore);
        if (enemyInstance != null && enemyInstance.TryGetComponent<EnemyScoreValue>(out var scoreValue))
            score = scoreValue.BaseScore;

        EnemyKilled?.Invoke(enemyInstance, score);

        if (!string.IsNullOrEmpty(roomNodeId) && !HasAliveEnemiesInRoom(roomNodeId))
            CompleteRoomCombat(roomNodeId);
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

    private float GetEnemyStatsMultiplier()
    {
        if (runController == null)
            runController = GetComponent<DungeonRunController>();

        return runController != null ? runController.EnemyStatsMultiplier : 1f;
    }

    private void TryStartRoomLock(GeneratedRoomInfo roomInfo)
    {
        if (!lockUnclearedEnemyRooms || roomInfo == null || roomInfo.IsConnector || string.IsNullOrEmpty(roomInfo.NodeId))
        {
            LogRoomLock($"Lock skipped: enabled={lockUnclearedEnemyRooms} room={FormatRoom(roomInfo)}", repeated: true);
            return;
        }

        var aliveEnemies = CountAliveEnemiesInRoom(roomInfo.NodeId);
        if (clearedRoomNodeIds.Contains(roomInfo.NodeId) || aliveEnemies <= 0)
        {
            LogRoomLock($"Lock skipped for {FormatRoom(roomInfo)}: cleared={clearedRoomNodeIds.Contains(roomInfo.NodeId)} aliveEnemies={aliveEnemies}", repeated: true);
            return;
        }

        if (string.Equals(lockedRoomNodeId, roomInfo.NodeId, StringComparison.Ordinal))
        {
            LogRoomLock($"Room already locked. room={FormatRoom(roomInfo)} aliveEnemies={aliveEnemies}", repeated: true);
            return;
        }

        LogRoomLock($"Starting room lock. room={FormatRoom(roomInfo)} aliveEnemies={aliveEnemies}", repeated: false);
        UnlockCurrentRoom();
        lockedRoomNodeId = roomInfo.NodeId;
        StartCompanionTeleportRoutine(roomInfo);
        CloseRoomPassages(roomInfo);
    }

    private void CompleteRoomCombat(string roomNodeId)
    {
        clearedRoomNodeIds.Add(roomNodeId);
        if (string.Equals(lockedRoomNodeId, roomNodeId, StringComparison.Ordinal))
            UnlockCurrentRoom();
    }

    private bool HasAliveEnemiesInRoom(string roomNodeId)
    {
        return CountAliveEnemiesInRoom(roomNodeId) > 0;
    }

    private int CountAliveEnemiesInRoom(string roomNodeId)
    {
        if (string.IsNullOrEmpty(roomNodeId) || !enemiesByRoomNodeId.TryGetValue(roomNodeId, out var enemies))
            return 0;

        var count = 0;
        for (var i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            if (enemy != null && enemy.IsAlive)
                count++;
        }

        return count;
    }

    private void TeleportCompanionsToPlayer(GeneratedRoomInfo roomInfo)
    {
        var partyController = ResolvePlayerPartyController();
        PartyMemberRuntime activeMember = null;
        var anchor = playerTarget != null ? playerTarget.position : transform.position;
        var partyControllerFound = partyController != null;
        var teleportedCount = 0;
        if (partyController != null)
        {
            activeMember = partyController.ActiveMember;
            if (activeMember != null)
                anchor = activeMember.transform.position;

            teleportedCount += partyController.TeleportCompanionsToActiveMember(roomInfo);
        }

        var partyMembers = FindObjectsOfType<PartyMemberRuntime>(true);
        var aliveCount = 0;
        for (var i = 0; i < partyMembers.Length; i++)
        {
            var member = partyMembers[i];
            if (member == null || member == activeMember || !member.IsAlive)
                continue;

            aliveCount++;
            if (partyController == null)
            {
                member.TeleportTo(anchor);
                teleportedCount++;
            }
        }

        LogRoomLock(
            $"Teleport attempt. partyController={partyControllerFound} active={(activeMember != null ? activeMember.name : "null")} anchor={anchor} foundMembers={partyMembers.Length} registeredMembers={PartyMemberRuntime.ActiveMembers.Count} aliveNonActive={aliveCount} teleported={teleportedCount}",
            repeated: false);
    }

    private PlayerPartyController ResolvePlayerPartyController()
    {
        if (generatedLevelRuntime != null &&
            generatedLevelRuntime.SpawnedPlayerInstance != null &&
            generatedLevelRuntime.SpawnedPlayerInstance.TryGetComponent<PlayerPartyController>(out var spawnedPartyController))
        {
            return spawnedPartyController;
        }

        return FindObjectOfType<PlayerPartyController>();
    }

    private void TryRefreshCurrentPlayerRoom()
    {
        if (generatedLevelRuntime == null)
            return;

        var roomPosition = playerTarget != null ? playerTarget.position : transform.position;
        var partyController = ResolvePlayerPartyController();
        if (partyController != null && partyController.ActiveMember != null)
            roomPosition = partyController.ActiveMember.transform.position;

        if (!generatedLevelRuntime.TryGetRoomAtWorldPosition(roomPosition, out var roomInfo) || roomInfo == null)
        {
            LogRoomLock($"No room at player position {roomPosition}. playerTarget={(playerTarget != null ? playerTarget.name : "null")}", repeated: true);
            return;
        }

        if (!string.Equals(lastObservedRoomNodeId, roomInfo.NodeId, StringComparison.Ordinal))
        {
            lastObservedRoomNodeId = roomInfo.NodeId;
            LogRoomLock($"Observed current room: {FormatRoom(roomInfo)} at position={roomPosition}", repeated: false);
        }

        RefreshRoomActivation(roomInfo);
        TryStartRoomLock(roomInfo);
    }

    private void CloseRoomPassages(GeneratedRoomInfo roomInfo)
    {
        if (generatedLevelRuntime == null || generatedLevelRuntime.GraphMapBuilder == null || generatedLevelRuntime.GraphMapBuilder.targetGrid == null)
            return;
        if (roomInfo.FloorCells == null)
            return;

        var blockedCells = new HashSet<Vector3Int>();
        foreach (var floorCell in roomInfo.FloorCells)
        {
            for (var i = 0; i < CardinalCellOffsets.Length; i++)
            {
                var neighborCell = floorCell + CardinalCellOffsets[i];
                if (!generatedLevelRuntime.TryGetRoomAtCell(neighborCell, out var neighborRoom) ||
                    neighborRoom == null ||
                    ReferenceEquals(neighborRoom, roomInfo) ||
                    string.Equals(neighborRoom.NodeId, roomInfo.NodeId, StringComparison.Ordinal))
                {
                    continue;
                }

                blockedCells.Add(neighborCell);
            }
        }

        foreach (var blockedCell in blockedCells)
            CreateRoomLockBlocker(blockedCell);

        LogRoomLock($"Closed room passages. room={FormatRoom(roomInfo)} blockerCells={blockedCells.Count}", repeated: false);
    }

    private void CreateRoomLockBlocker(Vector3Int cell)
    {
        var grid = generatedLevelRuntime.GraphMapBuilder.targetGrid;
        var blocker = new GameObject("RoomLockBlocker");
        blocker.transform.SetParent(grid.transform, false);
        blocker.transform.position = grid.GetCellCenterWorld(cell);

        var collider2D = blocker.AddComponent<BoxCollider2D>();
        collider2D.size = Vector2.one;

        var renderer = blocker.AddComponent<SpriteRenderer>();
        renderer.sprite = GetRoomLockBlockerSprite();
        renderer.color = roomLockBlockerColor;
        renderer.sortingOrder = 70;

        roomLockBlockers.Add(blocker);
    }

    private Sprite GetRoomLockBlockerSprite()
    {
        if (roomLockBlockerSprite != null)
            return roomLockBlockerSprite;

        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "RoomLockBlocker",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixel(0, 0, Color.white);
        texture.Apply(false, true);
        roomLockBlockerSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        return roomLockBlockerSprite;
    }

    private void UnlockCurrentRoom()
    {
        if (!string.IsNullOrEmpty(lockedRoomNodeId) || roomLockBlockers.Count > 0)
            LogRoomLock($"Unlocking room. room={lockedRoomNodeId} blockers={roomLockBlockers.Count}", repeated: false);

        for (var i = 0; i < roomLockBlockers.Count; i++)
        {
            if (roomLockBlockers[i] != null)
                Destroy(roomLockBlockers[i]);
        }

        roomLockBlockers.Clear();
        lockedRoomNodeId = string.Empty;
        StopCompanionTeleportRoutine();
    }

    private void StartCompanionTeleportRoutine(GeneratedRoomInfo roomInfo = null)
    {
        StopCompanionTeleportRoutine();
        companionTeleportRoutine = StartCoroutine(TeleportCompanionsToPlayerRoutine(roomInfo));
    }

    private void StopCompanionTeleportRoutine()
    {
        if (companionTeleportRoutine == null)
            return;

        StopCoroutine(companionTeleportRoutine);
        companionTeleportRoutine = null;
    }

    private IEnumerator TeleportCompanionsToPlayerRoutine(GeneratedRoomInfo roomInfo)
    {
        TeleportCompanionsToPlayer(roomInfo);
        yield return new WaitForFixedUpdate();
        TeleportCompanionsToPlayer(roomInfo);
        companionTeleportRoutine = null;
    }

    private void LogRoomLock(string message, bool repeated)
    {
        if (!verboseRoomLockLogs)
            return;
        if (repeated && Time.unscaledTime < nextRepeatedLogTime)
            return;

        if (repeated)
            nextRepeatedLogTime = Time.unscaledTime + Mathf.Max(0.1f, repeatedLogIntervalSeconds);

        Debug.Log($"[LevelEnemyController/RoomLock] {message}", this);
    }

    private static string FormatRoom(GeneratedRoomInfo roomInfo)
    {
        if (roomInfo == null)
            return "null";

        return $"{roomInfo.NodeId} prefab={roomInfo.PrefabName} connector={roomInfo.IsConnector} hasSpawns={roomInfo.HasEnemySpawns}";
    }
}
