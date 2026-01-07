// Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Validation.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private readonly List<RoomPlacement> validateRoomsScratch = new();

    private bool TryValidateGlobal(Dictionary<string, RoomPlacement> rooms, out string error)
    {
        error = null;
        if (rooms == null || rooms.Count == 0)
        {
            error = "Global invalid: empty layout (0 rooms placed).";
            return false;
        }

        // Global solutions must place every node referenced by the graph.
        var expectedNodes = graphAsset?.Nodes?.Count ?? 0;
        if (expectedNodes > 0 && rooms.Count < expectedNodes)
        {
            error = $"Global invalid: missing node placements ({rooms.Count}/{expectedNodes}).";
            return false;
        }

        // Global solutions must include placements for every edge endpoint.
        foreach (var edge in graphAsset.Edges)
        {
            if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                continue;
            if (!rooms.ContainsKey(edge.fromNodeId) || !rooms.ContainsKey(edge.toNodeId))
            {
                error = $"Global invalid: missing placement for edge {edge.fromNodeId}->{edge.toNodeId}.";
                return false;
            }
        }

        if (!TryValidateLayout(rooms, out error))
            return false;

        var start = rooms.Keys.First();
        var visited = new HashSet<string>();
        var q = new Queue<string>();
        visited.Add(start);
        q.Enqueue(start);

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var edge in graphAsset.GetEdgesFor(cur))
            {
                var other = edge.fromNodeId == cur ? edge.toNodeId : edge.fromNodeId;
                if (string.IsNullOrEmpty(other) || !rooms.ContainsKey(other))
                    continue;
                if (!RoomsTouchEitherWay(rooms[cur], rooms[other]))
                    continue;
                if (visited.Add(other))
                    q.Enqueue(other);
            }
        }

        if (visited.Count != rooms.Count)
        {
            error = $"Global invalid: disconnected layout (reachable {visited.Count}/{rooms.Count}).";
            return false;
        }

        return true;
    }

    private bool TryValidateLayout(Dictionary<string, RoomPlacement> rooms, out string error)
    {
        using var _ps = PS(S_TryValidateLayout);
        error = null;
        if (rooms == null)
        {
            error = "Layout invalid: rooms is null.";
            return false;
        }

        validateRoomsScratch.Clear();
        foreach (var p in rooms.Values)
            validateRoomsScratch.Add(p);
        var list = validateRoomsScratch;
        for (int i = 0; i < list.Count; i++)
        {
            for (int j = i + 1; j < list.Count; j++)
            {
                var a = list[i];
                var b = list[j];
                if (a == null || b == null)
                    continue;

                var aFloor = a.Shape?.FloorCells;
                var bFloor = b.Shape?.FloorCells;
                var aWall = a.Shape?.WallCells;
                var bWall = b.Shape?.WallCells;
                if (aFloor == null || bFloor == null || aWall == null || bWall == null)
                    continue;

                TryGetBiteAllowance(a, b, out var allowedFloorOverlap, out var allowedWallA, out var allowedWallB);

                var deltaBA = b.Root - a.Root;
                var floorOverlapIllegal = settings != null && settings.UseBitsetOverlap
                    ? CountIllegalOverlapBitset(a, b, OverlapType.FloorFloor, allowedFloorOverlap, allowedWallA, allowedWallB, out var overlapCellWorld)
                    : CountOverlapShifted(aFloor, bFloor, deltaBA, allowedFloorOverlap, a.Root, out overlapCellWorld, earlyStopAtTwo: true);
                if (floorOverlapIllegal > 0)
                {
                    error = $"Layout invalid: floor overlap between {a.NodeId} and {b.NodeId} (count={floorOverlapIllegal}, cell={overlapCellWorld}).";
                    return false;
                }

                var wallOnFloorA = settings != null && settings.UseBitsetOverlap
                    ? CountIllegalOverlapBitset(a, b, OverlapType.AWallOnBFloor, allowedFloorOverlap, allowedWallA, allowedWallB, out var badA)
                    : CountOverlapShifted(aWall, bFloor, deltaBA, allowedWallA, a.Root, out badA, earlyStopAtTwo: true);
                if (wallOnFloorA > 0)
                {
                    error = $"Layout invalid: wall({a.NodeId}) overlaps floor({b.NodeId}) at {badA}.";
                    return false;
                }

                var deltaAB = a.Root - b.Root;
                var wallOnFloorB = settings != null && settings.UseBitsetOverlap
                    ? CountIllegalOverlapBitset(a, b, OverlapType.BWallOnAFloor, allowedFloorOverlap, allowedWallA, allowedWallB, out var badB)
                    : CountOverlapShifted(bWall, aFloor, deltaAB, allowedWallB, b.Root, out badB, earlyStopAtTwo: true);
                if (wallOnFloorB > 0)
                {
                    error = $"Layout invalid: wall({b.NodeId}) overlaps floor({a.NodeId}) at {badB}.";
                    return false;
                }
            }
        }

        foreach (var edge in graphAsset.Edges)
        {
            if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                continue;
            // Partial layouts are allowed during chain search: skip edges whose endpoints are not yet placed.
            if (!rooms.TryGetValue(edge.fromNodeId, out var a) || !rooms.TryGetValue(edge.toNodeId, out var b))
                continue;
            if (IsConnector(a.Prefab) == IsConnector(b.Prefab))
            {
                error = $"Layout invalid: edge {edge.fromNodeId}->{edge.toNodeId} connects same-type modules (expected Room↔Corridor).";
                return false;
            }
            if (!RoomsTouchEitherWay(a, b))
            {
                error = $"Layout invalid: edge {edge.fromNodeId}->{edge.toNodeId} not satisfied (no touching sockets / CS mismatch).";
                return false;
            }

            // Additional bite-depth consistency check: the room socket must lie on the connector inward ray within BiteDepth.
            var conn = IsConnector(a.Prefab) ? a : b;
            var room = ReferenceEquals(conn, a) ? b : a;
            if (!TryFindBiteDepth(conn, room, out _, out _, out _, out _))
            {
                error = $"Layout invalid: edge {edge.fromNodeId}->{edge.toNodeId} has no matching bite-depth socket pair.";
                return false;
            }
        }

        return true;
    }

    private enum OverlapType : byte
    {
        FloorFloor = 0,
        AWallOnBFloor = 1,
        BWallOnAFloor = 2
    }

    private int CountIllegalOverlapBitset(
        RoomPlacement a,
        RoomPlacement b,
        OverlapType overlapType,
        AllowedWorldCells allowedFloorOverlap,
        AllowedWorldCells allowedWallA,
        AllowedWorldCells allowedWallB,
        out Vector2Int lastOverlapWorld)
    {
        lastOverlapWorld = default;
        if (a?.Shape == null || b?.Shape == null)
            return 0;

        var bitsA = GetBitsets(a.Shape);
        var bitsB = GetBitsets(b.Shape);
        if (bitsA == null || bitsB == null)
            return 0;

        BitGrid fixedGrid;
        BitGrid movingGrid;
        AllowedWorldCells allowed;
        Vector2Int fixedRoot;
        Vector2Int delta;

        switch (overlapType)
        {
            case OverlapType.FloorFloor:
                fixedGrid = bitsA.Floor;
                movingGrid = bitsB.Floor;
                allowed = allowedFloorOverlap;
                fixedRoot = a.Root;
                delta = b.Root - a.Root;
                break;
            case OverlapType.AWallOnBFloor:
                fixedGrid = bitsA.Wall;
                movingGrid = bitsB.Floor;
                allowed = allowedWallA;
                fixedRoot = a.Root;
                delta = b.Root - a.Root;
                break;
            case OverlapType.BWallOnAFloor:
                fixedGrid = bitsB.Wall;
                movingGrid = bitsA.Floor;
                allowed = allowedWallB;
                fixedRoot = b.Root;
                delta = a.Root - b.Root;
                break;
            default:
                return 0;
        }

        if (fixedGrid == null || movingGrid == null)
            return 0;

        var shift = (movingGrid.Min + delta) - fixedGrid.Min;
        return fixedGrid.CountIllegalOverlapsShifted(movingGrid, shift, fixedRoot, allowed, earlyStopAtTwo: true, out lastOverlapWorld);
    }

    private int CountIllegalOverlapFallback(
        RoomPlacement a,
        RoomPlacement b,
        OverlapType overlapType,
        AllowedWorldCells allowedFloorOverlap,
        AllowedWorldCells allowedWallA,
        AllowedWorldCells allowedWallB,
        out Vector2Int lastOverlapWorld)
    {
        // Only used when bitset mode is disabled.
        lastOverlapWorld = default;
        if (a?.Shape == null || b?.Shape == null)
            return 0;

        switch (overlapType)
        {
            case OverlapType.FloorFloor:
                return CountOverlapShifted(
                    a.Shape.FloorCells,
                    b.Shape.FloorCells,
                    b.Root - a.Root,
                    allowedFloorOverlap,
                    a.Root,
                    out lastOverlapWorld,
                    earlyStopAtTwo: true);
            case OverlapType.AWallOnBFloor:
                return CountOverlapShifted(
                    a.Shape.WallCells,
                    b.Shape.FloorCells,
                    b.Root - a.Root,
                    allowedWallA,
                    a.Root,
                    out lastOverlapWorld,
                    earlyStopAtTwo: true);
            case OverlapType.BWallOnAFloor:
                return CountOverlapShifted(
                    b.Shape.WallCells,
                    a.Shape.FloorCells,
                    a.Root - b.Root,
                    allowedWallB,
                    b.Root,
                    out lastOverlapWorld,
                    earlyStopAtTwo: true);
            default:
                return 0;
        }
    }

    private bool IsValidLayout(Dictionary<string, RoomPlacement> rooms)
    {
        var start = profiling != null ? NowTicks() : 0;
        var ok = TryValidateLayout(rooms, out _);
        if (profiling != null)
        {
            profiling.IsValidLayoutCalls++;
            profiling.IsValidLayoutTicks += NowTicks() - start;
        }
        return ok;
    }

    private bool IsGloballyValid(Dictionary<string, RoomPlacement> rooms)
    {
        return TryValidateGlobal(rooms, out _);
    }

    private bool RoomsTouchEitherWay(RoomPlacement a, RoomPlacement b)
    {
        return RoomsTouch(a, b) || RoomsTouch(b, a);
    }

    private bool RoomsTouch(RoomPlacement a, RoomPlacement b)
    {
        if (a == null || b == null || a.Prefab == null || b.Prefab == null)
            return false;
        if (!configSpaceLibrary.TryGetSpace(a.Prefab, b.Prefab, out var space, out _))
            return false;
        var delta = b.Root - a.Root;
        return space != null && space.Contains(delta);
    }
}
