// Assets/scripts/Generation/Graph/MapGraphLevelSolver.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Solver that iterates faces in ascending size and assigns rooms/connections with backtracking.
/// </summary>
public class MapGraphLevelSolver
{
    private readonly MapGraphAsset graphAsset;
    private readonly List<MapGraphFaceBuilder.Face> orderedFaces = new();
    private readonly AssignmentState state = new();
    private readonly System.Random rng = new();
    private ShapeLibrary shapeLibrary;
    private ConfigurationSpaceLibrary configSpaceLibrary;

    public MapGraphLevelSolver(MapGraphAsset graphAsset)
    {
        this.graphAsset = graphAsset;
    }

    private static bool IsSimpleCycle(MapGraphFaceBuilder.Face face)
    {
        if (face == null || face.Nodes == null || face.Edges == null)
            return false;
        var distinctCount = face.Nodes.Select(n => n?.id).Where(id => !string.IsNullOrEmpty(id)).Distinct().Count();
        return distinctCount == face.Nodes.Count && face.Edges.Count == face.Nodes.Count;
    }

    public bool TrySolve(out IReadOnlyDictionary<string, RoomTypeAsset> nodeAssignments, out IReadOnlyDictionary<(string,string), ConnectionTypeAsset> edgeAssignments, out string error)
    {
        nodeAssignments = null;
        edgeAssignments = null;
        error = null;
        if (graphAsset == null)
        {
            error = "Graph asset is null.";
            return false;
        }

        if (!MapGraphFaceBuilder.TryBuildFaces(graphAsset, out var faces, out error))
            return false;

        orderedFaces.Clear();
        orderedFaces.AddRange(faces
            .OrderByDescending(IsSimpleCycle)   // cycles first
            .ThenBy(f => f.Nodes.Count));       // then by size
        state.Clear();

        if (!TrySolveFace(0))
        {
            error = "Unable to place rooms for provided graph.";
            return false;
        }

        nodeAssignments = new Dictionary<string, RoomTypeAsset>(state.NodeRooms);
        edgeAssignments = new Dictionary<(string,string), ConnectionTypeAsset>(state.EdgeConnections);
        return true;
    }

    /// <summary>
    /// Solves and immediately places prefabs with full backtracking. Result is stamped into provided tilemaps.
    /// </summary>
    public bool TrySolveAndPlace(Grid targetGrid, Tilemap floorMap, Tilemap wallMap, bool clearMaps, int randomSeed, bool verboseLogs, out string error, float maxDurationSeconds = 5f, bool destroyPlacedInstances = true)
    {
        return TrySolveAndPlace(targetGrid, floorMap, wallMap, clearMaps, randomSeed, verboseLogs, null, out error, maxDurationSeconds, destroyPlacedInstances);
    }

    /// <summary>
    /// Solves and immediately places prefabs with full backtracking. Result is stamped into provided tilemaps.
    /// Allows overriding the start room cell (e.g., from MapGraphBuilder transform).
    /// </summary>
    public bool TrySolveAndPlace(Grid targetGrid, Tilemap floorMap, Tilemap wallMap, bool clearMaps, int randomSeed, bool verboseLogs, Vector3Int? startCell, out string error, float maxDurationSeconds = 5f, bool destroyPlacedInstances = true)
    {
        error = null;
        if (targetGrid == null || floorMap == null)
        {
            error = "Target grid and floor map are required.";
            return false;
        }

        var stamp = new TileStampService(targetGrid, floorMap, wallMap);
        if (!PrecomputeGeometry(stamp, out error))
            return false;

        if (!TrySolve(out var nodeAssign, out var edgeAssign, out error))
            return false;

        if (!MapGraphFaceBuilder.TryBuildFaces(graphAsset, out var faces, out error))
            return false;

        var ordered = faces.OrderBy(f => f.Nodes.Count).ToList();
        var rngLocal = randomSeed != 0 ? new System.Random(randomSeed) : new System.Random(UnityEngine.Random.Range(int.MinValue, int.MaxValue));

        var solveStartTime = Time.realtimeSinceStartup;
        var placer = new PlacementState(stamp, rngLocal, nodeAssign, edgeAssign, verboseLogs, startCell, solveStartTime, maxDurationSeconds, shapeLibrary, configSpaceLibrary);
        if (clearMaps)
            stamp.ClearMaps();

        if (!placer.Place(ordered, graphAsset))
        {
            error = placer.LastError ?? "Failed to place full graph layout.";
            if (verboseLogs)
            {
                var duration = Time.realtimeSinceStartup - solveStartTime;
                Debug.Log($"[MapGraphLevelSolver] Placement failed after {duration:0.000}s: {error}");
            }
            placer.Cleanup();
            return false;
        }

        placer.StampAll(disableRenderers: !destroyPlacedInstances);
        if (destroyPlacedInstances)
            placer.DestroyPlacedInstances();
        if (verboseLogs)
        {
            var duration = Time.realtimeSinceStartup - solveStartTime;
            Debug.Log($"[MapGraphLevelSolver] Placement completed in {duration:0.000}s.");
        }
        return true;
    }

