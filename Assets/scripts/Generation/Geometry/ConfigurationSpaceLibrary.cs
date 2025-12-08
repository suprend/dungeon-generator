// Assets/scripts/Generation/Geometry/ConfigurationSpaceLibrary.cs
using System.Collections.Generic;
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

    public ConfigurationSpaceLibrary(ShapeLibrary shapeLibrary)
    {
        this.shapeLibrary = shapeLibrary;
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

        if (cache.TryGetValue((fixedPrefab, movingPrefab), out space))
            return true;

        if (!shapeLibrary.TryGetShape(fixedPrefab, out var fixedShape, out error))
            return false;
        if (!shapeLibrary.TryGetShape(movingPrefab, out var movingShape, out error))
            return false;

        var offsets = ComputeOffsets(fixedShape, movingShape);
        space = new ConfigurationSpace(offsets);
        cache[(fixedPrefab, movingPrefab)] = space;
        return true;
    }

    private HashSet<Vector2Int> ComputeOffsets(ModuleShape fixedShape, ModuleShape movingShape)
    {
        var offsets = new HashSet<Vector2Int>();
        if (fixedShape == null || movingShape == null)
            return offsets;

        var socketPairs = CollectCompatibleSocketPairs(fixedShape, movingShape);
        foreach (var (aSock, bSock) in socketPairs)
        {
            foreach (var aCell in aSock.ContactCells)
            {
                foreach (var bCell in bSock.ContactCells)
                {
                    var delta = aCell - bCell; // place moving so contact strips align
                    var allowed = BuildAllowedOverlap(bSock.ContactCells, delta);
                    if (HasOverlap(fixedShape.SolidCells, movingShape.SolidCells, delta, allowed))
                        continue;
                    offsets.Add(delta);
                }
            }
        }

        if (offsets.Count == 0)
        {
            // Fallback: allow socket-to-socket alignment even if overlap checks would fail
            foreach (var (aSock, bSock) in socketPairs)
            {
                offsets.Add(aSock.CellOffset - bSock.CellOffset);
            }
        }

        return offsets;
    }

    private HashSet<Vector2Int> BuildAllowedOverlap(IReadOnlyCollection<Vector2Int> contactCells, Vector2Int delta)
    {
        var allowed = new HashSet<Vector2Int>();
        if (contactCells == null)
            return allowed;
        foreach (var c in contactCells)
            allowed.Add(c + delta);
        return allowed;
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

    private int NormalizeWidth(int width) => Mathf.Max(1, width);
}
