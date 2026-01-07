using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new();

        public bool Equals(T x, T y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private readonly struct BiteOffsetInfo
    {
        public Vector2Int ConnectorSocketOffset { get; }
        public DoorSide Side { get; }
        public int K { get; }

        public BiteOffsetInfo(Vector2Int connectorSocketOffset, DoorSide side, int k)
        {
            ConnectorSocketOffset = connectorSocketOffset;
            Side = side;
            K = k;
        }
    }

    private sealed class BiteOffsetMap
    {
        public Dictionary<Vector2Int, BiteOffsetInfo> ByDeltaRoot { get; }

        public BiteOffsetMap(Dictionary<Vector2Int, BiteOffsetInfo> byDeltaRoot)
        {
            ByDeltaRoot = byDeltaRoot;
        }
    }

    // Cache for fast bite-depth lookup by deltaRoot = room.Root - connector.Root.
    // Keyed by (connectorShape, roomShape) using reference identity.
    private static readonly Dictionary<ModuleShape, Dictionary<ModuleShape, BiteOffsetMap>> BiteOffsetCacheByConnectorShape =
        new(ReferenceEqualityComparer<ModuleShape>.Instance);

    private static BiteOffsetMap GetBiteOffsetMap(ModuleShape connectorShape, ModuleShape roomShape)
    {
        if (connectorShape == null || roomShape == null)
            return null;

        if (!BiteOffsetCacheByConnectorShape.TryGetValue(connectorShape, out var byRoom))
        {
            byRoom = new Dictionary<ModuleShape, BiteOffsetMap>(ReferenceEqualityComparer<ModuleShape>.Instance);
            BiteOffsetCacheByConnectorShape[connectorShape] = byRoom;
        }

        if (byRoom.TryGetValue(roomShape, out var cached))
            return cached;

        var built = BuildBiteOffsetMap(connectorShape, roomShape);
        byRoom[roomShape] = built;
        return built;
    }

    private static void WarmupBiteOffsetMaps(ShapeLibrary shapeLibrary, IReadOnlyCollection<GameObject> connectorPrefabs, IEnumerable<GameObject> allRoomPrefabs)
    {
        if (shapeLibrary == null || connectorPrefabs == null || connectorPrefabs.Count == 0 || allRoomPrefabs == null)
            return;

        var connectorSet = connectorPrefabs as HashSet<GameObject> ?? new HashSet<GameObject>(connectorPrefabs);

        foreach (var connPrefab in connectorSet)
        {
            if (connPrefab == null)
                continue;
            if (!shapeLibrary.TryGetShape(connPrefab, out var connShape, out _))
                continue;
            if (connShape == null)
                continue;

            foreach (var roomPrefab in allRoomPrefabs)
            {
                if (roomPrefab == null || connectorSet.Contains(roomPrefab))
                    continue;
                if (!shapeLibrary.TryGetShape(roomPrefab, out var roomShape, out _))
                    continue;
                if (roomShape == null)
                    continue;
                GetBiteOffsetMap(connShape, roomShape);
            }
        }
    }

    private static BiteOffsetMap BuildBiteOffsetMap(ModuleShape connectorShape, ModuleShape roomShape)
    {
        if (connectorShape?.Sockets == null || roomShape?.Sockets == null)
            return new BiteOffsetMap(new Dictionary<Vector2Int, BiteOffsetInfo>());

        var connBits = GetBitsets(connectorShape);
        var roomBits = GetBitsets(roomShape);
        var connFloor = connBits?.Floor;
        var connWall = connBits?.Wall;
        var roomFloor = roomBits?.Floor;
        var roomWall = roomBits?.Wall;

        // Typical connector has 2 sockets; rooms can have many.
        // We build all valid deltaRoot such that:
        // room.Root - connector.Root == connSockOffset - roomSockOffset + inward*k, 0 <= k < BiteDepth
        // Additionally, we only keep deltas where the room and connector do not overlap outside the bite allowance
        // (strict-bite), so we don't generate fake "connectable" positions that will never be accepted by annealing.
        // For collisions, keep the first match to preserve legacy iteration order.
        var map = new Dictionary<Vector2Int, BiteOffsetInfo>();

        foreach (var connSock in connectorShape.Sockets)
        {
            if (connSock == null)
                continue;

            var side = connSock.Side;
            var inward = InwardVector(side);
            var connOffset = connSock.CellOffset;
            var maxDepth = Mathf.Max(1, connSock.BiteDepth);

            foreach (var roomSock in roomShape.Sockets)
            {
                if (roomSock == null)
                    continue;
                if (roomSock.Side != side.Opposite())
                    continue;

                var roomOffset = roomSock.CellOffset;
                var baseDelta = connOffset - roomOffset;
                for (var k = 0; k < maxDepth; k++)
                {
                    var deltaRoot = baseDelta + inward * k;
                    if (!IsStrictBiteDeltaAllowed(connFloor, connWall, roomFloor, roomWall, connOffset, side, deltaRoot, k))
                        continue;
                    if (!map.ContainsKey(deltaRoot))
                        map.Add(deltaRoot, new BiteOffsetInfo(connOffset, side, k));
                }
            }
        }

        return new BiteOffsetMap(map);
    }

    private static bool IsStrictBiteDeltaAllowed(
        BitGrid connFloor,
        BitGrid connWall,
        BitGrid roomFloor,
        BitGrid roomWall,
        Vector2Int connectorSocketOffset,
        DoorSide side,
        Vector2Int deltaRoot,
        int k)
    {
        // Coordinate system:
        // - connectorRoot is treated as (0,0)
        // - roomRoot is deltaRoot
        // Allowed overlaps are expressed in connector-local/world coordinates.
        var inward = InwardVector(side);
        var tangent = TangentVector(side);
        var baseCell = connectorSocketOffset;
        var doorCell = baseCell + inward * k;

        var allowedFloor = AllowedWorldCells.Rays(baseCell, inward, tangent, k, rayMask: 1);
        var allowedConnWalls = AllowedWorldCells.Rays(baseCell, inward, tangent, k, rayMask: 2);
        var allowedDoorOnly = new AllowedWorldCells(doorCell);

        static Vector2Int Shift(BitGrid fixedGrid, BitGrid movingGrid, Vector2Int movingRoot)
        {
            return (movingGrid.Min + movingRoot) - fixedGrid.Min;
        }

        var fixedRoot = Vector2Int.zero;

        if (connFloor != null && roomFloor != null)
        {
            var shift = Shift(connFloor, roomFloor, deltaRoot);
            if (connFloor.CountIllegalOverlapsShifted(roomFloor, shift, fixedRoot, allowedFloor, earlyStopAtTwo: true, out _) > 0)
                return false;
        }

        if (connWall != null && roomFloor != null)
        {
            var shift = Shift(connWall, roomFloor, deltaRoot);
            if (connWall.CountIllegalOverlapsShifted(roomFloor, shift, fixedRoot, allowedConnWalls, earlyStopAtTwo: true, out _) > 0)
                return false;
        }

        if (connFloor != null && roomWall != null)
        {
            var shift = Shift(connFloor, roomWall, deltaRoot);
            if (connFloor.CountIllegalOverlapsShifted(roomWall, shift, fixedRoot, allowedDoorOnly, earlyStopAtTwo: true, out _) > 0)
                return false;
        }

        return true;
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
        using var _ps = PS(S_GetBiteAllowance);
        // Returns “legal overlap” sets for a placed Room↔Connector pair that are neighbors in the graph.
        //
        // We support “bite depth” by allowing the connector to be effectively shortened when the room socket
        // is placed deeper inside the connector up to BiteDepth.
        //
        // Overlaps we allow (in world cells):
        // - Floor↔floor: along the connector inward ray for k in [0..X]
        // - Wall(connector)↔floor(other): along the two tangent rays at distance ±1 from the inward ray for k in [0..X]
        // - Wall(room)↔floor(connector): only at the final door cell (room sockets are authored in walls; floor often exists too)
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
        // During layout/energy we allow exactly that single room-wall on connector-floor overlap at the chosen door cell.
        var doorCell = baseCell + inward * maxK;
        if (aIsConnector)
            allowedWallBOnFloorA = new AllowedWorldCells(doorCell);
        else
            allowedWallAOnFloorB = new AllowedWorldCells(doorCell);

        return true;
    }

    private bool TryGetBiteAllowanceRaw(
        string aNodeId,
        GameObject aPrefab,
        ModuleShape aShape,
        Vector2Int aRoot,
        string bNodeId,
        GameObject bPrefab,
        ModuleShape bShape,
        Vector2Int bRoot,
        out AllowedWorldCells allowedFloorOverlap,
        out AllowedWorldCells allowedWallAOnFloorB,
        out AllowedWorldCells allowedWallBOnFloorA)
    {
        allowedFloorOverlap = AllowedWorldCells.None;
        allowedWallAOnFloorB = AllowedWorldCells.None;
        allowedWallBOnFloorA = AllowedWorldCells.None;

        if (string.IsNullOrEmpty(aNodeId) || string.IsNullOrEmpty(bNodeId))
            return false;
        if (aPrefab == null || bPrefab == null || aShape == null || bShape == null)
            return false;

        if (!AreNeighbors(aNodeId, bNodeId))
            return false;

        var aIsConnector = IsConnector(aPrefab);
        var bIsConnector = IsConnector(bPrefab);
        if (aIsConnector == bIsConnector)
            return false;

        var connShape = aIsConnector ? aShape : bShape;
        var connRoot = aIsConnector ? aRoot : bRoot;
        var connPrefab = aIsConnector ? aPrefab : bPrefab;

        var roomShape = aIsConnector ? bShape : aShape;
        var roomRoot = aIsConnector ? bRoot : aRoot;
        var roomPrefab = aIsConnector ? bPrefab : aPrefab;

        if (!TryFindBiteDepthRaw(connPrefab, connShape, connRoot, roomPrefab, roomShape, roomRoot, out var baseCell, out var inward, out var tangent, out var maxK))
            return false;

        allowedFloorOverlap = AllowedWorldCells.Rays(baseCell, inward, tangent, maxK, rayMask: 1);
        var allowedConnectorWalls = AllowedWorldCells.Rays(baseCell, inward, tangent, maxK, rayMask: 2);
        if (aIsConnector)
            allowedWallAOnFloorB = allowedConnectorWalls;
        else
            allowedWallBOnFloorA = allowedConnectorWalls;

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
        // Finds a compatible socket pair and returns:
        // - connectorSocketBaseWorld: connector socket cell at depth 0 (the authored socket position)
        // - inward/tangent: connector local basis (world directions)
        // - maxKInclusive (=X): chosen depth where room socket lands on connector inward ray.
        //
        // The room socket must lie exactly on the connector inward ray, at distance X where 0 <= X < BiteDepth.
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

        // Fast path: cached lookup by deltaRoot in O(1).
        var map = GetBiteOffsetMap(connector.Shape, room.Shape);
        if (map != null && map.ByDeltaRoot != null)
        {
            var deltaRoot = room.Root - connector.Root;
            if (map.ByDeltaRoot.TryGetValue(deltaRoot, out var info))
            {
                connectorSocketBaseWorld = connector.Root + info.ConnectorSocketOffset;
                inward = InwardVector(info.Side);
                tangent = TangentVector(info.Side);
                maxKInclusive = info.K;
                return true;
            }

            // Cache is complete for (connectorShape, roomShape): if deltaRoot isn't present, no valid bite exists.
            return false;
        }

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

    private bool TryFindBiteDepthRaw(
        GameObject connectorPrefab,
        ModuleShape connectorShape,
        Vector2Int connectorRoot,
        GameObject roomPrefab,
        ModuleShape roomShape,
        Vector2Int roomRoot,
        out Vector2Int connectorSocketBaseWorld,
        out Vector2Int inward,
        out Vector2Int tangent,
        out int maxKInclusive)
    {
        connectorSocketBaseWorld = default;
        inward = default;
        tangent = default;
        maxKInclusive = 0;

        if (connectorPrefab == null || roomPrefab == null || connectorShape == null || roomShape == null)
            return false;
        if (!IsConnector(connectorPrefab) || IsConnector(roomPrefab))
            return false;

        var map = GetBiteOffsetMap(connectorShape, roomShape);
        if (map != null && map.ByDeltaRoot != null)
        {
            var deltaRoot = roomRoot - connectorRoot;
            if (!map.ByDeltaRoot.TryGetValue(deltaRoot, out var info))
                return false;

            connectorSocketBaseWorld = connectorRoot + info.ConnectorSocketOffset;
            inward = InwardVector(info.Side);
            tangent = TangentVector(info.Side);
            maxKInclusive = info.K;
            return true;
        }

        // Fallback (should be rare because warmup builds maps for all pairs).
        foreach (var connSock in connectorShape.Sockets)
        {
            if (connSock == null)
                continue;

            inward = InwardVector(connSock.Side);
            tangent = TangentVector(connSock.Side);
            var baseWorld = connectorRoot + connSock.CellOffset;
            var maxDepth = Mathf.Max(1, connSock.BiteDepth);

            foreach (var roomSock in roomShape.Sockets)
            {
                if (roomSock == null)
                    continue;
                if (roomSock.Side != connSock.Side.Opposite())
                    continue;

                var roomWorld = roomRoot + roomSock.CellOffset;
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
}