    private bool TrySolveFace(int faceIndex)
    {
        if (faceIndex >= orderedFaces.Count)
            return true;

        var face = orderedFaces[faceIndex];
        return TryAssignNodeInFace(faceIndex, 0);
    }

    /// <summary>
    /// Precomputes shapes and configuration spaces for all prefabs referenced by the graph asset.
    /// </summary>
    public bool PrecomputeGeometry(TileStampService stamp, out string error)
    {
        error = null;
        if (graphAsset == null)
        {
            error = "Graph asset is null.";
            return false;
        }

        shapeLibrary = new ShapeLibrary(stamp);
        configSpaceLibrary = new ConfigurationSpaceLibrary(shapeLibrary);

        var prefabs = new HashSet<GameObject>();
        // Collect room prefabs
        foreach (var node in graphAsset.Nodes)
        {
            if (node?.roomType?.prefabs == null) continue;
            foreach (var prefab in node.roomType.prefabs)
                if (prefab != null) prefabs.Add(prefab);
        }

        // Default room type prefabs
        if (graphAsset.DefaultRoomType?.prefabs != null)
            foreach (var prefab in graphAsset.DefaultRoomType.prefabs)
                if (prefab != null) prefabs.Add(prefab);

        // Collect connector prefabs
        foreach (var edge in graphAsset.Edges)
        {
            var conn = edge?.connectionType ?? graphAsset.DefaultConnectionType;
            if (conn?.prefabs == null) continue;
            foreach (var prefab in conn.prefabs)
                if (prefab != null) prefabs.Add(prefab);
        }

        // Build shapes
        foreach (var prefab in prefabs)
        {
            if (!shapeLibrary.TryGetShape(prefab, out _, out error))
                return false;
        }

        // Build configuration spaces for all ordered pairs
        var prefabList = prefabs.ToList();
        for (int i = 0; i < prefabList.Count; i++)
        {
            for (int j = 0; j < prefabList.Count; j++)
            {
                if (!configSpaceLibrary.TryGetSpace(prefabList[i], prefabList[j], out _, out error))
                    return false;
            }
        }

        return true;
    }

    private bool TryAssignNodeInFace(int faceIndex, int nodeIndex)
    {
        var face = orderedFaces[faceIndex];
        if (nodeIndex >= face.Nodes.Count)
        {
            var depthBeforeEdges = state.Depth;
            if (!TryAssignAvailableEdgesForFace(face, faceIndex))
            {
                state.RollbackToDepth(depthBeforeEdges);
                return false;
            }

            if (TrySolveFace(faceIndex + 1))
                return true;

            state.RollbackToDepth(depthBeforeEdges);
            return false;
        }

        var node = face.Nodes[nodeIndex];
        if (node == null || string.IsNullOrEmpty(node.id) || state.NodeRooms.ContainsKey(node.id))
            return TryAssignNodeInFace(faceIndex, nodeIndex + 1);

        var roomCandidates = new List<RoomTypeAsset>(GatherRoomCandidates(node));
        roomCandidates.Shuffle(rng);
        var depthBeforeNode = state.Depth;

        foreach (var candidate in roomCandidates)
        {
            if (!state.PushNode(node.id, candidate, faceIndex))
                continue;

            if (!TryAssignAvailableEdgesForFace(face, faceIndex))
            {
                state.RollbackToDepth(depthBeforeNode);
                continue;
            }

            if (TryAssignNodeInFace(faceIndex, nodeIndex + 1))
                return true;

            state.RollbackToDepth(depthBeforeNode);
        }

        return false;
    }

