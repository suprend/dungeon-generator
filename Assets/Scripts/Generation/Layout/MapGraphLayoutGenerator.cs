// Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Generates planar room layouts using configuration spaces, chain decomposition, and simulated annealing.
/// Produces multiple candidate layouts via backtracking-style stack search (GenerateLayout + AddChain).
/// </summary>
public sealed partial class MapGraphLayoutGenerator
{
    private static Dictionary<string, RoomPlacement> CloneRoomsDeep(Dictionary<string, RoomPlacement> rooms)
    {
        if (rooms == null || rooms.Count == 0)
            return new Dictionary<string, RoomPlacement>();

        var clone = new Dictionary<string, RoomPlacement>(rooms.Count);
        foreach (var kv in rooms)
        {
            var p = kv.Value;
            if (p == null)
                continue;
            clone[kv.Key] = new RoomPlacement(p.NodeId, p.Prefab, p.Shape, p.Root);
        }
        return clone;
    }

    public sealed class Settings
    {
        public int MaxLayoutsPerChain { get; set; } = 8;
        public int TemperatureSteps { get; set; } = 12;
        public int InnerIterations { get; set; } = 64;
        public float Cooling { get; set; } = 0.65f;
        public float ChangePrefabProbability { get; set; } = 0.35f;
        public int MaxWiggleCandidates { get; set; } = 16;
        public float WiggleProbability { get; set; } = 0.95f;
        public int MaxFallbackCandidates { get; set; } = 128;
        public bool VerboseConfigSpaceLogs { get; set; } = false;
        public int MaxConfigSpaceLogs { get; set; } = 64;
        public bool LogConfigSpaceSizeSummary { get; set; } = false;
        public int MaxConfigSpaceSizePairs { get; set; } = 12;
        public bool LogLayoutProfiling { get; set; } = false;

        // Optional: use bitset-based overlap counting in IntersectionPenaltyFast (keeps HashSet fallback).
        public bool UseBitsetOverlap { get; set; } = true;

        // When no layouts are produced for a chain, emit diagnostics about the best (lowest-energy) state encountered.
        public bool DebugNoLayouts { get; set; } = false;
        public int DebugNoLayoutsTopPairs { get; set; } = 6;
        public int DebugNoLayoutsTopEdges { get; set; } = 16;
        public bool DebugEnergyMismatch { get; set; } = true;
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
        public GameObject Prefab { get; set; }
        public ModuleShape Shape { get; set; }
        public Vector2Int Root { get; set; }

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

    private static readonly Dictionary<(Grid grid, Tilemap floor, Tilemap wall), ShapeLibrary> ShapeLibrariesByStamp = new();
    private static readonly Dictionary<(Grid grid, Tilemap floor, Tilemap wall), ConfigurationSpaceLibrary> ConfigSpaceLibrariesByStamp = new();

    private ShapeLibrary shapeLibrary;
    private ConfigurationSpaceLibrary configSpaceLibrary;
    private MapGraphAsset graphAsset;
    private List<MapGraphChainBuilder.Chain> orderedChains;
    private Dictionary<string, List<GameObject>> roomPrefabLookup;
    private Dictionary<string, MapGraphAsset.NodeData> nodeById;
    private Dictionary<RoomTypeAsset, List<GameObject>> prefabsByRoomType;
    private Dictionary<string, HashSet<string>> neighborLookup;
    private Dictionary<string, int> nodeIndexById;
    private string[] nodeIdByIndex;
    private int[][] neighborIndicesByIndex;
    private HashSet<GameObject> connectorPrefabs;
    private string lastFailureDetail;

    private sealed class LayoutProfiling
    {
        public int AddChainCalls;
        public int AddChainSmallCalls;
        public int CandidateLayoutsAccepted;

        public int StackPops;
        public int StackPushes;
        public int MaxStackDepth;

        public int InitLayoutNodesScored;
        public int InitLayoutCandidatesGenerated;
        public int InitLayoutCandidatesScored;

        public int FindPositionCandidatesCalls;
        public long FindPositionCandidatesTicks;
        public int WiggleCandidatesCalls;
        public long WiggleCandidatesTicks;

        public int IsValidLayoutCalls;
        public long IsValidLayoutTicks;

        public long FacesChainsTicks;
        public long WarmupShapesTicks;
        public long WarmupConfigSpacesTicks;

        public long GetInitialLayoutTicks;
        public long SaLoopTicks;
        public long TotalTryGenerateTicks;
    }

    private LayoutProfiling profiling;

    private static long NowTicks() => Stopwatch.GetTimestamp();
    private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    public MapGraphLayoutGenerator(int? seed = null, Settings settings = null)
    {
        rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        this.settings = settings ?? new Settings();
    }

    public bool TryGenerate(
        MapGraphAsset graphAsset,
        TileStampService stamp,
        out LayoutResult layout,
        out string error,
        int? maxLayoutsPerChain = null,
        List<MapGraphChainBuilder.Chain> precomputedChains = null)
    {
        using var _ps = PS(S_TryGenerate);
        var tryGenerateStart = NowTicks();
        layout = null;
        error = null;

        this.graphAsset = graphAsset;
        profiling = settings.LogLayoutProfiling ? new LayoutProfiling() : null;
        if (!TryBuildLayoutContext(graphAsset, stamp, precomputedChains, out var ctx, out error))
            return false;

        orderedChains = ctx.OrderedChains;
        shapeLibrary = ctx.ShapeLibrary;
        configSpaceLibrary = ctx.ConfigSpaceLibrary;
        roomPrefabLookup = ctx.RoomPrefabLookup;
        nodeById = ctx.NodeById;
        prefabsByRoomType = ctx.PrefabsByRoomType;
        connectorPrefabs = ctx.ConnectorPrefabs;
        neighborLookup = ctx.NeighborLookup;
        nodeIndexById = ctx.NodeIndexById;
        nodeIdByIndex = ctx.NodeIdByIndex;
        neighborIndicesByIndex = ctx.NeighborIndicesByIndex;

        return TryRunStackSearch(ctx, maxLayoutsPerChain, out layout, out error, tryGenerateStart);
    }

