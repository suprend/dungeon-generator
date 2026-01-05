// Assets/Scripts/Generation/Geometry/ConfigurationSpaceLibrary.Compute.cs
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
            if (fixedIsConnector == movingIsConnector)
            {
                // NOTE: With the current ConfigurationSpaceLibrary.TryGetSpace(...) contract this branch is not reachable:
                // same-type pairs (Room↔Room / Connector↔Connector) return ConfigurationSpace.Empty without computing offsets.
                // Kept for potential future use / diagnostics.
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

                var allowedDoorCellOnly = AllowedFixedCells.DoorOnly(aSock.CellOffset);
                if (HasOverlap(fixedShape.FloorCells, movingShape.WallCells, delta, allowedDoorCellOnly, out var badFloorWall))
                {
                    Tally(summary, "WallOnFloor");
                    var overlaps = verbose ? CollectOverlapCells(fixedShape.FloorCells, movingShape.WallCells, delta, allowedDoorCellOnly, overlapPreviewLimit) : null;
                    var extra = overlaps != null && overlaps.Count > 0 ? $" overlaps=[{string.Join(",", overlaps)}]" : string.Empty;
                    LogVerbose(ref logged, fixedShape, movingShape, aSock, bSock, delta, $"reject wall(on moving) overlaps floor at {badFloorWall}{extra}");
                    continue;
                }

                if (HasOverlap(fixedShape.WallCells, movingShape.FloorCells, delta, allowedDoorCellOnly, out var badWallFloor))
                {
                    Tally(summary, "FloorOnWall");
                    var overlaps = verbose ? CollectOverlapCells(fixedShape.WallCells, movingShape.FloorCells, delta, allowedDoorCellOnly, overlapPreviewLimit) : null;
                    var extra = overlaps != null && overlaps.Count > 0 ? $" overlaps=[{string.Join(",", overlaps)}]" : string.Empty;
                    LogVerbose(ref logged, fixedShape, movingShape, aSock, bSock, delta, $"reject floor(on moving) overlaps wall at {badWallFloor}{extra}");
                    continue;
                }

                offsets.Add(delta);
                Tally(summary, "OK");
                LogVerbose(ref logged, fixedShape, movingShape, aSock, bSock, delta, "accept (legacy)");
                continue;
            }

            // Room↔Connector: allow variable bite depth by aligning the room socket to any depth X along the connector inward ray.
            var connectorIsFixed = fixedIsConnector;
            var connSock = connectorIsFixed ? aSock : bSock;
            var connShape = connectorIsFixed ? fixedShape : movingShape;
            var roomSock = connectorIsFixed ? bSock : aSock;
            var roomShape = connectorIsFixed ? movingShape : fixedShape;

            var maxDepth = Mathf.Max(1, connSock.BiteDepth);
            var inward = InwardVector(connSock.Side);
            var tangent = TangentVector(connSock.Side);

            for (var depthX = 0; depthX < maxDepth; depthX++)
            {
                // We “slide” the room socket along the connector inward ray.
                // depthX=0 means the room socket aligns with the authored connector socket cell.
                // depthX>0 means the room socket aligns deeper inside the connector by X tiles.
                var fixedDoorCell = aSock.CellOffset;
                var movingDoorCell = bSock.CellOffset;
                if (connectorIsFixed)
                    fixedDoorCell = connSock.CellOffset + inward * depthX;
                else
                    movingDoorCell = connSock.CellOffset + inward * depthX;

                // Root offset to align the chosen door cells.
                var delta = fixedDoorCell - movingDoorCell;

                // Require the aligned "door" cell to be floor for both shapes, otherwise this delta cannot represent a passage.
                // Note: room sockets are often authored in the wall Tilemap, but they typically still have a floor tile under them.
                // Wall-vs-floor at the door cell is allowed separately below via allowedDoorCellOnly.
                if (connShape.FloorCells == null || roomShape.FloorCells == null ||
                    !connShape.FloorCells.Contains(connSock.CellOffset + inward * depthX) ||
                    !roomShape.FloorCells.Contains(roomSock.CellOffset))
                {
                    Tally(summary, "DoorNotOnFloor");
                    LogVerbose(ref logged, fixedShape, movingShape, aSock, bSock, delta, $"reject depthX={depthX} door not on floor");
                    continue;
                }

                // Connector base (depth 0) expressed in fixed-local coordinates.
                var connBaseFixed = connectorIsFixed ? connSock.CellOffset : (connSock.CellOffset + delta);
                var maxK = depthX;

                // Allowed overlap “carve mask” for this (fixed,moving,delta,depthX), expressed in fixed-local coordinates:
                // - floor ray: connBaseFixed + inward*k for k in [0..X]
                // - wall rays: (connBaseFixed + inward*k) ± tangent for k in [0..X]
                var allowedFloorRay = AllowedFixedCells.Rays(connBaseFixed, inward, tangent, maxK, rayMask: 1);

                // Reject any floor↔floor overlaps outside the carved floor-ray prefix (0..X).
                if (HasOverlapOutsideAllowed(fixedShape.FloorCells, movingShape.FloorCells, delta, allowedFloorRay, out var badFloorFloor))
                {
                    Tally(summary, "FloorOverlapOutsideCut");
                    var overlaps = verbose ? CollectOverlapCells(fixedShape.FloorCells, movingShape.FloorCells, delta, allowedFloorRay, overlapPreviewLimit) : null;
                    var extra = overlaps != null && overlaps.Count > 0 ? $" overlaps=[{string.Join(",", overlaps)}]" : string.Empty;
                    LogVerbose(ref logged, fixedShape, movingShape, aSock, bSock, delta, $"reject depthX={depthX} floor overlap outside cut at {badFloorFloor}{extra}");
                    continue;
                }

                // Allow wall↔floor overlaps only along the two carved wall rays (0..X).
                var doorCellFixed = connectorIsFixed ? fixedDoorCell : (movingDoorCell + delta);
                var allowedForMovingWalls = AllowedFixedCells.DoorPlusOptionalRays(doorCellFixed, includeRays: movingIsConnector, connBaseFixed, inward, tangent, maxK, rayMask: 2);
                var allowedForFixedWalls = AllowedFixedCells.DoorPlusOptionalRays(doorCellFixed, includeRays: fixedIsConnector, connBaseFixed, inward, tangent, maxK, rayMask: 2);

                if (HasOverlap(fixedShape.FloorCells, movingShape.WallCells, delta, allowedForMovingWalls, out var badFloorWall))
                {
                    Tally(summary, "WallOnFloor");
                    var overlaps = verbose ? CollectOverlapCells(fixedShape.FloorCells, movingShape.WallCells, delta, allowedForMovingWalls, overlapPreviewLimit) : null;
                    var extra = overlaps != null && overlaps.Count > 0 ? $" overlaps=[{string.Join(",", overlaps)}]" : string.Empty;
                    LogVerbose(ref logged, fixedShape, movingShape, aSock, bSock, delta, $"reject depthX={depthX} wall(on moving) overlaps floor at {badFloorWall}{extra}");
                    continue;
                }

                if (HasOverlap(fixedShape.WallCells, movingShape.FloorCells, delta, allowedForFixedWalls, out var badWallFloor))
                {
                    Tally(summary, "FloorOnWall");
                    var overlaps = verbose ? CollectOverlapCells(fixedShape.WallCells, movingShape.FloorCells, delta, allowedForFixedWalls, overlapPreviewLimit) : null;
                    var extra = overlaps != null && overlaps.Count > 0 ? $" overlaps=[{string.Join(",", overlaps)}]" : string.Empty;
                    LogVerbose(ref logged, fixedShape, movingShape, aSock, bSock, delta, $"reject depthX={depthX} floor(on moving) overlaps wall at {badWallFloor}{extra}");
                    continue;
                }

                offsets.Add(delta);
                Tally(summary, "OK");
                LogVerbose(ref logged, fixedShape, movingShape, aSock, bSock, delta, $"accept depthX={depthX}");
            }
        }

        if (verbose && offsets.Count == 0 && summary != null && logged < maxVerboseLogs)
        {
            var msg = string.Join(", ", summary.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}"));
            Debug.LogWarning($"[ConfigSpace] No offsets. Summary: {msg}");
        }

        return offsets;
    }

    private static Vector2Int InwardVector(DoorSide side)
    {
        return side switch
        {
            DoorSide.North => Vector2Int.down,
            DoorSide.South => Vector2Int.up,
            DoorSide.East => Vector2Int.left,
            DoorSide.West => Vector2Int.right,
            _ => Vector2Int.down
        };
    }

    private static Vector2Int TangentVector(DoorSide side)
    {
        return side == DoorSide.North || side == DoorSide.South ? Vector2Int.right : Vector2Int.up;
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
