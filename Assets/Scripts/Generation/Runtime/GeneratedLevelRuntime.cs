using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(GraphMapBuilder))]
public sealed class GeneratedLevelRuntime : MonoBehaviour
{
    [SerializeField] private GraphMapBuilder graphMapBuilder;
    [SerializeField] private bool spawnPlayerAfterBuild;
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Component cinemachineCamera;

    private static readonly IReadOnlyList<GeneratedRoomInfo> EmptyRooms = Array.Empty<GeneratedRoomInfo>();

    private readonly Dictionary<Vector3Int, GeneratedRoomInfo> roomByFloorCell = new();
    private GameObject spawnedPlayerInstance;

    public GraphMapBuilder GraphMapBuilder => graphMapBuilder;
    public IReadOnlyList<GeneratedRoomInfo> LastGeneratedRooms => graphMapBuilder != null ? graphMapBuilder.LastGeneratedRooms : EmptyRooms;
    public GameObject SpawnedPlayerInstance => spawnedPlayerInstance;
    public event Action GeneratedRoomsChanged;
    public event Action<GameObject> PlayerSpawned;

    private void Awake()
    {
        if (graphMapBuilder == null)
            graphMapBuilder = GetComponent<GraphMapBuilder>();
    }

    private void Reset()
    {
        if (graphMapBuilder == null)
            graphMapBuilder = GetComponent<GraphMapBuilder>();
    }

    private void OnEnable()
    {
        if (graphMapBuilder != null)
            graphMapBuilder.GeneratedRoomsChanged += HandleGeneratedRoomsChanged;

        HandleGeneratedRoomsChanged();
    }

    private void OnDisable()
    {
        if (graphMapBuilder != null)
            graphMapBuilder.GeneratedRoomsChanged -= HandleGeneratedRoomsChanged;
    }

    public bool TryGetRoomAtWorldPosition(Vector3 worldPosition, out GeneratedRoomInfo roomInfo)
    {
        roomInfo = null;
        if (graphMapBuilder == null || graphMapBuilder.targetGrid == null)
            return false;

        var cell = graphMapBuilder.targetGrid.WorldToCell(worldPosition);
        return roomByFloorCell.TryGetValue(cell, out roomInfo);
    }

    public bool TryGetRoomAtCell(Vector3Int cell, out GeneratedRoomInfo roomInfo)
    {
        return roomByFloorCell.TryGetValue(cell, out roomInfo);
    }

    private void HandleGeneratedRoomsChanged()
    {
        RebuildRoomLookup();
        GeneratedRoomsChanged?.Invoke();
        TrySpawnPlayer();
    }

    private void RebuildRoomLookup()
    {
        roomByFloorCell.Clear();

        var rooms = LastGeneratedRooms;
        if (rooms == null)
            return;

        for (var i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            if (room == null || room.FloorCells == null)
                continue;

            foreach (var floorCell in room.FloorCells)
                roomByFloorCell[floorCell] = room;
        }
    }

    private void TrySpawnPlayer()
    {
        if (!spawnPlayerAfterBuild || playerPrefab == null)
            return;
        if (!Application.isPlaying)
        {
            Debug.Log("[GeneratedLevelRuntime] Player spawn is skipped outside Play Mode.");
            return;
        }

        GeneratedRoomInfo startRoom = null;
        var rooms = LastGeneratedRooms;
        for (var i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            if (room != null && room.IsStartRoom && !room.IsConnector)
            {
                startRoom = room;
                break;
            }
        }

        if (startRoom == null)
        {
            Debug.LogWarning("[GeneratedLevelRuntime] No generated room has StartRoomSpawn; player was not spawned.");
            return;
        }

        if (spawnedPlayerInstance != null)
            Destroy(spawnedPlayerInstance);

        spawnedPlayerInstance = Instantiate(playerPrefab, startRoom.SpawnWorldPosition, Quaternion.identity);
        if (spawnedPlayerInstance.TryGetComponent<PlayerRoomTracker>(out var playerRoomTracker))
            playerRoomTracker.SetGeneratedLevelRuntime(this);
        else
        {
            playerRoomTracker = spawnedPlayerInstance.AddComponent<PlayerRoomTracker>();
            playerRoomTracker.SetGeneratedLevelRuntime(this);
        }

        if (!spawnedPlayerInstance.TryGetComponent<Health>(out var health))
            health = spawnedPlayerInstance.AddComponent<Health>();
        health.Configure(health.MaxHealth, false);

        if (!spawnedPlayerInstance.TryGetComponent<HealthBarView>(out _))
            Debug.LogWarning("[GeneratedLevelRuntime] Player prefab has no HealthBarView; player HP bar is disabled.", spawnedPlayerInstance);
        if (!spawnedPlayerInstance.TryGetComponent<PlayerBowAttack>(out _))
            Debug.LogWarning("[GeneratedLevelRuntime] Player prefab has no PlayerBowAttack; ranged attack is disabled.", spawnedPlayerInstance);

        TryBindCinemachineTarget(spawnedPlayerInstance.transform);
        PlayerSpawned?.Invoke(spawnedPlayerInstance);
    }

    private void TryBindCinemachineTarget(Transform target)
    {
        if (target == null)
            return;

        var cameraComponent = ResolveCinemachineCameraComponent(cinemachineCamera);
        if (cameraComponent == null)
            return;

        var type = cameraComponent.GetType();
        var followAssigned = TryAssignTransformMember(cameraComponent, type, "Follow", target);

        if (!followAssigned)
            Debug.LogWarning($"[GeneratedLevelRuntime] Assigned camera component '{type.Name}' does not expose a writable Follow Transform.");
    }

    private static Component ResolveCinemachineCameraComponent(Component configuredComponent)
    {
        if (configuredComponent == null)
            return null;

        if (HasTransformMember(configuredComponent.GetType(), "Follow"))
            return configuredComponent;

        var host = configuredComponent.transform;
        if (host == null)
            return null;

        var siblingComponents = host.GetComponents<Component>();
        for (var i = 0; i < siblingComponents.Length; i++)
        {
            var sibling = siblingComponents[i];
            if (sibling == null)
                continue;

            if (HasTransformMember(sibling.GetType(), "Follow"))
                return sibling;
        }

        return configuredComponent;
    }

    private static bool HasTransformMember(Type targetType, string memberName)
    {
        var property = targetType.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (property != null && typeof(Transform).IsAssignableFrom(property.PropertyType))
            return true;

        var field = targetType.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
        return field != null && typeof(Transform).IsAssignableFrom(field.FieldType);
    }

    private static bool TryAssignTransformMember(object targetObject, Type targetType, string memberName, Transform value)
    {
        var property = targetType.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.CanWrite && typeof(Transform).IsAssignableFrom(property.PropertyType))
        {
            property.SetValue(targetObject, value);
            return true;
        }

        var field = targetType.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (field != null && typeof(Transform).IsAssignableFrom(field.FieldType))
        {
            field.SetValue(targetObject, value);
            return true;
        }

        return false;
    }
}
