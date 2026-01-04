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
        private readonly byte mode; // 0 = none, 1 = explicit set (1 or 3), 2 = ray mask
        private readonly byte count;
        private readonly Vector2Int a;
        private readonly Vector2Int b;
        private readonly Vector2Int c;

        // Ray-mode fields (world cells).
        private readonly Vector2Int rayBase;
        private readonly Vector2Int rayInward;
        private readonly Vector2Int rayTangent;
        private readonly int rayMaxK;
        private readonly byte rayMask; // 1 = floor ray, 2 = wall rays

        public AllowedWorldCells(Vector2Int one)
        {
            mode = 1;
            count = 1;
            a = one;
            b = default;
            c = default;
            rayBase = default;
            rayInward = default;
            rayTangent = default;
            rayMaxK = 0;
            rayMask = 0;
        }

        public AllowedWorldCells(Vector2Int one, Vector2Int two, Vector2Int three)
        {
            mode = 1;
            count = 3;
            a = one;
            b = two;
            c = three;
            rayBase = default;
            rayInward = default;
            rayTangent = default;
            rayMaxK = 0;
            rayMask = 0;
        }

        private AllowedWorldCells(Vector2Int rayBase, Vector2Int rayInward, Vector2Int rayTangent, int rayMaxK, byte rayMask)
        {
            mode = 2;
            count = 0;
            a = default;
            b = default;
            c = default;
            this.rayBase = rayBase;
            this.rayInward = rayInward;
            this.rayTangent = rayTangent;
            this.rayMaxK = rayMaxK;
            this.rayMask = rayMask;
        }

        public static AllowedWorldCells Rays(Vector2Int rayBase, Vector2Int rayInward, Vector2Int rayTangent, int maxKInclusive, byte rayMask)
        {
            if (maxKInclusive < 0 || rayMask == 0)
                return None;
            return new AllowedWorldCells(rayBase, rayInward, rayTangent, maxKInclusive, rayMask);
        }

        public bool Contains(Vector2Int cell)
        {
            if (mode == 0)
                return false;

            if (mode == 1)
            {
                if (count == 0)
                    return false;
                if (cell == a)
                    return true;
                if (count == 1)
                    return false;
                return cell == b || cell == c;
            }

            var offset = cell - rayBase;
            var k = offset.x * rayInward.x + offset.y * rayInward.y;
            if (k < 0 || k > rayMaxK)
                return false;
            var perp = offset - rayInward * k;
            if ((rayMask & 1) != 0 && perp == Vector2Int.zero)
                return true;
            if ((rayMask & 2) != 0 && (perp == rayTangent || perp == -rayTangent))
                return true;
            return false;
        }
    }

    private static Vector2Int InwardVector(DoorSide side)
    {
        return side switch
        {
            DoorSide.North => Vector2Int.down,
            DoorSide.South => Vector2Int.up,
            DoorSide.East => Vector2Int.left,
            DoorSide.West => Vector2Int.right,
            _ => Vector2Int.down
        };
    }

    private static Vector2Int TangentVector(DoorSide side)
    {
        return side == DoorSide.North || side == DoorSide.South ? Vector2Int.right : Vector2Int.up;
    }

    private bool TryGetBiteAllowance(
        RoomPlacement a,
        RoomPlacement b,
        out AllowedWorldCells allowedFloorOverlap,
        out AllowedWorldCells allowedWallAOnFloorB,
        out AllowedWorldCells allowedWallBOnFloorA)
    {
        allowedFloorOverlap = AllowedWorldCells.None;
        allowedWallAOnFloorB = AllowedWorldCells.None;
        allowedWallBOnFloorA = AllowedWorldCells.None;

        if (a == null || b == null)
            return false;

        if (!AreNeighbors(a.NodeId, b.NodeId))
            return false;

        var aIsConnector = IsConnector(a.Prefab);
        var bIsConnector = IsConnector(b.Prefab);
        if (aIsConnector == bIsConnector)
            return false;

        var conn = aIsConnector ? a : b;
        var room = aIsConnector ? b : a;

        if (!TryFindBiteDepth(conn, room, out var baseCell, out var inward, out var tangent, out var maxK))
            return false;

        allowedFloorOverlap = AllowedWorldCells.Rays(baseCell, inward, tangent, maxK, rayMask: 1);
        var allowedConnectorWalls = AllowedWorldCells.Rays(baseCell, inward, tangent, maxK, rayMask: 2);
        if (aIsConnector)
            allowedWallAOnFloorB = allowedConnectorWalls;
        else
            allowedWallBOnFloorA = allowedConnectorWalls;

        // Room socket cell is authored as a wall and becomes a door only when the edge is actually used in placement.
        // During layout/energy we allow that single room-wall on connector-floor overlap at the chosen door cell.
        var doorCell = baseCell + inward * maxK;
        if (aIsConnector)
            allowedWallBOnFloorA = new AllowedWorldCells(doorCell);
        else
            allowedWallAOnFloorB = new AllowedWorldCells(doorCell);

        return true;
    }

    private bool TryFindBiteDepth(
        RoomPlacement connector,
        RoomPlacement room,
        out Vector2Int connectorSocketBaseWorld,
        out Vector2Int inward,
        out Vector2Int tangent,
        out int maxKInclusive)
    {
        connectorSocketBaseWorld = default;
        inward = default;
        tangent = default;
        maxKInclusive = 0;

        if (connector?.Shape?.Sockets == null || room?.Shape?.Sockets == null)
            return false;
        if (connector.Prefab == null || room.Prefab == null)
            return false;
        if (!IsConnector(connector.Prefab) || IsConnector(room.Prefab))
            return false;

        foreach (var connSock in connector.Shape.Sockets)
        {
            if (connSock == null)
                continue;

            inward = InwardVector(connSock.Side);
            tangent = TangentVector(connSock.Side);
            var baseWorld = connector.Root + connSock.CellOffset;
            var maxDepth = Mathf.Max(1, connSock.BiteDepth);

            foreach (var roomSock in room.Shape.Sockets)
            {
                if (roomSock == null)
                    continue;
                if (roomSock.Side != connSock.Side.Opposite())
                    continue;

                var roomWorld = room.Root + roomSock.CellOffset;
                var delta = roomWorld - baseWorld;
                var x = delta.x * inward.x + delta.y * inward.y;
                if (x < 0 || x >= maxDepth)
                    continue;
                if (delta != inward * x)
                    continue;

                connectorSocketBaseWorld = baseWorld;
                maxKInclusive = x;
                return true;
            }
        }
        return false;
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

                TryGetBiteAllowance(a, b, out var allowedFloorOverlap, out var allowedWallA, out var allowedWallB);

                var deltaBA = b.Root - a.Root;
                var floorOverlapIllegal = CountOverlapShifted(aFloor, bFloor, deltaBA, allowedFloorOverlap, a.Root, out var overlapCellWorld, earlyStopAtTwo: true);
                if (floorOverlapIllegal > 0)
                {
                    error = $"Layout invalid: floor overlap between {a.NodeId} and {b.NodeId} (count={floorOverlapIllegal}, cell={overlapCellWorld}).";
                    return false;
                }

                var wallOnFloorA = CountOverlapShifted(aWall, bFloor, deltaBA, allowedWallA, a.Root, out var badA, earlyStopAtTwo: true);
                if (wallOnFloorA > 0)
                {
                    error = $"Layout invalid: wall({a.NodeId}) overlaps floor({b.NodeId}) at {badA}.";
                    return false;
                }

                var deltaAB = a.Root - b.Root;
                var wallOnFloorB = CountOverlapShifted(bWall, aFloor, deltaAB, allowedWallB, b.Root, out var badB, earlyStopAtTwo: true);
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
        // Kept for backward-compat / diagnostics; the new bite-depth rules no longer rely on overlapCount==1.
        if (a == null || b == null)
            return false;
        if (!AreNeighbors(a.NodeId, b.NodeId))
            return false;
        if (IsConnector(a.Prefab) == IsConnector(b.Prefab))
            return false;
        return RoomsTouchEitherWay(a, b);
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
        // Legacy helper retained for older call sites; bite-depth uses TryGetBiteAllowance instead.
        if (wallOwner == null || floorOwner == null)
            return AllowedWorldCells.None;
        return new AllowedWorldCells(biteCell);
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
