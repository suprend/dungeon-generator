// Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Prefabs.cs
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private List<GameObject> GetRoomPrefabs(MapGraphAsset.NodeData node)
    {
        if (node == null)
            return new List<GameObject>();
        if (prefabsByRoomType != null)
        {
            var type = node.roomType != null ? node.roomType : graphAsset?.DefaultRoomType;
            if (type != null && prefabsByRoomType.TryGetValue(type, out var byType))
                return new List<GameObject>(byType);
        }
        if (roomPrefabLookup != null && roomPrefabLookup.TryGetValue(node.id, out var list))
            return new List<GameObject>(list);
        return new List<GameObject>();
    }

    private static Dictionary<string, MapGraphAsset.NodeData> BuildNodeById(MapGraphAsset graph)
    {
        var map = new Dictionary<string, MapGraphAsset.NodeData>();
        if (graph?.Nodes == null)
            return map;
        foreach (var node in graph.Nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.id))
                continue;
            map[node.id] = node;
        }
        return map;
    }

    private static Dictionary<RoomTypeAsset, List<GameObject>> BuildPrefabsByRoomType(MapGraphAsset graph)
    {
        var map = new Dictionary<RoomTypeAsset, List<GameObject>>();
        if (graph?.Nodes == null)
            return map;

        void Ensure(RoomTypeAsset type)
        {
            if (type == null || map.ContainsKey(type))
                return;
            var list = new List<GameObject>();
            if (type.prefabs != null)
            {
                for (int i = 0; i < type.prefabs.Count; i++)
                {
                    var p = type.prefabs[i];
                    if (p != null)
                        list.Add(p);
                }
            }
            map[type] = list;
        }

        if (graph.DefaultRoomType != null)
            Ensure(graph.DefaultRoomType);

        foreach (var node in graph.Nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.id))
                continue;
            var type = node.roomType != null ? node.roomType : graph.DefaultRoomType;
            Ensure(type);
        }

        return map;
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
