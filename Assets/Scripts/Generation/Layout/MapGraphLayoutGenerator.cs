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

        // Performance: avoid enumerating huge candidate sets in SA inner loops by sampling random offsets
        // and verifying them against neighbor spaces (rejection sampling).
        public bool UseRejectionSamplingCandidates { get; set; } = true;

        // Performance: cap overlap penalty contribution per pair during SA energy evaluation.
        // 0 = compute exact overlaps. Lower values are faster but less accurate (strict validation is unchanged).
        public int OverlapPenaltyCap { get; set; } = 0;

        // Extra headroom used when OverlapPenaltyCap is enabled and we still want exact energy for small overlaps.
        public int OverlapPenaltyCapSlack { get; set; } = 64;

        // Performance: pick SA perturbation targets preferentially from nodes that currently contribute
        // most overlap/edge penalty (tournament selection), with some exploration to avoid local minima.
        public bool UseConflictDrivenTargetSelection { get; set; } = true;
        public int TargetSelectionTournamentK { get; set; } = 4;
        public float TargetSelectionExplorationProbability { get; set; } = 0.15f;

        // Heuristic: for graph bridges/articulation points, prefer placing critical branches farther away from the current cluster.
        public bool UseBridgeExpansionBias { get; set; } = false;

        // Heuristic: for cycle-chains, keep the two open ends at a distance that remains closable
        // for the remaining unplaced nodes.
        public bool UseCycleClosureBias { get; set; } = true;

        // Geometry rule: if enabled, wall↔wall overlaps are treated as illegal in layout energy,
        // strict validation, and placement occupancy.
        public bool DisallowWallWallOverlap { get; set; } = false;

        // Internal: absolute realtime deadline for the current layout-generation attempt.
        // <= 0 means no deadline.
        public float LayoutDeadlineRealtime { get; set; } = 0f;

        // When no layouts are produced for a chain, emit diagnostics about the best (lowest-energy) state encountered.
        public bool DebugNoLayouts { get; set; } = false;
        public int DebugNoLayoutsTopPairs { get; set; } = 6;
        public int DebugNoLayoutsTopEdges { get; set; } = 16;
        public bool DebugEnergyMismatch { get; set; } = true;

        public Settings Clone()
        {
            return new Settings
            {
                MaxLayoutsPerChain = MaxLayoutsPerChain,
                TemperatureSteps = TemperatureSteps,
                InnerIterations = InnerIterations,
                Cooling = Cooling,
                ChangePrefabProbability = ChangePrefabProbability,
                MaxWiggleCandidates = MaxWiggleCandidates,
                WiggleProbability = WiggleProbability,
                MaxFallbackCandidates = MaxFallbackCandidates,
                VerboseConfigSpaceLogs = VerboseConfigSpaceLogs,
                MaxConfigSpaceLogs = MaxConfigSpaceLogs,
                LogConfigSpaceSizeSummary = LogConfigSpaceSizeSummary,
                MaxConfigSpaceSizePairs = MaxConfigSpaceSizePairs,
                LogLayoutProfiling = LogLayoutProfiling,
                UseBitsetOverlap = UseBitsetOverlap,
                UseRejectionSamplingCandidates = UseRejectionSamplingCandidates,
                OverlapPenaltyCap = OverlapPenaltyCap,
                OverlapPenaltyCapSlack = OverlapPenaltyCapSlack,
                UseConflictDrivenTargetSelection = UseConflictDrivenTargetSelection,
                TargetSelectionTournamentK = TargetSelectionTournamentK,
                TargetSelectionExplorationProbability = TargetSelectionExplorationProbability,
                UseBridgeExpansionBias = UseBridgeExpansionBias,
                UseCycleClosureBias = UseCycleClosureBias,
                DisallowWallWallOverlap = DisallowWallWallOverlap,
                LayoutDeadlineRealtime = LayoutDeadlineRealtime,
                DebugNoLayouts = DebugNoLayouts,
                DebugNoLayoutsTopPairs = DebugNoLayoutsTopPairs,
                DebugNoLayoutsTopEdges = DebugNoLayoutsTopEdges,
                DebugEnergyMismatch = DebugEnergyMismatch
            };
        }
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
    private bool annealingActive;

    private static readonly Dictionary<(Grid grid, Tilemap floor, Tilemap wall), ShapeLibrary> ShapeLibrariesByStamp = new();
    private static readonly Dictionary<(Grid grid, Tilemap floor, Tilemap wall), ConfigurationSpaceLibrary> ConfigSpaceLibrariesByStamp = new();

    private ShapeLibrary shapeLibrary;
    private ConfigurationSpaceLibrary configSpaceLibrary;
    private MapGraphAsset graphAsset;
    private List<MapGraphChainBuilder.Chain> orderedChains;
    private Dictionary<(string, string), BridgeInfo> bridgeInfoByEdge;
    private Dictionary<(string, string), CycleEdgeGapStats> cycleEdgeGapStatsByEdge;
    private Dictionary<string, NodeTopologyInfo> nodeTopologyInfoById;
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

    private readonly struct CycleEdgeGapStats
    {
        public float MinStep { get; }
        public float PreferredStep { get; }
        public float MaxStep { get; }

        public CycleEdgeGapStats(float minStep, float preferredStep, float maxStep)
        {
            MinStep = Mathf.Max(1f, minStep);
            PreferredStep = Mathf.Clamp(preferredStep, MinStep, Mathf.Max(MinStep, maxStep));
            MaxStep = Mathf.Max(PreferredStep, maxStep);
        }
    }

    private readonly struct BridgeInfo
    {
        public string FromNodeId { get; }
        public string ToNodeId { get; }
        public int FromSideSize { get; }
        public int ToSideSize { get; }

        public BridgeInfo(string fromNodeId, string toNodeId, int fromSideSize, int toSideSize)
        {
            FromNodeId = fromNodeId;
            ToNodeId = toNodeId;
            FromSideSize = fromSideSize;
            ToSideSize = toSideSize;
        }

        public int GetSideSize(string nodeId)
        {
            if (string.Equals(nodeId, FromNodeId, StringComparison.Ordinal))
                return FromSideSize;
            if (string.Equals(nodeId, ToNodeId, StringComparison.Ordinal))
                return ToSideSize;
            return 0;
        }
    }

    private readonly struct NodeTopologyInfo
    {
        public bool IsArticulation { get; }
        public int SplitComponentCount { get; }
        public int MaxSeparatedComponentSize { get; }
        public int IncidentBridgeCount { get; }
        public int MaxIncidentBridgeComponentSize { get; }
        public float Priority { get; }

        public NodeTopologyInfo(
            bool isArticulation,
            int splitComponentCount,
            int maxSeparatedComponentSize,
            int incidentBridgeCount,
            int maxIncidentBridgeComponentSize)
        {
            IsArticulation = isArticulation;
            SplitComponentCount = splitComponentCount;
            MaxSeparatedComponentSize = maxSeparatedComponentSize;
            IncidentBridgeCount = incidentBridgeCount;
            MaxIncidentBridgeComponentSize = maxIncidentBridgeComponentSize;
            Priority =
                (isArticulation ? 4f : 0f) +
                Mathf.Max(0, splitComponentCount - 1) * 2f +
                maxSeparatedComponentSize * 0.2f +
                incidentBridgeCount * 1.5f +
                maxIncidentBridgeComponentSize * 0.1f;
        }

        public NodeTopologyInfo WithBridgeStats(int incidentBridgeCount, int maxIncidentBridgeComponentSize)
        {
            return new NodeTopologyInfo(
                IsArticulation,
                SplitComponentCount,
                MaxSeparatedComponentSize,
                incidentBridgeCount,
                maxIncidentBridgeComponentSize);
        }
    }

    private static long NowTicks() => Stopwatch.GetTimestamp();
    private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private bool CheckLayoutDeadline(string scope)
    {
        if (settings == null || settings.LayoutDeadlineRealtime <= 0f)
            return true;
        if (Time.realtimeSinceStartup <= settings.LayoutDeadlineRealtime)
            return true;

        lastFailureDetail = string.IsNullOrEmpty(scope)
            ? "Layout time limit exceeded during layout generation."
            : $"Layout time limit exceeded during {scope}.";
        return false;
    }

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
        bridgeInfoByEdge = ctx.BridgeInfoByEdge;
        cycleEdgeGapStatsByEdge = ctx.CycleEdgeGapStatsByEdge;
        nodeTopologyInfoById = ctx.NodeTopologyInfoById;
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

    private static void BuildTopologyInfo(
        MapGraphAsset graphAsset,
        out Dictionary<(string, string), BridgeInfo> bridgeInfoByEdge,
        out Dictionary<string, NodeTopologyInfo> nodeTopologyInfoById)
    {
        var result = new Dictionary<(string, string), BridgeInfo>();
        var nodeInfo = new Dictionary<string, NodeTopologyInfo>();
        if (graphAsset == null)
        {
            bridgeInfoByEdge = result;
            nodeTopologyInfoById = nodeInfo;
            return;
        }

        var adjacency = new Dictionary<string, List<string>>();
        void EnsureNode(string id)
        {
            if (string.IsNullOrEmpty(id) || adjacency.ContainsKey(id))
                return;
            adjacency[id] = new List<string>();
            nodeInfo[id] = default;
        }

        if (graphAsset.Nodes != null)
        {
            foreach (var node in graphAsset.Nodes)
                EnsureNode(node?.id);
        }

        if (graphAsset.Edges != null)
        {
            foreach (var edge in graphAsset.Edges)
            {
                if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                    continue;

                EnsureNode(edge.fromNodeId);
                EnsureNode(edge.toNodeId);
                adjacency[edge.fromNodeId].Add(edge.toNodeId);
                adjacency[edge.toNodeId].Add(edge.fromNodeId);
            }
        }

        var discovered = new Dictionary<string, int>(adjacency.Count);
        var low = new Dictionary<string, int>(adjacency.Count);
        var subtreeSize = new Dictionary<string, int>(adjacency.Count);
        var componentVisited = new HashSet<string>();
        var time = 0;

        void DfsBridge(string nodeId, string parentId, int componentSize)
        {
            time++;
            discovered[nodeId] = time;
            low[nodeId] = time;
            subtreeSize[nodeId] = 1;
            var childCount = 0;
            var separatedSizes = new List<int>();

            if (!adjacency.TryGetValue(nodeId, out var neighbors))
                return;

            for (var i = 0; i < neighbors.Count; i++)
            {
                var otherId = neighbors[i];
                if (string.Equals(otherId, parentId, StringComparison.Ordinal))
                    continue;

                if (!discovered.ContainsKey(otherId))
                {
                    childCount++;
                    DfsBridge(otherId, nodeId, componentSize);
                    subtreeSize[nodeId] += subtreeSize[otherId];
                    low[nodeId] = Mathf.Min(low[nodeId], low[otherId]);

                    if (low[otherId] >= discovered[nodeId])
                        separatedSizes.Add(subtreeSize[otherId]);

                    if (low[otherId] > discovered[nodeId])
                    {
                        var key = MapGraphKey.NormalizeKey(nodeId, otherId);
                        result[key] = new BridgeInfo(
                            nodeId,
                            otherId,
                            Mathf.Max(1, componentSize - subtreeSize[otherId]),
                            Mathf.Max(1, subtreeSize[otherId]));
                    }
                }
                else
                {
                    low[nodeId] = Mathf.Min(low[nodeId], discovered[otherId]);
                }
            }

            var isRoot = string.IsNullOrEmpty(parentId);
            var isArticulation = isRoot ? childCount > 1 : separatedSizes.Count > 0;
            if (!isArticulation)
                return;

            var separatedTotal = 0;
            var maxSeparated = 0;
            for (var i = 0; i < separatedSizes.Count; i++)
            {
                var size = separatedSizes[i];
                separatedTotal += size;
                if (size > maxSeparated)
                    maxSeparated = size;
            }

            var remainder = Mathf.Max(0, componentSize - 1 - separatedTotal);
            if (remainder > 0)
                maxSeparated = Mathf.Max(maxSeparated, remainder);

            var splitComponentCount = separatedSizes.Count + (remainder > 0 ? 1 : 0);
            if (isRoot && splitComponentCount == 0 && childCount > 1)
                splitComponentCount = childCount;

            nodeInfo[nodeId] = new NodeTopologyInfo(
                true,
                Mathf.Max(2, splitComponentCount),
                Mathf.Max(1, maxSeparated),
                0,
                0);
        }

        foreach (var nodeId in adjacency.Keys)
        {
            if (componentVisited.Contains(nodeId))
                continue;

            var stack = new Stack<string>();
            var componentNodes = new List<string>();
            stack.Push(nodeId);
            componentVisited.Add(nodeId);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                componentNodes.Add(current);
                if (!adjacency.TryGetValue(current, out var neighbors))
                    continue;
                for (var i = 0; i < neighbors.Count; i++)
                {
                    var otherId = neighbors[i];
                    if (componentVisited.Add(otherId))
                        stack.Push(otherId);
                }
            }

            var componentSize = componentNodes.Count;
            for (var i = 0; i < componentNodes.Count; i++)
            {
                var startNodeId = componentNodes[i];
                if (!discovered.ContainsKey(startNodeId))
                    DfsBridge(startNodeId, null, componentSize);
            }
        }

        foreach (var kv in result)
        {
            var bridge = kv.Value;

            if (!nodeInfo.TryGetValue(bridge.FromNodeId, out var fromInfo))
                fromInfo = default;
            nodeInfo[bridge.FromNodeId] = fromInfo.WithBridgeStats(
                fromInfo.IncidentBridgeCount + 1,
                Mathf.Max(fromInfo.MaxIncidentBridgeComponentSize, bridge.ToSideSize));

            if (!nodeInfo.TryGetValue(bridge.ToNodeId, out var toInfo))
                toInfo = default;
            nodeInfo[bridge.ToNodeId] = toInfo.WithBridgeStats(
                toInfo.IncidentBridgeCount + 1,
                Mathf.Max(toInfo.MaxIncidentBridgeComponentSize, bridge.FromSideSize));
        }

        bridgeInfoByEdge = result;
        nodeTopologyInfoById = nodeInfo;
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