    private IEnumerable<RoomTypeAsset> GatherRoomCandidates(MapGraphAsset.NodeData node)
    {
        if (node.roomType != null)
            return new[] { node.roomType };
        if (graphAsset.DefaultRoomType != null)
            return new[] { graphAsset.DefaultRoomType };
        return Array.Empty<RoomTypeAsset>();
    }

    private bool TryAssignAvailableEdgesForFace(MapGraphFaceBuilder.Face face, int faceIndex)
    {
        if (face?.Edges == null)
            return true;

        foreach (var edge in face.Edges)
        {
            if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                continue;

            if (!state.NodeRooms.ContainsKey(edge.fromNodeId) || !state.NodeRooms.ContainsKey(edge.toNodeId))
                continue;

            var key = NormalizeKey(edge.fromNodeId, edge.toNodeId);
            if (state.EdgeConnections.ContainsKey(key))
                continue;

            var connection = edge.connectionType != null ? edge.connectionType : graphAsset.DefaultConnectionType;
            if (connection == null)
                return false;

            if (!state.PushEdge(key, connection, faceIndex))
                return false;
        }

        return true;
    }

    private static (string, string) NormalizeKey(string a, string b)
    {
        return string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
    }

    private sealed class AssignmentState
    {
        private readonly Stack<Frame> frames = new();
        public Dictionary<string, RoomTypeAsset> NodeRooms { get; } = new();
        public Dictionary<(string,string), ConnectionTypeAsset> EdgeConnections { get; } = new();

        public int Depth => frames.Count;

        public void Clear()
        {
            frames.Clear();
            NodeRooms.Clear();
            EdgeConnections.Clear();
        }

        public bool PushNode(string nodeId, RoomTypeAsset room, int faceIndex)
        {
            if (NodeRooms.ContainsKey(nodeId))
                return NodeRooms[nodeId] == room;

            NodeRooms[nodeId] = room;
            frames.Push(Frame.ForNode(faceIndex, nodeId, room));
            return true;
        }

        public bool PushEdge((string,string) edgeKey, ConnectionTypeAsset connection, int faceIndex)
        {
            if (EdgeConnections.ContainsKey(edgeKey))
                return EdgeConnections[edgeKey] == connection;

            EdgeConnections[edgeKey] = connection;
            frames.Push(Frame.ForEdge(faceIndex, edgeKey, connection));
            return true;
        }

        public void RollbackToDepth(int depth)
        {
            while (frames.Count > depth)
            {
                var frame = frames.Pop();
                switch (frame.Type)
                {
                    case FrameType.Node:
                        if (frame.NodeId != null)
                            NodeRooms.Remove(frame.NodeId);
                        break;
                    case FrameType.Edge:
                        EdgeConnections.Remove(frame.EdgeKey);
                        break;
                }
            }
        }

        private readonly struct Frame
        {
            public readonly int FaceIndex;
            public readonly FrameType Type;
            public readonly string NodeId;
            public readonly RoomTypeAsset Room;
            public readonly (string,string) EdgeKey;
            public readonly ConnectionTypeAsset Connection;

            private Frame(int faceIndex, FrameType type, string nodeId, RoomTypeAsset room, (string,string) edgeKey, ConnectionTypeAsset connection)
            {
                FaceIndex = faceIndex;
                Type = type;
                NodeId = nodeId;
                Room = room;
                EdgeKey = edgeKey;
                Connection = connection;
            }

            public static Frame ForNode(int faceIndex, string nodeId, RoomTypeAsset room) =>
                new(faceIndex, FrameType.Node, nodeId, room, default, null);

            public static Frame ForEdge(int faceIndex, (string,string) edgeKey, ConnectionTypeAsset connection) =>
                new(faceIndex, FrameType.Edge, null, null, edgeKey, connection);
        }

        private enum FrameType
        {
            Node,
            Edge
        }
    }

    private sealed class PlacementState
    {
        private static bool IsSimpleCycle(MapGraphFaceBuilder.Face face)
        {
            if (face == null || face.Nodes == null || face.Edges == null)
                return false;
            var distinctCount = face.Nodes.Select(n => n?.id).Where(id => !string.IsNullOrEmpty(id)).Distinct().Count();
            return distinctCount == face.Nodes.Count && face.Edges.Count == face.Nodes.Count;
        }

        private readonly TileStampService stamp;
        private readonly System.Random rng;
        private readonly IReadOnlyDictionary<string, RoomTypeAsset> nodeAssignments;
        private readonly IReadOnlyDictionary<(string, string), ConnectionTypeAsset> edgeAssignments;
        private readonly bool verboseLogs;
        private readonly Vector3Int? startCellOverride;
        private readonly float startTime;
        private readonly float maxDurationSeconds;
        public string LastError { get; private set; }

