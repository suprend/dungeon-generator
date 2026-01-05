// Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Warmup.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed partial class MapGraphLayoutGenerator
{
    private bool TryBuildLayoutContext(
        MapGraphAsset graphAsset,
        TileStampService stamp,
        List<MapGraphChainBuilder.Chain> precomputedChains,
        out LayoutContext ctx,
        out string error)
    {
        ctx = null;
        error = null;
        if (graphAsset == null)
        {
            error = "Graph asset is null.";
            return false;
        }
        if (stamp == null)
        {
            error = "TileStampService is required for layout generation.";
            return false;
        }

        // Neighbor lookup / indices.
        var neighborLookupLocal = neighborLookup;
        if (neighborLookupLocal == null)
        {
            using (PS(S_BuildNeighborLookup))
            {
                neighborLookupLocal = BuildNeighborLookup(graphAsset);
            }
        }

        // Keep the existing helper that builds node indices based on the current neighbor lookup.
        neighborLookup = neighborLookupLocal;
        BuildNodeIndexAndAdjacency(graphAsset);

        // Chains.
        List<MapGraphChainBuilder.Chain> chains;
        if (precomputedChains != null)
        {
            chains = precomputedChains;
        }
        else
        {
            var facesChainsStart = profiling != null ? NowTicks() : 0;
            using (PS(S_BuildFacesChains))
            {
                var prevFaceBuilderDebug = MapGraphFaceBuilder.LogProfiling;
                MapGraphFaceBuilder.SetDebug(settings.LogLayoutProfiling);
                if (!MapGraphFaceBuilder.TryBuildFaces(graphAsset, out var faces, out error))
                {
                    MapGraphFaceBuilder.SetDebug(prevFaceBuilderDebug);
                    return false;
                }
                if (!MapGraphChainBuilder.TryBuildChains(graphAsset, faces, out chains, out error))
                {
                    MapGraphFaceBuilder.SetDebug(prevFaceBuilderDebug);
                    return false;
                }
                MapGraphFaceBuilder.SetDebug(prevFaceBuilderDebug);
            }
            if (profiling != null)
                profiling.FacesChainsTicks += NowTicks() - facesChainsStart;
        }
        chains ??= new List<MapGraphChainBuilder.Chain>();

        // Libraries (long-lived per stampKey).
        var stampKey = (stamp.Grid, stamp.FloorMap, stamp.WallMap);
        if (!ShapeLibrariesByStamp.TryGetValue(stampKey, out var shapeLib))
        {
            shapeLib = new ShapeLibrary(stamp);
            ShapeLibrariesByStamp[stampKey] = shapeLib;
        }
        if (!ConfigSpaceLibrariesByStamp.TryGetValue(stampKey, out var csLib))
        {
            csLib = new ConfigurationSpaceLibrary(shapeLib);
            ConfigSpaceLibrariesByStamp[stampKey] = csLib;
        }
        csLib.SetDebug(settings.VerboseConfigSpaceLogs, settings.MaxConfigSpaceLogs);

        // Prefabs lookup.
        Dictionary<string, List<GameObject>> roomLookup;
        using (PS(S_BuildRoomPrefabLookup))
        {
            roomLookup = BuildRoomPrefabLookup(graphAsset);
        }
        if (roomLookup.Count == 0)
        {
            error = "No room prefabs available for layout generation.";
            return false;
        }

        // Connector prefab set.
        var connectorPrefabsLocal = new HashSet<GameObject>();
        using (PS(S_BuildConnectorPrefabSet))
        {
            foreach (var edge in graphAsset.Edges)
            {
                var conn = edge?.connectionType ?? graphAsset.DefaultConnectionType;
                if (conn?.prefabs == null)
                    continue;
                foreach (var p in conn.prefabs)
                    if (p != null) connectorPrefabsLocal.Add(p);
            }
        }

        // Warmup shapes.
        var warmupShapesStart = profiling != null ? NowTicks() : 0;
        using (PS(S_WarmupShapes))
        {
            foreach (var prefab in roomLookup.Values.SelectMany(x => x).Distinct())
            {
                if (!shapeLib.TryGetShape(prefab, out _, out error))
                    return false;
            }
        }
        if (profiling != null)
            profiling.WarmupShapesTicks += NowTicks() - warmupShapesStart;

        var prefabList = roomLookup.Values.SelectMany(x => x).Distinct().ToList();
        if (settings.LogConfigSpaceSizeSummary)
        {
            var shapeCount = 0;
            var floorMin = int.MaxValue;
            var floorMax = 0;
            long floorSum = 0;
            var wallMin = int.MaxValue;
            var wallMax = 0;
            long wallSum = 0;

            var topShapes = new List<(int total, int floor, int wall, string name)>();
            const int maxTopShapes = 8;

            foreach (var prefab in prefabList)
            {
                if (prefab == null)
                    continue;
                if (!shapeLib.TryGetShape(prefab, out var shape, out _))
                    continue;

                var floor = shape?.FloorCells?.Count ?? 0;
                var wall = shape?.WallCells?.Count ?? 0;
                var total = floor + wall;
                shapeCount++;
                floorMin = Mathf.Min(floorMin, floor);
                floorMax = Mathf.Max(floorMax, floor);
                floorSum += floor;
                wallMin = Mathf.Min(wallMin, wall);
                wallMax = Mathf.Max(wallMax, wall);
                wallSum += wall;

                if (topShapes.Count < maxTopShapes)
                {
                    topShapes.Add((total, floor, wall, prefab.name));
                    topShapes.Sort((a, b) => b.total.CompareTo(a.total));
                }
                else if (total > topShapes[topShapes.Count - 1].total)
                {
                    topShapes[topShapes.Count - 1] = (total, floor, wall, prefab.name);
                    topShapes.Sort((a, b) => b.total.CompareTo(a.total));
                }
            }

            if (shapeCount > 0)
            {
                var floorAvg = floorSum / (float)shapeCount;
                var wallAvg = wallSum / (float)shapeCount;
                var top = topShapes.Count > 0
                    ? string.Join(", ", topShapes.Select(s => $"{s.name}:{s.total} (f={s.floor},w={s.wall})"))
                    : "<none>";
                Debug.Log($"[LayoutGenerator] Shape sizes: prefabs={shapeCount} floor[min={floorMin} max={floorMax} avg={floorAvg:0.0}] wall[min={wallMin} max={wallMax} avg={wallAvg:0.0}] top=[{top}]");
            }
        }

        // Warmup config spaces (only room<->connector pairs).
        var csPairs = 0;
        var csEmpty = 0;
        var csMin = int.MaxValue;
        var csMax = 0;
        long csSum = 0;
        var csNonEmptyPairs = 0;
        long csNonEmptySum = 0;
        var topPairs = settings.LogConfigSpaceSizeSummary
            ? new List<(int count, string fixedName, string movingName)>()
            : null;
        var maxTop = Mathf.Clamp(settings.MaxConfigSpaceSizePairs, 0, 64);
        var warmupCsStart = profiling != null ? NowTicks() : 0;
        using (PS(S_WarmupConfigSpaces))
        {
            for (int i = 0; i < prefabList.Count; i++)
            {
                for (int j = 0; j < prefabList.Count; j++)
                {
                    var iIsConnector = connectorPrefabsLocal.Contains(prefabList[i]);
                    var jIsConnector = connectorPrefabsLocal.Contains(prefabList[j]);
                    if (iIsConnector == jIsConnector)
                        continue;

                    if (!csLib.TryGetSpace(prefabList[i], prefabList[j], out var space, out error))
                        return false;

                    if (settings.LogConfigSpaceSizeSummary && space != null)
                    {
                        var count = space.Offsets != null ? space.Offsets.Count : 0;
                        csPairs++;
                        csSum += count;
                        csMin = Mathf.Min(csMin, count);
                        csMax = Mathf.Max(csMax, count);
                        if (count == 0) csEmpty++;
                        else
                        {
                            csNonEmptyPairs++;
                            csNonEmptySum += count;
                        }

                        if (maxTop > 0)
                        {
                            var fixedName = prefabList[i] != null ? prefabList[i].name : "<null>";
                            var movingName = prefabList[j] != null ? prefabList[j].name : "<null>";
                            if (topPairs.Count < maxTop)
                            {
                                topPairs.Add((count, fixedName, movingName));
                                topPairs.Sort((a, b) => b.count.CompareTo(a.count));
                            }
                            else if (count > topPairs[topPairs.Count - 1].count)
                            {
                                topPairs[topPairs.Count - 1] = (count, fixedName, movingName);
                                topPairs.Sort((a, b) => b.count.CompareTo(a.count));
                            }
                        }
                    }
                }
            }
        }
        if (profiling != null)
            profiling.WarmupConfigSpacesTicks += NowTicks() - warmupCsStart;

        if (settings.LogConfigSpaceSizeSummary && csPairs > 0)
        {
            var avg = csSum / (float)csPairs;
            var avgNonEmpty = csNonEmptyPairs > 0 ? csNonEmptySum / (float)csNonEmptyPairs : 0f;
            var top = topPairs != null && topPairs.Count > 0
                ? string.Join(", ", topPairs.Select(p => $"{p.fixedName}->{p.movingName}:{p.count}"))
                : "<none>";
            Debug.Log($"[LayoutGenerator] ConfigSpace sizes: pairs={csPairs} empty={csEmpty} nonEmpty={csNonEmptyPairs} min={csMin} max={csMax} avg={avg:0.0} avgNonEmpty={avgNonEmpty:0.0} top=[{top}]");
        }

        ctx = new LayoutContext
        {
            OrderedChains = chains,
            ShapeLibrary = shapeLib,
            ConfigSpaceLibrary = csLib,
            RoomPrefabLookup = roomLookup,
            ConnectorPrefabs = connectorPrefabsLocal,
            NeighborLookup = neighborLookupLocal,
            NodeIndexById = nodeIndexById,
            NodeIdByIndex = nodeIdByIndex,
            NeighborIndicesByIndex = neighborIndicesByIndex
        };
        return true;
    }
}
