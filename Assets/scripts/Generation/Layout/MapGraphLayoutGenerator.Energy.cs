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
        public Dictionary<(string a, string b), float> PairPenalty { get; }
        public Dictionary<(string a, string b), float> EdgePenalty { get; }
        public float TotalEnergy => OverlapWeight * OverlapPenaltySum + DistanceWeight * DistancePenaltySum;

        public EnergyCache(
            float overlapPenaltySum,
            float distancePenaltySum,
            Dictionary<(string a, string b), float> pairPenalty,
            Dictionary<(string a, string b), float> edgePenalty)
        {
            OverlapPenaltySum = overlapPenaltySum;
            DistancePenaltySum = distancePenaltySum;
            PairPenalty = pairPenalty ?? new Dictionary<(string a, string b), float>();
            EdgePenalty = edgePenalty ?? new Dictionary<(string a, string b), float>();
        }
    }

    private float ComputeEnergy(Dictionary<string, RoomPlacement> rooms)
    {
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
        if (rooms == null)
            return new EnergyCache(0f, 0f, new Dictionary<(string a, string b), float>(), new Dictionary<(string a, string b), float>());

        float overlapSum = 0f;
        var pairPenalty = new Dictionary<(string a, string b), float>();
        roomListScratch.Clear();
        foreach (var p in rooms.Values)
            roomListScratch.Add(p);
        for (int i = 0; i < roomListScratch.Count; i++)
        {
            for (int j = i + 1; j < roomListScratch.Count; j++)
            {
                var a = roomListScratch[i];
                var b = roomListScratch[j];
                if (a == null || b == null)
                    continue;
                var key = PairKey(a.NodeId, b.NodeId);
                var p = IntersectionPenalty(a, b);
                pairPenalty[key] = p;
                overlapSum += p;
            }
        }

        float distSum = 0f;
        var edgePenalty = new Dictionary<(string a, string b), float>();
        if (graphAsset != null)
        {
            foreach (var edge in graphAsset.Edges)
            {
                if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                    continue;
                if (!rooms.TryGetValue(edge.fromNodeId, out var a) || !rooms.TryGetValue(edge.toNodeId, out var b))
                    continue;
                var key = PairKey(edge.fromNodeId, edge.toNodeId);
                var p = ComputeEdgeDistancePenalty(a, b);
                edgePenalty[key] = p;
                distSum += p;
            }
        }

        return new EnergyCache(overlapSum, distSum, pairPenalty, edgePenalty);
    }

    private EnergyCache UpdateEnergyCacheForMove(EnergyCache baseCache, Dictionary<string, RoomPlacement> roomsBefore, Dictionary<string, RoomPlacement> roomsAfter, string changedId)
    {
        if (baseCache == null)
            return BuildEnergyCache(roomsAfter);
        if (roomsAfter == null || string.IsNullOrEmpty(changedId) || !roomsAfter.TryGetValue(changedId, out var changedAfter))
            return BuildEnergyCache(roomsAfter);

        var nextPairPenalty = new Dictionary<(string a, string b), float>(baseCache.PairPenalty);
        var nextEdgePenalty = new Dictionary<(string a, string b), float>(baseCache.EdgePenalty);
        float overlapSum = baseCache.OverlapPenaltySum;
        float distSum = baseCache.DistancePenaltySum;

        // Update pair penalties for (changed, other).
        foreach (var otherId in roomsAfter.Keys)
        {
            if (otherId == changedId)
                continue;
            if (!roomsAfter.TryGetValue(otherId, out var otherPlacement) || otherPlacement == null)
                continue;

            var key = PairKey(changedId, otherId);
            if (nextPairPenalty.TryGetValue(key, out var oldP))
                overlapSum -= oldP;
            var newP = IntersectionPenalty(changedAfter, otherPlacement);
            nextPairPenalty[key] = newP;
            overlapSum += newP;
        }

        // Update distance penalties only for edges incident to changed node.
        if (graphAsset != null)
        {
            var touched = new HashSet<(string a, string b)>();
            foreach (var edge in graphAsset.GetEdgesFor(changedId))
            {
                if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                    continue;
                var aId = edge.fromNodeId;
                var bId = edge.toNodeId;
                var key = PairKey(aId, bId);
                if (!touched.Add(key))
                    continue;
                if (!roomsAfter.TryGetValue(aId, out var a) || !roomsAfter.TryGetValue(bId, out var b))
                    continue;

                if (nextEdgePenalty.TryGetValue(key, out var oldD))
                    distSum -= oldD;
                var newD = ComputeEdgeDistancePenalty(a, b);
                nextEdgePenalty[key] = newD;
                distSum += newD;
            }
        }

        return new EnergyCache(overlapSum, distSum, nextPairPenalty, nextEdgePenalty);
    }

    private float ComputeEdgeDistancePenalty(RoomPlacement a, RoomPlacement b)
    {
        if (a == null || b == null)
            return 0f;
        if (RoomsTouchEitherWay(a, b))
            return 0f;
        var da = CenterOf(a);
        var db = CenterOf(b);
        var diff = da - db;
        return diff.sqrMagnitude;
    }

    private static (string a, string b) PairKey(string a, string b)
    {
        if (string.CompareOrdinal(a, b) <= 0)
            return (a, b);
        return (b, a);
    }

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
        if (a?.Shape == null || b?.Shape == null)
            return 0f;

        var aFloor = a.Shape.FloorCells;
        var bFloor = b.Shape.FloorCells;
        var aWall = a.Shape.WallCells;
        var bWall = b.Shape.WallCells;
        if (aFloor == null || bFloor == null || aWall == null || bWall == null)
            return 0f;

        var penalty = 0f;

        // Delta from A-local to B-local overlap checks.
        var deltaBA = b.Root - a.Root;

        TryGetBiteAllowance(a, b, out var allowedFloor, out var allowedWallA, out var allowedWallB);

        // Floor↔floor overlaps (except allowed bite-depth cut cells).
        var illegalFloorFloor = CountOverlapShifted(aFloor, bFloor, deltaBA, allowedFloor, a.Root, out _, earlyStopAtTwo: false);
        if (illegalFloorFloor > 0)
            penalty += illegalFloorFloor;

        // aWalls vs bFloors
        penalty += CountOverlapShifted(aWall, bFloor, deltaBA, allowedWallA, a.Root, out _, earlyStopAtTwo: false);

        // bWalls vs aFloors: invert delta (A relative to B)
        var deltaAB = a.Root - b.Root;
        penalty += CountOverlapShifted(bWall, aFloor, deltaAB, allowedWallB, b.Root, out _, earlyStopAtTwo: false);

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
