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

                CarveConnectorEntranceWalls(placement);

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
            // Allow overlap only at the bite cell for each satisfied Room↔Corridor edge.
            // Additionally, allow connector side-wall cells adjacent to the bite cell to overlap room floors.
            var allowedFloorOverlap = new HashSet<Vector3Int>();
            var allowedFloorOnWall = new HashSet<Vector3Int>();
            var allowedWallOnFloor = new HashSet<Vector3Int>();
            var placementIsConnector = placement?.Meta is ConnectorMeta;

            static IEnumerable<Vector3Int> SideBiteCells(Vector3Int biteCell, DoorSide side)
            {
                var tangent = side == DoorSide.North || side == DoorSide.South ? Vector3Int.right : Vector3Int.up;
                yield return biteCell + tangent;
                yield return biteCell - tangent;
            }

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

                // Find aligned sockets between placement and otherPlacement
                foreach (var sockA in placement.Meta.Sockets ?? Array.Empty<DoorSocket>())
                {
                    if (sockA == null) continue;
                    var cellA = stamp.CellFromWorld(sockA.transform.position);
                    foreach (var sockB in otherPlacement.Meta.Sockets ?? Array.Empty<DoorSocket>())
                    {
                        if (sockB == null) continue;
                        if (sockA.Side != sockB.Side.Opposite()) continue;
                        var cellB = stamp.CellFromWorld(sockB.transform.position);
                        if (cellA != cellB) continue;

                        allowedFloorOverlap.Add(cellA);
                        allowedFloorOnWall.Add(cellA);
                        allowedWallOnFloor.Add(cellA);

                        if (placementIsConnector)
                        {
                            foreach (var c in SideBiteCells(cellA, sockA.Side))
                                allowedWallOnFloor.Add(c);
                        }
                    }
                }
            }

            // Floors cannot overlap floors except allowed; walls cannot overlap floors; floors cannot overlap walls unless allowed.
            if (HasOverlap(placement.FloorCells, occupiedFloor, allowedFloorOverlap))
            {
                LastError = $"Layout room {placement.Prefab.name} overlaps existing floors.";
                return true;
            }
            if (HasOverlap(placement.WallCells, occupiedFloor, allowedWallOnFloor))
            {
                LastError = $"Layout room {placement.Prefab.name} walls overlap existing floors.";
                return true;
            }
            if (HasOverlap(placement.FloorCells, occupiedWall, allowedFloorOnWall))
            {
                LastError = $"Layout room {placement.Prefab.name} floors overlap existing walls.";
                return true;
            }
            return false;
        }

        private void CarveConnectorEntranceWalls(Placement placement, IEnumerable<DoorSocket> socketsOverride = null)
        {
            if (placement?.Meta is not ConnectorMeta)
                return;

            var sockets = socketsOverride ?? (placement.Meta.Sockets ?? Array.Empty<DoorSocket>());
            foreach (var sock in sockets)
            {
                if (sock == null)
                    continue;
                var biteCell = stamp.CellFromWorld(sock.transform.position);
                CarveConnectorEntranceWalls(placement.WallCells, biteCell, sock.Side);
            }
        }

        private void CarveConnectorEntranceWalls(HashSet<Vector3Int> connectorWalls, Vector3Int biteCell, DoorSide side)
        {
            if (connectorWalls == null)
                return;

            var tangent = side == DoorSide.North || side == DoorSide.South ? Vector3Int.right : Vector3Int.up;
            connectorWalls.Remove(biteCell + tangent);
            connectorWalls.Remove(biteCell - tangent);
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
                ? anchorMeta.Sockets.Where(s => s && !usedSockets.Contains(s)).ToList()
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
                        var connRootCell = anchorCell - s1.CellOffset;
                        BuildPlacementFromBlueprint(connBlueprint, connRootCell, out var connFloors, out var connWalls);
                        CarveConnectorEntranceWalls(connWalls, anchorCell, s1.Side);
                        var allowedAnchorStrip = AllowedWidthStrip(anchorCell, s1.Side, s1.Width);
                        if (HasOverlap(connFloors, occupiedFloor, allowedAnchorStrip))
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
                            var s2Cell = connRootCell + s2.CellOffset;
                            CarveConnectorEntranceWalls(connWalls, s2Cell, s2.Side);
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

                                var roomRootCell = s2Cell - roomSock.CellOffset;
                                if (!FitsConfigSpace(connPrefab, roomPrefab, connRootCell, roomRootCell))
                                {
                                    LastError ??= $"Config space empty for {connPrefab.name}->{roomPrefab.name}.";
                                    continue;
                                }
                                BuildPlacementFromBlueprint(roomBlueprint, roomRootCell, out var roomFloors, out var roomWalls);

                                // Overlap checks
                                var allowedRoomFloor = new HashSet<Vector3Int>(connFloors);
                                allowedRoomFloor.Add(s2Cell);
                                if (HasOverlap(roomFloors, occupiedFloor, allowedRoomFloor))
                                {
                                    LastError ??= $"Room {roomPrefab.name} floor overlaps on edge {anchorId}->{targetId}.";
                                    continue;
                                }
                                var allowedRoomWallReplace = new HashSet<Vector3Int>(connFloors);
                                if (HasOverlap(roomWalls, occupiedWall, allowedRoomWallReplace))
                                {
                                    LastError ??= $"Room {roomPrefab.name} walls overlap on edge {anchorId}->{targetId}.";
                                    continue;
                                }

                                var allowedConnectorWallReplace = new HashSet<Vector3Int>();
                                if (anchorPlacement != null)
                                    foreach (var wc in anchorPlacement.WallCells) allowedConnectorWallReplace.Add(wc);
                                foreach (var wc in roomWalls) allowedConnectorWallReplace.Add(wc);
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

                                CarveConnectorEntranceWalls(connPlacement, new[] { s1Actual, s2Actual });

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
                ? anchorPlacement.Meta.Sockets.Where(s => s && !usedSockets.Contains(s)).ToList()
                : new List<DoorSocket>();
            anchorSockets.Shuffle(rng);
            if (anchorSockets.Count == 0)
            {
                LastError = $"No sockets available on anchor {anchorId} for target {targetId}.";
                return false;
            }

            var targetSockets = targetPlacement.Meta.Sockets != null
                ? targetPlacement.Meta.Sockets.Where(s => s && !usedSockets.Contains(s)).ToList()
                : new List<DoorSocket>();
            targetSockets.Shuffle(rng);
            if (targetSockets.Count == 0)
            {
                LastError = $"No sockets available on target {targetId} for anchor {anchorId}.";
                return false;
            }

            // Fast path: if rooms already satisfy config-space and have aligned sockets, mark edge placed without connector.
            if (FitsConfigSpace(anchorPrefab, targetPrefab, anchorPlacement.RootCell, targetPlacement.RootCell))
            {
                foreach (var aSock in anchorSockets)
                {
                    var aCell = stamp.CellFromWorld(aSock.transform.position);
                    foreach (var tSock in targetSockets.Where(s => s.Side == aSock.Side.Opposite() && NormalizeWidth(s.Width) == NormalizeWidth(aSock.Width)))
                    {
                        var tCell = stamp.CellFromWorld(tSock.transform.position);
                        if (aCell != tCell)
                            continue;

                        usedSockets.Add(aSock);
                        usedSockets.Add(tSock);
                        placedEdges.Add(key);
                        return continueAfterPlacement == null || continueAfterPlacement();
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
                        var connRootCell = anchorCell - s1.CellOffset;
                        BuildPlacementFromBlueprint(connBlueprint, connRootCell, out var connFloors, out var connWalls);
                        CarveConnectorEntranceWalls(connWalls, anchorCell, s1.Side);

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
                                var s2Cell = connRootCell + s2.CellOffset;
                                if (s2Cell != targetCell)
                                    continue;

                                CarveConnectorEntranceWalls(connWalls, targetCell, s2.Side);

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

                                CarveConnectorEntranceWalls(connPlacement, new[] { s1Actual, s2Actual });

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

            LastError ??= $"No placement found for edge {anchorId}->{targetId}.";
            return false;
        }
    }
}
