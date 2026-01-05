using System.Collections.Generic;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private int CountOverlapShifted(
        HashSet<Vector2Int> fixedLocal,
        HashSet<Vector2Int> movingLocal,
        Vector2Int deltaMovingMinusFixed,
        AllowedWorldCells allowedWorld,
        Vector2Int fixedRoot,
        out Vector2Int lastOverlapWorld,
        bool earlyStopAtTwo)
    {
        using var _ps = PS(S_CountOverlapShifted);
        lastOverlapWorld = default;
        if (fixedLocal == null || movingLocal == null || fixedLocal.Count == 0 || movingLocal.Count == 0)
            return 0;

        var count = 0;
        if (movingLocal.Count <= fixedLocal.Count)
        {
            foreach (var c in movingLocal)
            {
                var fixedCell = c + deltaMovingMinusFixed;
                if (!fixedLocal.Contains(fixedCell))
                    continue;

                var world = fixedRoot + fixedCell;
                if (allowedWorld.Contains(world))
                    continue;

                count++;
                lastOverlapWorld = world;
                if (earlyStopAtTwo && count > 1)
                    return count;
            }
        }
        else
        {
            foreach (var fixedCell in fixedLocal)
            {
                var movingCell = fixedCell - deltaMovingMinusFixed;
                if (!movingLocal.Contains(movingCell))
                    continue;

                var world = fixedRoot + fixedCell;
                if (allowedWorld.Contains(world))
                    continue;

                count++;
                lastOverlapWorld = world;
                if (earlyStopAtTwo && count > 1)
                    return count;
            }
        }
        return count;
    }

    private bool IsAllowedBiteOverlap(RoomPlacement a, RoomPlacement b, int overlapCount)
    {
        // Kept for backward-compat / diagnostics; the new bite-depth rules no longer rely on overlapCount==1.
        if (a == null || b == null)
            return false;
        if (!AreNeighbors(a.NodeId, b.NodeId))
            return false;
        if (IsConnector(a.Prefab) == IsConnector(b.Prefab))
            return false;
        return RoomsTouchEitherWay(a, b);
    }

    private bool IsConnector(GameObject prefab)
    {
        if (prefab == null)
            return false;
        if (connectorPrefabs != null)
            return connectorPrefabs.Contains(prefab);
        return prefab.GetComponent<ConnectorMeta>() != null;
    }

    private bool AreNeighbors(string aId, string bId)
    {
        if (string.IsNullOrEmpty(aId) || string.IsNullOrEmpty(bId))
            return false;

        if (neighborLookup != null && neighborLookup.TryGetValue(aId, out var set))
            return set.Contains(bId);

        if (graphAsset == null)
            return false;
        foreach (var e in graphAsset.GetEdgesFor(aId))
        {
            if (e == null)
                continue;
            if ((e.fromNodeId == aId && e.toNodeId == bId) || (e.fromNodeId == bId && e.toNodeId == aId))
                return true;
        }
        return false;
    }

    private AllowedWorldCells AllowedWallOnFloorCells(RoomPlacement wallOwner, RoomPlacement floorOwner, Vector2Int biteCell)
    {
        // Legacy helper retained for older call sites; bite-depth uses TryGetBiteAllowance instead.
        if (wallOwner == null || floorOwner == null)
            return AllowedWorldCells.None;
        return new AllowedWorldCells(biteCell);
    }

    private bool TryGetSocketSideAtWorldCell(RoomPlacement placement, Vector2Int worldCell, out DoorSide side)
    {
        side = default;
        if (placement?.Shape?.Sockets == null)
            return false;

        foreach (var sock in placement.Shape.Sockets)
        {
            if (sock == null)
                continue;
            if (placement.Root + sock.CellOffset != worldCell)
                continue;
            side = sock.Side;
            return true;
        }
        return false;
    }

    private bool HasOverlapExcept(HashSet<Vector2Int> a, HashSet<Vector2Int> b, HashSet<Vector2Int> allowedCells)
    {
        if (a == null || b == null || a.Count == 0 || b.Count == 0)
            return false;
        var iter = a.Count <= b.Count ? a : b;
        var other = ReferenceEquals(iter, a) ? b : a;
        foreach (var c in iter)
        {
            if (!other.Contains(c))
                continue;
            if (allowedCells != null && allowedCells.Contains(c))
                continue;
            return true;
        }
        return false;
    }

    private int CountOverlapAll(HashSet<Vector2Int> a, HashSet<Vector2Int> b, HashSet<Vector2Int> allowedCells, out Vector2Int lastOverlapCell)
    {
        lastOverlapCell = default;
        if (a == null || b == null || a.Count == 0 || b.Count == 0)
            return 0;

        var count = 0;
        var iter = a.Count <= b.Count ? a : b;
        var other = ReferenceEquals(iter, a) ? b : a;
        foreach (var c in iter)
        {
            if (!other.Contains(c))
                continue;
            if (allowedCells != null && allowedCells.Contains(c))
                continue;
            count++;
            lastOverlapCell = c;
        }
        return count;
    }
}

