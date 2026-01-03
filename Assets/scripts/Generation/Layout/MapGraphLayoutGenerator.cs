// Assets/scripts/Generation/Graph/MapGraphLayoutGenerator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Stopwatch = System.Diagnostics.Stopwatch;
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
        public bool LogConfigSpaceSizeSummary { get; set; } = false;
        public int MaxConfigSpaceSizePairs { get; set; } = 12;
        public bool LogLayoutProfiling { get; set; } = false;
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

    private sealed class LayoutProfiling
    {
        public int AddChainCalls;
        public int AddChainSmallCalls;
        public int CandidateLayoutsAccepted;

        public int StackPops;
        public int StackPushes;
        public int MaxStackDepth;

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
        var tryGenerateStart = NowTicks();
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
        profiling = settings.LogLayoutProfiling ? new LayoutProfiling() : null;

        if (precomputedChains != null)
        {
            orderedChains = precomputedChains;
        }
        else
        {
            var facesChainsStart = profiling != null ? NowTicks() : 0;
            var prevFaceBuilderDebug = MapGraphFaceBuilder.LogProfiling;
            MapGraphFaceBuilder.SetDebug(settings.LogLayoutProfiling);
            if (!MapGraphFaceBuilder.TryBuildFaces(graphAsset, out var faces, out error))
            {
                MapGraphFaceBuilder.SetDebug(prevFaceBuilderDebug);
                return false;
            }
            if (!MapGraphChainBuilder.TryBuildChains(graphAsset, faces, out var chains, out error))
            {
                MapGraphFaceBuilder.SetDebug(prevFaceBuilderDebug);
                return false;
            }
            MapGraphFaceBuilder.SetDebug(prevFaceBuilderDebug);
            if (profiling != null)
                profiling.FacesChainsTicks += NowTicks() - facesChainsStart;
            orderedChains = chains;
        }

        orderedChains ??= new List<MapGraphChainBuilder.Chain>();

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

        var warmupShapesStart = profiling != null ? NowTicks() : 0;
        foreach (var prefab in roomPrefabLookup.Values.SelectMany(x => x).Distinct())
        {
            if (!shapeLibrary.TryGetShape(prefab, out _, out error))
                return false;
        }
        if (profiling != null)
            profiling.WarmupShapesTicks += NowTicks() - warmupShapesStart;

        var prefabList = roomPrefabLookup.Values.SelectMany(x => x).Distinct().ToList();
        if (settings.LogConfigSpaceSizeSummary)
        {
            var shapeCount = 0;
            var floorMin = int.MaxValue;
            var floorMax = 0;
            long floorSum = 0;
            var wallMin = int.MaxValue;
            var wallMax = 0;
            long wallSum = 0;

            var topShapes = new List<(int total, int floor, int wall, string name)>();
            const int maxTopShapes = 8;

            foreach (var prefab in prefabList)
            {
                if (prefab == null)
                    continue;
                if (!shapeLibrary.TryGetShape(prefab, out var shape, out _))
                    continue;

                var floor = shape?.FloorCells?.Count ?? 0;
                var wall = shape?.WallCells?.Count ?? 0;
                var total = floor + wall;
                shapeCount++;
                floorMin = Mathf.Min(floorMin, floor);
                floorMax = Mathf.Max(floorMax, floor);
                floorSum += floor;
                wallMin = Mathf.Min(wallMin, wall);
                wallMax = Mathf.Max(wallMax, wall);
                wallSum += wall;

                if (topShapes.Count < maxTopShapes)
                {
                    topShapes.Add((total, floor, wall, prefab.name));
                    topShapes.Sort((a, b) => b.total.CompareTo(a.total));
                }
                else if (total > topShapes[topShapes.Count - 1].total)
                {
                    topShapes[topShapes.Count - 1] = (total, floor, wall, prefab.name);
                    topShapes.Sort((a, b) => b.total.CompareTo(a.total));
                }
            }

            if (shapeCount > 0)
            {
                var floorAvg = floorSum / (float)shapeCount;
                var wallAvg = wallSum / (float)shapeCount;
                var top = topShapes.Count > 0
                    ? string.Join(", ", topShapes.Select(s => $"{s.name}:{s.total} (f={s.floor},w={s.wall})"))
                    : "<none>";
                Debug.Log($"[LayoutGenerator] Shape sizes: prefabs={shapeCount} floor[min={floorMin} max={floorMax} avg={floorAvg:0.0}] wall[min={wallMin} max={wallMax} avg={wallAvg:0.0}] top=[{top}]");
            }
        }

        var csPairs = 0;
        var csEmpty = 0;
        var csMin = int.MaxValue;
        var csMax = 0;
        long csSum = 0;
        var csNonEmptyPairs = 0;
        long csNonEmptySum = 0;
        var topPairs = settings.LogConfigSpaceSizeSummary
            ? new List<(int count, string fixedName, string movingName)>()
            : null;
        var maxTop = Mathf.Clamp(settings.MaxConfigSpaceSizePairs, 0, 64);
        var warmupCsStart = profiling != null ? NowTicks() : 0;
        for (int i = 0; i < prefabList.Count; i++)
        {
            for (int j = 0; j < prefabList.Count; j++)
            {
                if (connectorPrefabs.Contains(prefabList[i]) && connectorPrefabs.Contains(prefabList[j]))
                    continue;
                if (!configSpaceLibrary.TryGetSpace(prefabList[i], prefabList[j], out var space, out error))
                    return false;

                if (settings.LogConfigSpaceSizeSummary && space != null)
                {
                    var count = space.Offsets != null ? space.Offsets.Count : 0;
                    csPairs++;
                    csSum += count;
                    csMin = Mathf.Min(csMin, count);
                    csMax = Mathf.Max(csMax, count);
                    if (count == 0) csEmpty++;
                    else
                    {
                        csNonEmptyPairs++;
                        csNonEmptySum += count;
                    }

                    if (maxTop > 0)
                    {
                        var fixedName = prefabList[i] != null ? prefabList[i].name : "<null>";
                        var movingName = prefabList[j] != null ? prefabList[j].name : "<null>";
                        if (topPairs.Count < maxTop)
                        {
                            topPairs.Add((count, fixedName, movingName));
                            topPairs.Sort((a, b) => b.count.CompareTo(a.count));
                        }
                        else if (count > topPairs[topPairs.Count - 1].count)
                        {
                            topPairs[topPairs.Count - 1] = (count, fixedName, movingName);
                            topPairs.Sort((a, b) => b.count.CompareTo(a.count));
                        }
                    }
                }
            }
        }
        if (profiling != null)
            profiling.WarmupConfigSpacesTicks += NowTicks() - warmupCsStart;

        if (settings.LogConfigSpaceSizeSummary && csPairs > 0)
        {
            var avg = csSum / (float)csPairs;
            var avgNonEmpty = csNonEmptyPairs > 0 ? csNonEmptySum / (float)csNonEmptyPairs : 0f;
            var top = topPairs != null && topPairs.Count > 0
                ? string.Join(", ", topPairs.Select(p => $"{p.fixedName}->{p.movingName}:{p.count}"))
                : "<none>";
            Debug.Log($"[LayoutGenerator] ConfigSpace sizes: pairs={csPairs} empty={csEmpty} nonEmpty={csNonEmptyPairs} min={csMin} max={csMax} avg={avg:0.0} avgNonEmpty={avgNonEmpty:0.0} top=[{top}]");
        }

        lastFailureDetail = null;

        var initialLayout = new LayoutState(new Dictionary<string, RoomPlacement>(), 0, energyCache: null);
        var stack = new Stack<LayoutState>();
        stack.Push(initialLayout);
        if (profiling != null)
        {
            profiling.StackPushes++;
            profiling.MaxStackDepth = Mathf.Max(profiling.MaxStackDepth, stack.Count);
        }

        while (stack.Count > 0)
        {
            var state = stack.Pop();
            if (profiling != null)
                profiling.StackPops++;
            if (state.ChainIndex >= orderedChains.Count)
            {
                if (TryValidateGlobal(state.Rooms, out var globalError))
                {
                    layout = new LayoutResult(state.Rooms);
                    if (profiling != null)
                    {
                        profiling.TotalTryGenerateTicks = NowTicks() - tryGenerateStart;
                        LogProfilingSummary(profiling);
                    }
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
                stack.Push(exp.WithChainIndex(state.ChainIndex + 1));
                if (profiling != null)
                {
                    profiling.StackPushes++;
                    profiling.MaxStackDepth = Mathf.Max(profiling.MaxStackDepth, stack.Count);
                }
            }
        }

        error = string.IsNullOrEmpty(lastFailureDetail)
            ? "Failed to generate layout for all chains."
            : $"Failed to generate layout for all chains. Last failure: {lastFailureDetail}";

        if (profiling != null)
        {
            profiling.TotalTryGenerateTicks = NowTicks() - tryGenerateStart;
            LogProfilingSummary(profiling);
        }
        return false;
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
            $"wiggleMs={wiggleMs:0.0} (calls={p.WiggleCandidatesCalls})");
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
