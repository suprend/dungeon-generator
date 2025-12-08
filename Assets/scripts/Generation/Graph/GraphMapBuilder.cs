// Assets/scripts/Generation/Graph/GraphMapBuilder.cs
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Thin wrapper to run MapGraphLevelSolver with prefab placement and stamping.
/// </summary>
public class GraphMapBuilder : MonoBehaviour
{
    [Header("Graph")]
    public MapGraphAsset graph;

    [Header("Global Tilemap layers")]
    public Grid targetGrid;
    public Tilemap floorMap;
    public Tilemap wallMap;

    [Header("Execution")]
    public bool runOnStart = true;
    public bool clearOnRun = true;
    public int randomSeed = 0; // 0 = random
    public bool verboseLogs = false;

    private void Awake()
    {
        if (runOnStart)
            Build();
    }

    [ContextMenu("Build From Graph")]
    public void Build()
    {
        if (graph == null)
        {
            Debug.LogWarning("[GraphMapBuilder] Graph asset is not assigned.");
            return;
        }
        if (targetGrid == null || floorMap == null)
        {
            Debug.LogWarning("[GraphMapBuilder] Target Grid and Floor Map are required.");
            return;
        }

        var solver = new MapGraphLevelSolver(graph);
        Vector3Int? startCell = targetGrid != null ? targetGrid.WorldToCell(transform.position) : null;
        if (!solver.TrySolveAndPlace(targetGrid, floorMap, wallMap, clearOnRun, randomSeed, verboseLogs, startCell, out var error))
        {
            Debug.LogError($"[GraphMapBuilder] Generation failed: {error}");
        }
    }
}