    private static Dictionary<string, HashSet<string>> BuildNeighborLookup(MapGraphAsset graphAsset)
    {
        var map = new Dictionary<string, HashSet<string>>();
        if (graphAsset?.Edges == null)
            return map;

        foreach (var e in graphAsset.Edges)
        {
            if (e == null || string.IsNullOrEmpty(e.fromNodeId) || string.IsNullOrEmpty(e.toNodeId))
                continue;

            if (!map.TryGetValue(e.fromNodeId, out var from))
                map[e.fromNodeId] = from = new HashSet<string>();
            if (!map.TryGetValue(e.toNodeId, out var to))
                map[e.toNodeId] = to = new HashSet<string>();

            from.Add(e.toNodeId);
            to.Add(e.fromNodeId);
        }

        return map;
    }

    private void BuildNodeIndexAndAdjacency(MapGraphAsset graphAsset)
    {
        var idSet = new HashSet<string>();
        if (graphAsset?.Nodes != null)
        {
            foreach (var n in graphAsset.Nodes)
            {
                if (!string.IsNullOrEmpty(n?.id))
                    idSet.Add(n.id);
            }
        }
        if (graphAsset?.Edges != null)
        {
            foreach (var e in graphAsset.Edges)
            {
                if (!string.IsNullOrEmpty(e?.fromNodeId))
                    idSet.Add(e.fromNodeId);
                if (!string.IsNullOrEmpty(e?.toNodeId))
                    idSet.Add(e.toNodeId);
            }
        }

        var ids = idSet.ToList();
        ids.Sort(StringComparer.Ordinal);
        nodeIdByIndex = ids.ToArray();
        nodeIndexById = new Dictionary<string, int>(nodeIdByIndex.Length);
        for (var i = 0; i < nodeIdByIndex.Length; i++)
            nodeIndexById[nodeIdByIndex[i]] = i;

        neighborIndicesByIndex = new int[nodeIdByIndex.Length][];
        if (neighborLookup == null)
            return;

        for (var i = 0; i < nodeIdByIndex.Length; i++)
        {
            var id = nodeIdByIndex[i];
            if (!neighborLookup.TryGetValue(id, out var neigh) || neigh == null || neigh.Count == 0)
            {
                neighborIndicesByIndex[i] = Array.Empty<int>();
                continue;
            }

            var tmp = new List<int>(neigh.Count);
            foreach (var otherId in neigh)
            {
                if (string.IsNullOrEmpty(otherId))
                    continue;
                if (nodeIndexById.TryGetValue(otherId, out var j))
                    tmp.Add(j);
            }
            neighborIndicesByIndex[i] = tmp.ToArray();
        }
    }

    private void LogProfilingSummary(LayoutProfiling p)
    {
        if (p == null)
            return;

        var totalMs = TicksToMs(p.TotalTryGenerateTicks);
        var findMs = TicksToMs(p.FindPositionCandidatesTicks);
        var wiggleMs = TicksToMs(p.WiggleCandidatesTicks);
        var validMs = TicksToMs(p.IsValidLayoutTicks);
        var facesChainsMs = TicksToMs(p.FacesChainsTicks);
        var warmupShapesMs = TicksToMs(p.WarmupShapesTicks);
        var warmupCsMs = TicksToMs(p.WarmupConfigSpacesTicks);
        var initMs = TicksToMs(p.GetInitialLayoutTicks);
        var saMs = TicksToMs(p.SaLoopTicks);

        Debug.Log(
            $"[LayoutGenerator][prof] totalMs={totalMs:0.0} " +
            $"pops={p.StackPops} pushes={p.StackPushes} maxStack={p.MaxStackDepth} " +
            $"addChain={p.AddChainCalls} addChainSmall={p.AddChainSmallCalls} accepted={p.CandidateLayoutsAccepted} " +
            $"facesChainsMs={facesChainsMs:0.0} warmupShapesMs={warmupShapesMs:0.0} warmupCsMs={warmupCsMs:0.0} " +
            $"getInitialMs={initMs:0.0} saLoopMs={saMs:0.0} isValidMs={validMs:0.0} (calls={p.IsValidLayoutCalls}) " +
            $"findPosMs={findMs:0.0} (calls={p.FindPositionCandidatesCalls}) " +
            $"wiggleMs={wiggleMs:0.0} (calls={p.WiggleCandidatesCalls}) " +
            $"initNodes={p.InitLayoutNodesScored} initCandidatesGen={p.InitLayoutCandidatesGenerated} initCandidatesScored={p.InitLayoutCandidatesScored}");
    }

    private sealed class LayoutState
    {
        public Dictionary<string, RoomPlacement> Rooms { get; }
        public int ChainIndex { get; }
        public EnergyCache EnergyCache { get; }

        public LayoutState(Dictionary<string, RoomPlacement> rooms, int chainIndex, EnergyCache energyCache)
        {
            Rooms = rooms ?? new Dictionary<string, RoomPlacement>();
            ChainIndex = chainIndex;
            EnergyCache = energyCache;
        }

        public LayoutState WithChainIndex(int chainIndex)
        {
            return new LayoutState(Rooms, chainIndex, EnergyCache);
        }
    }
}
