// Assets/scripts/Generation/Graph/MapGraphLevelSolver.Geometry.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class MapGraphLevelSolver
{
    /// <summary>
    /// Precomputes shapes and configuration spaces for all prefabs referenced by the graph asset.
    /// </summary>
    public bool PrecomputeGeometry(TileStampService stamp, out string error)
    {
        error = null;
        if (graphAsset == null)
        {
            error = "Graph asset is null.";
            return false;
        }

        shapeLibrary = new ShapeLibrary(stamp);
        configSpaceLibrary = new ConfigurationSpaceLibrary(shapeLibrary);

        var prefabs = new HashSet<GameObject>();
        var connectorPrefabs = new HashSet<GameObject>();
        // Collect room prefabs
        foreach (var node in graphAsset.Nodes)
        {
            if (node?.roomType?.prefabs == null) continue;
            foreach (var prefab in node.roomType.prefabs)
                if (prefab != null) prefabs.Add(prefab);
        }

        // Default room type prefabs
        if (graphAsset.DefaultRoomType?.prefabs != null)
            foreach (var prefab in graphAsset.DefaultRoomType.prefabs)
                if (prefab != null) prefabs.Add(prefab);

        // Collect connector prefabs
        foreach (var edge in graphAsset.Edges)
        {
            var conn = edge?.connectionType ?? graphAsset.DefaultConnectionType;
            if (conn?.prefabs == null) continue;
            foreach (var prefab in conn.prefabs)
                if (prefab != null)
                {
                    prefabs.Add(prefab);
                    connectorPrefabs.Add(prefab);
                }
        }

        // Build shapes
        foreach (var prefab in prefabs)
        {
            if (!shapeLibrary.TryGetShape(prefab, out _, out error))
                return false;
        }

        // Build configuration spaces for all ordered pairs
        var prefabList = prefabs.ToList();
        for (int i = 0; i < prefabList.Count; i++)
        {
            for (int j = 0; j < prefabList.Count; j++)
            {
                if (connectorPrefabs.Contains(prefabList[i]) && connectorPrefabs.Contains(prefabList[j]))
                    continue;
                if (!configSpaceLibrary.TryGetSpace(prefabList[i], prefabList[j], out _, out error))
                    return false;
            }
        }

        return true;
    }
}

