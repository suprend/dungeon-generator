// Assets/scripts/Generation/Geometry/ConfigurationSpaceLibrary.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Represents the discrete configuration space between two shapes: allowed offsets of the moving shape.
/// </summary>
public sealed class ConfigurationSpace
{
    public HashSet<Vector2Int> Offsets { get; }
    public bool IsEmpty => Offsets.Count == 0;

    public ConfigurationSpace(HashSet<Vector2Int> offsets)
    {
        Offsets = offsets ?? new HashSet<Vector2Int>();
    }

    public bool Contains(Vector2Int delta) => Offsets.Contains(delta);
}

/// <summary>
/// Precomputes configuration spaces for shape pairs using cached ModuleShapes.
/// </summary>
public sealed class ConfigurationSpaceLibrary
{
    private readonly ShapeLibrary shapeLibrary;
    private readonly Dictionary<(GameObject fixedPrefab, GameObject movingPrefab), ConfigurationSpace> cache = new();
    private bool verbose;
    private int maxVerboseLogs = 64;

    public ConfigurationSpaceLibrary(ShapeLibrary shapeLibrary)
    {
        this.shapeLibrary = shapeLibrary;
    }

    public void SetDebug(bool enabled, int maxLogs = 64)
    {
        verbose = enabled;
        maxVerboseLogs = Mathf.Max(0, maxLogs);
    }

    public bool TryGetSpace(GameObject fixedPrefab, GameObject movingPrefab, out ConfigurationSpace space, out string error)
    {
        space = null;
        error = null;

        if (fixedPrefab == null || movingPrefab == null)
        {
            error = "Prefabs must be provided for configuration space lookup.";
            return false;
        }

        // By design, only Room ↔ Connector configuration spaces are valid.
        if (IsConnector(fixedPrefab) == IsConnector(movingPrefab))
        {
            space = new ConfigurationSpace(new HashSet<Vector2Int>());
            return true;
        }

        if (cache.TryGetValue((fixedPrefab, movingPrefab), out space))
            return true;

        if (!shapeLibrary.TryGetShape(fixedPrefab, out var fixedShape, out error))
            return false;
        if (!shapeLibrary.TryGetShape(movingPrefab, out var movingShape, out error))
            return false;

        var offsets = ComputeOffsets(fixedShape, movingShape, IsConnector(fixedPrefab), IsConnector(movingPrefab));
        space = new ConfigurationSpace(offsets);
        if (offsets.Count == 0)
        {
            Debug.LogWarning($"[ConfigSpace] Empty offsets for {fixedPrefab.name} -> {movingPrefab.name}");
        }
        cache[(fixedPrefab, movingPrefab)] = space;
        return true;
    }

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
            // Only exact socket-to-socket alignment to avoid gaps.
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

            // Additional constraint: no walls may overlap another module's floor.
            // Allow moving floor to overlap fixed wall only at the bite cell (door carved from room wall).
            // Also allow moving wall to overlap fixed floor at the bite cell: some prefabs mark the door cell as wall.
            var allowedDoorCellOnly = new HashSet<Vector2Int> { aSock.CellOffset };
            var allowedDoorPlusSide = allowedDoorCellOnly;

            // Corridor entrance has 2 side-wall cells that should be "carved" by the room when biting in.
            // Allow connector wall cells to overlap room floor at the bite cell and its two tangent neighbors.
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

    private bool IsConnector(GameObject prefab)
    {
        return prefab != null && prefab.GetComponent<ConnectorMeta>() != null;
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

    private bool HasOverlap(HashSet<Vector2Int> fixedSolid, HashSet<Vector2Int> movingSolid, Vector2Int delta, HashSet<Vector2Int> allowed = null)
    {
        if (fixedSolid == null || movingSolid == null)
            return true;

        foreach (var c in movingSolid)
        {
            var shifted = c + delta;
            if (allowed != null && allowed.Contains(shifted))
                continue;
            if (fixedSolid.Contains(shifted))
                return true;
        }
        return false;
    }

    private bool HasOverlap(HashSet<Vector2Int> fixedSolid, HashSet<Vector2Int> movingSolid, Vector2Int delta, HashSet<Vector2Int> allowed, out Vector2Int badCell)
    {
        badCell = default;
        if (fixedSolid == null || movingSolid == null)
            return true;

        foreach (var c in movingSolid)
        {
            var shifted = c + delta;
            if (allowed != null && allowed.Contains(shifted))
                continue;
            if (!fixedSolid.Contains(shifted))
                continue;
            badCell = shifted;
            return true;
        }
        return false;
    }

    private List<Vector2Int> CollectOverlapCells(HashSet<Vector2Int> fixedSolid, HashSet<Vector2Int> movingSolid, Vector2Int delta, HashSet<Vector2Int> allowed, int max)
    {
        var res = new List<Vector2Int>();
        if (fixedSolid == null || movingSolid == null || max <= 0)
            return res;

        foreach (var c in movingSolid)
        {
            var shifted = c + delta;
            if (allowed != null && allowed.Contains(shifted))
                continue;
            if (!fixedSolid.Contains(shifted))
                continue;
            res.Add(shifted);
            if (res.Count >= max)
                break;
        }
        return res;
    }

    private bool HasExactBiteOverlap(HashSet<Vector2Int> fixedSolid, HashSet<Vector2Int> movingSolid, Vector2Int delta, Vector2Int requiredOverlapCell)
    {
        if (fixedSolid == null || movingSolid == null)
            return false;

        var overlapCount = 0;
        foreach (var c in movingSolid)
        {
            var shifted = c + delta;
            if (!fixedSolid.Contains(shifted))
                continue;
            overlapCount++;
            if (overlapCount > 1)
                return false;
            if (shifted != requiredOverlapCell)
                return false;
        }

        return overlapCount == 1;
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

    private void LogVerbose(ref int logged, ModuleShape fixedShape, ModuleShape movingShape, ShapeSocket aSock, ShapeSocket bSock, Vector2Int delta, string detail)
    {
        if (!verbose)
            return;
        if (maxVerboseLogs <= 0)
            return;
        if (logged >= maxVerboseLogs)
            return;

        logged++;
        Debug.Log($"[ConfigSpace][dbg] {aSock.Side}@{aSock.CellOffset} vs {bSock.Side}@{bSock.CellOffset} delta={delta} => {detail}");
    }

    private IEnumerable<Vector2Int> GetSideBiteCells(Vector2Int biteCell, DoorSide side)
    {
        var tangent = side == DoorSide.North || side == DoorSide.South ? Vector2Int.right : Vector2Int.up;
        yield return biteCell + tangent;
        yield return biteCell - tangent;
    }

    private int NormalizeWidth(int width) => 1;
}
