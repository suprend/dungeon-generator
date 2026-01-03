// Assets/scripts/Generation/Graph/MapGraphLevelSolver.LayoutWorkflow.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public partial class MapGraphLevelSolver
{
    /// <summary>
    /// Generates a high-level room layout (positions + chosen prefabs) using configuration spaces and simulated annealing.
    /// </summary>
    public bool TryGenerateLayout(
        Grid targetGrid,
        Tilemap floorMap,
        Tilemap wallMap,
        int randomSeed,
        out MapGraphLayoutGenerator.LayoutResult layout,
        out string error,
        MapGraphLayoutGenerator.Settings layoutSettings = null,
        int? maxLayoutsPerChain = null)
    {
        layout = null;
        error = null;
        if (graphAsset == null)
        {
            error = "Graph asset is null.";
            return false;
        }
        if (targetGrid == null || floorMap == null)
        {
            error = "Target grid and floor map are required for layout generation.";
            return false;
        }

        var stamp = new TileStampService(targetGrid, floorMap, wallMap);
        var originalGraph = graphAsset;
        var expandedGraph = BuildCorridorGraph(graphAsset);
        var generator = new MapGraphLayoutGenerator(randomSeed, layoutSettings);
        var ok = generator.TryGenerate(expandedGraph, stamp, out layout, out error, maxLayoutsPerChain);
        graphAsset = originalGraph;
        return ok;
    }

    /// <summary>
    /// Solves and immediately places prefabs with full backtracking. Result is stamped into provided tilemaps.
    /// </summary>
    public bool TrySolveAndPlace(Grid targetGrid, Tilemap floorMap, Tilemap wallMap, bool clearMaps, int randomSeed, bool verboseLogs, out string error, float maxDurationSeconds = 5f, bool destroyPlacedInstances = true)
    {
        return TrySolveAndPlace(targetGrid, floorMap, wallMap, clearMaps, randomSeed, verboseLogs, null, out error, maxDurationSeconds, destroyPlacedInstances);
    }

    /// <summary>
    /// Uses the new layout generator (configuration spaces + simulated annealing) to produce a layout
    /// and then stamps it into the tilemaps. Falls back to connector placement between already placed rooms.
    /// </summary>
    public bool TrySolveAndPlaceWithLayout(
        Grid targetGrid,
        Tilemap floorMap,
        Tilemap wallMap,
        bool clearMaps,
        int randomSeed,
        bool verboseLogs,
        Vector3Int? startCell,
        out string error,
        float maxDurationSeconds = 5f,
        bool destroyPlacedInstances = true,
        MapGraphLayoutGenerator.Settings layoutSettings = null,
        int? maxLayoutsPerChain = null,
        int layoutAttempts = 1)
    {
        error = null;
        if (targetGrid == null || floorMap == null)
        {
            error = "Target grid and floor map are required.";
            return false;
        }

        var originalGraph = graphAsset;
        var expandedGraph = BuildCorridorGraph(graphAsset);
        graphAsset = expandedGraph;

        try
        {
            var totalStartTime = Time.realtimeSinceStartup;
            var stamp = new TileStampService(targetGrid, floorMap, wallMap);
            var precomputeStartTime = Time.realtimeSinceStartup;
            if (!PrecomputeGeometry(stamp, out error))
                return false;
            var precomputeSeconds = Time.realtimeSinceStartup - precomputeStartTime;

            if (layoutSettings != null)
                configSpaceLibrary?.SetDebug(layoutSettings.VerboseConfigSpaceLogs, layoutSettings.MaxConfigSpaceLogs);

            var solveStartTime = Time.realtimeSinceStartup;
            if (!TryBuildDirectAssignments(expandedGraph, out var nodeAssign, out var edgeAssign, out error))
                return false;
            var solveSeconds = Time.realtimeSinceStartup - solveStartTime;

            var layoutStartTime = Time.realtimeSinceStartup;
            var baseSeed = randomSeed != 0 ? randomSeed : UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            var attempts = randomSeed != 0 ? 1 : Mathf.Max(1, layoutAttempts);
            string lastError = null;

            var faceChainStart = Time.realtimeSinceStartup;
            var prevFaceBuilderDebug = MapGraphFaceBuilder.LogProfiling;
            var shouldLogFaceProfiling = layoutSettings != null && layoutSettings.LogLayoutProfiling;
            MapGraphFaceBuilder.SetDebug(shouldLogFaceProfiling);
            if (!MapGraphFaceBuilder.TryBuildFaces(expandedGraph, out var faces, out error))
            {
                MapGraphFaceBuilder.SetDebug(prevFaceBuilderDebug);
                return false;
            }
            if (!MapGraphChainBuilder.TryBuildChains(expandedGraph, faces, out var precomputedChains, out error))
            {
                MapGraphFaceBuilder.SetDebug(prevFaceBuilderDebug);
                return false;
            }
            MapGraphFaceBuilder.SetDebug(prevFaceBuilderDebug);
            var faceChainSecondsShared = Time.realtimeSinceStartup - faceChainStart;

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                var attemptStart = Time.realtimeSinceStartup;
                float layoutSeconds = 0f;
                float faceChainSeconds = faceChainSecondsShared;
                float placeSeconds = 0f;
                float stampSeconds = 0f;

                var attemptSeed = unchecked(baseSeed + attempt);
                var layoutGenerator = new MapGraphLayoutGenerator(attemptSeed, layoutSettings);
                var layoutGenStart = Time.realtimeSinceStartup;
                if (!layoutGenerator.TryGenerate(expandedGraph, stamp, out var layout, out var genError, maxLayoutsPerChain, precomputedChains))
                {
                    lastError = genError;
                    if (verboseLogs)
                        Debug.Log($"[MapGraphLevelSolver] Layout generation attempt {attempt + 1}/{attempts} failed: {genError}");
                    continue;
                }
                layoutSeconds = Time.realtimeSinceStartup - layoutGenStart;

                var ordered = precomputedChains;
                var rngLocal = new System.Random(attemptSeed);

                var placer = new PlacementState(stamp, rngLocal, nodeAssign, edgeAssign, verboseLogs, startCell, layoutStartTime, maxDurationSeconds, shapeLibrary, configSpaceLibrary);
                if (clearMaps)
                    stamp.ClearMaps();

                var placeStart = Time.realtimeSinceStartup;
                if (!placer.PlaceFromLayout(layout, ordered, expandedGraph))
                {
                    lastError = placer.LastError ?? "Failed to place layout.";
                    if (verboseLogs)
                    {
                        placeSeconds = Time.realtimeSinceStartup - placeStart;
                        var attemptSeconds = Time.realtimeSinceStartup - attemptStart;
                        var totalSeconds = Time.realtimeSinceStartup - totalStartTime;
                        Debug.Log($"[MapGraphLevelSolver] Layout placement attempt {attempt + 1}/{attempts} failed after {attemptSeconds:0.000}s: {lastError}");
                        Debug.Log($"[MapGraphLevelSolver] Timings (s): precompute={precomputeSeconds:0.000} solve={solveSeconds:0.000} layout={layoutSeconds:0.000} faces+chains(shared)={faceChainSeconds:0.000} place={placeSeconds:0.000} stamp={stampSeconds:0.000} total={totalSeconds:0.000}");
                    }
                    placer.Cleanup();
                    continue;
                }
                placeSeconds = Time.realtimeSinceStartup - placeStart;

                var stampStart = Time.realtimeSinceStartup;
                placer.StampAll(disableRenderers: !destroyPlacedInstances);
                if (destroyPlacedInstances)
                    placer.DestroyPlacedInstances();
                stampSeconds = Time.realtimeSinceStartup - stampStart;
                if (verboseLogs)
                {
                    var attemptSeconds = Time.realtimeSinceStartup - attemptStart;
                    var totalSeconds = Time.realtimeSinceStartup - totalStartTime;
                    Debug.Log($"[MapGraphLevelSolver] Layout placement completed in {attemptSeconds:0.000}s.");
                    Debug.Log($"[MapGraphLevelSolver] Timings (s): precompute={precomputeSeconds:0.000} solve={solveSeconds:0.000} layout={layoutSeconds:0.000} faces+chains(shared)={faceChainSeconds:0.000} place={placeSeconds:0.000} stamp={stampSeconds:0.000} total={totalSeconds:0.000}");
                }
                return true;
            }

            error = lastError ?? "Failed to generate/place layout after retries.";
            return false;
        }
        finally
        {
            graphAsset = originalGraph;
        }
    }

    /// <summary>
    /// Solves and immediately places prefabs with full backtracking. Result is stamped into provided tilemaps.
    /// Allows overriding the start room cell (e.g., from MapGraphBuilder transform).
    /// </summary>
    public bool TrySolveAndPlace(
        Grid targetGrid,
        Tilemap floorMap,
        Tilemap wallMap,
        bool clearMaps,
        int randomSeed,
        bool verboseLogs,
        Vector3Int? startCell,
        out string error,
        float maxDurationSeconds = 5f,
        bool destroyPlacedInstances = true,
        MapGraphLayoutGenerator.Settings layoutSettings = null,
        int? maxLayoutsPerChain = null,
        int layoutAttempts = 1)
    {
        return TrySolveAndPlaceWithLayout(
            targetGrid,
            floorMap,
            wallMap,
            clearMaps,
            randomSeed,
            verboseLogs,
            startCell,
            out error,
            maxDurationSeconds,
            destroyPlacedInstances,
            layoutSettings,
            maxLayoutsPerChain,
            layoutAttempts);
    }
}
