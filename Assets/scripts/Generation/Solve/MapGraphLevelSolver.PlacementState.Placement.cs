// Assets/scripts/Generation/Graph/MapGraphLevelSolver.PlacementState.Placement.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;

public partial class MapGraphLevelSolver
{
    private sealed partial class PlacementState
    {
        private bool TryPlaceRoom(string nodeId, Vector3Int targetCell)
        {
            if (!CheckTimeLimit())
                return false;

            if (!nodeAssignments.TryGetValue(nodeId, out var roomType) || roomType == null)
            {
                LastError = $"Node {nodeId} has no room type.";
                return false;
            }

            var prefabCandidates = GetRoomPrefabs(roomType, null, null, out var prefabError);
            if (prefabCandidates.Count == 0)
            {
                LastError = prefabError ?? $"Room type {roomType.name} has no prefabs.";
            }
            prefabCandidates.Shuffle(rng);

            foreach (var prefab in prefabCandidates)
            {
                if (!TryGetBlueprint(prefab, out var blueprint, out var bpError))
                {
                    LastError ??= bpError;
                    continue;
                }

                BuildPlacementFromBlueprint(blueprint, targetCell, out var floorCells, out var wallCells);
                if (HasOverlap(floorCells, occupiedFloor) || HasOverlap(wallCells, occupiedWall))
                    continue;

                var inst = UnityEngine.Object.Instantiate(prefab, Vector3.zero, Quaternion.identity, stampWorldParent());
                var meta = inst.GetComponent<ModuleMetaBase>();
                if (meta == null)
                {
                    UnityEngine.Object.Destroy(inst);
                    continue;
                }

                meta.ResetUsed();
                AlignToCell(inst.transform, targetCell);

                if (!TryComputePlacement(meta, prefab, out var placement))
                {
                    UnityEngine.Object.Destroy(inst);
                    continue;
                }

                CommitPlacement(nodeId, placement);
                return true;
            }

            LastError ??= $"No prefab could be placed for node {nodeId}.";
            return false;
        }

        private bool PreplaceLayoutRooms(MapGraphLayoutGenerator.LayoutResult layout, Vector3Int offset, MapGraphAsset graph)
        {
            if (!TryBuildConnectorCarvePlan(layout, offset, graph, out var connectorCarvePlan))
                return false;
            if (!TryBuildRoomDoorCarvePlan(layout, offset, graph, out var roomDoorCarvePlan))
                return false;

            foreach (var kv in layout.Rooms)
            {
                var room = kv.Value;
                if (room == null || room.Prefab == null)
                {
                    LastError = $"Layout room for node {kv.Key} is missing prefab.";
                    return false;
                }

                var root = new Vector3Int(room.Root.x + offset.x, room.Root.y + offset.y, 0);

                if (!TryGetBlueprint(room.Prefab, out var blueprint, out var bpError))
                {
                    LastError = bpError;
                    return false;
                }

                BuildPlacementFromBlueprint(blueprint, root, out var floorCells, out var wallCells);
                var inst = UnityEngine.Object.Instantiate(room.Prefab, Vector3.zero, Quaternion.identity, stampWorldParent());
                var meta = inst.GetComponent<ModuleMetaBase>();
                if (meta == null)
                {
                    UnityEngine.Object.Destroy(inst);
                    LastError = $"Prefab {room.Prefab.name} has no ModuleMetaBase.";
                    return false;
                }

                meta.ResetUsed();
                AlignToCell(inst.transform, root);

                if (!TryComputePlacement(meta, room.Prefab, out var placement))
                {
                    UnityEngine.Object.Destroy(inst);
                    LastError = $"Failed to compute placement for {room.Prefab.name}.";
                    return false;
                }

                if (placement.Meta is ConnectorMeta &&
                    connectorCarvePlan.TryGetValue(room.NodeId, out var carveInstr) &&
                    carveInstr != null)
                {
                    for (var i = 0; i < carveInstr.Count; i++)
                    {
                        var ci = carveInstr[i];
                        CarveConnectorBiteRays(placement.FloorCells, placement.WallCells, ci.SocketBaseCell, ci.Side, ci.DepthX);
                    }
                }

                if (placement.Meta is not ConnectorMeta &&
                    roomDoorCarvePlan.TryGetValue(room.NodeId, out var doorCells) &&
                    doorCells != null)
                {
                    foreach (var doorCell in doorCells)
                        CarveRoomDoorCell(placement, doorCell, updateOccupancy: false);
                }

                if (OverlapsOutsideAllowedSockets(room.NodeId, placement, graph))
                {
                    UnityEngine.Object.Destroy(inst);
                    return false;
                }

                CommitPlacement(room.NodeId, placement);
            }

            ValidateAllEdgesTouch(graph);
            return true;
        }

