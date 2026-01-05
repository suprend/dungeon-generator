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
    public bool IsEmpty => Offsets.Count == 0;

    public ConfigurationSpace(HashSet<Vector2Int> offsets)
    {
        Offsets = offsets ?? new HashSet<Vector2Int>();
        OffsetsList = Offsets.Count > 0 ? Offsets.ToList() : new List<Vector2Int>();
    }

    private ConfigurationSpace(HashSet<Vector2Int> offsets, IReadOnlyList<Vector2Int> offsetsList)
    {
        Offsets = offsets ?? EmptyOffsetsSet;
        OffsetsList = offsetsList ?? EmptyOffsetsList;
    }

    public bool Contains(Vector2Int delta) => Offsets.Contains(delta);
}
