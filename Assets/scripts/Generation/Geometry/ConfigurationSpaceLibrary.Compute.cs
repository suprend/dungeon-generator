// Assets/scripts/Generation/Geometry/ConfigurationSpaceLibrary.Compute.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class ConfigurationSpaceLibrary
{
    private HashSet<Vector2Int> ComputeOffsets(ModuleShape fixedShape, ModuleShape movingShape, bool fixedIsConnector, bool movingIsConnector)
    {
        var offsets = new HashSet<Vector2Int>();
        if (fixedShape == null || movingShape == null)
            return offsets;

        var logged = 0;
        var summary = verbose ? new Dictionary<string, int>() : null;
        const int overlapPreviewLimit = 8;

        var socketPairs = CollectCompatibleSocketPairs(fixedShape, movingShape);
        foreach (var (aSock, bSock) in socketPairs)
        {
            var delta = aSock.CellOffset - bSock.CellOffset;
            var biteStatus = GetBiteOverlapStatus(fixedShape.FloorCells, movingShape.FloorCells, delta, aSock.CellOffset, out var biteCell, out var biteCount);
            if (biteStatus != "OK")
            {
                Tally(summary, $"Bite:{biteStatus}");
                var overlaps = verbose ? CollectOverlapCells(fixedShape.FloorCells, movingShape.FloorCells, delta, allowed: null, max: overlapPreviewLimit) : null;
                var extra = overlaps != null && overlaps.Count > 0 ? $" overlaps=[{string.Join(",", overlaps)}]" : string.Empty;
                LogVerbose(ref logged, fixedShape, movingShape, aSock, bSock, delta, $"reject bite={biteStatus} overlapCount={biteCount} cell={biteCell}{extra}");
                continue;
            }

            var allowedDoorCellOnly = new HashSet<Vector2Int> { aSock.CellOffset };
            var allowedDoorPlusSide = allowedDoorCellOnly;

            if ((movingIsConnector && !fixedIsConnector) || (fixedIsConnector && !movingIsConnector))
            {
                allowedDoorPlusSide = new HashSet<Vector2Int>(allowedDoorCellOnly);
                foreach (var c in GetSideBiteCells(aSock.CellOffset, aSock.Side))
                    allowedDoorPlusSide.Add(c);
            }

            var allowedForMovingWalls = movingIsConnector ? allowedDoorPlusSide : allowedDoorCellOnly;
            if (HasOverlap(fixedShape.FloorCells, movingShape.WallCells, delta, allowedForMovingWalls, out var badFloorWall))
            {
                Tally(summary, "WallOnFloor");
                var overlaps = verbose ? CollectOverlapCells(fixedShape.FloorCells, movingShape.WallCells, delta, allowedForMovingWalls, overlapPreviewLimit) : null;
                var extra = overlaps != null && overlaps.Count > 0 ? $" overlaps=[{string.Join(",", overlaps)}]" : string.Empty;
                LogVerbose(ref logged, fixedShape, movingShape, aSock, bSock, delta, $"reject wall(on moving) overlaps floor at {badFloorWall}{extra}");
                continue;
            }

            var allowedForFixedWalls = fixedIsConnector ? allowedDoorPlusSide : allowedDoorCellOnly;
            if (HasOverlap(fixedShape.WallCells, movingShape.FloorCells, delta, allowedForFixedWalls, out var badWallFloor))
            {
                Tally(summary, "FloorOnWall");
                var overlaps = verbose ? CollectOverlapCells(fixedShape.WallCells, movingShape.FloorCells, delta, allowedForFixedWalls, overlapPreviewLimit) : null;
                var extra = overlaps != null && overlaps.Count > 0 ? $" overlaps=[{string.Join(",", overlaps)}]" : string.Empty;
                LogVerbose(ref logged, fixedShape, movingShape, aSock, bSock, delta, $"reject floor(on moving) overlaps wall at {badWallFloor}{extra}");
                continue;
            }

            offsets.Add(delta);
            Tally(summary, "OK");
            LogVerbose(ref logged, fixedShape, movingShape, aSock, bSock, delta, "accept");
        }

        if (verbose && offsets.Count == 0 && summary != null && logged < maxVerboseLogs)
        {
            var msg = string.Join(", ", summary.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}"));
            Debug.LogWarning($"[ConfigSpace] No offsets. Summary: {msg}");
        }

        return offsets;
    }

    private List<(ShapeSocket, ShapeSocket)> CollectCompatibleSocketPairs(ModuleShape fixedShape, ModuleShape movingShape)
    {
        var pairs = new List<(ShapeSocket, ShapeSocket)>();
        if (fixedShape?.Sockets == null || movingShape?.Sockets == null)
            return pairs;

        foreach (var a in fixedShape.Sockets)
        {
            if (a == null) continue;
            foreach (var b in movingShape.Sockets)
            {
                if (b == null) continue;
                if (a.Side != b.Side.Opposite()) continue;
                pairs.Add((a, b));
            }
        }

        return pairs;
    }

    private string GetBiteOverlapStatus(HashSet<Vector2Int> fixedFloor, HashSet<Vector2Int> movingFloor, Vector2Int delta, Vector2Int requiredCell, out Vector2Int overlapCell, out int overlapCount)
    {
        overlapCell = default;
        overlapCount = 0;
        if (fixedFloor == null || movingFloor == null)
            return "Null";

        foreach (var c in movingFloor)
        {
            var shifted = c + delta;
            if (!fixedFloor.Contains(shifted))
                continue;
            overlapCount++;
            overlapCell = shifted;
            if (overlapCount > 1)
                return "TooMany";
        }

        if (overlapCount == 0)
            return "None";
        if (overlapCell != requiredCell)
            return "WrongCell";
        return "OK";
    }

    private void Tally(Dictionary<string, int> summary, string key)
    {
        if (summary == null)
            return;
        if (!summary.TryGetValue(key, out var count))
            count = 0;
        summary[key] = count + 1;
    }

    private IEnumerable<Vector2Int> GetSideBiteCells(Vector2Int biteCell, DoorSide side)
    {
        var tangent = side == DoorSide.North || side == DoorSide.South ? Vector2Int.right : Vector2Int.up;
        yield return biteCell + tangent;
        yield return biteCell - tangent;
    }
}