        private void ValidateAllEdgesTouch(MapGraphAsset graph)
        {
            if (graph == null || configSpaceLibrary == null)
                return;

            foreach (var edge in graph.Edges)
            {
                if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                    continue;
                if (!placedNodes.TryGetValue(edge.fromNodeId, out var a) || !placedNodes.TryGetValue(edge.toNodeId, out var b))
                    continue;

                // Design constraint (see README): edges are valid only as Room ↔ CorridorRoom.
                var aIsCorridor = a.Meta is ConnectorMeta;
                var bIsCorridor = b.Meta is ConnectorMeta;
                if (aIsCorridor == bIsCorridor)
                {
                    LastError = $"Invalid edge {edge.fromNodeId}->{edge.toNodeId}: expected Room↔Corridor only.";
                    throw new InvalidOperationException(LastError);
                }

                if (!configSpaceLibrary.TryGetSpace(a.Prefab, b.Prefab, out var space, out _))
                {
                    LastError = $"Edge {edge.fromNodeId}->{edge.toNodeId} missing configuration space.";
                    throw new InvalidOperationException(LastError);
                }
                if (space == null || space.IsEmpty)
                {
                    LastError = $"Edge {edge.fromNodeId}->{edge.toNodeId} has empty configuration space.";
                    throw new InvalidOperationException(LastError);
                }

                var delta = b.RootCell - a.RootCell;
                if (!space.Contains(new Vector2Int(delta.x, delta.y)))
                {
                    LastError = $"Edge {edge.fromNodeId}->{edge.toNodeId} not satisfied in layout.";
                    throw new InvalidOperationException(LastError);
                }
            }
        }

        private bool OverlapsOutsideAllowedSockets(string nodeId, Placement placement, MapGraphAsset graph)
        {
            // Allow floor↔floor overlap only at the satisfied "door cell" between a room and a connector.
            var allowedFloorOverlap = new HashSet<Vector3Int>();

            if (graph != null && placement?.Meta != null)
            {
                foreach (var edge in graph.Edges)
                {
                    if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                        continue;
                    var aId = edge.fromNodeId;
                    var bId = edge.toNodeId;
                    if (aId != nodeId && bId != nodeId)
                        continue;

                    var otherId = aId == nodeId ? bId : aId;
                    if (!placedNodes.TryGetValue(otherId, out var otherPlacement) || otherPlacement?.Meta == null)
                        continue;

                    var placementIsConnector = placement.Meta is ConnectorMeta;
                    var otherIsConnector = otherPlacement.Meta is ConnectorMeta;
                    if (placementIsConnector == otherIsConnector)
                        continue;

                    var connPlacement = placementIsConnector ? placement : otherPlacement;
                    var roomPlacement = placementIsConnector ? otherPlacement : placement;

                    foreach (var connSock in connPlacement.Meta.Sockets ?? Array.Empty<DoorSocket>())
                    {
                        if (connSock == null)
                            continue;
                        foreach (var roomSock in roomPlacement.Meta.Sockets ?? Array.Empty<DoorSocket>())
                        {
                            if (roomSock == null)
                                continue;
                            if (!TryComputeBiteDepth(connSock, roomSock, out _))
                                continue;
                            // roomSock cell is exactly the chosen door cell.
                            allowedFloorOverlap.Add(stamp.CellFromWorld(roomSock.transform.position));
                        }
                    }
                }
            }

            if (HasOverlap(placement.FloorCells, occupiedFloor, allowedFloorOverlap))
            {
                LastError = $"Layout room {placement.Prefab.name} overlaps existing floors.";
                return true;
            }
            if (HasOverlap(placement.WallCells, occupiedFloor))
            {
                LastError = $"Layout room {placement.Prefab.name} walls overlap existing floors.";
                return true;
            }
            if (HasOverlap(placement.FloorCells, occupiedWall))
            {
                LastError = $"Layout room {placement.Prefab.name} floors overlap existing walls.";
                return true;
            }
            return false;
        }

        private readonly struct ConnectorCarveInstruction
        {
            public Vector3Int SocketBaseCell { get; }
            public DoorSide Side { get; }
            public int DepthX { get; }

            public ConnectorCarveInstruction(Vector3Int socketBaseCell, DoorSide side, int depthX)
            {
                SocketBaseCell = socketBaseCell;
                Side = side;
                DepthX = Mathf.Max(0, depthX);
            }
        }

        private static Vector3Int InwardVector(DoorSide side)
        {
            return side switch
            {
                DoorSide.North => Vector3Int.down,
                DoorSide.South => Vector3Int.up,
                DoorSide.East => Vector3Int.left,
                DoorSide.West => Vector3Int.right,
                _ => Vector3Int.down
            };
        }

        private static Vector3Int TangentVector(DoorSide side)
        {
            return side == DoorSide.North || side == DoorSide.South ? Vector3Int.right : Vector3Int.up;
        }

        private static void CarveConnectorBiteRays(HashSet<Vector3Int> connectorFloors, HashSet<Vector3Int> connectorWalls, Vector3Int socketBaseCell, DoorSide side, int depthX)
        {
            if (connectorFloors == null || connectorWalls == null)
                return;

            var inward = InwardVector(side);
            var tangent = TangentVector(side);
            var maxK = Mathf.Max(0, depthX);

            for (var k = 0; k <= maxK; k++)
            {
                var center = socketBaseCell + inward * k;
                var left = center + tangent;
                var right = center - tangent;

                // Keep connector floor at the final door cell (k == depthX) to avoid "holes" when the room has no floor tile there.
                if (k < maxK)
                    connectorFloors.Remove(center);
                connectorWalls.Remove(center);

                connectorFloors.Remove(left);
                connectorWalls.Remove(left);
                connectorFloors.Remove(right);
                connectorWalls.Remove(right);
            }
        }

