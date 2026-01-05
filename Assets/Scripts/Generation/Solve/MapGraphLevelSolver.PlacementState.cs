// Assets/Scripts/Generation/Solve/MapGraphLevelSolver.PlacementState.cs
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
        private readonly bool verboseLogs;
        private readonly Vector3Int? startCellOverride;
        private readonly float startTime;
        private readonly float maxDurationSeconds;
        public string LastError { get; private set; }

        private readonly Dictionary<string, Placement> placedNodes = new();
        private readonly List<Placement> placementStack = new();
        private readonly HashSet<Vector3Int> occupiedFloor = new();
        private readonly HashSet<Vector3Int> occupiedWall = new();
        private readonly Dictionary<GameObject, GeometryCache> geometryCache = new();
        private readonly Dictionary<GameObject, ModuleBlueprint> blueprintCache = new();
        private readonly Dictionary<(ConnectionTypeAsset conn, DoorSide side, int width), List<GameObject>> connectorPrefabCache = new();
        private readonly Dictionary<(RoomTypeAsset room, DoorSide side, int width), List<GameObject>> roomPrefabCache = new();

        private readonly int totalNodes;

        private readonly ShapeLibrary shapeLibrary;
        private readonly ConfigurationSpaceLibrary configSpaceLibrary;

        public PlacementState(
            TileStampService stamp,
            System.Random rng,
            bool verboseLogs,
            Vector3Int? startCellOverride,
            float startTime,
            float maxDurationSeconds,
            int totalNodes,
            ShapeLibrary shapeLibrary,
            ConfigurationSpaceLibrary configSpaceLibrary)
        {
            this.stamp = stamp;
            this.rng = rng;
            this.verboseLogs = verboseLogs;
            this.startCellOverride = startCellOverride;
            this.startTime = startTime;
            this.maxDurationSeconds = maxDurationSeconds;
            this.shapeLibrary = shapeLibrary;
            this.configSpaceLibrary = configSpaceLibrary;
            this.totalNodes = Mathf.Max(0, totalNodes);
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
    }
}