        private readonly Dictionary<string, Placement> placedNodes = new();
        private readonly List<Placement> placementStack = new();
        private readonly HashSet<Vector3Int> occupiedFloor = new();
        private readonly HashSet<Vector3Int> occupiedWall = new();
        private readonly HashSet<DoorSocket> usedSockets = new();
        private readonly Dictionary<GameObject, GeometryCache> geometryCache = new();
        private readonly Dictionary<GameObject, ModuleBlueprint> blueprintCache = new();
        private readonly Dictionary<(ConnectionTypeAsset conn, DoorSide side, int width), List<GameObject>> connectorPrefabCache = new();
        private readonly Dictionary<(RoomTypeAsset room, DoorSide side, int width), List<GameObject>> roomPrefabCache = new();
        private readonly HashSet<(string,string)> placedEdges = new();

        private readonly int totalNodes;
        private readonly int totalEdges;

        private readonly ShapeLibrary shapeLibrary;
        private readonly ConfigurationSpaceLibrary configSpaceLibrary;

        public PlacementState(TileStampService stamp, System.Random rng, IReadOnlyDictionary<string, RoomTypeAsset> nodeAssignments, IReadOnlyDictionary<(string, string), ConnectionTypeAsset> edgeAssignments, bool verboseLogs, Vector3Int? startCellOverride, float startTime, float maxDurationSeconds, ShapeLibrary shapeLibrary, ConfigurationSpaceLibrary configSpaceLibrary)
        {
            this.stamp = stamp;
            this.rng = rng;
            this.nodeAssignments = nodeAssignments;
            this.edgeAssignments = edgeAssignments;
            this.verboseLogs = verboseLogs;
            this.startCellOverride = startCellOverride;
            this.startTime = startTime;
            this.maxDurationSeconds = maxDurationSeconds;
            this.shapeLibrary = shapeLibrary;
            this.configSpaceLibrary = configSpaceLibrary;
            totalNodes = nodeAssignments?.Count ?? 0;
            totalEdges = edgeAssignments?.Count ?? 0;
        }

        public bool Place(List<MapGraphFaceBuilder.Face> orderedFaces, MapGraphAsset graph)
        {
            if (orderedFaces == null || orderedFaces.Count == 0)
                return true;

            if (!CheckTimeLimit())
                return false;

            var startNode = orderedFaces.SelectMany(f => f.Nodes).FirstOrDefault(n => n != null && !string.IsNullOrEmpty(n.id) && nodeAssignments.ContainsKey(n.id));
            if (startNode == null)
            {
                Log("No start node found.");
                LastError = "No start node found.";
                return false;
            }

            var startCell = startCellOverride ?? GraphPosToCell(startNode.position);
            if (!TryPlaceRoom(startNode.id, startCell))
            {
                LastError = $"Failed to place start node {startNode.id}.";
                return false;
            }

            return PlaceFaces(orderedFaces, graph, 0, 0);
        }

        private bool PlaceFaces(List<MapGraphFaceBuilder.Face> faces, MapGraphAsset graph, int faceIndex, int edgeIndex)
        {
            if (!CheckTimeLimit())
                return false;

            if (placedNodes.Count >= nodeAssignments.Count && placedEdges.Count >= edgeAssignments.Count)
                return true;

            if (faceIndex >= faces.Count)
                return placedNodes.Count >= nodeAssignments.Count && placedEdges.Count >= edgeAssignments.Count;

            var face = faces[faceIndex];
            if (face == null || face.Edges == null || face.Edges.Count == 0)
                return PlaceFaces(faces, graph, faceIndex + 1, 0);

            if (placedNodes.Count >= nodeAssignments.Count && placedEdges.Count >= edgeAssignments.Count)
                return true;

            for (int i = edgeIndex; i < face.Edges.Count; i++)
            {
                var edge = face.Edges[i];
                if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                    continue;

                var key = NormalizeKey(edge.fromNodeId, edge.toNodeId);
                if (!edgeAssignments.ContainsKey(key))
                    continue;
                if (placedEdges.Contains(key))
                    continue;

                var aPlaced = placedNodes.ContainsKey(edge.fromNodeId);
                var bPlaced = placedNodes.ContainsKey(edge.toNodeId);

                if (!aPlaced && !bPlaced)
                    continue;

                int depthBefore = placementStack.Count;
                var anchorId = edge.fromNodeId;
                var targetId = edge.toNodeId;

                bool placed;
                if (aPlaced && bPlaced)
                    placed = TryPlaceEdgeBetweenPlaced(anchorId, targetId, edge, graph, () => PlaceFaces(faces, graph, faceIndex, i + 1));
                else
                    placed = TryPlaceEdge(aPlaced ? anchorId : targetId, aPlaced ? targetId : anchorId, edge, graph, () => PlaceFaces(faces, graph, faceIndex, i + 1));

                if (placed)
                    return true;

                RollbackToDepth(depthBefore);
            }

            return PlaceFaces(faces, graph, faceIndex + 1, 0);
        }

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

