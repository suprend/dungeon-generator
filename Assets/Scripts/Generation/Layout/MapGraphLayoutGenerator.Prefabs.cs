// Assets/scripts/Generation/Graph/MapGraphLayoutGenerator.Prefabs.cs
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private List<GameObject> GetRoomPrefabs(MapGraphAsset.NodeData node)
    {
        if (node == null)
            return new List<GameObject>();
        if (roomPrefabLookup.TryGetValue(node.id, out var list))
            return new List<GameObject>(list);
        return new List<GameObject>();
    }

    private Dictionary<string, List<GameObject>> BuildRoomPrefabLookup(MapGraphAsset graph)
    {
        var lookup = new Dictionary<string, List<GameObject>>();
        foreach (var node in graph.Nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.id))
                continue;
            var list = new List<GameObject>();
            var type = node.roomType != null ? node.roomType : graph.DefaultRoomType;
            if (type?.prefabs != null)
            {
                foreach (var p in type.prefabs)
                {
                    if (p != null)
                        list.Add(p);
                }
            }
            lookup[node.id] = list;
        }
        return lookup;
    }
}

