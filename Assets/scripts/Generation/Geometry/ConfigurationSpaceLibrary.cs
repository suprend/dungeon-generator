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
    private readonly Dictionary<GameObject, bool> isConnectorCache = new();
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

        // Fast hit-path: avoid GetComponent/other work when cached.
        if (cache.TryGetValue((fixedPrefab, movingPrefab), out space))
            return true;

        // By design, only Room ↔ Connector configuration spaces are valid.
        var fixedIsConnector = IsConnectorCached(fixedPrefab);
        var movingIsConnector = IsConnectorCached(movingPrefab);
        if (fixedIsConnector == movingIsConnector)
        {
            space = ConfigurationSpace.Empty;
            cache[(fixedPrefab, movingPrefab)] = space;
            return true;
        }

        if (!shapeLibrary.TryGetShape(fixedPrefab, out var fixedShape, out error))
            return false;
        if (!shapeLibrary.TryGetShape(movingPrefab, out var movingShape, out error))
            return false;

        var offsets = ComputeOffsets(fixedShape, movingShape, fixedIsConnector, movingIsConnector);
        space = offsets.Count == 0 ? ConfigurationSpace.Empty : new ConfigurationSpace(offsets);
        if (offsets.Count == 0 && verbose)
            Debug.LogWarning($"[ConfigSpace] Empty offsets for {fixedPrefab.name} -> {movingPrefab.name}");
        cache[(fixedPrefab, movingPrefab)] = space;
        return true;
    }

    private bool IsConnectorCached(GameObject prefab)
    {
        if (prefab == null)
            return false;
        if (isConnectorCache.TryGetValue(prefab, out var cached))
            return cached;
        var isConn = prefab.GetComponent<ConnectorMeta>() != null;
        isConnectorCache[prefab] = isConn;
        return isConn;
    }

    private int NormalizeWidth(int width) => 1;
}
