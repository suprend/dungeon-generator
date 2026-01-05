// Assets/scripts/Generation/Graph/GraphGenRunner.cs
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple MonoBehaviour wrapper to run MapGraphLevelSolver in play mode.
/// </summary>
public class GraphGenRunner : MonoBehaviour
{
    [Tooltip("Graph asset to solve.")]
    public MapGraphAsset graph;

    [Tooltip("Run solver automatically on Start().")]
    public bool runOnStart = true;

    [Tooltip("Log assignments to the console after solving.")]
    public bool logAssignments = true;

    public event Action<IReadOnlyDictionary<string, RoomTypeAsset>, IReadOnlyDictionary<(string, string), ConnectionTypeAsset>> OnSolved;

    private void Start()
    {
        if (runOnStart)
            Run();
    }

    [ContextMenu("Run Graph Solver")]
    public void Run()
    {
        if (graph == null)
        {
            Debug.LogWarning("[GraphGenRunner] Graph asset is not assigned.");
            return;
        }

        var solver = new MapGraphLevelSolver(graph);
        if (!solver.TrySolve(out var nodeAssignments, out var edgeAssignments, out var error))
        {
            Debug.LogError($"[GraphGenRunner] Solve failed: {error}");
            return;
        }

        if (logAssignments)
        {
            foreach (var kv in nodeAssignments)
                Debug.Log($"[GraphGenRunner] Node {kv.Key} -> {(kv.Value ? kv.Value.name : "null")}");
            foreach (var kv in edgeAssignments)
                Debug.Log($"[GraphGenRunner] Edge {kv.Key} -> {(kv.Value ? kv.Value.name : "null")}");
        }

        OnSolved?.Invoke(nodeAssignments, edgeAssignments);
    }
}
