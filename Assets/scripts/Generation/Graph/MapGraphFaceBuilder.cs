// Assets/scripts/Generation/Graph/MapGraphFaceBuilder.cs
using System;
using System.Collections.Generic;
using System.Linq;
using GraphPlanarityTesting.Graphs.DataStructures;
using GraphPlanarityTesting.PlanarityTesting.BoyerMyrvold;

/// <summary>
/// Utility that converts <see cref="MapGraphAsset"/> into planar faces using the Boyer–Myrvold algorithm.
/// </summary>
public static class MapGraphFaceBuilder
{
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
        faces = null;
        error = null;
        if (graphAsset == null)
        {
            error = "Graph asset is null.";
            return false;
        }

        graphAsset.EnsureIds();

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

        var edgeLookup = BuildEdgeLookup(graphAsset.Edges);
        foreach (var edge in graphAsset.Edges)
        {
            if (string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                continue;
            if (!nodeLookup.ContainsKey(edge.fromNodeId) || !nodeLookup.ContainsKey(edge.toNodeId))
                continue;
            try
            {
                graph.AddEdge(edge.fromNodeId, edge.toNodeId);
            }
            catch (ArgumentException)
            {
                // Duplicate edges or broken references are ignored for now.
            }
        }

        var planarity = new BoyerMyrvold<string>();
        if (!planarity.TryGetPlanarFaces(graph, out var planarFaces))
        {
            error = "Graph is not planar or faces could not be computed.";
            return false;
        }

        faces = new List<Face>();
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
        }

        return true;
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