        private void CarveRoomDoorCell(Placement roomPlacement, Vector3Int doorCell, bool updateOccupancy)
        {
            if (roomPlacement?.Meta == null)
                return;
            if (roomPlacement.Meta is ConnectorMeta)
                return;

            roomPlacement.WallCells.Remove(doorCell);
            roomPlacement.FloorCells.Add(doorCell);

            if (updateOccupancy)
            {
                occupiedWall.Remove(doorCell);
                occupiedFloor.Add(doorCell);
            }
        }

        private void CarveConnectorBiteRays(Placement connectorPlacement, Vector3Int socketBaseCell, DoorSide side, int depthX, bool updateOccupancy)
        {
            if (connectorPlacement?.Meta == null)
                return;
            if (connectorPlacement.Meta is not ConnectorMeta)
                return;

            var inward = InwardVector(side);
            var tangent = TangentVector(side);
            var maxK = Mathf.Max(0, depthX);

            for (var k = 0; k <= maxK; k++)
            {
                var center = socketBaseCell + inward * k;
                var left = center + tangent;
                var right = center - tangent;

                if (k < maxK)
                    connectorPlacement.FloorCells.Remove(center);
                connectorPlacement.WallCells.Remove(center);
                connectorPlacement.FloorCells.Remove(left);
                connectorPlacement.WallCells.Remove(left);
                connectorPlacement.FloorCells.Remove(right);
                connectorPlacement.WallCells.Remove(right);

                if (updateOccupancy)
                {
                    if (k < maxK)
                        occupiedFloor.Remove(center);
                    occupiedWall.Remove(center);
                    occupiedFloor.Remove(left);
                    occupiedWall.Remove(left);
                    occupiedFloor.Remove(right);
                    occupiedWall.Remove(right);
                }
            }
        }

        private bool TryComputeBiteDepth(DoorSocket connectorSocket, DoorSocket roomSocket, out int depthX)
        {
            depthX = 0;
            if (connectorSocket == null || roomSocket == null || stamp == null)
                return false;
            if (roomSocket.Side != connectorSocket.Side.Opposite())
                return false;

            var baseCell = stamp.CellFromWorld(connectorSocket.transform.position);
            var roomCell = stamp.CellFromWorld(roomSocket.transform.position);
            var inward = InwardVector(connectorSocket.Side);
            var delta = roomCell - baseCell;
            var x = delta.x * inward.x + delta.y * inward.y;
            if (x < 0 || x >= Mathf.Max(1, connectorSocket.BiteDepth))
                return false;
            if (delta != inward * x)
                return false;

            depthX = x;
            return true;
        }

        private bool TryBuildConnectorCarvePlan(
            MapGraphLayoutGenerator.LayoutResult layout,
            Vector3Int offset,
            MapGraphAsset graph,
            out Dictionary<string, List<ConnectorCarveInstruction>> carvePlan)
        {
            carvePlan = new Dictionary<string, List<ConnectorCarveInstruction>>();
            if (layout == null || graph == null)
            {
                LastError = "Missing layout or graph for carve plan.";
                return false;
            }

            var offset2 = new Vector2Int(offset.x, offset.y);

            foreach (var edge in graph.Edges)
            {
                if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                    continue;
                if (!layout.Rooms.TryGetValue(edge.fromNodeId, out var a) || a == null)
                {
                    LastError = $"Carve plan: missing layout node {edge.fromNodeId}.";
                    return false;
                }
                if (!layout.Rooms.TryGetValue(edge.toNodeId, out var b) || b == null)
                {
                    LastError = $"Carve plan: missing layout node {edge.toNodeId}.";
                    return false;
                }

                var aIsConnector = a.Prefab != null && a.Prefab.GetComponent<ConnectorMeta>() != null;
                var bIsConnector = b.Prefab != null && b.Prefab.GetComponent<ConnectorMeta>() != null;
                if (aIsConnector == bIsConnector)
                    continue;

                var conn = aIsConnector ? a : b;
                var room = aIsConnector ? b : a;

                if (!TryFindLayoutBiteDepth(conn, room, offset2, out var baseCell, out var side, out var depthX))
                {
                    LastError = $"Carve plan: cannot match bite depth for edge {edge.fromNodeId}->{edge.toNodeId}.";
                    return false;
                }

                if (!carvePlan.TryGetValue(conn.NodeId, out var list))
                {
                    list = new List<ConnectorCarveInstruction>();
                    carvePlan[conn.NodeId] = list;
                }
                list.Add(new ConnectorCarveInstruction(baseCell, side, depthX));
            }

            return true;
        }

