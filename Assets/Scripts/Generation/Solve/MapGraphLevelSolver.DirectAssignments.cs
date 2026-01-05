// Assets/Scripts/Generation/Solve/MapGraphLevelSolver.DirectAssignments.cs
using System.Collections.Generic;

public partial class MapGraphLevelSolver
{
    private bool TryBuildDirectAssignments(
        MapGraphAsset graph,
        out IReadOnlyDictionary<string, RoomTypeAsset> nodeAssignments,
        out IReadOnlyDictionary<(string, string), ConnectionTypeAsset> edgeAssignments,
        out string error)
    {
        nodeAssignments = null;
        edgeAssignments = null;
        error = null;

        if (graph == null)
        {
            error = "Graph is null.";
            return false;
        }

        var nodes = new Dictionary<string, RoomTypeAsset>();
        foreach (var node in graph.Nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.id))
                continue;
            var type = node.roomType != null ? node.roomType : graph.DefaultRoomType;
            if (type == null)
            {
                error = $"Node {node.id} has no room type and DefaultRoomType is null.";
                return false;
            }
            nodes[node.id] = type;
        }

        var edges = new Dictionary<(string, string), ConnectionTypeAsset>();
        foreach (var edge in graph.Edges)
        {
            if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                continue;
            var type = edge.connectionType != null ? edge.connectionType : graph.DefaultConnectionType;
            if (type == null)
            {
                error = $"Edge {edge.fromNodeId}->{edge.toNodeId} has no connection type and DefaultConnectionType is null.";
                return false;
            }
            var key = MapGraphKey.NormalizeKey(edge.fromNodeId, edge.toNodeId);
            edges[key] = type;
        }

        nodeAssignments = nodes;
        edgeAssignments = edges;
        return true;
    }
}
