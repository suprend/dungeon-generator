// Assets/scripts/Generation/Graph/MapGraphLevelSolver.Legacy.cs
using System;
using UnityEngine;
using UnityEngine.Tilemaps;

public partial class MapGraphLevelSolver
{
    private bool TrySolveAndPlaceLegacy(Grid targetGrid, Tilemap floorMap, Tilemap wallMap, bool clearMaps, int randomSeed, bool verboseLogs, Vector3Int? startCell, out string error, float maxDurationSeconds, bool destroyPlacedInstances)
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
        if (!MapGraphChainBuilder.TryBuildChains(graphAsset, faces, out var chains, out error))
            return false;

        var ordered = chains;
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
}

