// Assets/scripts/Generation/Geometry/ConfigurationSpaceLibrary.Overlap.cs
using System.Collections.Generic;
using UnityEngine;

public sealed partial class ConfigurationSpaceLibrary
{
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
}

