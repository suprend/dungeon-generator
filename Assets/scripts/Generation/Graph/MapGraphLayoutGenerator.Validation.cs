// Assets/scripts/Generation/Graph/MapGraphLayoutGenerator.Validation.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
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

                var overlapCount = CountOverlapCells(a.WorldCells, b.WorldCells, out var overlapCell);
                var allowedBite = overlapCount == 1 && IsAllowedBiteOverlap(a, b, overlapCount);
                if (overlapCount > 0 && !allowedBite)
                {
                    error = $"Layout invalid: floor overlap between {a.NodeId} and {b.NodeId} (count={overlapCount}, cell={overlapCell}).";
                    return false;
                }

                var allowedA = allowedBite ? AllowedWallOnFloorCells(a, b, overlapCell) : null;
                var allowedB = allowedBite ? AllowedWallOnFloorCells(b, a, overlapCell) : null;
                if (TryFindIllegalOverlap(a.WorldWallCells, b.WorldCells, allowedA, out var badA))
                {
                    error = $"Layout invalid: wall({a.NodeId}) overlaps floor({b.NodeId}) at {badA}.";
                    return false;
                }
                if (TryFindIllegalOverlap(b.WorldWallCells, a.WorldCells, allowedB, out var badB))
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

            var overlapCount = CountOverlapCells(a.WorldCells, b.WorldCells, out var overlapCell);
            if (overlapCount != 1)
            {
                error = $"Layout invalid: edge {edge.fromNodeId}->{edge.toNodeId} expected 1-tile bite, got overlapCount={overlapCount} (cell={overlapCell}).";
                return false;
            }
        }

        return true;
    }

    private bool TryFindIllegalOverlap(HashSet<Vector2Int> walls, HashSet<Vector2Int> floors, HashSet<Vector2Int> allowedCells, out Vector2Int badCell)
    {
        badCell = default;
        if (walls == null || floors == null || walls.Count == 0 || floors.Count == 0)
            return false;

        var iter = walls.Count <= floors.Count ? walls : floors;
        var other = ReferenceEquals(iter, walls) ? floors : walls;
        foreach (var c in iter)
        {
            if (!other.Contains(c))
                continue;
            if (allowedCells != null && allowedCells.Contains(c))
                continue;
            badCell = c;
            return true;
        }
        return false;
    }

    private int CountOverlapCells(HashSet<Vector2Int> a, HashSet<Vector2Int> b, out Vector2Int overlapCell)
    {
        overlapCell = default;
        if (a == null || b == null || a.Count == 0 || b.Count == 0)
            return 0;

        var count = 0;
        var iter = a.Count <= b.Count ? a : b;
        var other = ReferenceEquals(iter, a) ? b : a;
        foreach (var c in iter)
        {
            if (!other.Contains(c))
                continue;
            count++;
            overlapCell = c;
            if (count > 1)
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
        return TryValidateLayout(rooms, out _);
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

    private HashSet<Vector2Int> AllowedWallOnFloorCells(RoomPlacement wallOwner, RoomPlacement floorOwner, Vector2Int biteCell)
    {
        if (wallOwner == null || floorOwner == null)
            return null;

        var allowed = new HashSet<Vector2Int> { biteCell };

        if (!IsConnector(wallOwner.Prefab) || IsConnector(floorOwner.Prefab))
            return allowed;

        if (!TryGetSocketSideAtWorldCell(wallOwner, biteCell, out var side) &&
            !TryGetSocketSideAtWorldCell(floorOwner, biteCell, out side))
        {
            var delta = floorOwner.Root - wallOwner.Root;
            side = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? DoorSide.East : DoorSide.North;
        }

        var tangent = side == DoorSide.North || side == DoorSide.South ? Vector2Int.right : Vector2Int.up;
        allowed.Add(biteCell + tangent);
        allowed.Add(biteCell - tangent);
        return allowed;
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