        private bool TryPlaceEdge(string anchorId, string targetId, MapGraphAsset.EdgeData edge, MapGraphAsset graph, Func<bool> continueAfterPlacement)
        {
            if (!CheckTimeLimit())
                return false;

            var key = NormalizeKey(edge.fromNodeId, edge.toNodeId);
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

                                connPlacement.UsedSockets.AddRange(new[] { s1Actual, anchorSock });
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

            var key = NormalizeKey(edge.fromNodeId, edge.toNodeId);
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

                                connPlacement.UsedSockets.AddRange(new[] { s1Actual, anchorSock, targetSock });
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

        private void AlignToCell(Transform moduleRoot, Vector3Int targetCell)
        {
            if (!moduleRoot || stamp == null) return;
            moduleRoot.position = stamp.WorldFromCell(targetCell);
        }

        private void AlignSocketToCell(Transform moduleRoot, DoorSocket socket, Vector3Int targetCell)
        {
            if (!moduleRoot || !socket || stamp == null) return;
            var currentCell = stamp.CellFromWorld(socket.transform.position);
            var delta = stamp.WorldFromCell(targetCell) - stamp.WorldFromCell(currentCell);
            moduleRoot.position += delta;
        }

        private IEnumerable<DoorSocket> GetMatchingSockets(IEnumerable<DoorSocket> sockets, DoorSide side, int? width)
        {
            if (sockets == null) yield break;
            foreach (var socket in sockets)
            {
                if (socket == null) continue;
                if (side != socket.Side) continue;
                if (width.HasValue && NormalizeWidth(socket.Width) != NormalizeWidth(width.Value)) continue;
                yield return socket;
            }
        }

        private static int NormalizeWidth(int width) => Mathf.Max(1, width);

        private Vector3Int GraphPosToCell(Vector2 graphPos)
        {
            return new Vector3Int(Mathf.RoundToInt(graphPos.x), Mathf.RoundToInt(graphPos.y), 0);
        }

        private DoorSide GetApproxSide(Vector2 from, Vector2 to)
        {
            var dir = to - from;
            if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.y))
                return dir.x >= 0 ? DoorSide.East : DoorSide.West;
            return dir.y >= 0 ? DoorSide.North : DoorSide.South;
        }

        private bool TryComputePlacement(ModuleMetaBase meta, GameObject prefab, out Placement placement)
        {
            placement = null;
            if (meta == null || stamp == null)
                return false;

            var rootCell = stamp.CellFromWorld(meta.transform.position);
            var cacheKey = prefab != null ? prefab : meta.gameObject;

            if (!geometryCache.TryGetValue(cacheKey, out var cached))
            {
                var floorCells = stamp.CollectModuleFloorCells(meta);
                var wallCells = stamp.CollectModuleWallCells(meta);
                var floorOffsets = new List<Vector3Int>(floorCells.Count);
                foreach (var c in floorCells)
                    floorOffsets.Add(c - rootCell);
                var wallOffsets = new List<Vector3Int>(wallCells.Count);
                foreach (var c in wallCells)
                    wallOffsets.Add(c - rootCell);
                cached = new GeometryCache(floorOffsets, wallOffsets);
                geometryCache[cacheKey] = cached;
            }

            var floors = new HashSet<Vector3Int>();
            foreach (var off in cached.FloorOffsets)
                floors.Add(rootCell + off);

            var walls = new HashSet<Vector3Int>();
            foreach (var off in cached.WallOffsets)
                walls.Add(rootCell + off);

            placement = new Placement(meta, floors, walls, prefab, rootCell);
            return true;
        }

