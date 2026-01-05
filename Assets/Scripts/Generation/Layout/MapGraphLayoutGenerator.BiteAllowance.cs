using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private readonly struct AllowedWorldCells
    {
        public static AllowedWorldCells None => default;
        // Compact “allowed overlap mask” used in tight loops (layout validation + energy).
        // Modes:
        // - 0: empty
        // - 1: explicit small set (1 or 3 cells)
        // - 2: 3-ray mask in world space:
        //   - center ray (floor) at perp==0
        //   - two tangent rays (walls) at perp==±tangent
        //   k is the distance along the inward vector, constrained to [0..rayMaxK].
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

            // Project onto inward axis to get “depth” along the connector bite ray.
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
}
