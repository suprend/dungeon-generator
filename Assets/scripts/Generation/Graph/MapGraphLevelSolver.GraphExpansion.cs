// Assets/scripts/Generation/Graph/MapGraphLevelSolver.GraphExpansion.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public partial class MapGraphLevelSolver
{
    private MapGraphAsset BuildCorridorGraph(MapGraphAsset source)
    {
        if (source == null)
            return null;

        var expanded = ScriptableObject.CreateInstance<MapGraphAsset>();
        expanded.DefaultRoomType = source.DefaultRoomType;
        expanded.DefaultConnectionType = source.DefaultConnectionType;

        var nodes = new List<MapGraphAsset.NodeData>();
        var edges = new List<MapGraphAsset.EdgeData>();
        var nodeMap = new Dictionary<string, MapGraphAsset.NodeData>();

        foreach (var n in source.Nodes)
        {
            if (n == null || string.IsNullOrEmpty(n.id))
                continue;
            var copy = new MapGraphAsset.NodeData
            {
                id = n.id,
                label = n.label,
                roomType = n.roomType != null ? n.roomType : source.DefaultRoomType,
                notes = n.notes,
                position = n.position
            };
            nodes.Add(copy);
            nodeMap[n.id] = copy;
        }

        var corridorTypes = new Dictionary<ConnectionTypeAsset, RoomTypeAsset>();
        RoomTypeAsset GetCorridorRoomType(ConnectionTypeAsset conn)
        {
            var key = conn != null ? conn : source.DefaultConnectionType;
            if (key == null)
                return null;
            if (corridorTypes.TryGetValue(key, out var rt))
                return rt;
            rt = ScriptableObject.CreateInstance<RoomTypeAsset>();
            rt.prefabs = key.prefabs != null ? new List<GameObject>(key.prefabs) : new List<GameObject>();
            rt.name = $"{key.name}_Corridor";
            corridorTypes[key] = rt;
            return rt;
        }

        foreach (var e in source.Edges)
        {
            if (e == null || string.IsNullOrEmpty(e.fromNodeId) || string.IsNullOrEmpty(e.toNodeId))
                continue;
            if (!nodeMap.TryGetValue(e.fromNodeId, out var a) || !nodeMap.TryGetValue(e.toNodeId, out var b))
                continue;

            var conn = e.connectionType != null ? e.connectionType : source.DefaultConnectionType;
            var corridorRoom = GetCorridorRoomType(conn);
            var corridorNode = new MapGraphAsset.NodeData
            {
                id = Guid.NewGuid().ToString("N"),
                label = conn != null ? conn.name : "Corridor",
                roomType = corridorRoom,
                position = (a.position + b.position) * 0.5f
            };
            nodes.Add(corridorNode);

            edges.Add(new MapGraphAsset.EdgeData
            {
                fromNodeId = a.id,
                toNodeId = corridorNode.id,
                connectionType = conn
            });
            edges.Add(new MapGraphAsset.EdgeData
            {
                fromNodeId = corridorNode.id,
                toNodeId = b.id,
                connectionType = conn
            });
        }

        var nodesField = typeof(MapGraphAsset).GetField("nodes", BindingFlags.NonPublic | BindingFlags.Instance);
        var edgesField = typeof(MapGraphAsset).GetField("edges", BindingFlags.NonPublic | BindingFlags.Instance);
        nodesField?.SetValue(expanded, nodes);
        edgesField?.SetValue(expanded, edges);
        expanded.EnsureIds();
        return expanded;
    }
}

