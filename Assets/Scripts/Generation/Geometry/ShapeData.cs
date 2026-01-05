// Assets/scripts/Generation/Geometry/ShapeData.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Describes a contact socket extracted from a module prefab.
/// </summary>
public sealed class ShapeSocket
{
    public DoorSide Side { get; }
    public int Width { get; }
    public int BiteDepth { get; }
    public Vector2Int CellOffset { get; }
    public IReadOnlyCollection<Vector2Int> ContactCells { get; }

    public ShapeSocket(DoorSide side, int width, int biteDepth, Vector2Int cellOffset, IReadOnlyCollection<Vector2Int> contactCells)
    {
        Side = side;
        Width = Mathf.Max(1, width);
        BiteDepth = Mathf.Max(1, biteDepth);
        CellOffset = cellOffset;
        ContactCells = contactCells ?? new List<Vector2Int>();
    }
}

/// <summary>
/// Captures the discrete geometry of a module (room or connector) in grid cells.
/// </summary>
public sealed class ModuleShape
{
    public HashSet<Vector2Int> FloorCells { get; }
    public HashSet<Vector2Int> WallCells { get; }
    public HashSet<Vector2Int> SolidCells { get; }
    public IReadOnlyList<ShapeSocket> Sockets { get; }
    public Vector2Int Min { get; }
    public Vector2Int Max { get; }
    public Vector2Int WallMin { get; }
    public Vector2Int WallMax { get; }

    public ModuleShape(HashSet<Vector2Int> floorCells, HashSet<Vector2Int> wallCells, IReadOnlyList<ShapeSocket> sockets)
    {
        FloorCells = floorCells ?? new HashSet<Vector2Int>();
        WallCells = wallCells ?? new HashSet<Vector2Int>();
        // For configuration space overlap, consider only floor cells to allow wall sharing/replacement.
        SolidCells = new HashSet<Vector2Int>(FloorCells);
        Sockets = sockets ?? new List<ShapeSocket>();
        Min = ComputeMin();
        Max = ComputeMax();
        WallMin = ComputeWallMin();
        WallMax = ComputeWallMax();
    }

    private Vector2Int ComputeMin()
    {
        if (SolidCells.Count == 0)
            return Vector2Int.zero;
        var minX = SolidCells.Min(c => c.x);
        var minY = SolidCells.Min(c => c.y);
        return new Vector2Int(minX, minY);
    }

    private Vector2Int ComputeMax()
    {
        if (SolidCells.Count == 0)
            return Vector2Int.zero;
        var maxX = SolidCells.Max(c => c.x);
        var maxY = SolidCells.Max(c => c.y);
        return new Vector2Int(maxX, maxY);
    }

    private Vector2Int ComputeWallMin()
    {
        if (WallCells.Count == 0)
            return Min;
        var minX = WallCells.Min(c => c.x);
        var minY = WallCells.Min(c => c.y);
        return new Vector2Int(minX, minY);
    }

    private Vector2Int ComputeWallMax()
    {
        if (WallCells.Count == 0)
            return Max;
        var maxX = WallCells.Max(c => c.x);
        var maxY = WallCells.Max(c => c.y);
        return new Vector2Int(maxX, maxY);
    }
}