        private bool TryGetBlueprint(GameObject prefab, out ModuleBlueprint blueprint, out string error)
        {
            blueprint = null;
            error = null;
            if (prefab == null)
            {
                error = "Prefab is null.";
                return false;
            }

            if (blueprintCache.TryGetValue(prefab, out blueprint))
                return true;

            var inst = UnityEngine.Object.Instantiate(prefab, Vector3.zero, Quaternion.identity, stampWorldParent());
            var meta = inst ? inst.GetComponent<ModuleMetaBase>() : null;
            if (meta == null)
            {
                if (inst) UnityEngine.Object.Destroy(inst);
                error = $"Prefab {prefab.name} has no ModuleMetaBase.";
                return false;
            }

            meta.ResetUsed();
            AlignToCell(meta.transform, Vector3Int.zero);
            var rootCell = stamp.CellFromWorld(meta.transform.position);

            var floorCells = stamp.CollectModuleFloorCells(meta);
            var wallCells = stamp.CollectModuleWallCells(meta);

            var floorOffsets = new List<Vector3Int>(floorCells.Count);
            foreach (var c in floorCells) floorOffsets.Add(c - rootCell);
            var wallOffsets = new List<Vector3Int>(wallCells.Count);
            foreach (var c in wallCells) wallOffsets.Add(c - rootCell);

            var sockets = new List<SocketInfo>();
            if (meta.Sockets != null)
            {
                foreach (var s in meta.Sockets)
                {
                    if (s == null) continue;
                    var sockCell = stamp.CellFromWorld(s.transform.position);
                    sockets.Add(new SocketInfo(s.Side, NormalizeWidth(s.Width), sockCell - rootCell));
                }
            }

            blueprint = new ModuleBlueprint(floorOffsets, wallOffsets, sockets);
            blueprintCache[prefab] = blueprint;
            UnityEngine.Object.Destroy(inst);
            return true;
        }

        private void BuildPlacementFromBlueprint(ModuleBlueprint blueprint, Vector3Int rootCell, out HashSet<Vector3Int> floors, out HashSet<Vector3Int> walls)
        {
            floors = new HashSet<Vector3Int>();
            walls = new HashSet<Vector3Int>();
            if (blueprint == null) return;
            foreach (var off in blueprint.FloorOffsets)
                floors.Add(rootCell + off);
            foreach (var off in blueprint.WallOffsets)
                walls.Add(rootCell + off);
        }

        private DoorSocket FindSocketAtCell(IEnumerable<DoorSocket> sockets, SocketInfo target, Vector3Int rootCell, DoorSocket exclude = null)
        {
            if (sockets == null || target == null) return null;
            var wantedCell = rootCell + target.CellOffset;
            foreach (var socket in sockets)
            {
                if (socket == null || socket == exclude) continue;
                if (socket.Side != target.Side) continue;
                if (NormalizeWidth(socket.Width) != NormalizeWidth(target.Width)) continue;
                var cell = stamp.CellFromWorld(socket.transform.position);
                if (cell == wantedCell)
                    return socket;
            }
            return null;
        }

        private List<GameObject> GetConnectorPrefabs(ConnectionTypeAsset connectionType, DoorSide requiredSide, int requiredWidth, out string error)
        {
            error = null;
            if (connectionType == null)
            {
                error = "Connection type is null.";
                return new List<GameObject>();
            }

            var key = (connectionType, requiredSide, NormalizeWidth(requiredWidth));
            if (connectorPrefabCache.TryGetValue(key, out var cached))
                return new List<GameObject>(cached);

            var prefabs = connectionType.prefabs ?? new List<GameObject>();
            var result = new List<GameObject>();
            foreach (var prefab in prefabs)
            {
                if (prefab == null) continue;
                if (!TryGetBlueprint(prefab, out var bp, out _)) continue;
                if (bp.Sockets.Any(s => s.Side == requiredSide && NormalizeWidth(s.Width) == NormalizeWidth(requiredWidth)))
                    result.Add(prefab);
            }

            if (result.Count == 0 && prefabs.Count == 0)
                error = $"Connection type {connectionType.name} has no prefabs.";
            else if (result.Count == 0)
                error = $"No connector prefabs match side {requiredSide} width {NormalizeWidth(requiredWidth)} for {connectionType.name}.";

            connectorPrefabCache[key] = new List<GameObject>(result);
            return result;
        }

