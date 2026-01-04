// Assets/scripts/Generation/Graph/MapGraphLayoutGenerator.Energy.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private const float OverlapWeight = 1000f;
    private const float DistanceWeight = 1f;

    private readonly List<RoomPlacement> roomListScratch = new();

    private sealed class EnergyCache
    {
        public float OverlapPenaltySum { get; set; }
        public float DistancePenaltySum { get; set; }
        public int NodeCount { get; }
        public RoomPlacement[] PlacementsByIndex { get; }
        public bool[] IsPlaced { get; }
        public float[] PairPenalty { get; }
        public float[] EdgePenalty { get; }
        public float TotalEnergy => OverlapWeight * OverlapPenaltySum + DistanceWeight * DistancePenaltySum;

        public EnergyCache(
            int nodeCount,
            RoomPlacement[] placementsByIndex,
            bool[] isPlaced,
            float overlapPenaltySum,
            float distancePenaltySum,
            float[] pairPenalty,
            float[] edgePenalty)
        {
            NodeCount = Mathf.Max(0, nodeCount);
            PlacementsByIndex = placementsByIndex ?? new RoomPlacement[NodeCount];
            IsPlaced = isPlaced ?? new bool[NodeCount];
            OverlapPenaltySum = overlapPenaltySum;
            DistancePenaltySum = distancePenaltySum;
            PairPenalty = pairPenalty ?? new float[PairArrayLength(NodeCount)];
            EdgePenalty = edgePenalty ?? new float[PairArrayLength(NodeCount)];
        }
    }

    private static int PairArrayLength(int n) => n <= 1 ? 0 : (n * (n - 1)) / 2;

    private static int PairIndex(int a, int b, int n)
    {
        if (a == b || n <= 1)
            return -1;
        if (a > b)
        {
            var t = a;
            a = b;
            b = t;
        }
        // Index into packed upper triangle (excluding diagonal).
        // rowStart(a) = a*(n-1) - (a*(a+1))/2
        var rowStart = a * (n - 1) - (a * (a + 1)) / 2;
        return rowStart + (b - a - 1);
    }

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

    private EnergyCache BuildEnergyCache(Dictionary<string, RoomPlacement> rooms)
    {
        using var _ps = PS(S_BuildEnergyCache);
        if (rooms == null || nodeIdByIndex == null || nodeIndexById == null)
            return new EnergyCache(0, null, null, 0f, 0f, null, null);

        var nodeCount = nodeIdByIndex.Length;
        var placementsByIndex = new RoomPlacement[nodeCount];
        var isPlaced = new bool[nodeCount];
        foreach (var kv in rooms)
        {
            if (kv.Value == null)
                continue;
            if (!nodeIndexById.TryGetValue(kv.Key, out var idx))
                continue;
            placementsByIndex[idx] = kv.Value;
            isPlaced[idx] = true;
        }

        float overlapSum = 0f;
        var pairPenalty = new float[PairArrayLength(nodeCount)];
        var placedIndices = new List<int>(rooms.Count);
        for (var i = 0; i < nodeCount; i++)
        {
            if (isPlaced[i])
                placedIndices.Add(i);
        }
        for (int pi = 0; pi < placedIndices.Count; pi++)
        {
            var i = placedIndices[pi];
            var a = placementsByIndex[i];
            if (a == null)
                continue;
            for (int pj = pi + 1; pj < placedIndices.Count; pj++)
            {
                var j = placedIndices[pj];
                var b = placementsByIndex[j];
                if (a == null || b == null)
                    continue;
                var p = IntersectionPenalty(a, b);
                var idx = PairIndex(i, j, nodeCount);
                if (idx >= 0)
                    pairPenalty[idx] = p;
                overlapSum += p;
            }
        }

        float distSum = 0f;
        var edgePenalty = new float[PairArrayLength(nodeCount)];
        if (neighborIndicesByIndex != null)
        {
            for (int pi = 0; pi < placedIndices.Count; pi++)
            {
                var i = placedIndices[pi];
                var a = placementsByIndex[i];
                if (a == null)
                    continue;
                var neigh = neighborIndicesByIndex[i];
                if (neigh == null)
                    continue;
                for (var k = 0; k < neigh.Length; k++)
                {
                    var j = neigh[k];
                    if (j <= i)
                        continue;
                    if (j < 0 || j >= nodeCount || !isPlaced[j])
                        continue;
                    var b = placementsByIndex[j];
                    if (b == null)
                        continue;
                    var p = ComputeEdgeDistancePenalty(a, b);
                    var idx = PairIndex(i, j, nodeCount);
                    if (idx >= 0)
                        edgePenalty[idx] = p;
                    distSum += p;
                }
            }
        }

        return new EnergyCache(nodeCount, placementsByIndex, isPlaced, overlapSum, distSum, pairPenalty, edgePenalty);
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
