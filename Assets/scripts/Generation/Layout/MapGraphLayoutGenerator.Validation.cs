// Assets/scripts/Generation/Graph/MapGraphLayoutGenerator.Validation.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private readonly struct AllowedWorldCells
    {
        public static AllowedWorldCells None => default;
        private readonly byte count;
        private readonly Vector2Int a;
        private readonly Vector2Int b;
        private readonly Vector2Int c;

        public AllowedWorldCells(Vector2Int one)
        {
            count = 1;
            a = one;
            b = default;
            c = default;
        }

        public AllowedWorldCells(Vector2Int one, Vector2Int two, Vector2Int three)
        {
            count = 3;
            a = one;
            b = two;
            c = three;
        }

        public bool Contains(Vector2Int cell)
        {
            if (count == 0)
                return false;
            if (cell == a)
                return true;
            if (count == 1)
                return false;
            return cell == b || cell == c;
        }
    }

    private bool TryValidateGlobal(Dictionary<string, RoomPlacement> rooms, out string error)
    {
        error = null;
        if (!TryValidateLayout(rooms, out error))
            return false;

        if (rooms == null || rooms.Count == 0)
        {
            error = "Global invalid: empty layout (0 rooms placed).";
            return false;
        }

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
        error = null;
        if (rooms == null)
        {
            error = "Layout invalid: rooms is null.";
            return false;
        }

        var list = rooms.Values.ToList();
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

                var deltaBA = b.Root - a.Root;
                var floorOverlapCount = CountOverlapShifted(aFloor, bFloor, deltaBA, AllowedWorldCells.None, a.Root, out var overlapCellWorld, earlyStopAtTwo: true);
                var allowedBite = floorOverlapCount == 1 && IsAllowedBiteOverlap(a, b, 1);
                if (floorOverlapCount > 0 && !allowedBite)
                {
                    error = $"Layout invalid: floor overlap between {a.NodeId} and {b.NodeId} (count={floorOverlapCount}, cell={overlapCellWorld}).";
                    return false;
                }

                var allowedA = allowedBite ? AllowedWallOnFloorCells(a, b, overlapCellWorld) : AllowedWorldCells.None;
                var allowedB = allowedBite ? AllowedWallOnFloorCells(b, a, overlapCellWorld) : AllowedWorldCells.None;

                var wallOnFloorA = CountOverlapShifted(aWall, bFloor, deltaBA, allowedA, a.Root, out var badA, earlyStopAtTwo: true);
                if (wallOnFloorA > 0)
                {
                    error = $"Layout invalid: wall({a.NodeId}) overlaps floor({b.NodeId}) at {badA}.";
                    return false;
                }

                var deltaAB = a.Root - b.Root;
                var wallOnFloorB = CountOverlapShifted(bWall, aFloor, deltaAB, allowedB, b.Root, out var badB, earlyStopAtTwo: true);
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
            if (!rooms.TryGetValue(edge.fromNodeId, out var a) || !rooms.TryGetValue(edge.toNodeId, out var b))
            {
                error = $"Layout invalid: missing placement for edge {edge.fromNodeId}->{edge.toNodeId}.";
                return false;
            }
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

            var aFloor = a.Shape?.FloorCells;
            var bFloor = b.Shape?.FloorCells;
            if (aFloor == null || bFloor == null)
                continue;

            var deltaBA = b.Root - a.Root;
            var overlapCount = CountOverlapShifted(aFloor, bFloor, deltaBA, AllowedWorldCells.None, a.Root, out var overlapCellWorld, earlyStopAtTwo: false);
            if (overlapCount != 1)
            {
                error = $"Layout invalid: edge {edge.fromNodeId}->{edge.toNodeId} expected 1-tile bite, got overlapCount={overlapCount} (cell={overlapCellWorld}).";
                return false;
            }
        }

        return true;
    }

    private int CountOverlapShifted(
        HashSet<Vector2Int> fixedLocal,
        HashSet<Vector2Int> movingLocal,
        Vector2Int deltaMovingMinusFixed,
        AllowedWorldCells allowedWorld,
        Vector2Int fixedRoot,
        out Vector2Int lastOverlapWorld,
        bool earlyStopAtTwo)
    {
        lastOverlapWorld = default;
        if (fixedLocal == null || movingLocal == null || fixedLocal.Count == 0 || movingLocal.Count == 0)
            return 0;

        var count = 0;
        foreach (var c in movingLocal)
        {
            var fixedCell = c + deltaMovingMinusFixed;
            if (!fixedLocal.Contains(fixedCell))
                continue;

            var world = fixedRoot + fixedCell;
            if (allowedWorld.Contains(world))
                continue;

            count++;
            lastOverlapWorld = world;
            if (earlyStopAtTwo && count > 1)
                return count;
        }
        return count;
    }

    private bool IsAllowedBiteOverlap(RoomPlacement a, RoomPlacement b, int overlapCount)
    {
        if (a == null || b == null)
            return false;
        if (overlapCount != 1)
            return false;
        if (!AreNeighbors(a.NodeId, b.NodeId))
            return false;
        if (IsConnector(a.Prefab) == IsConnector(b.Prefab))
            return false;
        if (!RoomsTouchEitherWay(a, b))
            return false;
        return true;
    }

    private bool IsConnector(GameObject prefab)
    {
        return prefab != null && prefab.GetComponent<ConnectorMeta>() != null;
    }

    private bool AreNeighbors(string aId, string bId)
    {
        if (graphAsset == null || string.IsNullOrEmpty(aId) || string.IsNullOrEmpty(bId))
            return false;

        return graphAsset.GetEdgesFor(aId).Any(e =>
            e != null &&
            ((e.fromNodeId == aId && e.toNodeId == bId) || (e.fromNodeId == bId && e.toNodeId == aId)));
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

    private AllowedWorldCells AllowedWallOnFloorCells(RoomPlacement wallOwner, RoomPlacement floorOwner, Vector2Int biteCell)
    {
        if (wallOwner == null || floorOwner == null)
            return AllowedWorldCells.None;

        if (!IsConnector(wallOwner.Prefab) || IsConnector(floorOwner.Prefab))
            return new AllowedWorldCells(biteCell);

        if (!TryGetSocketSideAtWorldCell(wallOwner, biteCell, out var side) &&
            !TryGetSocketSideAtWorldCell(floorOwner, biteCell, out side))
        {
            var delta = floorOwner.Root - wallOwner.Root;
            side = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? DoorSide.East : DoorSide.North;
        }

        var tangent = side == DoorSide.North || side == DoorSide.South ? Vector2Int.right : Vector2Int.up;
        return new AllowedWorldCells(biteCell, biteCell + tangent, biteCell - tangent);
    }

    private bool TryGetSocketSideAtWorldCell(RoomPlacement placement, Vector2Int worldCell, out DoorSide side)
    {
        side = default;
        if (placement?.Shape?.Sockets == null)
            return false;

        foreach (var sock in placement.Shape.Sockets)
        {
            if (sock == null)
                continue;
            if (placement.Root + sock.CellOffset != worldCell)
                continue;
            side = sock.Side;
            return true;
        }
        return false;
    }

    private bool HasOverlapExcept(HashSet<Vector2Int> a, HashSet<Vector2Int> b, HashSet<Vector2Int> allowedCells)
    {
        if (a == null || b == null || a.Count == 0 || b.Count == 0)
            return false;
        var iter = a.Count <= b.Count ? a : b;
        var other = ReferenceEquals(iter, a) ? b : a;
        foreach (var c in iter)
        {
            if (!other.Contains(c))
                continue;
            if (allowedCells != null && allowedCells.Contains(c))
                continue;
            return true;
        }
        return false;
    }

    private int CountOverlapAll(HashSet<Vector2Int> a, HashSet<Vector2Int> b, HashSet<Vector2Int> allowedCells, out Vector2Int lastOverlapCell)
    {
        lastOverlapCell = default;
        if (a == null || b == null || a.Count == 0 || b.Count == 0)
            return 0;

        var count = 0;
        var iter = a.Count <= b.Count ? a : b;
        var other = ReferenceEquals(iter, a) ? b : a;
        foreach (var c in iter)
        {
            if (!other.Contains(c))
                continue;
            if (allowedCells != null && allowedCells.Contains(c))
                continue;
            count++;
            lastOverlapCell = c;
        }
        return count;
    }
}
