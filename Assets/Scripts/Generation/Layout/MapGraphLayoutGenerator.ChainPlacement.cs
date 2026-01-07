// Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.ChainPlacement.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private List<LayoutState> AddChain(LayoutState baseState, MapGraphChainBuilder.Chain chain, int maxLayouts)
    {
        using var _ps = PS(S_AddChain);
        if (profiling != null)
            profiling.AddChainCalls++;
        var deepProfile = settings != null && settings.LogLayoutProfiling;

        var generated = new List<LayoutState>();
        if (chain == null || chain.Nodes == null || chain.Nodes.Count == 0)
        {
            lastFailureDetail = "Chain is null/empty.";
            return generated;
        }

        if (chain.Nodes.Count <= 2)
            return AddChainSmall(baseState, chain, maxLayouts);

        var initStart = profiling != null ? NowTicks() : 0;
        LayoutState current;
        using (PSIf(deepProfile, S_AddChain_InitLayout))
        {
            current = GetInitialLayout(baseState, chain);
        }
        if (profiling != null)
            profiling.GetInitialLayoutTicks += NowTicks() - initStart;
        if (current == null)
        {
            var chainIds = string.Join(",", chain.Nodes.Select(n => n?.id));
            lastFailureDetail = $"GetInitialLayout returned null for chain [{chainIds}]";
            Debug.LogWarning($"[LayoutGenerator] {lastFailureDetail}");
            return generated;
        }

        var rooms = current.Rooms;
        EnergyCache energyCache;
        using (PSIf(deepProfile, S_AddChain_BuildEnergyCache))
        {
            energyCache = current.EnergyCache ?? BuildEnergyCache(rooms);
        }
        float temperature = EstimateInitialTemperature(chain, rooms);

        var bestEnergy = energyCache.TotalEnergy;
        var bestRoomsSnapshot = settings.DebugNoLayouts ? CloneRoomsDeep(rooms) : null;

        var chainNodeIndices = new List<int>(chain.Nodes.Count);
        for (var i = 0; i < chain.Nodes.Count; i++)
        {
            var n = chain.Nodes[i];
            if (n == null || string.IsNullOrEmpty(n.id))
                continue;
            if (nodeIndexById != null && nodeIndexById.TryGetValue(n.id, out var idx))
                chainNodeIndices.Add(idx);
        }
        var movableNodeIndices = new List<int>(chainNodeIndices.Count);
        for (var i = 0; i < chainNodeIndices.Count; i++)
        {
            var idx = chainNodeIndices[i];
            if (idx >= 0 && idx < energyCache.NodeCount && energyCache.IsPlaced[idx])
                movableNodeIndices.Add(idx);
        }

        var pairChanges = new List<PairPenaltyChange>(Mathf.Max(4, rooms.Count));
        var edgeChanges = new List<EdgePenaltyChange>(Mathf.Max(4, rooms.Count));
        var positionCandidates = new List<Vector2Int>(Mathf.Max(16, settings.MaxFallbackCandidates));
        var wiggleCandidates = new List<Vector2Int>(Mathf.Max(16, settings.MaxWiggleCandidates));

        var saStart = profiling != null ? NowTicks() : 0;
        using (PSIf(deepProfile, S_AddChain_SA))
        {
            for (int t = 0; t < Mathf.Max(1, settings.TemperatureSteps); t++)
            {
                for (int i = 0; i < Mathf.Max(1, settings.InnerIterations); i++)
                {
                    var currentEnergy = energyCache.TotalEnergy;
                    pairChanges.Clear();
                    edgeChanges.Clear();
                    if (!TryPerturbInPlace(rooms, energyCache, movableNodeIndices, positionCandidates, wiggleCandidates, pairChanges, edgeChanges, out var undo))
                        continue;

                    var perturbedEnergy = energyCache.TotalEnergy;
                    var delta = perturbedEnergy - currentEnergy;

                    // Expensive O(n^2) validation is only paid when energy says we're at a “complete” solution
                    // (no illegal overlaps + all currently-placed edges touch).
                    using (PSIf(deepProfile, S_AddChain_OutputCheck))
                    {
                        if (ShouldValidateForOutput(energyCache) && DifferentEnough(rooms, generated, chain))
                        {
                            if (TryValidateLayout(rooms, out var validationError))
                            {
                                using (PSIf(deepProfile, S_AddChain_Snapshot))
                                {
                                    var snapshotRooms = CloneRoomsDeep(rooms);
                                    var snapshotCache = BuildEnergyCache(snapshotRooms);
                                    var snapshot = new LayoutState(snapshotRooms, baseState.ChainIndex, snapshotCache);
                                    generated.Add(snapshot);
                                }
                                if (profiling != null)
                                    profiling.CandidateLayoutsAccepted++;
                                if (generated.Count >= maxLayouts)
                                {
                                    if (profiling != null)
                                        profiling.SaLoopTicks += NowTicks() - saStart;
                                    return generated;
                                }
                            }
                            else
                            {
                                if (settings != null && settings.DebugEnergyMismatch)
                                {
                                    // DIAGNOSTIC: Why is energy low but layout invalid?
                                    var freshEnergy = ComputeEnergy(rooms);
                                    Debug.LogError($"[LayoutGenerator] Energy-Validation Mismatch! CachedEnergy={energyCache.TotalEnergy:F4} FreshEnergy={freshEnergy:F4}. Error: {validationError}");

                                    // Brute-force check removed. Fix applied in GetInitialLayout.
                                }
                            }
                        }
                    }

                    if (delta < 0f)
                        continue;

                    using (PSIf(deepProfile, S_AddChain_AcceptReject))
                    {
                        var p = Mathf.Exp(-delta / Mathf.Max(0.0001f, temperature));
                        if (rng.NextDouble() >= p)
                            UndoMove(rooms, energyCache, undo, pairChanges, edgeChanges);
                    }

                    if (settings.DebugNoLayouts)
                    {
                        var eNow = energyCache.TotalEnergy;
                        if (eNow < bestEnergy)
                        {
                            bestEnergy = eNow;
                            bestRoomsSnapshot = CloneRoomsDeep(rooms);
                        }
                    }
                }

                temperature *= Mathf.Clamp(settings.Cooling, 0.01f, 0.999f);
            }
        }

        if (profiling != null)
            profiling.SaLoopTicks += NowTicks() - saStart;

        if (generated.Count == 0)
        {
            var chainIds = string.Join(",", chain.Nodes.Select(n => n?.id));
            lastFailureDetail = $"AddChain produced 0 layouts for chain [{chainIds}]";
            Debug.LogWarning($"[LayoutGenerator] {lastFailureDetail}");

            if (settings.DebugNoLayouts && bestRoomsSnapshot != null)
                DebugNoLayoutsDump(chain, bestRoomsSnapshot);
        }

        return generated;
    }

    private List<LayoutState> AddChainSmall(LayoutState baseState, MapGraphChainBuilder.Chain chain, int maxLayouts)
    {
        using var _ps = PS(S_AddChainSmall);
        if (profiling != null)
            profiling.AddChainSmallCalls++;

        var generated = new List<LayoutState>();
        if (chain == null || chain.Nodes == null || chain.Nodes.Count == 0)
            return generated;

        if (chain.Edges == null || chain.Edges.Count == 0)
        {
            var rooms = new Dictionary<string, RoomPlacement>(baseState.Rooms);
            var cache = baseState.EnergyCache != null ? CloneEnergyCache(baseState.EnergyCache) : BuildEnergyCache(rooms);

            var node = chain.Nodes.FirstOrDefault(n => n != null && !string.IsNullOrEmpty(n.id));
            if (node == null)
                return generated;

            if (!rooms.ContainsKey(node.id))
            {
                var prefabs = GetRoomPrefabs(node);
                prefabs.Shuffle(rng);
                var singlePositionCandidates = new List<Vector2Int>(Mathf.Max(16, settings.MaxFallbackCandidates));

                GameObject bestPrefab = null;
                ModuleShape bestShape = null;
                Vector2Int bestRoot = default;
                float bestEnergy = float.MaxValue;
                for (var prefabIndex = 0; prefabIndex < prefabs.Count; prefabIndex++)
                {
                    var prefab = prefabs[prefabIndex];
                    if (prefab == null)
                        continue;
                    if (!shapeLibrary.TryGetShape(prefab, out var shape, out _))
                        continue;

                    FindPositionCandidates(node.id, prefab, shape, rooms, singlePositionCandidates);
                    for (var i = 0; i < singlePositionCandidates.Count; i++)
                    {
                        var pos = singlePositionCandidates[i];
                        var e = ComputeEnergyIfAddedAt(node.id, prefab, shape, pos, cache);
                        if (e < bestEnergy)
                        {
                            bestEnergy = e;
                            bestPrefab = prefab;
                            bestShape = shape;
                            bestRoot = pos;
                        }
                    }
                }

                if (bestPrefab == null || bestShape == null)
                    return generated;

                var bestPlacement = new RoomPlacement(node.id, bestPrefab, bestShape, bestRoot);
                AddPlacementToEnergyCacheInPlace(rooms, cache, bestPlacement);
                rooms[node.id] = bestPlacement;
            }

            if (ShouldValidateForOutput(cache) && IsValidLayout(rooms))
            {
                generated.Add(new LayoutState(rooms, baseState.ChainIndex, cache));
                if (profiling != null)
                    profiling.CandidateLayoutsAccepted++;
            }
            return generated;
        }

        if (chain.Nodes.Count != 2)
            return generated;

        var baseRooms = new Dictionary<string, RoomPlacement>(baseState.Rooms);
        var baseCache2 = baseState.EnergyCache ?? BuildEnergyCache(baseRooms);

        var order = BuildChainBfsOrder(chain, baseRooms);
        if (order.Count < 2)
            return generated;

        var first = order[0];
        var second = order[1];
        if (first == null || second == null || string.IsNullOrEmpty(first.id) || string.IsNullOrEmpty(second.id))
            return generated;

        var positionCandidates = new List<Vector2Int>(Mathf.Max(16, settings.MaxFallbackCandidates));
        var firstStates = new List<(Dictionary<string, RoomPlacement> rooms, EnergyCache cache)>();

        if (baseRooms.TryGetValue(first.id, out var placedFirst) && placedFirst != null)
        {
            firstStates.Add((baseRooms, baseCache2));
        }
        else
        {
            var prefabs = GetRoomPrefabs(first);
            prefabs.Shuffle(rng);
            for (var prefabIndex = 0; prefabIndex < prefabs.Count; prefabIndex++)
            {
                var prefab = prefabs[prefabIndex];
                if (prefab == null)
                    continue;
                if (!shapeLibrary.TryGetShape(prefab, out var shape, out _))
                    continue;

                FindPositionCandidates(first.id, prefab, shape, baseRooms, positionCandidates);
                for (var i = 0; i < positionCandidates.Count; i++)
                {
                    var pos = positionCandidates[i];
                    var rooms1 = new Dictionary<string, RoomPlacement>(baseRooms)
                    {
                        [first.id] = new RoomPlacement(first.id, prefab, shape, pos)
                    };
                    var cache1 = BuildEnergyCache(rooms1);
                    firstStates.Add((rooms1, cache1));
                    if (firstStates.Count >= Mathf.Max(8, maxLayouts * 2))
                        break;
                }
                if (firstStates.Count >= Mathf.Max(8, maxLayouts * 2))
                    break;
            }
        }

        for (var stateIndex = 0; stateIndex < firstStates.Count; stateIndex++)
        {
            var (rooms1, _) = firstStates[stateIndex];
            if (rooms1.TryGetValue(second.id, out var placedSecond) && placedSecond != null)
            {
                var cache2 = BuildEnergyCache(rooms1);
                if (ShouldValidateForOutput(cache2) && IsValidLayout(rooms1) && DifferentEnough(rooms1, generated, chain))
                {
                    generated.Add(new LayoutState(new Dictionary<string, RoomPlacement>(rooms1), baseState.ChainIndex, cache2));
                    if (profiling != null)
                        profiling.CandidateLayoutsAccepted++;
                    if (generated.Count >= maxLayouts)
                        return generated;
                }
                continue;
            }

            var prefabs2 = GetRoomPrefabs(second);
            prefabs2.Shuffle(rng);
            for (var prefabIndex = 0; prefabIndex < prefabs2.Count; prefabIndex++)
            {
                var prefab = prefabs2[prefabIndex];
                if (prefab == null)
                    continue;
                if (!shapeLibrary.TryGetShape(prefab, out var shape, out _))
                    continue;

                FindPositionCandidates(second.id, prefab, shape, rooms1, positionCandidates);
                for (var i = 0; i < positionCandidates.Count; i++)
                {
                    var pos = positionCandidates[i];
                    var rooms2 = new Dictionary<string, RoomPlacement>(rooms1)
                    {
                        [second.id] = new RoomPlacement(second.id, prefab, shape, pos)
                    };
                    var cache2 = BuildEnergyCache(rooms2);
                    if (!ShouldValidateForOutput(cache2))
                        continue;
                    if (!IsValidLayout(rooms2))
                        continue;
                    if (!DifferentEnough(rooms2, generated, chain))
                        continue;

                    generated.Add(new LayoutState(rooms2, baseState.ChainIndex, cache2));
                    if (profiling != null)
                        profiling.CandidateLayoutsAccepted++;
                    if (generated.Count >= maxLayouts)
                        return generated;
                }
            }
        }

        return generated;
    }

    private LayoutState GetInitialLayout(LayoutState baseState, MapGraphChainBuilder.Chain chain)
    {
        var deepProfile = settings != null && settings.LogLayoutProfiling;

        Dictionary<string, RoomPlacement> rooms;
        using (PSIf(deepProfile, S_InitLayout_CloneRooms))
        {
            // SA mutates placements in-place; deep clone to avoid affecting other LayoutState instances.
            rooms = CloneRoomsDeep(baseState.Rooms);
        }

        EnergyCache cache;
        using (PSIf(deepProfile, S_InitLayout_CloneCache))
        {
            cache = baseState.EnergyCache != null ? CloneEnergyCache(baseState.EnergyCache) : BuildEnergyCache(rooms);
            // CRITICAL FIX: Rebind cache to the new room objects.
            // CloneEnergyCache shallow-copies the PlacementsByIndex array, so it points to the OLD instances.
            // We must update it to point to the NEW instances in 'rooms' so that SA modifications 
            // are reflected in the 'rooms' dictionary (which is returned in the result).
            if (baseState.EnergyCache != null && rooms.Count > 0)
            {
                foreach (var kv in rooms)
                {
                    if (nodeIndexById.TryGetValue(kv.Key, out var idx) && idx >= 0 && idx < cache.NodeCount)
                    {
                        cache.PlacementsByIndex[idx] = kv.Value;
                    }
                }
            }
        }

        List<MapGraphAsset.NodeData> order;
        using (PSIf(deepProfile, S_InitLayout_BuildOrder))
        {
            order = BuildChainBfsOrder(chain, rooms);
        }
        var positionCandidates = new List<Vector2Int>(Mathf.Max(16, settings.MaxFallbackCandidates));
        using (PSIf(deepProfile, S_InitLayout_NodeLoop))
        {
            foreach (var node in order)
            {
                if (node == null || string.IsNullOrEmpty(node.id))
                    continue;
                if (rooms.ContainsKey(node.id))
                    continue;

                List<GameObject> prefabs;
                using (PSIf(deepProfile, S_InitLayout_GetPrefabs))
                {
                    prefabs = GetRoomPrefabs(node);
                }
                using (PSIf(deepProfile, S_InitLayout_Shuffle))
                {
                    prefabs.Shuffle(rng);
                }

                GameObject bestPrefab = null;
                ModuleShape bestShape = null;
                Vector2Int bestRoot = default;
                float bestEnergy = float.MaxValue;
                if (profiling != null)
                    profiling.InitLayoutNodesScored++;
                for (var prefabIndex = 0; prefabIndex < prefabs.Count; prefabIndex++)
                {
                    var prefab = prefabs[prefabIndex];
                    if (prefab == null)
                        continue;

                    ModuleShape shape;
                    using (PSIf(deepProfile, S_InitLayout_TryGetShape))
                    {
                        if (!shapeLibrary.TryGetShape(prefab, out shape, out _))
                            continue;
                    }

                    using (PSIf(deepProfile, S_InitLayout_FindCandidates))
                    {
                        FindPositionCandidates(node.id, prefab, shape, rooms, positionCandidates);
                    }
                    if (profiling != null)
                        profiling.InitLayoutCandidatesGenerated += positionCandidates.Count;

                    using (PSIf(deepProfile, S_InitLayout_ScoreCandidates))
                    {
                        for (var i = 0; i < positionCandidates.Count; i++)
                        {
                            if (profiling != null)
                                profiling.InitLayoutCandidatesScored++;
                            var pos = positionCandidates[i];
                            var energy = ComputeEnergyIfAddedAt(node.id, prefab, shape, pos, cache);
                            if (energy < bestEnergy)
                            {
                                bestEnergy = energy;
                                bestPrefab = prefab;
                                bestShape = shape;
                                bestRoot = pos;
                            }
                        }
                    }
                }

                if (bestPrefab == null || bestShape == null)
                {
                    var chainIds = string.Join(",", chain.Nodes.Select(n => n?.id));
                    lastFailureDetail = $"No candidates for node {node.id} in chain [{chainIds}]";
                    Debug.LogWarning($"[LayoutGenerator] {lastFailureDetail}");
                    return null;
                }

                using (PSIf(deepProfile, S_InitLayout_AddPlacement))
                {
                    var bestPlacement = new RoomPlacement(node.id, bestPrefab, bestShape, bestRoot);
                    AddPlacementToEnergyCacheInPlace(rooms, cache, bestPlacement);
                    rooms[node.id] = bestPlacement;
                }
            }
        }

        return new LayoutState(rooms, baseState.ChainIndex, cache);
    }

    private List<MapGraphAsset.NodeData> BuildChainBfsOrder(MapGraphChainBuilder.Chain chain, Dictionary<string, RoomPlacement> placed)
    {
        var result = new List<MapGraphAsset.NodeData>();
        var queue = new Queue<MapGraphAsset.NodeData>();
        var seen = new HashSet<string>();

        foreach (var node in chain.Nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.id))
                continue;
            var hasPlacedNeighbor = graphAsset.GetEdgesFor(node.id).Any(e =>
                placed.ContainsKey(e.fromNodeId) || placed.ContainsKey(e.toNodeId));
            if (hasPlacedNeighbor)
            {
                queue.Enqueue(node);
                seen.Add(node.id);
            }
        }

        if (queue.Count == 0 && chain.Nodes.Count > 0)
        {
            var first = chain.Nodes[0];
            if (first != null)
            {
                queue.Enqueue(first);
                seen.Add(first.id);
            }
        }

        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            result.Add(n);
            foreach (var e in graphAsset.GetEdgesFor(n.id))
            {
                var otherId = e.fromNodeId == n.id ? e.toNodeId : e.fromNodeId;
                if (string.IsNullOrEmpty(otherId))
                    continue;
                var other = chain.Nodes.FirstOrDefault(nd => nd != null && nd.id == otherId);
                if (other == null || seen.Contains(other.id))
                    continue;
                queue.Enqueue(other);
                seen.Add(other.id);
            }
        }

        foreach (var node in chain.Nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.id))
                continue;
            if (seen.Contains(node.id))
                continue;
            result.Add(node);
            seen.Add(node.id);
        }

        return result;
    }

    private bool ShouldValidateForOutput(EnergyCache cache)
    {
        if (cache == null)
            return false;
        return cache.OverlapPenaltySum <= 0.0001f &&
               cache.DistancePenaltySum <= 0.0001f;
    }

    private bool DifferentEnough(Dictionary<string, RoomPlacement> candidateRooms, List<LayoutState> existing, MapGraphChainBuilder.Chain chain)
    {
        foreach (var other in existing)
        {
            if (!DiffersOnChain(candidateRooms, other.Rooms, chain))
                return false;
        }
        return true;
    }

    private bool DiffersOnChain(Dictionary<string, RoomPlacement> a, Dictionary<string, RoomPlacement> b, MapGraphChainBuilder.Chain chain)
    {
        foreach (var node in chain.Nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.id))
                continue;
            var hasA = a.TryGetValue(node.id, out var pa);
            var hasB = b.TryGetValue(node.id, out var pb);
            if (hasA != hasB)
                return true;
            if (!hasA)
                continue;
            if (pa.Prefab != pb.Prefab)
                return true;
            if (pa.Root != pb.Root)
            {
                var delta = pa.Root - pb.Root;
                var manhattan = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
                if (manhattan >= 2)
                    return true;
            }
        }
        return false;
    }
}