        private bool TryBuildRoomDoorCarvePlan(
            MapGraphLayoutGenerator.LayoutResult layout,
            Vector3Int offset,
            MapGraphAsset graph,
            out Dictionary<string, HashSet<Vector3Int>> roomDoorCarvePlan)
        {
            roomDoorCarvePlan = new Dictionary<string, HashSet<Vector3Int>>();
            if (layout == null || graph == null)
            {
                LastError = "Missing layout or graph for room door carve plan.";
                return false;
            }

            var offset2 = new Vector2Int(offset.x, offset.y);

            foreach (var edge in graph.Edges)
            {
                if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                    continue;
                if (!layout.Rooms.TryGetValue(edge.fromNodeId, out var a) || a == null)
                {
                    LastError = $"Room door carve plan: missing layout node {edge.fromNodeId}.";
                    return false;
                }
                if (!layout.Rooms.TryGetValue(edge.toNodeId, out var b) || b == null)
                {
                    LastError = $"Room door carve plan: missing layout node {edge.toNodeId}.";
                    return false;
                }

                var aIsConnector = a.Prefab != null && a.Prefab.GetComponent<ConnectorMeta>() != null;
                var bIsConnector = b.Prefab != null && b.Prefab.GetComponent<ConnectorMeta>() != null;
                if (aIsConnector == bIsConnector)
                    continue;

                var conn = aIsConnector ? a : b;
                var room = aIsConnector ? b : a;

                if (!TryFindLayoutBiteDepth(conn, room, offset2, out var baseCell, out var side, out var depthX))
                {
                    LastError = $"Room door carve plan: cannot match bite depth for edge {edge.fromNodeId}->{edge.toNodeId}.";
                    return false;
                }

                var doorCell = baseCell + InwardVector(side) * depthX;
                if (!roomDoorCarvePlan.TryGetValue(room.NodeId, out var set))
                {
                    set = new HashSet<Vector3Int>();
                    roomDoorCarvePlan[room.NodeId] = set;
                }
                set.Add(doorCell);
            }

            return true;
        }

        private bool TryFindLayoutBiteDepth(
            MapGraphLayoutGenerator.RoomPlacement connector,
            MapGraphLayoutGenerator.RoomPlacement room,
            Vector2Int offset,
            out Vector3Int connectorSocketBaseCell,
            out DoorSide connectorSide,
            out int depthX)
        {
            connectorSocketBaseCell = default;
            connectorSide = default;
            depthX = 0;

            if (connector?.Shape?.Sockets == null || room?.Shape?.Sockets == null)
                return false;
            if (connector.Prefab == null || room.Prefab == null)
                return false;

            var connRoot = connector.Root + offset;
            var roomRoot = room.Root + offset;

            foreach (var connSock in connector.Shape.Sockets)
            {
                if (connSock == null)
                    continue;
                var inward = new Vector2Int(InwardVector(connSock.Side).x, InwardVector(connSock.Side).y);
                var baseCell2 = connRoot + connSock.CellOffset;
                var maxDepth = Mathf.Max(1, connSock.BiteDepth);

                foreach (var roomSock in room.Shape.Sockets)
                {
                    if (roomSock == null)
                        continue;
                    if (roomSock.Side != connSock.Side.Opposite())
                        continue;

                    var roomCell2 = roomRoot + roomSock.CellOffset;
                    var delta = roomCell2 - baseCell2;
                    var x = delta.x * inward.x + delta.y * inward.y;
                    if (x < 0 || x >= maxDepth)
                        continue;
                    if (delta != inward * x)
                        continue;

                    connectorSocketBaseCell = new Vector3Int(baseCell2.x, baseCell2.y, 0);
                    connectorSide = connSock.Side;
                    depthX = x;
                    return true;
                }
            }

            return false;
        }

