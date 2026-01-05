// Assets/Scripts/Generation/Graph/MapGraphFaceBuilder.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GraphPlanarityTesting.Graphs.DataStructures;
using GraphPlanarityTesting.PlanarityTesting.BoyerMyrvold;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>
/// Utility that converts <see cref="MapGraphAsset"/> into planar faces using the Boyer–Myrvold algorithm.
/// </summary>
public static class MapGraphFaceBuilder
{
    public static bool LogProfiling { get; private set; }
    public static int MaxProfilingLogsPerSession { get; private set; } = 8;
    private static int profilingLogsEmitted;

    public static void SetDebug(bool logProfiling, int maxLogsPerSession = 8)
    {
        LogProfiling = logProfiling;
        MaxProfilingLogsPerSession = Mathf.Clamp(maxLogsPerSession, 0, 512);
    }

    private static long NowTicks() => Stopwatch.GetTimestamp();
    private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Represents a single face of the planar embedding.
    /// </summary>
    public sealed class Face
    {
        public IReadOnlyList<MapGraphAsset.NodeData> Nodes { get; }
        public IReadOnlyList<MapGraphAsset.EdgeData> Edges { get; }

        internal Face(List<MapGraphAsset.NodeData> nodes, List<MapGraphAsset.EdgeData> edges)
        {
            Nodes = nodes;
            Edges = edges;
        }
    }

