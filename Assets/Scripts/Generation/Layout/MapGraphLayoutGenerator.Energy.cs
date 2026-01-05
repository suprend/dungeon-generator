// Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Energy.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private const float OverlapWeight = 1000f;
    private const float DistanceWeight = 1f;

    private readonly List<RoomPlacement> roomListScratch = new();

    private float ComputeEnergy(Dictionary<string, RoomPlacement> rooms)
    {
        using var _ps = PS(S_ComputeEnergy);
        float overlapArea = 0f;
        roomListScratch.Clear();
        foreach (var p in rooms.Values)
            roomListScratch.Add(p);
        for (int i = 0; i < roomListScratch.Count; i++)
        {
            for (int j = i + 1; j < roomListScratch.Count; j++)
            {
                overlapArea += IntersectionPenalty(roomListScratch[i], roomListScratch[j]);
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

        return OverlapWeight * overlapArea + DistanceWeight * distPenalty;
    }

    private float ComputeEdgeDistancePenalty(RoomPlacement a, RoomPlacement b)
    {
        using var _ps = PS(S_ComputeEdgeDistancePenalty);
        if (a == null || b == null)
            return 0f;
        if (RoomsTouchEitherWay(a, b))
            return 0f;
        var da = CenterOf(a);
        var db = CenterOf(b);
        var diff = da - db;
        return diff.sqrMagnitude;
    }

    // PairKey removed in favor of index-based PairIndex().

    private float IntersectionArea(RoomPlacement a, RoomPlacement b)
    {
        if (a == null || b == null)
            return 0f;

        var aFloor = a.Shape?.FloorCells;
        var bFloor = b.Shape?.FloorCells;
        if (aFloor == null || bFloor == null)
            return 0f;

        var deltaBA = b.Root - a.Root;
        var overlapCount = CountOverlapShifted(aFloor, bFloor, deltaBA, AllowedWorldCells.None, a.Root, out _, earlyStopAtTwo: false);
        if (overlapCount <= 0)
            return 0f;
        if (overlapCount == 1 && IsAllowedBiteOverlap(a, b, 1))
            return 0f;
        return overlapCount;
    }

    private float IntersectionPenalty(RoomPlacement a, RoomPlacement b)
    {
        if (a == null || b == null)
            return 0f;

        return IntersectionPenaltyFast(a, b);
    }

    private float IntersectionPenaltyFast(RoomPlacement a, RoomPlacement b)
    {
        using var _ps = PS(S_IntersectionPenalty);
        if (a?.Shape == null || b?.Shape == null)
            return 0f;

        var aFloor = a.Shape.FloorCells;
        var bFloor = b.Shape.FloorCells;
        var aWall = a.Shape.WallCells;
        var bWall = b.Shape.WallCells;
        if (aFloor == null || bFloor == null || aWall == null || bWall == null)
            return 0f;

        static bool BoundsOverlap(Vector2Int aMin, Vector2Int aMax, Vector2Int bMin, Vector2Int bMax)
        {
            if (aMax.x < bMin.x || bMax.x < aMin.x)
                return false;
            if (aMax.y < bMin.y || bMax.y < aMin.y)
                return false;
            return true;
        }

        var aFloorMinW = a.Shape.Min + a.Root;
        var aFloorMaxW = a.Shape.Max + a.Root;
        var bFloorMinW = b.Shape.Min + b.Root;
        var bFloorMaxW = b.Shape.Max + b.Root;
        var aWallMinW = a.Shape.WallMin + a.Root;
        var aWallMaxW = a.Shape.WallMax + a.Root;
        var bWallMinW = b.Shape.WallMin + b.Root;
        var bWallMaxW = b.Shape.WallMax + b.Root;

        var checkFloorFloor = BoundsOverlap(aFloorMinW, aFloorMaxW, bFloorMinW, bFloorMaxW);
        var checkAWallBFloor = BoundsOverlap(aWallMinW, aWallMaxW, bFloorMinW, bFloorMaxW);
        var checkBWallAFloor = BoundsOverlap(bWallMinW, bWallMaxW, aFloorMinW, aFloorMaxW);
        if (!checkFloorFloor && !checkAWallBFloor && !checkBWallAFloor)
            return 0f;

        var penalty = 0f;

        // Delta from A-local to B-local overlap checks.
        var deltaBA = b.Root - a.Root;

        TryGetBiteAllowance(a, b, out var allowedFloor, out var allowedWallA, out var allowedWallB);

        if (checkFloorFloor)
        {
            // Floor↔floor overlaps (except allowed bite-depth cut cells).
            var illegalFloorFloor = CountOverlapShifted(aFloor, bFloor, deltaBA, allowedFloor, a.Root, out _, earlyStopAtTwo: false);
            if (illegalFloorFloor > 0)
                penalty += illegalFloorFloor;
        }

        if (checkAWallBFloor)
        {
            // aWalls vs bFloors
            penalty += CountOverlapShifted(aWall, bFloor, deltaBA, allowedWallA, a.Root, out _, earlyStopAtTwo: false);
        }

        if (checkBWallAFloor)
        {
            // bWalls vs aFloors: invert delta (A relative to B)
            var deltaAB = a.Root - b.Root;
            penalty += CountOverlapShifted(bWall, aFloor, deltaAB, allowedWallB, b.Root, out _, earlyStopAtTwo: false);
        }

        return penalty;
    }

    // CountOverlapShifted is implemented in Validation.cs to share the no-allocation AllowedWorldCells path.

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
