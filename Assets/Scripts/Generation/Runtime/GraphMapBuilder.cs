// Assets/scripts/Generation/Graph/GraphMapBuilder.cs
using UnityEngine;
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
    [Tooltip("Time limit for placement (seconds). 0 = unlimited")]
    public float placementTimeLimitSeconds = 5f;
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
    [Tooltip("How many different random seeds to try if layout generation/placement fails. Used only when randomSeed == 0.")]
    public int layoutAttempts = 1;
    [Tooltip("Log configuration space (CS) offsets counts for prefab pairs during layout generation.")]
    public bool logConfigSpaceSizeSummary = false;
    [Tooltip("How many largest CS pairs to include in the summary log.")]
    public int maxConfigSpaceSizePairs = 12;
    [Tooltip("Log layout generator micro-profiling summary (calls + time breakdown).")]
    public bool logLayoutProfiling = false;
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
                placementTimeLimitSeconds,
                destroyPlacedInstances,
                settings,
                maxLayoutsPerChain,
                layoutAttempts))
        {
            Debug.LogError($"[GraphMapBuilder] Generation failed: {error}");
        }
    }
}
