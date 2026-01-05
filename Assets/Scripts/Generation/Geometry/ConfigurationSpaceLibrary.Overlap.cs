// Assets/Scripts/Generation/Geometry/ConfigurationSpaceLibrary.Overlap.cs
using System.Collections.Generic;
using UnityEngine;

public sealed partial class ConfigurationSpaceLibrary
{
    // No-allocation “allowed overlap mask” for config-space overlap checks.
    // Works in fixed-local cell coordinates (same coordinate system as `allowed` in HasOverlap()).
    private readonly struct AllowedFixedCells
    {
        // mode:
        // 0 = none
        // 1 = door-only
        // 2 = rays-only
        // 3 = door + rays
        private readonly byte mode;

        // Door-only cell in fixed-local coordinates.
        private readonly Vector2Int doorCell;

        // Ray-mode fields (fixed-local coordinates).
        private readonly Vector2Int rayBase;
        private readonly Vector2Int rayInward;
        private readonly Vector2Int rayTangent;
        private readonly int rayMaxK;
        private readonly byte rayMask; // 1 = center (floor ray), 2 = wall rays (±tangent)

        public static AllowedFixedCells None => default;

        private AllowedFixedCells(Vector2Int doorCell)
        {
            mode = 1;
            this.doorCell = doorCell;
            rayBase = default;
            rayInward = default;
            rayTangent = default;
            rayMaxK = 0;
            rayMask = 0;
        }

        private AllowedFixedCells(Vector2Int rayBase, Vector2Int rayInward, Vector2Int rayTangent, int rayMaxK, byte rayMask, Vector2Int doorCell, bool includeDoor)
        {
            mode = (byte)((includeDoor ? 1 : 0) | 2);
            this.doorCell = doorCell;
            this.rayBase = rayBase;
            this.rayInward = rayInward;
            this.rayTangent = rayTangent;
            this.rayMaxK = rayMaxK;
            this.rayMask = rayMask;
        }

        public static AllowedFixedCells DoorOnly(Vector2Int doorCell)
        {
            return new AllowedFixedCells(doorCell);
        }

        public static AllowedFixedCells Rays(Vector2Int rayBase, Vector2Int rayInward, Vector2Int rayTangent, int maxKInclusive, byte rayMask)
        {
            if (maxKInclusive < 0 || rayMask == 0)
                return None;
            return new AllowedFixedCells(rayBase, rayInward, rayTangent, maxKInclusive, rayMask, doorCell: default, includeDoor: false);
        }

        public static AllowedFixedCells DoorPlusOptionalRays(Vector2Int doorCell, bool includeRays, Vector2Int rayBase, Vector2Int rayInward, Vector2Int rayTangent, int maxKInclusive, byte rayMask)
        {
            if (!includeRays || maxKInclusive < 0 || rayMask == 0)
                return DoorOnly(doorCell);
            return new AllowedFixedCells(rayBase, rayInward, rayTangent, maxKInclusive, rayMask, doorCell, includeDoor: true);
        }

        public bool Contains(Vector2Int fixedCell)
        {
            if (mode == 0)
                return false;

            if ((mode & 1) != 0 && fixedCell == doorCell)
                return true;

            if ((mode & 2) == 0)
                return false;

            var offset = fixedCell - rayBase;
            var k = offset.x * rayInward.x + offset.y * rayInward.y;
            if (k < 0 || k > rayMaxK)
                return false;

            var perp = offset - rayInward * k;
            if ((rayMask & 1) != 0 && perp == Vector2Int.zero)
                return true;
            if ((rayMask & 2) != 0 && (perp == rayTangent || perp == -rayTangent))
                return true;
            return false;
        }
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

    private bool HasOverlap(HashSet<Vector2Int> fixedSolid, HashSet<Vector2Int> movingSolid, Vector2Int delta, AllowedFixedCells allowed, out Vector2Int badCell)
    {
        badCell = default;
        if (fixedSolid == null || movingSolid == null)
            return true;

        foreach (var c in movingSolid)
        {
            var shifted = c + delta;
            if (allowed.Contains(shifted))
                continue;
            if (!fixedSolid.Contains(shifted))
                continue;
            badCell = shifted;
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

    private List<Vector2Int> CollectOverlapCells(HashSet<Vector2Int> fixedSolid, HashSet<Vector2Int> movingSolid, Vector2Int delta, AllowedFixedCells allowed, int max)
    {
        var res = new List<Vector2Int>();
        if (fixedSolid == null || movingSolid == null || max <= 0)
            return res;

        foreach (var c in movingSolid)
        {
            var shifted = c + delta;
            if (allowed.Contains(shifted))
                continue;
            if (!fixedSolid.Contains(shifted))
                continue;
            res.Add(shifted);
            if (res.Count >= max)
                break;
        }
        return res;
    }

    private bool HasOverlapOutsideAllowed(HashSet<Vector2Int> fixedLocal, HashSet<Vector2Int> movingLocal, Vector2Int delta, AllowedFixedCells allowedFixedCells, out Vector2Int badCellFixed)
    {
        badCellFixed = default;
        if (fixedLocal == null || movingLocal == null || fixedLocal.Count == 0 || movingLocal.Count == 0)
            return false;

        foreach (var c in movingLocal)
        {
            var fixedCell = c + delta;
            if (!fixedLocal.Contains(fixedCell))
                continue;
            if (allowedFixedCells.Contains(fixedCell))
                continue;
            badCellFixed = fixedCell;
            return true;
        }
        return false;
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
}
