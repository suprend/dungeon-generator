// Assets/Scripts/Generation/Geometry/ConfigurationSpace.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Represents the discrete configuration space between two shapes: allowed offsets of the moving shape.
/// </summary>
public sealed class ConfigurationSpace
{
    private static readonly HashSet<Vector2Int> EmptyOffsetsSet = new();
    private static readonly IReadOnlyList<Vector2Int> EmptyOffsetsList = new List<Vector2Int>();
    public static readonly ConfigurationSpace Empty = new(EmptyOffsetsSet, EmptyOffsetsList);

    public HashSet<Vector2Int> Offsets { get; }
    public IReadOnlyList<Vector2Int> OffsetsList { get; }
    public BitGrid Grid { get; }
    public bool IsEmpty => Offsets.Count == 0;

    public ConfigurationSpace(HashSet<Vector2Int> offsets)
    {
        Offsets = offsets ?? new HashSet<Vector2Int>();
        OffsetsList = Offsets.Count > 0 ? Offsets.ToList() : new List<Vector2Int>();
        Grid = CreateGrid(Offsets);
    }

    private ConfigurationSpace(HashSet<Vector2Int> offsets, IReadOnlyList<Vector2Int> offsetsList)
    {
        Offsets = offsets ?? EmptyOffsetsSet;
        OffsetsList = offsetsList ?? EmptyOffsetsList;
        Grid = CreateGrid(Offsets);
    }

    public bool Contains(Vector2Int delta) => Offsets.Contains(delta);

    private static BitGrid CreateGrid(HashSet<Vector2Int> offsets)
    {
        if (offsets == null || offsets.Count == 0)
            return null;
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        foreach (var p in offsets)
        {
            if (p.x < minX) minX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.x > maxX) maxX = p.x;
            if (p.y > maxY) maxY = p.y;
        }
        return BitGrid.Build(offsets, new Vector2Int(minX, minY), new Vector2Int(maxX, maxY));
    }
}
