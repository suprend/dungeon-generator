using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class GeneratedRoomInfo
{
    [SerializeField] private string nodeId;
    [SerializeField] private string prefabName;
    [SerializeField] private bool isConnector;
    [SerializeField] private bool isStartRoom;
    [SerializeField] private Vector3Int rootCell;
    [SerializeField] private BoundsInt cellBounds;
    [SerializeField] private Vector3 spawnWorldPosition;
    [SerializeField] private bool hasEnemySpawns;
    [SerializeField] private string selectedEnemyLayoutId;

    private readonly HashSet<Vector3Int> floorCells;
    private readonly HashSet<Vector3Int> wallCells;
    private readonly List<GeneratedEnemySpawnInfo> enemySpawns;

    public string NodeId => nodeId;
    public string PrefabName => prefabName;
    public bool IsConnector => isConnector;
    public bool IsStartRoom => isStartRoom;
    public Vector3Int RootCell => rootCell;
    public BoundsInt CellBounds => cellBounds;
    public Vector3 SpawnWorldPosition => spawnWorldPosition;
    public bool HasEnemySpawns => hasEnemySpawns;
    public string SelectedEnemyLayoutId => selectedEnemyLayoutId;
    public IReadOnlyCollection<Vector3Int> FloorCells => floorCells;
    public IReadOnlyCollection<Vector3Int> WallCells => wallCells;
    public IReadOnlyList<GeneratedEnemySpawnInfo> EnemySpawns => enemySpawns;

    public GeneratedRoomInfo(
        string nodeId,
        string prefabName,
        bool isConnector,
        bool isStartRoom,
        Vector3Int rootCell,
        BoundsInt cellBounds,
        Vector3 spawnWorldPosition,
        bool hasEnemySpawns,
        string selectedEnemyLayoutId,
        IEnumerable<GeneratedEnemySpawnInfo> enemySpawns,
        IEnumerable<Vector3Int> floorCells,
        IEnumerable<Vector3Int> wallCells)
    {
        this.nodeId = nodeId ?? string.Empty;
        this.prefabName = prefabName ?? string.Empty;
        this.isConnector = isConnector;
        this.isStartRoom = isStartRoom;
        this.rootCell = rootCell;
        this.cellBounds = cellBounds;
        this.spawnWorldPosition = spawnWorldPosition;
        this.hasEnemySpawns = hasEnemySpawns;
        this.selectedEnemyLayoutId = selectedEnemyLayoutId ?? string.Empty;
        this.enemySpawns = enemySpawns != null ? new List<GeneratedEnemySpawnInfo>(enemySpawns) : new List<GeneratedEnemySpawnInfo>();
        this.floorCells = floorCells != null ? new HashSet<Vector3Int>(floorCells) : new HashSet<Vector3Int>();
        this.wallCells = wallCells != null ? new HashSet<Vector3Int>(wallCells) : new HashSet<Vector3Int>();
    }

    public bool ContainsCell(Vector3Int cell)
    {
        return floorCells.Contains(cell);
    }

    public static BoundsInt ComputeBounds(IEnumerable<Vector3Int> cells)
    {
        if (cells == null)
            return default;

        using var enumerator = cells.GetEnumerator();
        if (!enumerator.MoveNext())
            return default;

        var min = enumerator.Current;
        var max = enumerator.Current;
        while (enumerator.MoveNext())
        {
            var cell = enumerator.Current;
            min = Vector3Int.Min(min, cell);
            max = Vector3Int.Max(max, cell);
        }

        return new BoundsInt(min, max - min + Vector3Int.one);
    }
}