        private List<GameObject> GetRoomPrefabs(RoomTypeAsset roomType, DoorSide? requiredSide, int? requiredWidth, out string error)
        {
            error = null;
            if (roomType == null)
            {
                error = "Room type is null.";
                return new List<GameObject>();
            }

            var normalizedWidth = requiredWidth.HasValue ? NormalizeWidth(requiredWidth.Value) : 0;
            var key = (roomType, requiredSide ?? DoorSide.North, normalizedWidth);
            if (roomPrefabCache.TryGetValue(key, out var cached))
                return new List<GameObject>(cached);

            var prefabs = roomType.prefabs ?? new List<GameObject>();
            var result = new List<GameObject>();
            foreach (var prefab in prefabs)
            {
                if (prefab == null) continue;
                if (!TryGetBlueprint(prefab, out var bp, out _)) continue;
                if (!requiredSide.HasValue)
                {
                    result.Add(prefab);
                    continue;
                }

                if (bp.Sockets.Any(s => s.Side == requiredSide.Value && NormalizeWidth(s.Width) == normalizedWidth))
                    result.Add(prefab);
            }

            if (result.Count == 0 && prefabs.Count == 0)
                error = $"Room type {roomType.name} has no prefabs.";
            else if (result.Count == 0)
                error = $"No room prefabs match side {requiredSide} width {normalizedWidth} for type {roomType.name}.";

            roomPrefabCache[key] = new List<GameObject>(result);
            return result;
        }

        private bool HasOverlap(HashSet<Vector3Int> cells, HashSet<Vector3Int> occupied, HashSet<Vector3Int> allowed = null)
        {
            foreach (var c in cells)
            {
                if (allowed != null && allowed.Contains(c)) continue;
                if (occupied.Contains(c)) return true;
            }
            return false;
        }

        private bool HasConfigSpace(GameObject fixedPrefab, GameObject movingPrefab)
        {
            if (configSpaceLibrary == null || fixedPrefab == null || movingPrefab == null)
                return false;
            if (!configSpaceLibrary.TryGetSpace(fixedPrefab, movingPrefab, out var space, out _))
                return false;
            return space != null && !space.IsEmpty;
        }

        private bool FitsConfigSpace(GameObject fixedPrefab, GameObject movingPrefab, Vector3Int fixedRootCell, Vector3Int movingRootCell)
        {
            if (configSpaceLibrary == null || fixedPrefab == null || movingPrefab == null)
                return false;
            if (!configSpaceLibrary.TryGetSpace(fixedPrefab, movingPrefab, out var space, out _))
                return false;
            var delta = new Vector2Int(movingRootCell.x - fixedRootCell.x, movingRootCell.y - fixedRootCell.y);
            return space.Contains(delta);
        }

        private bool CheckTimeLimit()
        {
            if (maxDurationSeconds <= 0f) return true;
            if (Time.realtimeSinceStartup - startTime <= maxDurationSeconds)
                return true;
            var msg = $"Placement time limit exceeded. Nodes placed {placedNodes.Count}/{totalNodes}, edges placed {placedEdges.Count}/{totalEdges}.";
            if (verboseLogs)
                Debug.Log($"[MapGraphLevelSolver] {msg}");
            LastError ??= msg;
            return false;
        }

        private HashSet<Vector3Int> AllowedWidthStrip(Vector3Int anchorCell, DoorSide side, int width)
        {
            var res = new HashSet<Vector3Int>();
            int k = Mathf.Max(1, width);
            int half = k / 2;
            var perp = side.PerpendicularAxis();
            for (int w = -half; w <= half; w++)
                res.Add(anchorCell + perp * w);
            return res;
        }

        private void CommitPlacement(string nodeId, Placement placement)
        {
            placementStack.Add(placement);
            foreach (var c in placement.FloorCells) occupiedFloor.Add(c);
            foreach (var c in placement.WallCells) occupiedWall.Add(c);
            foreach (var s in placement.UsedSockets) usedSockets.Add(s);
            if (placement.EdgeKey.HasValue)
                placedEdges.Add(placement.EdgeKey.Value);
            if (!string.IsNullOrEmpty(nodeId))
                placedNodes[nodeId] = placement;
        }

        private void RollbackToDepth(int depth)
        {
            while (placementStack.Count > depth)
            {
                var p = placementStack[placementStack.Count - 1];
                RemovePlacement(p);
                placementStack.RemoveAt(placementStack.Count - 1);
            }
        }

