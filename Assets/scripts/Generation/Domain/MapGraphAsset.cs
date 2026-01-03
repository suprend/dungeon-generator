// Assets/scripts/Generation/MapGraphAsset.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Scriptable description of a logical room graph where nodes reference room types
/// and edges reference connection types.
/// </summary>
[CreateAssetMenu(menuName = "Generation/Map Graph", fileName = "MapGraph")]
public class MapGraphAsset : ScriptableObject
{
    [SerializeField]
    private List<NodeData> nodes = new();

    [SerializeField]
    private List<EdgeData> edges = new();

    [SerializeField]
    private RoomTypeAsset defaultRoomType;

    [SerializeField]
    private ConnectionTypeAsset defaultConnectionType;

    public IReadOnlyList<NodeData> Nodes => nodes;
    public IReadOnlyList<EdgeData> Edges => edges;
    public RoomTypeAsset DefaultRoomType
    {
        get => defaultRoomType;
        set => defaultRoomType = value;
    }

    public ConnectionTypeAsset DefaultConnectionType
    {
        get => defaultConnectionType;
        set => defaultConnectionType = value;
    }

    /// <summary>
    /// Returns node data by name or null if it does not exist.
    /// </summary>
    public NodeData GetNodeById(string id) =>
        nodes.FirstOrDefault(n => string.Equals(n.id, id, StringComparison.Ordinal));

    /// <summary>
    /// Returns all edges connected to specified node name.
    /// </summary>
    public IEnumerable<EdgeData> GetEdgesFor(string nodeId)
    {
        foreach (var edge in edges)
        {
            if (edge.Connects(nodeId))
                yield return edge;
        }
    }

    private void OnValidate()
    {
        EnsureIds();
    }

    [Serializable]
    public class NodeData
    {
        [HideInInspector]
        public string id = Guid.NewGuid().ToString("N");

        [FormerlySerializedAs("name")]
        [Tooltip("Display name for this node (not required to be unique).")]
        public string label;

        [Tooltip("Room type this node represents.")]
        public RoomTypeAsset roomType;

        [Tooltip("Optional note for designers.")]
        public string notes;

        [HideInInspector]
        public Vector2 position;
    }

    [Serializable]
    public class EdgeData
    {
        [FormerlySerializedAs("fromNodeName")]
        [Tooltip("Identifier of the source node (matches NodeData.id).")]
        public string fromNodeId;

        [FormerlySerializedAs("toNodeName")]
        [Tooltip("Identifier of the target node (matches NodeData.id).")]
        public string toNodeId;

        [Tooltip("Connection type used for this edge.")]
        public ConnectionTypeAsset connectionType;

        public bool Connects(string nodeId)
        {
            return string.Equals(fromNodeId, nodeId, StringComparison.Ordinal) ||
                   string.Equals(toNodeId, nodeId, StringComparison.Ordinal);
        }

        public int GetWidth()
        {
            return connectionType ? Mathf.Max(1, connectionType.defaultWidth) : 1;
        }

        public bool Matches(string from, string to)
        {
            return (string.Equals(fromNodeId, from, StringComparison.Ordinal) && string.Equals(toNodeId, to, StringComparison.Ordinal)) ||
                   (string.Equals(fromNodeId, to, StringComparison.Ordinal) && string.Equals(toNodeId, from, StringComparison.Ordinal));
        }
    }

    public NodeData CreateNode()
    {
        var node = new NodeData
        {
            id = Guid.NewGuid().ToString("N"),
            label = defaultRoomType ? defaultRoomType.name : "Room",
            position = Vector2.zero,
            roomType = defaultRoomType
        };
        nodes.Add(node);
        return node;
    }

    public void RemoveNode(NodeData node)
    {
        if (node == null) return;
        nodes.Remove(node);
        edges.RemoveAll(e => e.Connects(node.id));
    }

    public EdgeData AddEdge(string fromNodeId, string toNodeId, ConnectionTypeAsset connectionType = null)
    {
        if (string.IsNullOrEmpty(fromNodeId) || string.IsNullOrEmpty(toNodeId)) return null;
        if (string.Equals(fromNodeId, toNodeId, StringComparison.Ordinal)) return null;
        if (edges.Any(e => e.Matches(fromNodeId, toNodeId)))
            return null;

        var edge = new EdgeData
        {
            fromNodeId = fromNodeId,
            toNodeId = toNodeId,
            connectionType = connectionType != null ? connectionType : defaultConnectionType
        };
        edges.Add(edge);
        return edge;
    }

    public void RemoveEdge(EdgeData edge)
    {
        if (edge == null) return;
        edges.Remove(edge);
    }

    public void RenameNode(NodeData node, string newLabel)
    {
        if (node == null) return;
        node.label = newLabel;
    }

    public void EnsureIds()
    {
        var usedIds = new HashSet<string>();
        foreach (var node in nodes)
        {
            if (string.IsNullOrEmpty(node.id) || usedIds.Contains(node.id))
            {
                node.id = Guid.NewGuid().ToString("N");
            }
            usedIds.Add(node.id);
        }

        foreach (var node in nodes)
        {
            if (string.IsNullOrEmpty(node.label))
            {
                node.label = GenerateDefaultLabel();
            }

            if (node.roomType == null && defaultRoomType != null)
            {
                node.roomType = defaultRoomType;
            }
        }

        var idSet = new HashSet<string>(nodes.Select(n => n.id));
        var labelToId = new Dictionary<string, string>();
        foreach (var node in nodes)
        {
            if (!string.IsNullOrEmpty(node.label) && !labelToId.ContainsKey(node.label))
            {
                labelToId[node.label] = node.id;
            }
        }
        string fallbackId = nodes.Count > 0 ? nodes[0].id : null;
        foreach (var edge in edges)
        {
            edge.fromNodeId = ResolveReference(edge.fromNodeId, idSet, labelToId, fallbackId);
            edge.toNodeId = ResolveReference(edge.toNodeId, idSet, labelToId, fallbackId);
            if (edge.connectionType == null)
                edge.connectionType = defaultConnectionType;
        }
    }

    private string ResolveReference(string currentValue, HashSet<string> idSet, Dictionary<string, string> labelToId, string fallbackId)
    {
        if (!string.IsNullOrEmpty(currentValue) && idSet.Contains(currentValue))
            return currentValue;
        if (!string.IsNullOrEmpty(currentValue) && labelToId.TryGetValue(currentValue, out var mapped))
            return mapped;
        return fallbackId ?? currentValue;
    }

    private string GenerateDefaultLabel() => defaultRoomType && !string.IsNullOrEmpty(defaultRoomType.name)
        ? defaultRoomType.name
        : "Room";
}
