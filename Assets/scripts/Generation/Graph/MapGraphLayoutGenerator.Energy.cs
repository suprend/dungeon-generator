// Assets/scripts/Generation/Graph/MapGraphLayoutGenerator.Energy.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private float ComputeEnergy(Dictionary<string, RoomPlacement> rooms)
    {
        const float overlapWeight = 1000f;
        const float distanceWeight = 1f;

        float overlapArea = 0f;
        var list = rooms.Values.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            for (int j = i + 1; j < list.Count; j++)
            {
                overlapArea += IntersectionPenalty(list[i], list[j]);
            }
        }

        float distPenalty = 0f;
        foreach (var edge in graphAsset.Edges)
        {
            if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                continue;
            if (!rooms.TryGetValue(edge.fromNodeId, out var a) || !rooms.TryGetValue(edge.toNodeId, out var b))
                continue;
            if (RoomsTouchEitherWay(a, b))
                continue;
            var da = CenterOf(a);
            var db = CenterOf(b);
            var diff = da - db;
            distPenalty += diff.sqrMagnitude;
        }

        return overlapWeight * overlapArea + distanceWeight * distPenalty;
    }

    private float IntersectionArea(RoomPlacement a, RoomPlacement b)
    {
        if (a == null || b == null)
            return 0f;

        var overlapCount = CountOverlapCells(a.WorldCells, b.WorldCells, out _);
        if (overlapCount == 0)
            return 0f;
        if (IsAllowedBiteOverlap(a, b, overlapCount))
            return 0f;
        return overlapCount;
    }

    private float IntersectionPenalty(RoomPlacement a, RoomPlacement b)
    {
        if (a == null || b == null)
            return 0f;

        var penalty = 0f;

        var floorOverlapCount = CountOverlapAll(a.WorldCells, b.WorldCells, (HashSet<Vector2Int>)null, out var lastFloorOverlapCell);
        var allowedBite = floorOverlapCount == 1 && IsAllowedBiteOverlap(a, b, 1);
        if (floorOverlapCount > 0 && !allowedBite)
            penalty += floorOverlapCount;

        var allowedA = allowedBite ? AllowedWallOnFloorCells(a, b, lastFloorOverlapCell) : null;
        var allowedB = allowedBite ? AllowedWallOnFloorCells(b, a, lastFloorOverlapCell) : null;
        penalty += CountOverlapAll(a.WorldWallCells, b.WorldCells, allowedA, out _);
        penalty += CountOverlapAll(b.WorldWallCells, a.WorldCells, allowedB, out _);

        return penalty;
    }

    private Vector2 CenterOf(RoomPlacement p)
    {
        if (p?.Shape?.FloorCells == null || p.Shape.FloorCells.Count == 0)
            return p?.Root ?? Vector2.zero;
        var sum = Vector2.zero;
        foreach (var c in p.Shape.FloorCells)
            sum += (Vector2)(c + p.Root);
        return sum / p.Shape.FloorCells.Count;
    }
}