    /// <summary>
    /// Builds planar faces for the provided graph.
    /// </summary>
    /// <param name="graphAsset">Source graph asset.</param>
    /// <param name="faces">Resulting faces in clockwise order.</param>
    /// <param name="error">Error description when returns false.</param>
    /// <returns>True if faces were produced, false when graph is not planar or invalid.</returns>
    public static bool TryBuildFaces(MapGraphAsset graphAsset, out List<Face> faces, out string error)
    {
        var t0 = NowTicks();
        long ensureIdsTicks = 0;
        long buildVerticesTicks = 0;
        long buildEdgeLookupTicks = 0;
        long addEdgesTicks = 0;
        long planarityTicks = 0;
        long materializeTicks = 0;

        faces = null;
        error = null;
        if (graphAsset == null)
        {
            error = "Graph asset is null.";
            return false;
        }

        var tEnsure = NowTicks();
        graphAsset.EnsureIds();
        ensureIdsTicks = NowTicks() - tEnsure;

        var tVertices = NowTicks();
        var graph = new UndirectedAdjacencyListGraph<string>();
        var nodeLookup = new Dictionary<string, MapGraphAsset.NodeData>();
        foreach (var node in graphAsset.Nodes)
        {
            if (string.IsNullOrEmpty(node.id))
                continue;

            if (!nodeLookup.ContainsKey(node.id))
            {
                nodeLookup[node.id] = node;
                graph.AddVertex(node.id);
            }
        }
        buildVerticesTicks = NowTicks() - tVertices;

        var tEdgeLookup = NowTicks();
        var edgeLookup = BuildEdgeLookup(graphAsset.Edges);
        buildEdgeLookupTicks = NowTicks() - tEdgeLookup;

        var totalEdges = graphAsset.Edges?.Count ?? 0;
        var uniqueEdges = edgeLookup.Count;
        var tAddEdges = NowTicks();
        var addedEdges = 0;
        var duplicateEdges = 0;
        foreach (var edge in graphAsset.Edges)
        {
            if (string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                continue;
            if (!nodeLookup.ContainsKey(edge.fromNodeId) || !nodeLookup.ContainsKey(edge.toNodeId))
                continue;
            try
            {
                graph.AddEdge(edge.fromNodeId, edge.toNodeId);
                addedEdges++;
            }
            catch (ArgumentException)
            {
                // Duplicate edges or broken references are ignored for now.
                duplicateEdges++;
            }
        }
        addEdgesTicks = NowTicks() - tAddEdges;

        var tPlanarity = NowTicks();
        var planarity = new BoyerMyrvold<string>();
        if (!planarity.TryGetPlanarFaces(graph, out var planarFaces))
        {
            planarityTicks = NowTicks() - tPlanarity;
            error = "Graph is not planar or faces could not be computed.";
            MaybeLogProfiling("FAILED", graphAsset, totalEdges, uniqueEdges, nodeLookup.Count, addedEdges, duplicateEdges,
                ensureIdsTicks, buildVerticesTicks, buildEdgeLookupTicks, addEdgesTicks, planarityTicks, materializeTicks, t0,
                facesCount: 0);
            return false;
        }
        planarityTicks = NowTicks() - tPlanarity;

        var tMaterialize = NowTicks();
        faces = new List<Face>();
        var facesCount = 0;
        var totalCycleNodes = 0;
        foreach (var cycle in planarFaces.Faces)
        {
            if (cycle == null || cycle.Count == 0)
                continue;

            var faceNodes = new List<MapGraphAsset.NodeData>(cycle.Count);
            foreach (var nodeId in cycle)
            {
                if (nodeId != null && nodeLookup.TryGetValue(nodeId, out var node))
                    faceNodes.Add(node);
            }

            if (faceNodes.Count == 0)
                continue;

            var faceEdges = new List<MapGraphAsset.EdgeData>(faceNodes.Count);
            for (int i = 0; i < cycle.Count; i++)
            {
                var a = cycle[i];
                var b = cycle[(i + 1) % cycle.Count];
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                    continue;
                if (edgeLookup.TryGetValue(NormalizeKey(a, b), out var edgeData))
                    faceEdges.Add(edgeData);
            }

            faces.Add(new Face(faceNodes, faceEdges));
            facesCount++;
            totalCycleNodes += cycle.Count;
        }
        materializeTicks = NowTicks() - tMaterialize;

        MaybeLogProfiling("OK", graphAsset, totalEdges, uniqueEdges, nodeLookup.Count, addedEdges, duplicateEdges,
            ensureIdsTicks, buildVerticesTicks, buildEdgeLookupTicks, addEdgesTicks, planarityTicks, materializeTicks, t0,
            facesCount, totalCycleNodes);

        return true;
    }

    private static void MaybeLogProfiling(
        string status,
        MapGraphAsset graphAsset,
        int totalEdges,
        int uniqueEdges,
        int nodes,
        int addedEdges,
        int duplicateEdges,
        long ensureIdsTicks,
        long buildVerticesTicks,
        long buildEdgeLookupTicks,
        long addEdgesTicks,
        long planarityTicks,
        long materializeTicks,
        long totalStartTicks,
        int facesCount,
        int totalCycleNodes = 0)
    {
        if (!LogProfiling)
            return;
        if (MaxProfilingLogsPerSession <= 0)
            return;
        if (profilingLogsEmitted >= MaxProfilingLogsPerSession)
            return;
        profilingLogsEmitted++;

        var totalTicks = NowTicks() - totalStartTicks;
        var totalMs = TicksToMs(totalTicks);
        var avgFaceLen = facesCount > 0 ? (totalCycleNodes / (float)facesCount) : 0f;

        Debug.Log(
            $"[Faces][prof] status={status} totalMs={totalMs:0.0} " +
            $"nodes={nodes} edges={totalEdges} uniqueEdges={uniqueEdges} addedEdges={addedEdges} dupEdgesCaught={duplicateEdges} " +
            $"faces={facesCount} avgFaceLen={avgFaceLen:0.0} " +
            $"stagesMs={{ensureIds={TicksToMs(ensureIdsTicks):0.0}, buildVertices={TicksToMs(buildVerticesTicks):0.0}, edgeLookup={TicksToMs(buildEdgeLookupTicks):0.0}, addEdges={TicksToMs(addEdgesTicks):0.0}, planarity={TicksToMs(planarityTicks):0.0}, materialize={TicksToMs(materializeTicks):0.0}}}");
    }

    private static Dictionary<(string, string), MapGraphAsset.EdgeData> BuildEdgeLookup(IEnumerable<MapGraphAsset.EdgeData> edges)
    {
        var lookup = new Dictionary<(string, string), MapGraphAsset.EdgeData>();
        if (edges == null)
            return lookup;

        foreach (var edge in edges)
        {
            if (string.IsNullOrEmpty(edge?.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                continue;
            var key = NormalizeKey(edge.fromNodeId, edge.toNodeId);
            lookup[key] = edge;
        }
        return lookup;
    }

    private static (string, string) NormalizeKey(string a, string b)
    {
        if (string.CompareOrdinal(a, b) <= 0)
            return (a, b);
        return (b, a);
    }
}
