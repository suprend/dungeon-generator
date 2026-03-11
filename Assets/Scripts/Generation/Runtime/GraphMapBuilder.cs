// Assets/Scripts/Generation/Runtime/GraphMapBuilder.cs
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

/// <summary>
/// Thin wrapper to run MapGraphLevelSolver with prefab placement and stamping.
/// </summary>
public class GraphMapBuilder : MonoBehaviour
{
    [Header("Graph")]
    public MapGraphAsset graph;

    [Header("Global Tilemap layers")]
    public Grid targetGrid;
    public Tilemap floorMap;
    public Tilemap wallMap;

    [Header("Execution")]
    public bool runOnStart = true;
    public bool clearOnRun = true;
    public int randomSeed = 0; // 0 = random
    public bool verboseLogs = false;
    [FormerlySerializedAs("placementTimeLimitSeconds")]
    [Tooltip("Time limit for one layout-generation try (SA/stack search), in seconds. 0 = unlimited.")]
    public float layoutTimeLimitSeconds = 5f;
    [Tooltip("Destroy placed prefab instances after stamping (keeps only tiles).")]
    public bool destroyPlacedInstances = true;

    [Header("Layout Generator (SA)")]
    public int maxLayoutsPerChain = 8;
    public int temperatureSteps = 24;
    public int innerIterations = 128;
    [Range(0.01f, 0.999f)]
    public float cooling = 0.65f;
    [Range(0f, 1f)]
    [Tooltip("Probability of changing a node prefab during SA perturbation (higher explores more, lower is faster/more stable).")]
    public float changePrefabProbability = 0.35f;
    public int maxWiggleCandidates = 16;
    public int maxFallbackCandidates = 256;
    [FormerlySerializedAs("layoutAttempts")]
    [Tooltip("How many layout seeds to try. Each retry uses seed+attemptIndex; with Seed=0 the base seed is random.")]
    public int layoutRetries = 1;
    [Tooltip("Log configuration space (CS) offsets counts for prefab pairs during layout generation.")]
    public bool logConfigSpaceSizeSummary = false;
    [Tooltip("How many largest CS pairs to include in the summary log.")]
    public int maxConfigSpaceSizePairs = 12;
    [Tooltip("Log layout generator micro-profiling summary (calls + time breakdown).")]
    public bool logLayoutProfiling = false;
    [Tooltip("Experimental: use bitset-based overlap counting in layout energy (keeps HashSet fallback).")]
    public bool useBitsetOverlap = false;
    [Tooltip("Performance: in SA, avoid enumerating huge candidate sets by sampling random offsets and checking them against neighbors (rejection sampling).")]
    public bool useRejectionSamplingCandidates = true;
    [Tooltip("Performance: in SA energy, cap overlap penalty per pair (0 = exact). Lower is faster but less accurate; strict output validation is unchanged.")]
    public int overlapPenaltyCap = 0;
    [Tooltip("Extra headroom when overlapPenaltyCap is enabled: counts are exact up to cap+slack before early-out.")]
    public int overlapPenaltyCapSlack = 64;
    [Tooltip("Performance: choose SA perturbation targets by current per-node penalties (tournament selection).")]
    public bool useConflictDrivenTargetSelection = true;
    [Tooltip("Tournament size for conflict-driven target selection (2–8). Higher focuses more on worst nodes.")]
    public int targetSelectionTournamentK = 4;
    [Range(0f, 1f)]
    [Tooltip("Chance to ignore conflict-driven selection and pick a random target (exploration).")]
    public float targetSelectionExplorationProbability = 0.15f;
    [Tooltip("Experimental. For graph bridges and articulation points, prefer growing critical branches away from the current cluster. Can reduce success rate on real graphs.")]
    public bool useBridgeExpansionBias = false;
    [Tooltip("Recommended. For cycle-chains, keep the two open ends at a geometrically closable distance during initial layout.")]
    public bool useCycleClosureBias = true;
    [Header("Debug")]
    public bool verboseConfigSpaceLogs = false;
    public int maxConfigSpaceLogs = 64;
    [Tooltip("When layout generation fails with 'AddChain produced 0 layouts', logs diagnostics about the best (lowest-energy) state reached during SA.")]
    public bool debugNoLayouts = false;
    [Tooltip("How many top overlapping pairs to log in DebugNoLayouts mode.")]
    public int debugNoLayoutsTopPairs = 6;
    [Tooltip("How many problematic edges to log in DebugNoLayouts mode.")]
    public int debugNoLayoutsTopEdges = 16;

    private void Awake()
    {
        if (runOnStart)
            Build();
    }

    [ContextMenu("Build From Graph")]
    public void Build()
    {
        if (graph == null)
        {
            Debug.LogWarning("[GraphMapBuilder] Graph asset is not assigned.");
            return;
        }
        if (targetGrid == null || floorMap == null)
        {
            Debug.LogWarning("[GraphMapBuilder] Target Grid and Floor Map are required.");
            return;
        }

        var solver = new MapGraphLevelSolver(graph);
        Vector3Int? startCell = targetGrid != null ? targetGrid.WorldToCell(transform.position) : null;
        var settings = new MapGraphLayoutGenerator.Settings
        {
            MaxLayoutsPerChain = maxLayoutsPerChain,
            TemperatureSteps = temperatureSteps,
            InnerIterations = innerIterations,
            Cooling = cooling,
            ChangePrefabProbability = changePrefabProbability,
            MaxWiggleCandidates = maxWiggleCandidates,
            MaxFallbackCandidates = maxFallbackCandidates,
            VerboseConfigSpaceLogs = verboseConfigSpaceLogs,
            MaxConfigSpaceLogs = maxConfigSpaceLogs,
            LogConfigSpaceSizeSummary = logConfigSpaceSizeSummary,
            MaxConfigSpaceSizePairs = maxConfigSpaceSizePairs,
            LogLayoutProfiling = logLayoutProfiling,
            UseBitsetOverlap = useBitsetOverlap,
            UseRejectionSamplingCandidates = useRejectionSamplingCandidates,
            OverlapPenaltyCap = overlapPenaltyCap,
            OverlapPenaltyCapSlack = overlapPenaltyCapSlack,
            UseConflictDrivenTargetSelection = useConflictDrivenTargetSelection,
            TargetSelectionTournamentK = targetSelectionTournamentK,
            TargetSelectionExplorationProbability = targetSelectionExplorationProbability,
            UseBridgeExpansionBias = useBridgeExpansionBias,
            UseCycleClosureBias = useCycleClosureBias,
            DebugNoLayouts = debugNoLayouts,
            DebugNoLayoutsTopPairs = debugNoLayoutsTopPairs,
            DebugNoLayoutsTopEdges = debugNoLayoutsTopEdges
        };

        if (!solver.TrySolveAndPlace(
                targetGrid,
                floorMap,
                wallMap,
                clearOnRun,
                randomSeed,
                verboseLogs,
                startCell,
                out var error,
                layoutTimeLimitSeconds,
                destroyPlacedInstances,
                settings,
                maxLayoutsPerChain,
                layoutRetries))
        {
            Debug.LogError($"[GraphMapBuilder] Generation failed: {error}");
        }
    }
}
