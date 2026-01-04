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
            if (fixedIsConnector == movingIsConnector)
            {
                // Legacy strict "1-tile bite" when the pair is not Room↔Connector.
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
                var fixedDoorCell = aSock.CellOffset;
                var movingDoorCell = bSock.CellOffset;
                if (connectorIsFixed)
                    fixedDoorCell = connSock.CellOffset + inward * depthX;
                else
                    movingDoorCell = connSock.CellOffset + inward * depthX;

                // Root offset to align the chosen door cells.
                var delta = fixedDoorCell - movingDoorCell;

                // Require the aligned "door" cell to be floor for both shapes, otherwise this delta cannot represent a passage.
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

                var allowedFloorRay = new HashSet<Vector2Int>();
                var allowedWallRays = new HashSet<Vector2Int>();
                for (var k = 0; k <= maxK; k++)
                {
                    var c = connBaseFixed + inward * k;
                    allowedFloorRay.Add(c);
                    allowedWallRays.Add(c + tangent);
                    allowedWallRays.Add(c - tangent);
                }

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
                var allowedDoorCellOnly = new HashSet<Vector2Int> { connectorIsFixed ? fixedDoorCell : (movingDoorCell + delta) };
                var allowedForMovingWalls = new HashSet<Vector2Int>(allowedDoorCellOnly);
                var allowedForFixedWalls = new HashSet<Vector2Int>(allowedDoorCellOnly);
                if (movingIsConnector)
                    allowedForMovingWalls.UnionWith(allowedWallRays);
                if (fixedIsConnector)
                    allowedForFixedWalls.UnionWith(allowedWallRays);

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

    private bool HasOverlapOutsideAllowed(HashSet<Vector2Int> fixedLocal, HashSet<Vector2Int> movingLocal, Vector2Int delta, HashSet<Vector2Int> allowedFixedCells, out Vector2Int badCellFixed)
    {
        badCellFixed = default;
        if (fixedLocal == null || movingLocal == null || fixedLocal.Count == 0 || movingLocal.Count == 0)
            return false;
        foreach (var c in movingLocal)
        {
            var fixedCell = c + delta;
            if (!fixedLocal.Contains(fixedCell))
                continue;
            if (allowedFixedCells != null && allowedFixedCells.Contains(fixedCell))
                continue;
            badCellFixed = fixedCell;
            return true;
        }
        return false;
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
