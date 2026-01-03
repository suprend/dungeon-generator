// Assets/scripts/Generation/Geometry/ConfigurationSpace.cs
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

