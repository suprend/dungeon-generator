// Assets/scripts/Generation/Geometry/ConfigurationSpaceLibrary.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Precomputes configuration spaces for shape pairs using cached ModuleShapes.
/// </summary>
public sealed partial class ConfigurationSpaceLibrary
{
    private readonly ShapeLibrary shapeLibrary;
    private readonly Dictionary<(GameObject fixedPrefab, GameObject movingPrefab), ConfigurationSpace> cache = new();
    private bool verbose;
    private int maxVerboseLogs = 64;

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

    private bool IsConnector(GameObject prefab)
    {
        return prefab != null && prefab.GetComponent<ConnectorMeta>() != null;
    }

    private int NormalizeWidth(int width) => 1;
}
