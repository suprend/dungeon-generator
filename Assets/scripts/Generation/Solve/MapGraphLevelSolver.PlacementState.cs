// Assets/scripts/Generation/Graph/MapGraphLevelSolver.PlacementState.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class MapGraphLevelSolver
{
    private sealed partial class PlacementState
    {
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
        private readonly HashSet<(ModuleMetaBase owner, string spanId)> usedSocketSpans = new();
        private readonly Dictionary<GameObject, GeometryCache> geometryCache = new();
        private readonly Dictionary<GameObject, ModuleBlueprint> blueprintCache = new();
        private readonly Dictionary<(ConnectionTypeAsset conn, DoorSide side, int width), List<GameObject>> connectorPrefabCache = new();
        private readonly Dictionary<(RoomTypeAsset room, DoorSide side, int width), List<GameObject>> roomPrefabCache = new();
        private readonly HashSet<(string, string)> placedEdges = new();

        private readonly int totalNodes;
        private readonly int totalEdges;

        private readonly ShapeLibrary shapeLibrary;
        private readonly ConfigurationSpaceLibrary configSpaceLibrary;

        public PlacementState(
            TileStampService stamp,
            System.Random rng,
            IReadOnlyDictionary<string, RoomTypeAsset> nodeAssignments,
            IReadOnlyDictionary<(string, string), ConnectionTypeAsset> edgeAssignments,
            bool verboseLogs,
            Vector3Int? startCellOverride,
            float startTime,
            float maxDurationSeconds,
            ShapeLibrary shapeLibrary,
            ConfigurationSpaceLibrary configSpaceLibrary)
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

        public bool Place(List<MapGraphChainBuilder.Chain> orderedChains, MapGraphAsset graph)
        {
            return PlaceInternal(orderedChains, graph);
        }

        public bool PlaceFromLayout(MapGraphLayoutGenerator.LayoutResult layout, List<MapGraphChainBuilder.Chain> orderedChains, MapGraphAsset graph)
        {
            if (layout == null || layout.Rooms == null || layout.Rooms.Count == 0)
            {
                LastError = "Layout is empty.";
                return false;
            }

            var offset = Vector3Int.zero;
            if (startCellOverride.HasValue)
            {
                var firstRoom = layout.Rooms.Values.FirstOrDefault();
                if (firstRoom != null)
                {
                    var root = new Vector3Int(firstRoom.Root.x, firstRoom.Root.y, 0);
                    offset = startCellOverride.Value - root;
                }
            }

            if (!PreplaceLayoutRooms(layout, offset, graph))
                return false;

            return true;
        }

        private bool PlaceInternal(List<MapGraphChainBuilder.Chain> orderedChains, MapGraphAsset graph)
        {
            if (orderedChains == null || orderedChains.Count == 0)
                return true;

            if (!CheckTimeLimit())
                return false;

            if (placedNodes.Count > 0)
                return PlaceChains(orderedChains, graph, 0, 0);

            var startNode = orderedChains
                .SelectMany(c => c.Nodes)
                .FirstOrDefault(n => n != null && !string.IsNullOrEmpty(n.id) && nodeAssignments.ContainsKey(n.id));
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

            return PlaceChains(orderedChains, graph, 0, 0);
        }

        private bool PlaceChains(List<MapGraphChainBuilder.Chain> chains, MapGraphAsset graph, int chainIndex, int edgeIndex)
        {
            if (!CheckTimeLimit())
                return false;

            if (placedNodes.Count >= nodeAssignments.Count && placedEdges.Count >= edgeAssignments.Count)
                return true;

            if (chainIndex >= chains.Count)
                return placedNodes.Count >= nodeAssignments.Count && placedEdges.Count >= edgeAssignments.Count;

            var chain = chains[chainIndex];
            if (chain == null || chain.Edges == null || chain.Edges.Count == 0)
                return PlaceChains(chains, graph, chainIndex + 1, 0);

            if (placedNodes.Count >= nodeAssignments.Count && placedEdges.Count >= edgeAssignments.Count)
                return true;

            for (int i = edgeIndex; i < chain.Edges.Count; i++)
            {
                var edge = chain.Edges[i];
                if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                    continue;

                var key = MapGraphKey.NormalizeKey(edge.fromNodeId, edge.toNodeId);
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
                    placed = TryPlaceEdgeBetweenPlaced(anchorId, targetId, edge, graph, () => PlaceChains(chains, graph, chainIndex, i + 1));
                else
                    placed = TryPlaceEdge(aPlaced ? anchorId : targetId, aPlaced ? targetId : anchorId, edge, graph, () => PlaceChains(chains, graph, chainIndex, i + 1));

                if (placed)
                    return true;

                RollbackToDepth(depthBefore);
            }

            return PlaceChains(chains, graph, chainIndex + 1, 0);
        }
    }
}
