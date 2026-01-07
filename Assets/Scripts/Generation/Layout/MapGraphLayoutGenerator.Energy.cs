// Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Energy.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private const float OverlapWeight = 1000f;
    private const float DistanceWeight = 1f;
    private const float InvalidEdgePenalty = 1000f;

    private readonly List<RoomPlacement> roomListScratch = new();

    private static bool BoundsOverlap(Vector2Int aMin, Vector2Int aMax, Vector2Int bMin, Vector2Int bMax)
    {
        if (aMax.x < bMin.x || bMax.x < aMin.x)
            return false;
        if (aMax.y < bMin.y || bMax.y < aMin.y)
            return false;
        return true;
    }

    private bool AreGraphNeighbors(string nodeIdA, string nodeIdB)
    {
        if (string.IsNullOrEmpty(nodeIdA) || string.IsNullOrEmpty(nodeIdB))
            return false;
        if (nodeIndexById == null || neighborIndicesByIndex == null)
            return false;

        if (!nodeIndexById.TryGetValue(nodeIdA, out var indexA))
            return false;
        if (!nodeIndexById.TryGetValue(nodeIdB, out var indexB))
            return false;

        if (indexA < 0 || indexA >= neighborIndicesByIndex.Length)
            return false;

        var neighbors = neighborIndicesByIndex[indexA];
        for (int i = 0; i < neighbors.Length; i++)
        {
            if (neighbors[i] == indexB)
                return true;
        }
        return false;
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

    private float ComputeEdgeDistancePenalty(RoomPlacement a, RoomPlacement b)
    {
        using var _ps = PS(S_ComputeEdgeDistancePenalty);
        if (a == null || b == null)
            return 0f;

        var aIsConnector = IsConnector(a.Prefab);
        var bIsConnector = IsConnector(b.Prefab);
        if (aIsConnector == bIsConnector)
            return InvalidEdgePenalty;

        if (RoomsTouchEitherWay(a, b))
        {
            // Touching by config-space is not sufficient: enforce bite-depth socket compatibility.
            var conn = aIsConnector ? a : b;
            var room = aIsConnector ? b : a;
            return TryFindBiteDepth(conn, room, out _, out _, out _, out _) ? 0f : InvalidEdgePenalty;
        }
        var da = CenterOf(a);
        var db = CenterOf(b);
        var diff = da - db;
        return diff.sqrMagnitude;
    }

    private float ComputeEdgeDistancePenaltyRaw(
        GameObject aPrefab,
        ModuleShape aShape,
        Vector2Int aRoot,
        GameObject bPrefab,
        ModuleShape bShape,
        Vector2Int bRoot)
    {
        if (aPrefab == null || bPrefab == null)
            return 0f;
        if (aShape == null || bShape == null)
            return 0f;

        var aIsConnector = IsConnector(aPrefab);
        var bIsConnector = IsConnector(bPrefab);
        if (aIsConnector == bIsConnector)
            return InvalidEdgePenalty;

        if (RoomsTouchEitherWayRaw(aPrefab, aRoot, bPrefab, bRoot))
        {
            var connPrefab = aIsConnector ? aPrefab : bPrefab;
            var connShape = aIsConnector ? aShape : bShape;
            var connRoot = aIsConnector ? aRoot : bRoot;
            var roomPrefab = aIsConnector ? bPrefab : aPrefab;
            var roomShape = aIsConnector ? bShape : aShape;
            var roomRoot = aIsConnector ? bRoot : aRoot;

            return TryFindBiteDepthRaw(connPrefab, connShape, connRoot, roomPrefab, roomShape, roomRoot, out _, out _, out _, out _)
                ? 0f
                : InvalidEdgePenalty;
        }
        var da = CenterOfRaw(aShape, aRoot);
        var db = CenterOfRaw(bShape, bRoot);
        var diff = da - db;
        return diff.sqrMagnitude;
    }

    // PairKey removed in favor of index-based PairIndex().

    private float IntersectionArea(RoomPlacement a, RoomPlacement b)
    {
        if (a == null || b == null)
            return 0f;

        if (a.Shape == null || b.Shape == null)
            return 0f;

        var deltaBA = b.Root - a.Root;
        var overlapCount = 0;

        if (settings != null && settings.UseBitsetOverlap)
        {
            var bitsA = GetBitsets(a.Shape);
            var bitsB = GetBitsets(b.Shape);
            if (bitsA?.Floor != null && bitsB?.Floor != null)
            {
                var shift = (bitsB.Floor.Min + deltaBA) - bitsA.Floor.Min;
                overlapCount = bitsA.Floor.CountOverlapsShifted(bitsB.Floor, shift);
            }
        }
        else
        {
            var aFloor = a.Shape.FloorCells;
            var bFloor = b.Shape.FloorCells;
            if (aFloor == null || bFloor == null)
                return 0f;
            overlapCount = CountOverlapShifted(aFloor, bFloor, deltaBA, AllowedWorldCells.None, a.Root, out _, earlyStopAtTwo: false);
        }

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

    private float IntersectionPenaltyRaw(
        string aNodeId,
        GameObject aPrefab,
        ModuleShape aShape,
        Vector2Int aRoot,
        string bNodeId,
        GameObject bPrefab,
        ModuleShape bShape,
        Vector2Int bRoot)
    {
        if (aShape == null || bShape == null)
            return 0f;

        if (settings != null && settings.UseBitsetOverlap)
            return IntersectionPenaltyFastBitsetRaw(aNodeId, aPrefab, aShape, aRoot, bNodeId, bPrefab, bShape, bRoot);

        var aFloor = aShape.FloorCells;
        var bFloor = bShape.FloorCells;
        var aWall = aShape.WallCells;
        var bWall = bShape.WallCells;
        if (aFloor == null || bFloor == null || aWall == null || bWall == null)
            return 0f;

        var aFloorMinW = aShape.Min + aRoot;
        var aFloorMaxW = aShape.Max + aRoot;
        var bFloorMinW = bShape.Min + bRoot;
        var bFloorMaxW = bShape.Max + bRoot;
        var aWallMinW = aShape.WallMin + aRoot;
        var aWallMaxW = aShape.WallMax + aRoot;
        var bWallMinW = bShape.WallMin + bRoot;
        var bWallMaxW = bShape.WallMax + bRoot;

        var checkFloorFloor = BoundsOverlap(aFloorMinW, aFloorMaxW, bFloorMinW, bFloorMaxW);
        var checkAWallBFloor = BoundsOverlap(aWallMinW, aWallMaxW, bFloorMinW, bFloorMaxW);
        var checkBWallAFloor = BoundsOverlap(bWallMinW, bWallMaxW, aFloorMinW, aFloorMaxW);
        if (!checkFloorFloor && !checkAWallBFloor && !checkBWallAFloor)
            return 0f;

        var penalty = 0f;

        var deltaBA = bRoot - aRoot;

        TryGetBiteAllowanceRaw(
            aNodeId,
            aPrefab,
            aShape,
            aRoot,
            bNodeId,
            bPrefab,
            bShape,
            bRoot,
            out var allowedFloor,
            out var allowedWallA,
            out var allowedWallB);

        if (checkFloorFloor)
        {
            var illegal = CountOverlapShifted(aFloor, bFloor, deltaBA, allowedFloor, aRoot, out _, earlyStopAtTwo: false);
            if (illegal > 0)
                penalty += illegal;
        }

        if (checkAWallBFloor)
            penalty += CountOverlapShifted(aWall, bFloor, deltaBA, allowedWallA, aRoot, out _, earlyStopAtTwo: false);

        if (checkBWallAFloor)
        {
            var deltaAB = aRoot - bRoot;
            penalty += CountOverlapShifted(bWall, aFloor, deltaAB, allowedWallB, bRoot, out _, earlyStopAtTwo: false);
        }

        return penalty;
    }



    private sealed class ShapeBitsets
    {
        public BitGrid Floor { get; }
        public BitGrid Wall { get; }

        public ShapeBitsets(BitGrid floor, BitGrid wall)
        {
            Floor = floor;
            Wall = wall;
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ModuleShape, ShapeBitsets> BitsetsByShape = new();

    private static ShapeBitsets GetBitsets(ModuleShape shape)
    {
        using var _ps = PS(S_GetBitsets);
        if (shape == null)
            return null;
        if (BitsetsByShape.TryGetValue(shape, out var cached))
            return cached;

        var floor = BitGrid.Build(shape.FloorCells, shape.Min, shape.Max);
        var wall = BitGrid.Build(shape.WallCells, shape.WallMin, shape.WallMax);
        cached = new ShapeBitsets(floor, wall);
        BitsetsByShape.Add(shape, cached);
        return cached;
    }

    private static int CountAllowedOverlapCells(BitGrid fixedGrid, Vector2Int fixedRoot, BitGrid movingGrid, Vector2Int movingRoot, AllowedWorldCells allowed)
    {
        if (fixedGrid == null || movingGrid == null)
            return 0;

        bool IsOverlapAtWorld(Vector2Int world)
        {
            var fixedLocal = world - fixedRoot;
            var movingLocal = world - movingRoot;
            return fixedGrid.ContainsLocal(fixedLocal) && movingGrid.ContainsLocal(movingLocal);
        }

        if (allowed.IsEmpty)
            return 0;

        if (allowed.TryGetExplicit(out var explicitCount, out var a, out var b, out var c))
        {
            var cnt = 0;
            if (explicitCount >= 1 && IsOverlapAtWorld(a)) cnt++;
            if (explicitCount >= 3)
            {
                if (IsOverlapAtWorld(b)) cnt++;
                if (IsOverlapAtWorld(c)) cnt++;
            }
            return cnt;
        }

        var total = 0;
        if (!allowed.TryGetRays(out var rayBase, out var rayInward, out var rayTangent, out var maxK, out var rayMask))
            return 0;

        for (int k = 0; k <= maxK; k++)
        {
            var center = rayBase + rayInward * k;
            if ((rayMask & 1) != 0 && IsOverlapAtWorld(center))
                total++;
            if ((rayMask & 2) != 0)
            {
                if (IsOverlapAtWorld(center + rayTangent))
                    total++;
                if (IsOverlapAtWorld(center - rayTangent))
                    total++;
            }
        }
        return total;
    }

    private float IntersectionPenaltyFastBitset(RoomPlacement a, RoomPlacement b)
    {
        var deepProfile = settings != null && settings.LogLayoutProfiling;
        using var _ps = PSIf(deepProfile, S_IntersectionPenalty_Bitset);

        if (a?.Shape == null || b?.Shape == null)
            return 0f;

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

        var bitsA = GetBitsets(a.Shape);
        var bitsB = GetBitsets(b.Shape);
        if (bitsA == null || bitsB == null)
            return 0f;

        var aFloor = bitsA.Floor;
        var bFloor = bitsB.Floor;
        var aWall = bitsA.Wall;
        var bWall = bitsB.Wall;

        // Delta from A-local to B-local overlap checks.
        var deltaBA = b.Root - a.Root;

        var floorTotal = 0;
        var aWallTotal = 0;
        var bWallTotal = 0;

        // First pass: count raw overlaps without computing bite allowance.
        // Most pairs have 0 overlaps even if their bounds touch; avoiding TryGetBiteAllowance() is a big win.

        // Floor↔floor overlaps.
        if (checkFloorFloor && aFloor != null && bFloor != null)
        {
            using var _psFloor = PSIf(deepProfile, S_IntersectionPenalty_Bitset_FloorFloor);
            var shift = (bFloor.Min + deltaBA) - aFloor.Min;
            floorTotal = aFloor.CountOverlapsShifted(bFloor, shift);
        }

        // aWalls vs bFloors.
        if (checkAWallBFloor && aWall != null && bFloor != null)
        {
            using var _psWallA = PSIf(deepProfile, S_IntersectionPenalty_Bitset_AWall_BFloor);
            var shift = (bFloor.Min + deltaBA) - aWall.Min;
            aWallTotal = aWall.CountOverlapsShifted(bFloor, shift);
        }

        // bWalls vs aFloors.
        if (checkBWallAFloor && bWall != null && aFloor != null)
        {
            using var _psWallB = PSIf(deepProfile, S_IntersectionPenalty_Bitset_BWall_AFloor);
            var deltaAB = a.Root - b.Root;
            var shift = (aFloor.Min + deltaAB) - bWall.Min;
            bWallTotal = bWall.CountOverlapsShifted(aFloor, shift);
        }

        if (floorTotal == 0 && aWallTotal == 0 && bWallTotal == 0)
            return 0f;

        // OPTIMIZATION: Bite allowance only matters for graph neighbors (rooms with doors).
        // Non-neighbors always get full overlap penalty, no need for expensive bite calculation.
        if (!AreGraphNeighbors(a.NodeId, b.NodeId))
        {
            return floorTotal + aWallTotal + bWallTotal;
        }

        AllowedWorldCells allowedFloor;
        AllowedWorldCells allowedWallA;
        AllowedWorldCells allowedWallB;
        using (PSIf(deepProfile, S_IntersectionPenalty_Bitset_BiteAllowance))
        {
            TryGetBiteAllowance(a, b, out allowedFloor, out allowedWallA, out allowedWallB);
        }

        var penalty = 0;

        // Second pass: subtract allowed bite overlaps (if any).
        if (floorTotal > 0)
        {
            var allowed = 0;
            using (PSIf(deepProfile, S_IntersectionPenalty_Bitset_Allowed))
            {
                if (!allowedFloor.IsEmpty)
                    allowed = CountAllowedOverlapCells(aFloor, a.Root, bFloor, b.Root, allowedFloor);
            }
            penalty += Mathf.Max(0, floorTotal - allowed);
        }

        if (aWallTotal > 0)
        {
            var allowed = 0;
            using (PSIf(deepProfile, S_IntersectionPenalty_Bitset_Allowed))
            {
                if (!allowedWallA.IsEmpty)
                    allowed = CountAllowedOverlapCells(aWall, a.Root, bFloor, b.Root, allowedWallA);
            }
            penalty += Mathf.Max(0, aWallTotal - allowed);
        }

        if (bWallTotal > 0)
        {
            var allowed = 0;
            using (PSIf(deepProfile, S_IntersectionPenalty_Bitset_Allowed))
            {
                if (!allowedWallB.IsEmpty)
                    allowed = CountAllowedOverlapCells(bWall, b.Root, aFloor, a.Root, allowedWallB);
            }
            penalty += Mathf.Max(0, bWallTotal - allowed);
        }

        return penalty;
    }

    private float IntersectionPenaltyFastBitsetRaw(
        string aNodeId,
        GameObject aPrefab,
        ModuleShape aShape,
        Vector2Int aRoot,
        string bNodeId,
        GameObject bPrefab,
        ModuleShape bShape,
        Vector2Int bRoot)
    {
        if (aShape == null || bShape == null)
            return 0f;

        var aFloorMinW = aShape.Min + aRoot;
        var aFloorMaxW = aShape.Max + aRoot;
        var bFloorMinW = bShape.Min + bRoot;
        var bFloorMaxW = bShape.Max + bRoot;
        var aWallMinW = aShape.WallMin + aRoot;
        var aWallMaxW = aShape.WallMax + aRoot;
        var bWallMinW = bShape.WallMin + bRoot;
        var bWallMaxW = bShape.WallMax + bRoot;

        var checkFloorFloor = BoundsOverlap(aFloorMinW, aFloorMaxW, bFloorMinW, bFloorMaxW);
        var checkAWallBFloor = BoundsOverlap(aWallMinW, aWallMaxW, bFloorMinW, bFloorMaxW);
        var checkBWallAFloor = BoundsOverlap(bWallMinW, bWallMaxW, aFloorMinW, aFloorMaxW);
        if (!checkFloorFloor && !checkAWallBFloor && !checkBWallAFloor)
            return 0f;

        var bitsA = GetBitsets(aShape);
        var bitsB = GetBitsets(bShape);
        if (bitsA == null || bitsB == null)
            return 0f;

        var aFloor = bitsA.Floor;
        var bFloor = bitsB.Floor;
        var aWall = bitsA.Wall;
        var bWall = bitsB.Wall;

        var deltaBA = bRoot - aRoot;

        var floorTotal = 0;
        var aWallTotal = 0;
        var bWallTotal = 0;

        if (checkFloorFloor && aFloor != null && bFloor != null)
        {
            var shift = (bFloor.Min + deltaBA) - aFloor.Min;
            floorTotal = aFloor.CountOverlapsShifted(bFloor, shift);
        }

        if (checkAWallBFloor && aWall != null && bFloor != null)
        {
            var shift = (bFloor.Min + deltaBA) - aWall.Min;
            aWallTotal = aWall.CountOverlapsShifted(bFloor, shift);
        }

        if (checkBWallAFloor && bWall != null && aFloor != null)
        {
            var deltaAB = aRoot - bRoot;
            var shift = (aFloor.Min + deltaAB) - bWall.Min;
            bWallTotal = bWall.CountOverlapsShifted(aFloor, shift);
        }

        if (floorTotal == 0 && aWallTotal == 0 && bWallTotal == 0)
            return 0f;

        TryGetBiteAllowanceRaw(
            aNodeId,
            aPrefab,
            aShape,
            aRoot,
            bNodeId,
            bPrefab,
            bShape,
            bRoot,
            out var allowedFloor,
            out var allowedWallA,
            out var allowedWallB);

        var penalty = 0;

        if (floorTotal > 0)
        {
            var allowed = allowedFloor.IsEmpty ? 0 : CountAllowedOverlapCells(aFloor, aRoot, bFloor, bRoot, allowedFloor);
            penalty += Mathf.Max(0, floorTotal - allowed);
        }

        if (aWallTotal > 0)
        {
            var allowed = allowedWallA.IsEmpty ? 0 : CountAllowedOverlapCells(aWall, aRoot, bFloor, bRoot, allowedWallA);
            penalty += Mathf.Max(0, aWallTotal - allowed);
        }

        if (bWallTotal > 0)
        {
            var allowed = allowedWallB.IsEmpty ? 0 : CountAllowedOverlapCells(bWall, bRoot, aFloor, aRoot, allowedWallB);
            penalty += Mathf.Max(0, bWallTotal - allowed);
        }

        return penalty;
    }

    private float IntersectionPenaltyFast(RoomPlacement a, RoomPlacement b)
    {
        using var _ps = PS(S_IntersectionPenalty);
        if (a?.Shape == null || b?.Shape == null)
            return 0f;

        if (settings != null && settings.UseBitsetOverlap)
        {
            // Optional fast path: bitset-based overlap counting (keeps HashSet fallback).
            return IntersectionPenaltyFastBitset(a, b);
        }

        using var _psHash = PSIf(settings != null && settings.LogLayoutProfiling, S_IntersectionPenalty_Hashset);

        var aFloor = a.Shape.FloorCells;
        var bFloor = b.Shape.FloorCells;
        var aWall = a.Shape.WallCells;
        var bWall = b.Shape.WallCells;
        if (aFloor == null || bFloor == null || aWall == null || bWall == null)
            return 0f;

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
        if (p?.Shape == null || p.Shape.FloorCount <= 0)
            return p?.Root ?? Vector2.zero;
        return (Vector2)p.Root + p.Shape.CenterLocal;
    }

    private Vector2 CenterOfRaw(ModuleShape shape, Vector2Int root)
    {
        if (shape == null || shape.FloorCount <= 0)
            return root;
        return (Vector2)root + shape.CenterLocal;
    }

    private bool RoomsTouchEitherWayRaw(GameObject aPrefab, Vector2Int aRoot, GameObject bPrefab, Vector2Int bRoot)
    {
        return RoomsTouchRaw(aPrefab, aRoot, bPrefab, bRoot) || RoomsTouchRaw(bPrefab, bRoot, aPrefab, aRoot);
    }

    private bool RoomsTouchRaw(GameObject aPrefab, Vector2Int aRoot, GameObject bPrefab, Vector2Int bRoot)
    {
        if (aPrefab == null || bPrefab == null)
            return false;
        if (!configSpaceLibrary.TryGetSpace(aPrefab, bPrefab, out var space, out _))
            return false;
        var delta = bRoot - aRoot;
        return space != null && space.Contains(delta);
    }
}