        private bool TryPlaceEdge(string anchorId, string targetId, MapGraphAsset.EdgeData edge, MapGraphAsset graph, Func<bool> continueAfterPlacement)
        {
            if (!CheckTimeLimit())
                return false;

            var key = MapGraphKey.NormalizeKey(edge.fromNodeId, edge.toNodeId);
            if (!edgeAssignments.TryGetValue(key, out var connectionType) || connectionType == null)
            {
                LastError = $"Edge {edge.fromNodeId}-{edge.toNodeId} has no connection type.";
                return false;
            }

            if (!placedNodes.TryGetValue(anchorId, out var anchorPlacement) || anchorPlacement?.Meta == null)
            {
                LastError = $"Anchor node {anchorId} is not placed.";
                return false;
            }
            var anchorPrefab = anchorPlacement.Prefab;

            if (!nodeAssignments.TryGetValue(targetId, out var targetRoomType) || targetRoomType == null)
            {
                LastError = $"Target node {targetId} has no room type.";
                return false;
            }

            var anchorMeta = anchorPlacement.Meta;
            var anchorSockets = anchorMeta.Sockets != null
                ? anchorMeta.Sockets.Where(s => s && !IsSocketBlocked(anchorMeta, s)).ToList()
                : new List<DoorSocket>();
            anchorSockets.Shuffle(rng);
            if (anchorSockets.Count == 0)
            {
                LastError = $"No sockets available on anchor {anchorId} for target {targetId}.";
                return false;
            }

            foreach (var anchorSock in anchorSockets)
            {
                var connectorPrefabs = GetConnectorPrefabs(connectionType, anchorSock.Side.Opposite(), NormalizeWidth(anchorSock.Width), out var prefabError);
                connectorPrefabs.Shuffle(rng);
                if (connectorPrefabs.Count == 0)
                {
                    LastError = prefabError ?? $"Connection type {connectionType.name} has no prefabs.";
                    return false;
                }

                var anchorCell = stamp.CellFromWorld(anchorSock.transform.position);

                foreach (var connPrefab in connectorPrefabs)
                {
                    if (anchorPrefab != null && !HasConfigSpace(anchorPrefab, connPrefab))
                        continue;
                    if (!TryGetBlueprint(connPrefab, out var connBlueprint, out var bpError))
                    {
                        LastError ??= bpError;
                        continue;
                    }

                    var s1Candidates = connBlueprint.Sockets
                        .Where(s => s.Side == anchorSock.Side.Opposite() && NormalizeWidth(s.Width) == NormalizeWidth(anchorSock.Width))
                        .ToList();
                    s1Candidates.Shuffle(rng);
                    if (s1Candidates.Count == 0)
                        s1Candidates = connBlueprint.Sockets.Where(s => s.Side == anchorSock.Side.Opposite()).ToList();
                    if (s1Candidates.Count == 0)
                        continue;

                    foreach (var s1 in s1Candidates)
                    {
                        var maxDepth1 = Mathf.Max(1, s1.BiteDepth);
                        for (var depthX1 = 0; depthX1 < maxDepth1; depthX1++)
                        {
                            var connRootCell = anchorCell - (s1.CellOffset + InwardVector(s1.Side) * depthX1);
                            BuildPlacementFromBlueprint(connBlueprint, connRootCell, out var connFloorsBase, out var connWallsBase);
                            CarveConnectorBiteRays(connFloorsBase, connWallsBase, connRootCell + s1.CellOffset, s1.Side, depthX1);

                            var allowedAnchorStrip = AllowedWidthStrip(anchorCell, s1.Side, s1.Width);
                            if (HasOverlap(connFloorsBase, occupiedFloor, allowedAnchorStrip))
                                continue;

                            var s2Candidates = connBlueprint.Sockets.Where(s => s != s1).ToList();
                            s2Candidates.Shuffle(rng);
                            if (s2Candidates.Count == 0)
                            {
                                LastError ??= $"Connector {connPrefab.name} has no secondary sockets for edge {anchorId}->{targetId}.";
                                continue;
                            }

                            foreach (var s2 in s2Candidates)
                            {
                                var maxDepth2 = Mathf.Max(1, s2.BiteDepth);
                                for (var depthX2 = 0; depthX2 < maxDepth2; depthX2++)
                                {
                                    var connFloors = new HashSet<Vector3Int>(connFloorsBase);
                                    var connWalls = new HashSet<Vector3Int>(connWallsBase);
                                    var s2BaseCell = connRootCell + s2.CellOffset;
                                    CarveConnectorBiteRays(connFloors, connWalls, s2BaseCell, s2.Side, depthX2);

                                    var roomPrefabs = GetRoomPrefabs(targetRoomType, s2.Side.Opposite(), s2.Width, out var roomPrefabsError);
                                    roomPrefabs.Shuffle(rng);
                                    if (roomPrefabs.Count == 0)
                                    {
                                        LastError = roomPrefabsError ?? $"Room type {targetRoomType.name} has no prefabs.";
                                        continue;
                                    }

                                    foreach (var roomPrefab in roomPrefabs)
                                    {
                                        if (!HasConfigSpace(connPrefab, roomPrefab))
                                            continue;
                                        if (!TryGetBlueprint(roomPrefab, out var roomBlueprint, out var roomBpError))
                                        {
                                            LastError ??= roomBpError;
                                            continue;
                                        }

                                        var roomSock = roomBlueprint.Sockets.FirstOrDefault(s =>
                                            s.Side == s2.Side.Opposite() && NormalizeWidth(s.Width) == NormalizeWidth(s2.Width));
                                        if (roomSock == null)
                                        {
                                            LastError ??= $"Room prefab {roomPrefab.name} missing socket for side {s2.Side.Opposite()} width {NormalizeWidth(s2.Width)}.";
                                            continue;
                                        }

                                        var s2TargetCell = s2BaseCell + InwardVector(s2.Side) * depthX2;
                                        var roomRootCell = s2TargetCell - roomSock.CellOffset;
                                        if (!FitsConfigSpace(connPrefab, roomPrefab, connRootCell, roomRootCell))
                                        {
                                            LastError ??= $"Config space empty for {connPrefab.name}->{roomPrefab.name}.";
                                            continue;
                                        }
                                        BuildPlacementFromBlueprint(roomBlueprint, roomRootCell, out var roomFloors, out var roomWalls);

                                        if (HasOverlap(roomFloors, occupiedFloor) || HasOverlap(roomWalls, occupiedFloor) || HasOverlap(roomFloors, occupiedWall))
                                        {
                                            LastError ??= $"Room {roomPrefab.name} overlaps on edge {anchorId}->{targetId}.";
                                            continue;
                                        }

                                        var allowedConnectorWallReplace = new HashSet<Vector3Int>();
                                        if (anchorPlacement != null)
                                            foreach (var wc in anchorPlacement.WallCells) allowedConnectorWallReplace.Add(wc);
                                        if (HasOverlap(connWalls, occupiedWall, allowedConnectorWallReplace))
                                        {
                                            LastError ??= $"Connector walls overlap on edge {anchorId}->{targetId}.";
                                            continue;
                                        }

                                        int depthBeforeConn = placementStack.Count;
                                        // Instantiate and commit after math checks pass
                                        var connInst = UnityEngine.Object.Instantiate(connPrefab, Vector3.zero, Quaternion.identity, stampWorldParent());
                                        var connMeta = connInst.GetComponent<ConnectorMeta>();
                                        if (connMeta == null)
                                        {
                                            UnityEngine.Object.Destroy(connInst);
                                            continue;
                                        }
                                        connMeta.ResetUsed();
                                        AlignToCell(connInst.transform, connRootCell);
                                        if (!TryComputePlacement(connMeta, connPrefab, out var connPlacement))
                                        {
                                            UnityEngine.Object.Destroy(connInst);
                                            continue;
                                        }
                                        if (!FitsConfigSpace(anchorPrefab, connPrefab, anchorPlacement.RootCell, connRootCell))
                                        {
                                            UnityEngine.Object.Destroy(connInst);
                                            continue;
                                        }

                                        var s1Actual = FindSocketAtCell(connMeta.Sockets, s1, connRootCell);
                                        var s2Actual = FindSocketAtCell(connMeta.Sockets, s2, connRootCell, s1Actual);
                                        if (s1Actual == null || s2Actual == null)
                                        {
                                            UnityEngine.Object.Destroy(connInst);
                                            continue;
                                        }

                                        CarveConnectorBiteRays(connPlacement.FloorCells, connPlacement.WallCells, stamp.CellFromWorld(s1Actual.transform.position), s1Actual.Side, depthX1);
                                        CarveConnectorBiteRays(connPlacement.FloorCells, connPlacement.WallCells, stamp.CellFromWorld(s2Actual.transform.position), s2Actual.Side, depthX2);

                                        var roomInst = UnityEngine.Object.Instantiate(roomPrefab, Vector3.zero, Quaternion.identity, stampWorldParent());
                                        var roomMeta = roomInst.GetComponent<RoomMeta>();
                                        if (roomMeta == null)
                                        {
                                            UnityEngine.Object.Destroy(connInst);
                                            UnityEngine.Object.Destroy(roomInst);
                                            continue;
                                        }
                                        roomMeta.ResetUsed();
                                        AlignToCell(roomInst.transform, roomRootCell);
                                        if (!TryComputePlacement(roomMeta, roomPrefab, out var roomPlacement))
                                        {
                                            UnityEngine.Object.Destroy(connInst);
                                            UnityEngine.Object.Destroy(roomInst);
                                            continue;
                                        }
                                        if (!FitsConfigSpace(connPrefab, roomPrefab, connRootCell, roomRootCell))
                                        {
                                            UnityEngine.Object.Destroy(connInst);
                                            UnityEngine.Object.Destroy(roomInst);
                                            continue;
                                        }

                                        var roomSockActual = FindSocketAtCell(roomMeta.Sockets, roomSock, roomRootCell);
                                        if (roomSockActual == null)
                                        {
                                            UnityEngine.Object.Destroy(connInst);
                                            UnityEngine.Object.Destroy(roomInst);
                                            continue;
                                        }

                                        // Carve doors only for actually-used room sockets.
                                        CarveRoomDoorCell(anchorPlacement, anchorCell, updateOccupancy: true);
                                        CarveRoomDoorCell(roomPlacement, stamp.CellFromWorld(roomSockActual.transform.position), updateOccupancy: false);

                                        connPlacement.UsedSockets.AddRange(new[] { s1Actual, s2Actual, anchorSock });
                                        roomPlacement.UsedSockets.Add(roomSockActual);
                                        connPlacement.EdgeKey = key;

                                        CommitPlacement(null, connPlacement);
                                        CommitPlacement(targetId, roomPlacement);
                                        placedEdges.Add(key);

                                        var success = continueAfterPlacement == null || continueAfterPlacement();
                                        if (success)
                                            return true;

                                        RollbackToDepth(depthBeforeConn);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            LastError ??= $"No placement found for edge {anchorId}->{targetId}.";
            return false;
        }

        private bool TryPlaceEdgeBetweenPlaced(string anchorId, string targetId, MapGraphAsset.EdgeData edge, MapGraphAsset graph, Func<bool> continueAfterPlacement)
        {
            if (!CheckTimeLimit())
                return false;

            var key = MapGraphKey.NormalizeKey(edge.fromNodeId, edge.toNodeId);
            if (!edgeAssignments.TryGetValue(key, out var connectionType) || connectionType == null)
            {
                LastError = $"Edge {edge.fromNodeId}-{edge.toNodeId} has no connection type.";
                return false;
            }

            if (!placedNodes.TryGetValue(anchorId, out var anchorPlacement) || anchorPlacement?.Meta == null)
            {
                LastError = $"Anchor node {anchorId} is not placed.";
                return false;
            }
            var anchorPrefab = anchorPlacement.Prefab;

            if (!placedNodes.TryGetValue(targetId, out var targetPlacement) || targetPlacement?.Meta == null)
            {
                LastError = $"Target node {targetId} is not placed.";
                return false;
            }
            var targetPrefab = targetPlacement.Prefab;

            var anchorSockets = anchorPlacement.Meta.Sockets != null
                ? anchorPlacement.Meta.Sockets.Where(s => s && !IsSocketBlocked(anchorPlacement.Meta, s)).ToList()
                : new List<DoorSocket>();
            anchorSockets.Shuffle(rng);
            if (anchorSockets.Count == 0)
            {
                LastError = $"No sockets available on anchor {anchorId} for target {targetId}.";
                return false;
            }

            var targetSockets = targetPlacement.Meta.Sockets != null
                ? targetPlacement.Meta.Sockets.Where(s => s && !IsSocketBlocked(targetPlacement.Meta, s)).ToList()
                : new List<DoorSocket>();
            targetSockets.Shuffle(rng);
            if (targetSockets.Count == 0)
            {
                LastError = $"No sockets available on target {targetId} for anchor {anchorId}.";
                return false;
            }

            // Fast path: if endpoints already satisfy config-space and socket constraints, mark edge placed without inserting a connector.
            if (FitsConfigSpace(anchorPrefab, targetPrefab, anchorPlacement.RootCell, targetPlacement.RootCell))
            {
                var anchorIsConnector = anchorPlacement.Meta is ConnectorMeta;
                var targetIsConnector = targetPlacement.Meta is ConnectorMeta;

                if (anchorIsConnector != targetIsConnector)
                {
                    var connSockets = anchorIsConnector ? anchorSockets : targetSockets;
                    var roomSockets = anchorIsConnector ? targetSockets : anchorSockets;
                    var connPlacement = anchorIsConnector ? anchorPlacement : targetPlacement;
                    var roomPlacement = anchorIsConnector ? targetPlacement : anchorPlacement;

                    foreach (var connSock in connSockets)
                    {
                        if (connSock == null)
                            continue;
                        foreach (var roomSock in roomSockets.Where(s =>
                                     s != null &&
                                     s.Side == connSock.Side.Opposite() &&
                                     NormalizeWidth(s.Width) == NormalizeWidth(connSock.Width)))
                        {
                            if (!TryComputeBiteDepth(connSock, roomSock, out _))
                                continue;

                            // Apply "used door" carve on the room, and bite-cut on the connector.
                            TryComputeBiteDepth(connSock, roomSock, out var depthX);
                            CarveRoomDoorCell(roomPlacement, stamp.CellFromWorld(roomSock.transform.position), updateOccupancy: true);
                            CarveConnectorBiteRays(connPlacement, stamp.CellFromWorld(connSock.transform.position), connSock.Side, depthX, updateOccupancy: true);

                            MarkSocketUsed(connSock);
                            MarkSocketUsed(roomSock);
                            AddUsedSocketToPlacement(connPlacement, connSock);
                            AddUsedSocketToPlacement(roomPlacement, roomSock);
                            placedEdges.Add(key);
                            return continueAfterPlacement == null || continueAfterPlacement();
                        }
                    }
                }
                else
                {
                    // Legacy fallback: require exact aligned socket cells.
                    foreach (var aSock in anchorSockets)
                    {
                        var aCell = stamp.CellFromWorld(aSock.transform.position);
                        foreach (var tSock in targetSockets.Where(s => s.Side == aSock.Side.Opposite() && NormalizeWidth(s.Width) == NormalizeWidth(aSock.Width)))
                        {
                            var tCell = stamp.CellFromWorld(tSock.transform.position);
                            if (aCell != tCell)
                                continue;

                            MarkSocketUsed(aSock);
                            MarkSocketUsed(tSock);
                            AddUsedSocketToPlacement(anchorPlacement, aSock);
                            AddUsedSocketToPlacement(targetPlacement, tSock);
                            placedEdges.Add(key);
                            return continueAfterPlacement == null || continueAfterPlacement();
                        }
                    }
                }
            }

            foreach (var anchorSock in anchorSockets)
            {
                var connectorPrefabs = GetConnectorPrefabs(connectionType, anchorSock.Side.Opposite(), NormalizeWidth(anchorSock.Width), out var prefabError);
                connectorPrefabs.Shuffle(rng);
                if (connectorPrefabs.Count == 0)
                {
                    LastError = prefabError ?? $"Connection type {connectionType.name} has no prefabs.";
                    return false;
                }

                var anchorCell = stamp.CellFromWorld(anchorSock.transform.position);

                foreach (var connPrefab in connectorPrefabs)
                {
                    if ((anchorPrefab != null && !HasConfigSpace(anchorPrefab, connPrefab)) || (targetPrefab != null && !HasConfigSpace(connPrefab, targetPrefab)))
                        continue;
                    if (!TryGetBlueprint(connPrefab, out var connBlueprint, out var bpError))
                    {
                        LastError ??= bpError;
                        continue;
                    }

                    var s1Candidates = connBlueprint.Sockets
                        .Where(s => s.Side == anchorSock.Side.Opposite() && NormalizeWidth(s.Width) == NormalizeWidth(anchorSock.Width))
                        .ToList();
                    s1Candidates.Shuffle(rng);
                    if (s1Candidates.Count == 0)
                        s1Candidates = connBlueprint.Sockets.Where(s => s.Side == anchorSock.Side.Opposite()).ToList();
                    if (s1Candidates.Count == 0)
                        continue;

                    foreach (var s1 in s1Candidates)
                    {
                        var maxDepth1 = Mathf.Max(1, s1.BiteDepth);
                        for (var depthX1 = 0; depthX1 < maxDepth1; depthX1++)
                        {
                            var connRootCell = anchorCell - (s1.CellOffset + InwardVector(s1.Side) * depthX1);
                            BuildPlacementFromBlueprint(connBlueprint, connRootCell, out var connFloorsBase, out var connWallsBase);
                            CarveConnectorBiteRays(connFloorsBase, connWallsBase, connRootCell + s1.CellOffset, s1.Side, depthX1);

                            var s2Candidates = connBlueprint.Sockets.Where(s => s != s1).ToList();
                            s2Candidates.Shuffle(rng);
                            if (s2Candidates.Count == 0)
                            {
                                LastError ??= $"Connector {connPrefab.name} has no secondary sockets for edge {anchorId}->{targetId}.";
                                continue;
                            }

                            foreach (var s2 in s2Candidates)
                            {
                                foreach (var targetSock in targetSockets.Where(ts =>
                                             ts.Side == s2.Side.Opposite() &&
                                             NormalizeWidth(ts.Width) == NormalizeWidth(s2.Width)))
                                {
                                    var targetCell = stamp.CellFromWorld(targetSock.transform.position);
                                    var s2BaseCell = connRootCell + s2.CellOffset;
                                    var inward2 = InwardVector(s2.Side);
                                    var delta2 = targetCell - s2BaseCell;
                                    var depthX2 = delta2.x * inward2.x + delta2.y * inward2.y;
                                    if (depthX2 < 0 || depthX2 >= Mathf.Max(1, s2.BiteDepth))
                                        continue;
                                    if (delta2 != inward2 * depthX2)
                                        continue;

                                    var connFloors = new HashSet<Vector3Int>(connFloorsBase);
                                    var connWalls = new HashSet<Vector3Int>(connWallsBase);
                                    CarveConnectorBiteRays(connFloors, connWalls, s2BaseCell, s2.Side, depthX2);

                                    if (!FitsConfigSpace(anchorPrefab, connPrefab, anchorPlacement.RootCell, connRootCell))
                                        continue;
                                    if (!FitsConfigSpace(connPrefab, targetPrefab, connRootCell, targetPlacement.RootCell))
                                        continue;

                                    var allowedFloor = AllowedWidthStrip(anchorCell, s1.Side, s1.Width);
                                    foreach (var c in AllowedWidthStrip(targetCell, s2.Side, s2.Width)) allowedFloor.Add(c);
                                    if (HasOverlap(connFloors, occupiedFloor, allowedFloor))
                                        continue;

                                    var allowedConnectorWallReplace = new HashSet<Vector3Int>();
                                    foreach (var wc in anchorPlacement.WallCells) allowedConnectorWallReplace.Add(wc);
                                    foreach (var wc in targetPlacement.WallCells) allowedConnectorWallReplace.Add(wc);
                                    if (HasOverlap(connWalls, occupiedWall, allowedConnectorWallReplace))
                                        continue;

                                    int depthBeforeConn = placementStack.Count;

                                    var connInst = UnityEngine.Object.Instantiate(connPrefab, Vector3.zero, Quaternion.identity, stampWorldParent());
                                    var connMeta = connInst.GetComponent<ConnectorMeta>();
                                    if (connMeta == null)
                                    {
                                        UnityEngine.Object.Destroy(connInst);
                                        continue;
                                    }
                                    connMeta.ResetUsed();
                                    AlignToCell(connInst.transform, connRootCell);
                                    if (!TryComputePlacement(connMeta, connPrefab, out var connPlacement))
                                    {
                                        UnityEngine.Object.Destroy(connInst);
                                        continue;
                                    }
                                    if (!FitsConfigSpace(anchorPrefab, connPrefab, anchorPlacement.RootCell, connRootCell))
                                    {
                                        UnityEngine.Object.Destroy(connInst);
                                        continue;
                                    }

                                    var s1Actual = FindSocketAtCell(connMeta.Sockets, s1, connRootCell);
                                    var s2Actual = FindSocketAtCell(connMeta.Sockets, s2, connRootCell, s1Actual);
                                    if (s1Actual == null || s2Actual == null)
                                    {
                                        UnityEngine.Object.Destroy(connInst);
                                        continue;
                                    }

                                    CarveConnectorBiteRays(connPlacement.FloorCells, connPlacement.WallCells, stamp.CellFromWorld(s1Actual.transform.position), s1Actual.Side, depthX1);
                                    CarveConnectorBiteRays(connPlacement.FloorCells, connPlacement.WallCells, stamp.CellFromWorld(s2Actual.transform.position), s2Actual.Side, depthX2);

                                    // Carve doors on the already-placed rooms that participate in this connection.
                                    CarveRoomDoorCell(anchorPlacement, anchorCell, updateOccupancy: true);
                                    CarveRoomDoorCell(targetPlacement, targetCell, updateOccupancy: true);

                                    connPlacement.UsedSockets.AddRange(new[] { s1Actual, s2Actual, anchorSock, targetSock });
                                    connPlacement.EdgeKey = key;

                                    CommitPlacement(null, connPlacement);
                                    placedEdges.Add(key);
                                    var success = continueAfterPlacement == null || continueAfterPlacement();
                                    if (success)
                                        return true;

                                    RollbackToDepth(depthBeforeConn);
                                }
                            }
                        }
                    }
                }
            }

            LastError ??= $"No placement found for edge {anchorId}->{targetId}.";
            return false;
        }
    }
}
