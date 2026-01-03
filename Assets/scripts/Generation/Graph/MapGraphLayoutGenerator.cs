// Assets/scripts/Generation/Graph/MapGraphLayoutGenerator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Generates planar room layouts using configuration spaces, chain decomposition, and simulated annealing.
/// Produces multiple candidate layouts via backtracking-style stack search (GenerateLayout + AddChain).
/// </summary>
public sealed partial class MapGraphLayoutGenerator
{
    public sealed class Settings
    {
        public int MaxLayoutsPerChain { get; set; } = 8;
        public int TemperatureSteps { get; set; } = 12;
        public int InnerIterations { get; set; } = 64;
        public float Cooling { get; set; } = 0.65f;
        public int MaxWiggleCandidates { get; set; } = 16;
        public int MaxFallbackCandidates { get; set; } = 128;
        public bool VerboseConfigSpaceLogs { get; set; } = false;
        public int MaxConfigSpaceLogs { get; set; } = 64;
    }

    public sealed class LayoutResult
    {
        public Dictionary<string, RoomPlacement> Rooms { get; }

        public LayoutResult(Dictionary<string, RoomPlacement> rooms)
        {
            Rooms = rooms ?? new Dictionary<string, RoomPlacement>();
        }
    }

    public sealed class RoomPlacement
    {
        public string NodeId { get; }
        public GameObject Prefab { get; }
        public ModuleShape Shape { get; }
        public Vector2Int Root { get; }

        public RoomPlacement(string nodeId, GameObject prefab, ModuleShape shape, Vector2Int root)
        {
            NodeId = nodeId;
            Prefab = prefab;
            Shape = shape;
            Root = root;
        }

        public HashSet<Vector2Int> WorldCells
        {
            get
            {
                var set = new HashSet<Vector2Int>();
                if (Shape?.FloorCells == null)
                    return set;
                foreach (var c in Shape.FloorCells)
                    set.Add(c + Root);
                return set;
            }
        }

        public HashSet<Vector2Int> WorldWallCells
        {
            get
            {
                var set = new HashSet<Vector2Int>();
                if (Shape?.WallCells == null)
                    return set;
                foreach (var c in Shape.WallCells)
                    set.Add(c + Root);
                return set;
            }
        }
    }

    private readonly System.Random rng;
    private readonly Settings settings;

    private ShapeLibrary shapeLibrary;
    private ConfigurationSpaceLibrary configSpaceLibrary;
    private MapGraphAsset graphAsset;
    private List<MapGraphChainBuilder.Chain> orderedChains;
    private Dictionary<string, List<GameObject>> roomPrefabLookup;
    private string lastFailureDetail;

    public MapGraphLayoutGenerator(int? seed = null, Settings settings = null)
    {
        rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        this.settings = settings ?? new Settings();
    }

    public bool TryGenerate(MapGraphAsset graphAsset, TileStampService stamp, out LayoutResult layout, out string error, int? maxLayoutsPerChain = null)
    {
        layout = null;
        error = null;
        if (graphAsset == null)
        {
            error = "Graph asset is null.";
            return false;
        }
        if (stamp == null)
        {
            error = "TileStampService is required for layout generation.";
            return false;
        }

        this.graphAsset = graphAsset;

        if (!MapGraphFaceBuilder.TryBuildFaces(graphAsset, out var faces, out error))
            return false;
        if (!MapGraphChainBuilder.TryBuildChains(graphAsset, faces, out var chains, out error))
            return false;
        orderedChains = chains;

        shapeLibrary = new ShapeLibrary(stamp);
        configSpaceLibrary = new ConfigurationSpaceLibrary(shapeLibrary);
        configSpaceLibrary.SetDebug(settings.VerboseConfigSpaceLogs, settings.MaxConfigSpaceLogs);

        roomPrefabLookup = BuildRoomPrefabLookup(graphAsset);
        if (roomPrefabLookup.Count == 0)
        {
            error = "No room prefabs available for layout generation.";
            return false;
        }

        var connectorPrefabs = new HashSet<GameObject>();
        foreach (var edge in graphAsset.Edges)
        {
            var conn = edge?.connectionType ?? graphAsset.DefaultConnectionType;
            if (conn?.prefabs == null) continue;
            foreach (var p in conn.prefabs)
                if (p != null) connectorPrefabs.Add(p);
        }

        foreach (var prefab in roomPrefabLookup.Values.SelectMany(x => x).Distinct())
        {
            if (!shapeLibrary.TryGetShape(prefab, out _, out error))
                return false;
        }

        var prefabList = roomPrefabLookup.Values.SelectMany(x => x).Distinct().ToList();
        for (int i = 0; i < prefabList.Count; i++)
        {
            for (int j = 0; j < prefabList.Count; j++)
            {
                if (connectorPrefabs.Contains(prefabList[i]) && connectorPrefabs.Contains(prefabList[j]))
                    continue;
                if (!configSpaceLibrary.TryGetSpace(prefabList[i], prefabList[j], out _, out error))
                    return false;
            }
        }

        lastFailureDetail = null;

        var initialLayout = new LayoutState(new Dictionary<string, RoomPlacement>(), 0);
        var stack = new Stack<LayoutState>();
        stack.Push(initialLayout);

        while (stack.Count > 0)
        {
            var state = stack.Pop();
            if (state.ChainIndex >= orderedChains.Count)
            {
                if (TryValidateGlobal(state.Rooms, out var globalError))
                {
                    layout = new LayoutResult(state.Rooms);
                    return true;
                }
                lastFailureDetail = globalError;
                continue;
            }

            var chain = orderedChains[state.ChainIndex];
            var maxLayouts = Mathf.Max(1, maxLayoutsPerChain ?? settings.MaxLayoutsPerChain);
            var expansions = AddChain(state, chain, maxLayouts);
            foreach (var exp in expansions)
            {
                stack.Push(new LayoutState(exp.Rooms, state.ChainIndex + 1));
            }
        }

        error = string.IsNullOrEmpty(lastFailureDetail)
            ? "Failed to generate layout for all chains."
            : $"Failed to generate layout for all chains. Last failure: {lastFailureDetail}";
        return false;
    }

    private sealed class LayoutState
    {
        public Dictionary<string, RoomPlacement> Rooms { get; }
        public int ChainIndex { get; }

        public LayoutState(Dictionary<string, RoomPlacement> rooms, int chainIndex)
        {
            Rooms = rooms ?? new Dictionary<string, RoomPlacement>();
            ChainIndex = chainIndex;
        }
    }
}

