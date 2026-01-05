// Assets/scripts/Generation/Graph/MapGraphChainBuilder.cs
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Builds ordered chains (degree<=2 subgraphs) from planar faces and remaining graph parts.
/// </summary>
public static class MapGraphChainBuilder
{
    public sealed class Chain
    {
        public List<MapGraphAsset.NodeData> Nodes { get; }
        public List<MapGraphAsset.EdgeData> Edges { get; }
        public bool IsCycle { get; }

        public Chain(List<MapGraphAsset.NodeData> nodes, List<MapGraphAsset.EdgeData> edges, bool isCycle)
        {
            Nodes = nodes ?? new List<MapGraphAsset.NodeData>();
            Edges = edges ?? new List<MapGraphAsset.EdgeData>();
            IsCycle = isCycle;
        }
    }

    public static bool TryBuildChains(MapGraphAsset graphAsset, List<MapGraphFaceBuilder.Face> faces, out List<Chain> chains, out string error)
    {
        chains = new List<Chain>();
        error = null;
        if (graphAsset == null)
        {
            error = "Graph asset is null.";
            return false;
        }

        faces ??= new List<MapGraphFaceBuilder.Face>();

        var coveredEdges = new HashSet<MapGraphAsset.EdgeData>();
        var coveredNodes = new HashSet<string>();

        // Face adjacency graph (by shared nodes)
        var faceAdj = BuildFaceAdjacency(faces);
        var startFaceIndex = faces
            .Select((f, idx) => (face: f, idx))
            .Where(t => t.face?.Nodes != null)
            .OrderBy(t => t.face.Nodes.Count)
            .Select(t => t.idx)
            .FirstOrDefault();

        var visitedFaces = new HashSet<int>();
        var queue = new Queue<int>();
        if (faces.Count > 0)
            queue.Enqueue(startFaceIndex);

        while (queue.Count > 0)
        {
            var idx = queue.Dequeue();
            if (visitedFaces.Contains(idx))
                continue;
            visitedFaces.Add(idx);

            var face = faces[idx];
            if (face?.Nodes != null && face.Nodes.Count > 0)
            {
                var chain = FaceToChain(face);
                if (chain != null && chain.Edges.All(e => e != null))
                {
                    chains.Add(chain);
                    foreach (var e in chain.Edges) coveredEdges.Add(e);
                    foreach (var n in chain.Nodes.Where(n => n != null && !string.IsNullOrEmpty(n.id)))
                        coveredNodes.Add(n.id);
                }
            }

            if (!faceAdj.TryGetValue(idx, out var neighbors)) continue;
            foreach (var n in neighbors.Where(n => !visitedFaces.Contains(n)).OrderBy(n => faces[n]?.Nodes?.Count ?? int.MaxValue))
                queue.Enqueue(n);
        }

        // Remaining edges/nodes into degree<=2 chains
        var remainingEdges = graphAsset.Edges.Where(e => e != null && !coveredEdges.Contains(e)).ToList();
        var remainingChains = BuildPathChains(graphAsset, remainingEdges);
        chains.AddRange(remainingChains);

        // If the graph has isolated nodes (degree 0) or nodes not covered by any chain, add them as singleton chains.
        // Also handle the degenerate case: graph with nodes but no edges/faces.
        var nodesCoveredByChains = new HashSet<string>(
            chains.SelectMany(c => c?.Nodes ?? new List<MapGraphAsset.NodeData>())
                .Where(n => n != null && !string.IsNullOrEmpty(n.id))
                .Select(n => n.id));

        foreach (var node in graphAsset.Nodes.Where(n => n != null && !string.IsNullOrEmpty(n.id)))
        {
            if (nodesCoveredByChains.Contains(node.id))
                continue;
            chains.Add(new Chain(new List<MapGraphAsset.NodeData> { node }, new List<MapGraphAsset.EdgeData>(), false));
            nodesCoveredByChains.Add(node.id);
        }

        return true;
    }