        public void StampAll(bool disableRenderers = true)
        {
            foreach (var placement in placementStack)
            {
                if (placement.Meta == null) continue;
                stamp.StampModuleFloor(placement.Meta);
                stamp.StampModuleWalls(placement.Meta);
                if (disableRenderers)
                    stamp.DisableRenderers(placement.Meta.transform);
            }
        }

        public void DestroyPlacedInstances()
        {
            foreach (var placement in placementStack)
            {
                if (placement.Meta != null)
                    UnityEngine.Object.Destroy(placement.Meta.gameObject);
            }
            placementStack.Clear();
            placedNodes.Clear();
            placedEdges.Clear();
            occupiedFloor.Clear();
            occupiedWall.Clear();
            usedSockets.Clear();
        }

        public void Cleanup()
        {
            foreach (var p in placementStack)
            {
                if (p.Meta != null)
                    UnityEngine.Object.Destroy(p.Meta.gameObject);
            }
            placementStack.Clear();
            placedNodes.Clear();
            placedEdges.Clear();
            occupiedFloor.Clear();
            occupiedWall.Clear();
            usedSockets.Clear();
        }

        private void RemovePlacement(Placement p)
        {
            foreach (var kv in placedNodes.Where(kv => kv.Value == p).ToList())
                placedNodes.Remove(kv.Key);
            foreach (var c in p.FloorCells) occupiedFloor.Remove(c);
            foreach (var c in p.WallCells) occupiedWall.Remove(c);
            foreach (var s in p.UsedSockets) usedSockets.Remove(s);
            if (p.EdgeKey.HasValue)
                placedEdges.Remove(p.EdgeKey.Value);
            if (p.Meta != null)
                UnityEngine.Object.Destroy(p.Meta.gameObject);
        }

        private void Log(string msg)
        {
            if (verboseLogs)
                Debug.Log($"[MapGraphLevelSolver] {msg}");
            if (string.IsNullOrEmpty(LastError))
                LastError = msg;
        }

        private Transform stampWorldParent()
        {
            var gridField = stamp.GetType().GetField("grid", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (gridField == null) return null;
            var grid = gridField.GetValue(stamp) as Grid;
            return grid != null ? grid.transform : null;
        }

        private sealed class Placement
        {
            public ModuleMetaBase Meta { get; }
            public HashSet<Vector3Int> FloorCells { get; }
            public HashSet<Vector3Int> WallCells { get; }
            public List<DoorSocket> UsedSockets { get; }
            public (string,string)? EdgeKey { get; set; }
            public GameObject Prefab { get; }
            public Vector3Int RootCell { get; }

            public Placement(ModuleMetaBase meta, HashSet<Vector3Int> floors, HashSet<Vector3Int> walls, GameObject prefab, Vector3Int rootCell)
            {
                Meta = meta;
                FloorCells = floors ?? new HashSet<Vector3Int>();
                WallCells = walls ?? new HashSet<Vector3Int>();
                UsedSockets = new List<DoorSocket>();
                EdgeKey = null;
                Prefab = prefab;
                RootCell = rootCell;
            }
        }

        private sealed class GeometryCache
        {
            public List<Vector3Int> FloorOffsets { get; }
            public List<Vector3Int> WallOffsets { get; }

            public GeometryCache(List<Vector3Int> floorOffsets, List<Vector3Int> wallOffsets)
            {
                FloorOffsets = floorOffsets ?? new List<Vector3Int>();
                WallOffsets = wallOffsets ?? new List<Vector3Int>();
            }
        }

        private sealed class ModuleBlueprint
        {
            public List<Vector3Int> FloorOffsets { get; }
            public List<Vector3Int> WallOffsets { get; }
            public List<SocketInfo> Sockets { get; }

            public ModuleBlueprint(List<Vector3Int> floorOffsets, List<Vector3Int> wallOffsets, List<SocketInfo> sockets)
            {
                FloorOffsets = floorOffsets ?? new List<Vector3Int>();
                WallOffsets = wallOffsets ?? new List<Vector3Int>();
                Sockets = sockets ?? new List<SocketInfo>();
            }
        }

        private sealed class SocketInfo
        {
            public DoorSide Side { get; }
            public int Width { get; }
            public Vector3Int CellOffset { get; }

            public SocketInfo(DoorSide side, int width, Vector3Int cellOffset)
            {
                Side = side;
                Width = width;
                CellOffset = cellOffset;
            }
        }
    }
}
