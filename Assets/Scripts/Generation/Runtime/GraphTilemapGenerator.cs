// Assets/scripts/Generation/Graph/GraphTilemapGenerator.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Places rooms/connectors from MapGraphLevelSolver into Tilemaps, similar to MapBuilder stamping.
/// Uses node positions from MapGraphAsset as grid coordinates.
/// </summary>
public class GraphTilemapGenerator : MonoBehaviour
{
    [Header("Graph")]
    public MapGraphAsset graph;
    public float positionScale = 1f; // multiplier for node.position -> grid cell

    [Header("Tilemaps")]
    public Grid targetGrid;
    public Tilemap floorMap;
    public Tilemap wallMap;

    [Header("Execution")]
    public bool runOnStart = true;
    public bool clearOnRun = true;
    public int randomSeed = 0; // 0 = random

    [Header("Debug")]
    public bool logAssignments = true;

    private System.Random rng;

    private void Start()
    {
        if (runOnStart)
            Run();
    }

    [ContextMenu("Generate From Graph")]
    public void Run()
    {
        if (graph == null)
        {
            Debug.LogWarning("[GraphTilemapGenerator] Graph asset is not assigned.");
            return;
        }

        if (targetGrid == null || floorMap == null)
        {
            Debug.LogWarning("[GraphTilemapGenerator] Grid and Floor Tilemap are required.");
            return;
        }

        rng = randomSeed != 0
            ? new System.Random(randomSeed)
            : new System.Random(UnityEngine.Random.Range(int.MinValue, int.MaxValue));

        var solver = new MapGraphLevelSolver(graph);
        if (!solver.TrySolve(out var nodeAssignments, out var edgeAssignments, out var error))
        {
            Debug.LogError($"[GraphTilemapGenerator] Solve failed: {error}");
            return;
        }

        var stamp = new TileStampService(targetGrid, floorMap, wallMap);
        if (clearOnRun)
            stamp.ClearMaps();

        var roomInstances = new Dictionary<string, ModuleMetaBase>();

        foreach (var node in graph.Nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.id))
                continue;

            if (!nodeAssignments.TryGetValue(node.id, out var roomType) || roomType == null)
                continue;

            var prefab = roomType.PickRandomPrefab(rng);
            if (prefab == null)
                continue;

            var roomInst = Instantiate(prefab, Vector3.zero, Quaternion.identity, targetGrid.transform);
            var meta = roomInst.GetComponent<ModuleMetaBase>();
            if (meta == null)
            {
                Destroy(roomInst);
                continue;
            }

            meta.ResetUsed();

            var cell = GraphPosToCell(node.position);
            AlignToCell(roomInst.transform, cell, stamp);

            stamp.StampModuleFloor(meta);
            stamp.StampModuleWalls(meta);
            stamp.DisableRenderers(roomInst.transform);

            roomInstances[node.id] = meta;

            if (logAssignments)
                Debug.Log($"[GraphTilemapGenerator] Placed node {node.id} -> {roomType.name} at cell {cell}");
        }

        foreach (var edge in graph.Edges)
        {
            if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                continue;
            if (!edgeAssignments.TryGetValue(NormalizeKey(edge.fromNodeId, edge.toNodeId), out var connType) || connType == null)
                continue;
            var prefab = connType.PickRandomPrefab(rng);
            if (prefab == null)
                continue;

            var connectorInst = Instantiate(prefab, Vector3.zero, Quaternion.identity, targetGrid.transform);
            var meta = connectorInst.GetComponent<ModuleMetaBase>();
            if (meta == null)
            {
                Destroy(connectorInst);
                continue;
            }

            meta.ResetUsed();

            var cellA = GraphPosToCell(GetNodePosition(edge.fromNodeId));
            var cellB = GraphPosToCell(GetNodePosition(edge.toNodeId));
            var midCell = Vector3Int.RoundToInt(((Vector3)cellA + (Vector3)cellB) * 0.5f);

            AlignToCell(connectorInst.transform, midCell, stamp);

            stamp.StampModuleFloor(meta);
            stamp.StampModuleWalls(meta);
            stamp.DisableRenderers(connectorInst.transform);

            if (logAssignments)
                Debug.Log($"[GraphTilemapGenerator] Placed edge {edge.fromNodeId}-{edge.toNodeId} -> {connType.name} at cell {midCell}");
        }
    }

    private Vector3Int GraphPosToCell(Vector2 graphPos)
    {
        var scaled = graphPos * positionScale;
        return new Vector3Int(Mathf.RoundToInt(scaled.x), Mathf.RoundToInt(scaled.y), 0);
    }

    private Vector2 GetNodePosition(string nodeId)
    {
        var node = graph.GetNodeById(nodeId);
        return node != null ? node.position : Vector2.zero;
    }

    private void AlignToCell(Transform t, Vector3Int cell, TileStampService stamp)
    {
        if (t == null || stamp == null) return;
        t.position = stamp.WorldFromCell(cell);
    }

    private static (string, string) NormalizeKey(string a, string b)
    {
        return string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
    }
}