    private static Dictionary<int, List<int>> BuildFaceAdjacency(List<MapGraphFaceBuilder.Face> faces)
    {
        var adj = new Dictionary<int, List<int>>();
        var nodeToFaces = new Dictionary<string, List<int>>();
        for (int i = 0; i < faces.Count; i++)
        {
            var face = faces[i];
            if (face?.Nodes == null) continue;
            foreach (var node in face.Nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.id)) continue;
                if (!nodeToFaces.TryGetValue(node.id, out var list))
                {
                    list = new List<int>();
                    nodeToFaces[node.id] = list;
                }
                list.Add(i);
            }
        }

        foreach (var kv in nodeToFaces)
        {
            var list = kv.Value;
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    AddAdj(adj, list[i], list[j]);
                    AddAdj(adj, list[j], list[i]);
                }
            }
        }

        return adj;
    }

    private static void AddAdj(Dictionary<int, List<int>> adj, int a, int b)
    {
        if (!adj.TryGetValue(a, out var list))
        {
            list = new List<int>();
            adj[a] = list;
        }
        if (!list.Contains(b))
            list.Add(b);
    }

    private static Chain FaceToChain(MapGraphFaceBuilder.Face face)
    {
        // Build a simple cycle (degree<=2) following the face order, skipping duplicate consecutive nodes.
        var orderedNodes = new List<MapGraphAsset.NodeData>();
        foreach (var n in face.Nodes)
        {
            if (n == null) continue;
            if (orderedNodes.Count == 0 || orderedNodes[^1]?.id != n.id)
                orderedNodes.Add(n);
        }
        if (orderedNodes.Count > 1 && orderedNodes[0]?.id == orderedNodes[^1]?.id)
            orderedNodes.RemoveAt(orderedNodes.Count - 1);

        // In non-2-connected planar graphs, some face walks repeat vertices/edges (e.g., outer face of a tree).
        // Those are not simple cycles and should not be treated as cycle-chains.
        if (orderedNodes.Count < 3)
            return null;

        var chainEdges = new List<MapGraphAsset.EdgeData>();
        for (int i = 0; i < orderedNodes.Count; i++)
        {
            var a = orderedNodes[i];
            var b = orderedNodes[(i + 1) % orderedNodes.Count];
            if (a == null || b == null) continue;
            var edge = face.Edges?.FirstOrDefault(e => e != null && e.Matches(a.id, b.id));
            if (edge != null)
                chainEdges.Add(edge);
        }

        var nodeIds = orderedNodes.Where(n => n != null && !string.IsNullOrEmpty(n.id)).Select(n => n.id).ToList();
        if (nodeIds.Count != orderedNodes.Count)
            return null;
        if (nodeIds.Distinct().Count() != nodeIds.Count)
            return null;
        if (chainEdges.Count != orderedNodes.Count)
            return null;
        if (chainEdges.Any(e => e == null))
            return null;
        if (chainEdges.Distinct().Count() != chainEdges.Count)
            return null;

        return new Chain(orderedNodes, chainEdges, true);
    }

    private static List<Chain> BuildPathChains(MapGraphAsset graphAsset, List<MapGraphAsset.EdgeData> edges)
    {
        var chains = new List<Chain>();
        if (edges == null || edges.Count == 0)
            return chains;

        var edgeVisited = new HashSet<MapGraphAsset.EdgeData>();
        var nodeAdj = new Dictionary<string, List<MapGraphAsset.EdgeData>>();
        foreach (var e in edges)
        {
            if (e == null || string.IsNullOrEmpty(e.fromNodeId) || string.IsNullOrEmpty(e.toNodeId))
                continue;
            if (!nodeAdj.TryGetValue(e.fromNodeId, out var lf))
                nodeAdj[e.fromNodeId] = lf = new List<MapGraphAsset.EdgeData>();
            lf.Add(e);
            if (!nodeAdj.TryGetValue(e.toNodeId, out var lt))
                nodeAdj[e.toNodeId] = lt = new List<MapGraphAsset.EdgeData>();
            lt.Add(e);
        }

        // Paths starting at degree != 2
        foreach (var kv in nodeAdj.Where(kv => kv.Value.Count != 2))
        {
            var startId = kv.Key;
            foreach (var edge in kv.Value)
            {
                if (edgeVisited.Contains(edge))
                    continue;
                chains.Add(WalkChain(graphAsset, startId, edge, nodeAdj, edgeVisited));
            }
        }

        // Remaining cycles (all degree 2)
        foreach (var e in edges)
        {
            if (edgeVisited.Contains(e))
                continue;
            chains.Add(WalkCycle(graphAsset, e, nodeAdj, edgeVisited));
        }

        return chains;
    }

    private static Chain WalkChain(MapGraphAsset graphAsset, string startNodeId, MapGraphAsset.EdgeData firstEdge, Dictionary<string, List<MapGraphAsset.EdgeData>> nodeAdj, HashSet<MapGraphAsset.EdgeData> edgeVisited)
    {
        var nodes = new List<MapGraphAsset.NodeData>();
        var chainEdges = new List<MapGraphAsset.EdgeData>();

        var currentNodeId = startNodeId;
        MapGraphAsset.EdgeData currentEdge = firstEdge;
        while (currentEdge != null)
        {
            edgeVisited.Add(currentEdge);
            chainEdges.Add(currentEdge);

            var nextNodeId = NextNodeId(currentNodeId, currentEdge);
            var currentNode = graphAsset.GetNodeById(currentNodeId);
            if (currentNode != null)
                nodes.Add(currentNode);

            var nextEdges = nodeAdj.TryGetValue(nextNodeId, out var list) ? list : new List<MapGraphAsset.EdgeData>();
            var nextEdge = nextEdges.FirstOrDefault(e => !edgeVisited.Contains(e));
            currentNodeId = nextNodeId;
            currentEdge = nextEdge;
        }

        var lastNode = graphAsset.GetNodeById(currentNodeId);
        if (lastNode != null && nodes.LastOrDefault()?.id != lastNode.id)
            nodes.Add(lastNode);

        return new Chain(nodes, chainEdges, false);
    }

    private static Chain WalkCycle(MapGraphAsset graphAsset, MapGraphAsset.EdgeData startEdge, Dictionary<string, List<MapGraphAsset.EdgeData>> nodeAdj, HashSet<MapGraphAsset.EdgeData> edgeVisited)
    {
        var nodes = new List<MapGraphAsset.NodeData>();
        var chainEdges = new List<MapGraphAsset.EdgeData>();

        var currentEdge = startEdge;
        var currentNodeId = startEdge.fromNodeId;
        while (currentEdge != null && !edgeVisited.Contains(currentEdge))
        {
            edgeVisited.Add(currentEdge);
            chainEdges.Add(currentEdge);
            var currentNode = graphAsset.GetNodeById(currentNodeId);
            if (currentNode != null)
                nodes.Add(currentNode);

            var nextNodeId = NextNodeId(currentNodeId, currentEdge);
            var nextEdges = nodeAdj.TryGetValue(nextNodeId, out var list) ? list : new List<MapGraphAsset.EdgeData>();
            var nextEdge = nextEdges.FirstOrDefault(e => !edgeVisited.Contains(e));
            currentNodeId = nextNodeId;
            currentEdge = nextEdge;
        }

        return new Chain(nodes, chainEdges, true);
    }

    private static string NextNodeId(string currentNodeId, MapGraphAsset.EdgeData edge)
    {
        if (edge == null) return currentNodeId;
        if (string.Equals(edge.fromNodeId, currentNodeId, StringComparison.Ordinal))
            return edge.toNodeId;
        return edge.fromNodeId;
    }
}
