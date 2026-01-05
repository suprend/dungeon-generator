// Assets/Scripts/Generation/Solve/MapGraphLevelSolver.PlacementState.Placement.cs
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

    }
}
