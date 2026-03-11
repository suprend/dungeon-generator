// Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.ChainPlacement.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private readonly struct CycleClosureState
    {
        public int EndAIndex { get; }
        public int EndBIndex { get; }
        public int RemainingDirection { get; }
        public int RemainingCount { get; }
        public int PlacedCount { get; }

        public CycleClosureState(int endAIndex, int endBIndex, int remainingDirection, int remainingCount, int placedCount)
        {
            EndAIndex = endAIndex;
            EndBIndex = endBIndex;
            RemainingDirection = remainingDirection;
            RemainingCount = remainingCount;
            PlacedCount = placedCount;
        }
    }

    private List<LayoutState> AddChain(LayoutState baseState, MapGraphChainBuilder.Chain chain, int maxLayouts)
    {
        using var _ps = PS(S_AddChain);
        if (!CheckLayoutDeadline("chain expansion"))
            return new List<LayoutState>();
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
        if (!CheckLayoutDeadline("chain initial layout"))
            return generated;
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

        var canFallbackSa = settings != null && (settings.UseConflictDrivenTargetSelection || settings.OverlapPenaltyCap > 0);

	        var saStart = profiling != null ? NowTicks() : 0;
	        using (PSIf(deepProfile, S_AddChain_SA))
	        {
	            var prevAnnealing = annealingActive;
	            annealingActive = true;
	            try
	            {
	                for (int t = 0; t < Mathf.Max(1, settings.TemperatureSteps); t++)
	                {
                        if (!CheckLayoutDeadline("SA"))
                            return generated;
	                    for (int i = 0; i < Mathf.Max(1, settings.InnerIterations); i++)
	                    {
                            if ((i & 15) == 0 && !CheckLayoutDeadline("SA"))
                                return generated;
	                        var currentEnergy = energyCache.TotalEnergy;
	                        pairChanges.Clear();
	                        edgeChanges.Clear();
	                        if (!TryPerturbInPlace(rooms, energyCache, movableNodeIndices, positionCandidates, wiggleCandidates, pairChanges, edgeChanges, out var undo))
	                            continue;

	                        var currentWithTopology = currentEnergy + undo.OldTopologyPenalty;
	                        var perturbedEnergy = energyCache.TotalEnergy;
	                        var perturbedWithTopology = perturbedEnergy + ComputeLocalTopologyPenalty(energyCache, undo.NodeIndex);
	                        var delta = perturbedWithTopology - currentWithTopology;

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
	            finally
	            {
	                annealingActive = prevAnnealing;
	            }
	        }

        if (profiling != null)
            profiling.SaLoopTicks += NowTicks() - saStart;

        if (generated.Count == 0 && canFallbackSa)
        {
            var chainIds = string.Join(",", chain.Nodes.Select(n => n?.id));
            Debug.LogWarning($"[LayoutGenerator] SA produced 0 layouts for chain [{chainIds}]. Retrying with safer settings (random target selection + exact overlaps, fresh init).");

            var prevConflict = settings.UseConflictDrivenTargetSelection;
            var prevCap = settings.OverlapPenaltyCap;
            var prevSlack = settings.OverlapPenaltyCapSlack;

            // Rebuild starting state for retry (fresh initial layout).
            var retryState = GetInitialLayout(baseState, chain);
            if (retryState == null)
            {
                Debug.LogWarning($"[LayoutGenerator] Retry init failed: GetInitialLayout returned null for chain [{chainIds}].");
            }
            else
            {
                rooms = retryState.Rooms;
                energyCache = retryState.EnergyCache ?? BuildEnergyCache(rooms);
                temperature = EstimateInitialTemperature(chain, rooms);

                movableNodeIndices.Clear();
                for (var i = 0; i < chainNodeIndices.Count; i++)
                {
                    var idx = chainNodeIndices[i];
                    if (idx >= 0 && idx < energyCache.NodeCount && energyCache.IsPlaced[idx])
                        movableNodeIndices.Add(idx);
                }

                var saRetryStart = profiling != null ? NowTicks() : 0;
                using (PSIf(deepProfile, S_AddChain_SA))
                {
                    var prevAnnealing = annealingActive;
                    annealingActive = true;
                    settings.UseConflictDrivenTargetSelection = false;
                    settings.OverlapPenaltyCap = 0;
                    settings.OverlapPenaltyCapSlack = 0;

                    try
                    {
                        for (int t = 0; t < Mathf.Max(1, settings.TemperatureSteps); t++)
                        {
                            if (!CheckLayoutDeadline("SA fallback"))
                                return generated;
                            for (int i = 0; i < Mathf.Max(1, settings.InnerIterations); i++)
                            {
                                if ((i & 15) == 0 && !CheckLayoutDeadline("SA fallback"))
                                    return generated;
                                var currentEnergy = energyCache.TotalEnergy;
                                pairChanges.Clear();
                                edgeChanges.Clear();
                                if (!TryPerturbInPlace(rooms, energyCache, movableNodeIndices, positionCandidates, wiggleCandidates, pairChanges, edgeChanges, out var undo))
                                    continue;

                                var currentWithTopology = currentEnergy + undo.OldTopologyPenalty;
                                var perturbedEnergy = energyCache.TotalEnergy;
                                var perturbedWithTopology = perturbedEnergy + ComputeLocalTopologyPenalty(energyCache, undo.NodeIndex);
                                var delta = perturbedWithTopology - currentWithTopology;

                                using (PSIf(deepProfile, S_AddChain_OutputCheck))
                                {
                                    if (ShouldValidateForOutput(energyCache) && DifferentEnough(rooms, generated, chain))
                                    {
                                        if (TryValidateLayout(rooms, out _))
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
                                                    profiling.SaLoopTicks += NowTicks() - saRetryStart;
                                                return generated;
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
                            }

                            temperature *= Mathf.Clamp(settings.Cooling, 0.01f, 0.999f);
                        }
                    }
                    finally
                    {
                        settings.UseConflictDrivenTargetSelection = prevConflict;
                        settings.OverlapPenaltyCap = prevCap;
                        settings.OverlapPenaltyCapSlack = prevSlack;
                        annealingActive = prevAnnealing;
                    }
                }

                if (profiling != null)
                    profiling.SaLoopTicks += NowTicks() - saRetryStart;
            }

        }

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
        if (!CheckLayoutDeadline("small chain expansion"))
            return new List<LayoutState>();
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
                            var score = energy
                                - ComputeTopologyExpansionBias(node.id, pos, rooms)
                                + ComputeCycleClosurePenalty(chain, node.id, pos, rooms);
                            if (score < bestEnergy)
                            {
                                bestEnergy = score;
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

    private float ComputeCycleClosurePenalty(
        MapGraphChainBuilder.Chain chain,
        string candidateNodeId,
        Vector2Int candidateRoot,
        Dictionary<string, RoomPlacement> placedRooms)
    {
        if (settings == null || !settings.UseCycleClosureBias || chain == null || !chain.IsCycle)
            return 0f;
        if (chain.Nodes == null || chain.Nodes.Count < 4)
            return 0f;
        if (placedRooms == null)
            return 0f;
        if (!TryBuildCycleClosureState(chain, placedRooms, candidateNodeId, out var state))
            return 0f;
        if (state.RemainingCount <= 0)
            return 0f;
        if (!TryGetHypotheticalCycleRoot(chain, state.EndAIndex, placedRooms, candidateNodeId, candidateRoot, out var rootA) ||
            !TryGetHypotheticalCycleRoot(chain, state.EndBIndex, placedRooms, candidateNodeId, candidateRoot, out var rootB))
            return 0f;

        var gap = Vector2.Distance(rootA, rootB);
        ComputeCycleGapRange(chain, state, out var gapMin, out var gapPreferredBase, out var gapMax);
        if (gapMax <= 0f || gapMax < gapMin)
            return 0f;

        var progress = state.PlacedCount / (float)chain.Nodes.Count;
        var weight = Mathf.Lerp(0.05f, 1.25f, progress * progress);
        if (state.RemainingCount <= 2)
            weight *= 1.5f;
        var preferredGap = ComputePreferredCycleGap(chain, state, gapMin, gapPreferredBase, gapMax);

        var penalty = 0f;
        if (gap < gapMin)
        {
            var delta = gapMin - gap;
            penalty += weight * delta * delta;
        }
        else if (gap > gapMax)
        {
            var delta = gap - gapMax;
            penalty += weight * delta * delta;
        }
        else
        {
            if (gap < preferredGap)
            {
                var delta = preferredGap - gap;
                penalty += weight * 0.18f * delta * delta;
            }
            else
            {
                var delta = gap - preferredGap;
                penalty += weight * 0.02f * delta * delta;
            }
        }

        penalty += ComputeCycleParallelBranchPenalty(chain, state, candidateNodeId, candidateRoot, placedRooms, preferredGap, weight);

        penalty -= ComputeCycleOutwardBias(chain, state, candidateNodeId, candidateRoot, placedRooms);
        return penalty;
    }

    private float ComputePreferredCycleGap(MapGraphChainBuilder.Chain chain, CycleClosureState state, float gapMin, float gapPreferredBase, float gapMax)
    {
        if (gapMax <= gapMin)
            return gapMin;

        var count = Mathf.Max(1, chain?.Nodes?.Count ?? 0);
        var closeProgress = 1f - (state.RemainingCount / (float)Mathf.Max(1, count - 2));
        closeProgress = Mathf.Clamp01(closeProgress);

        var wideTarget = Mathf.Lerp(gapPreferredBase, gapMax, 0.15f);
        var lateTarget = Mathf.Lerp(gapPreferredBase, gapMin, 0.1f);
        return Mathf.Lerp(wideTarget, lateTarget, closeProgress * closeProgress);
    }

    private bool TryBuildCycleClosureState(
        MapGraphChainBuilder.Chain chain,
        Dictionary<string, RoomPlacement> placedRooms,
        string candidateNodeId,
        out CycleClosureState state)
    {
        state = default;
        if (chain == null || !chain.IsCycle || chain.Nodes == null)
            return false;

        var nodeCount = chain.Nodes.Count;
        if (nodeCount < 4)
            return false;

        var placed = new bool[nodeCount];
        var placedCount = 0;
        for (var i = 0; i < nodeCount; i++)
        {
            var nodeId = chain.Nodes[i]?.id;
            var isPlaced = !string.IsNullOrEmpty(nodeId) &&
                           (string.Equals(nodeId, candidateNodeId, StringComparison.Ordinal) || placedRooms.ContainsKey(nodeId));
            placed[i] = isPlaced;
            if (isPlaced)
                placedCount++;
        }

        if (placedCount < 3 || placedCount >= nodeCount)
            return false;

        var ends = new List<int>(2);
        for (var i = 0; i < nodeCount; i++)
        {
            if (!placed[i])
                continue;
            var prevPlaced = placed[WrapCycleIndex(i - 1, nodeCount)];
            var nextPlaced = placed[WrapCycleIndex(i + 1, nodeCount)];
            var degree = (prevPlaced ? 1 : 0) + (nextPlaced ? 1 : 0);
            if (degree == 1)
                ends.Add(i);
        }

        if (ends.Count != 2)
            return false;

        var forwardAllUnplaced = TryGetCycleInteriorPath(ends[0], ends[1], 1, placed, out var forwardCount);
        var backwardAllUnplaced = TryGetCycleInteriorPath(ends[0], ends[1], -1, placed, out var backwardCount);

        if (forwardAllUnplaced == backwardAllUnplaced)
            return false;

        state = forwardAllUnplaced
            ? new CycleClosureState(ends[0], ends[1], 1, forwardCount, placedCount)
            : new CycleClosureState(ends[0], ends[1], -1, backwardCount, placedCount);
        return true;
    }

    private bool TryGetCycleInteriorPath(int fromIndex, int toIndex, int direction, bool[] placed, out int interiorCount)
    {
        interiorCount = 0;
        if (placed == null || placed.Length == 0)
            return false;

        var nodeCount = placed.Length;
        var index = WrapCycleIndex(fromIndex + direction, nodeCount);
        while (index != toIndex)
        {
            if (placed[index])
                return false;
            interiorCount++;
            index = WrapCycleIndex(index + direction, nodeCount);
        }

        return true;
    }

    private void ComputeCycleGapRange(
        MapGraphChainBuilder.Chain chain,
        CycleClosureState state,
        out float gapMin,
        out float gapPreferred,
        out float gapMax)
    {
        gapMin = 0f;
        gapPreferred = 0f;
        gapMax = 0f;
        if (chain?.Nodes == null || chain.Nodes.Count == 0)
            return;

        var nodeCount = chain.Nodes.Count;
        var currentIndex = state.EndAIndex;
        var nextIndex = WrapCycleIndex(currentIndex + state.RemainingDirection, nodeCount);
        while (currentIndex != state.EndBIndex)
        {
            var currentId = chain.Nodes[currentIndex]?.id;
            var nextId = chain.Nodes[nextIndex]?.id;
            if (!string.IsNullOrEmpty(currentId) &&
                !string.IsNullOrEmpty(nextId) &&
                cycleEdgeGapStatsByEdge != null &&
                cycleEdgeGapStatsByEdge.TryGetValue(MapGraphKey.NormalizeKey(currentId, nextId), out var stats))
            {
                gapMin += stats.MinStep;
                gapPreferred += stats.PreferredStep;
                gapMax += stats.MaxStep;
            }
            else
            {
                gapMin += 1f;
                gapPreferred += 4f;
                gapMax += 6f;
            }

            currentIndex = nextIndex;
            nextIndex = WrapCycleIndex(currentIndex + state.RemainingDirection, nodeCount);
        }

        gapMin *= 0.6f;
        gapPreferred *= 0.85f;
        gapMax *= 1.15f;
    }

    private float ComputeCycleParallelBranchPenalty(
        MapGraphChainBuilder.Chain chain,
        CycleClosureState state,
        string candidateNodeId,
        Vector2Int candidateRoot,
        Dictionary<string, RoomPlacement> placedRooms,
        float preferredGap,
        float weight)
    {
        if (chain?.Nodes == null || string.IsNullOrEmpty(candidateNodeId))
            return 0f;

        if (!TryGetHypotheticalCycleRoot(chain, state.EndAIndex, placedRooms, candidateNodeId, candidateRoot, out var rootA) ||
            !TryGetHypotheticalCycleRoot(chain, state.EndBIndex, placedRooms, candidateNodeId, candidateRoot, out var rootB))
            return 0f;

        if (!TryGetCycleEndAnchorIndex(chain, state.EndAIndex, placedRooms, candidateNodeId, out var anchorAIndex) ||
            !TryGetCycleEndAnchorIndex(chain, state.EndBIndex, placedRooms, candidateNodeId, out var anchorBIndex))
            return 0f;

        if (anchorAIndex != anchorBIndex)
            return 0f;
        if (!TryGetHypotheticalCycleRoot(chain, anchorAIndex, placedRooms, candidateNodeId, candidateRoot, out var anchorRoot))
            return 0f;

        var vecA = (Vector2)(rootA - anchorRoot);
        var vecB = (Vector2)(rootB - anchorRoot);
        if (vecA.sqrMagnitude <= 0.001f || vecB.sqrMagnitude <= 0.001f)
            return 0f;

        var dot = Vector2.Dot(vecA.normalized, vecB.normalized);
        if (dot < 0.55f)
            return 0f;

        var endpointDistance = Vector2.Distance(rootA, rootB);
        var minComfortGap = Mathf.Max(2f, preferredGap * 0.8f);
        if (endpointDistance >= minComfortGap)
            return 0f;

        var parallelFactor = Mathf.InverseLerp(0.55f, 1f, dot);
        var squeeze = minComfortGap - endpointDistance;
        return weight * 0.35f * (0.6f + 0.8f * parallelFactor) * squeeze * squeeze;
    }

    private bool TryGetCycleEndAnchorIndex(
        MapGraphChainBuilder.Chain chain,
        int endIndex,
        Dictionary<string, RoomPlacement> placedRooms,
        string candidateNodeId,
        out int anchorIndex)
    {
        anchorIndex = -1;
        if (chain?.Nodes == null || endIndex < 0 || endIndex >= chain.Nodes.Count)
            return false;

        var count = chain.Nodes.Count;
        var prevIndex = WrapCycleIndex(endIndex - 1, count);
        var nextIndex = WrapCycleIndex(endIndex + 1, count);
        var hasPrev = TryHasHypotheticalCyclePlacement(chain, prevIndex, placedRooms, candidateNodeId);
        var hasNext = TryHasHypotheticalCyclePlacement(chain, nextIndex, placedRooms, candidateNodeId);
        if (hasPrev == hasNext)
            return false;

        anchorIndex = hasPrev ? prevIndex : nextIndex;
        return true;
    }

    private float ComputeCycleOutwardBias(
        MapGraphChainBuilder.Chain chain,
        CycleClosureState state,
        string candidateNodeId,
        Vector2Int candidateRoot,
        Dictionary<string, RoomPlacement> placedRooms)
    {
        if (string.IsNullOrEmpty(candidateNodeId) || placedRooms == null || placedRooms.Count == 0)
            return 0f;
        if (chain?.Nodes == null)
            return 0f;

        var candidateIndex = -1;
        for (var i = 0; i < chain.Nodes.Count; i++)
        {
            if (string.Equals(chain.Nodes[i]?.id, candidateNodeId, StringComparison.Ordinal))
            {
                candidateIndex = i;
                break;
            }
        }

        if (candidateIndex < 0)
            return 0f;
        if (candidateIndex != state.EndAIndex && candidateIndex != state.EndBIndex)
            return 0f;

        var nodeCount = chain.Nodes.Count;
        var prevIndex = WrapCycleIndex(candidateIndex - 1, nodeCount);
        var nextIndex = WrapCycleIndex(candidateIndex + 1, nodeCount);

        if (!TryGetHypotheticalCycleRoot(chain, prevIndex, placedRooms, candidateNodeId, candidateRoot, out var prevRoot))
            prevRoot = default;
        if (!TryGetHypotheticalCycleRoot(chain, nextIndex, placedRooms, candidateNodeId, candidateRoot, out var nextRoot))
            nextRoot = default;

        var hasPrev = TryHasHypotheticalCyclePlacement(chain, prevIndex, placedRooms, candidateNodeId);
        var hasNext = TryHasHypotheticalCyclePlacement(chain, nextIndex, placedRooms, candidateNodeId);
        if (hasPrev == hasNext)
            return 0f;

        var anchorRoot = hasPrev ? prevRoot : nextRoot;
        var centroid = ComputePlacedRootCentroid(placedRooms);
        var outward = (Vector2)anchorRoot - centroid;
        var growth = (Vector2)(candidateRoot - anchorRoot);
        if (outward.sqrMagnitude <= 0.001f || growth.sqrMagnitude <= 0.001f)
            return 0f;

        return Mathf.Max(0f, Vector2.Dot(growth.normalized, outward.normalized)) * 0.75f;
    }

    private bool TryHasHypotheticalCyclePlacement(
        MapGraphChainBuilder.Chain chain,
        int nodeIndex,
        Dictionary<string, RoomPlacement> placedRooms,
        string candidateNodeId)
    {
        if (chain?.Nodes == null || nodeIndex < 0 || nodeIndex >= chain.Nodes.Count)
            return false;
        var nodeId = chain.Nodes[nodeIndex]?.id;
        return !string.IsNullOrEmpty(nodeId) &&
               (string.Equals(nodeId, candidateNodeId, StringComparison.Ordinal) || placedRooms.ContainsKey(nodeId));
    }

    private bool TryGetHypotheticalCycleRoot(
        MapGraphChainBuilder.Chain chain,
        int nodeIndex,
        Dictionary<string, RoomPlacement> placedRooms,
        string candidateNodeId,
        Vector2Int candidateRoot,
        out Vector2Int root)
    {
        root = default;
        if (chain?.Nodes == null || nodeIndex < 0 || nodeIndex >= chain.Nodes.Count)
            return false;

        var nodeId = chain.Nodes[nodeIndex]?.id;
        if (string.IsNullOrEmpty(nodeId))
            return false;
        if (string.Equals(nodeId, candidateNodeId, StringComparison.Ordinal))
        {
            root = candidateRoot;
            return true;
        }

        if (!placedRooms.TryGetValue(nodeId, out var placement) || placement == null)
            return false;

        root = placement.Root;
        return true;
    }

    private static int WrapCycleIndex(int index, int count)
    {
        if (count <= 0)
            return 0;
        var wrapped = index % count;
        return wrapped < 0 ? wrapped + count : wrapped;
    }

    private float ComputeTopologyExpansionBias(string nodeId, Vector2Int candidateRoot, Dictionary<string, RoomPlacement> placedRooms)
    {
        if (settings == null || !settings.UseBridgeExpansionBias || string.IsNullOrEmpty(nodeId) || placedRooms == null || placedRooms.Count == 0)
            return 0f;
        if ((bridgeInfoByEdge == null || bridgeInfoByEdge.Count == 0) &&
            (nodeTopologyInfoById == null || nodeTopologyInfoById.Count == 0))
            return 0f;

        var centroid = ComputePlacedRootCentroid(placedRooms);
        var totalBias = 0f;
        var hasTopologySignal = false;
        var placedNeighborCount = 0;
        var placedNeighborCentroid = Vector2.zero;

        foreach (var edge in graphAsset.GetEdgesFor(nodeId))
        {
            if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                continue;

            var otherId = edge.fromNodeId == nodeId ? edge.toNodeId : edge.fromNodeId;
            if (!placedRooms.TryGetValue(otherId, out var otherPlacement) || otherPlacement == null)
                continue;

            placedNeighborCentroid += otherPlacement.Root;
            placedNeighborCount++;

            if (bridgeInfoByEdge == null || !bridgeInfoByEdge.TryGetValue(MapGraphKey.NormalizeKey(nodeId, otherId), out var bridgeInfo))
            {
                if (nodeTopologyInfoById != null && nodeTopologyInfoById.TryGetValue(otherId, out var neighborTopo) && neighborTopo.Priority > 0f)
                {
                    hasTopologySignal = true;
                    var outwardFromCluster = (Vector2)otherPlacement.Root - centroid;
                    var moveFromNeighbor = (Vector2)(candidateRoot - otherPlacement.Root);
                    var projectedNeighbor = outwardFromCluster.sqrMagnitude > 0.001f
                        ? Vector2.Dot(moveFromNeighbor, outwardFromCluster.normalized)
                        : moveFromNeighbor.magnitude;
                    totalBias += Mathf.Clamp(projectedNeighbor, -10f, 10f) * neighborTopo.Priority * 0.12f;
                }
                continue;
            }

            hasTopologySignal = true;
            var outward = (Vector2)otherPlacement.Root - centroid;
            var move = (Vector2)(candidateRoot - otherPlacement.Root);
            var projected = outward.sqrMagnitude > 0.001f
                ? Vector2.Dot(move, outward.normalized)
                : move.magnitude;
            var componentScale = 1f + 0.25f * Mathf.Max(0, bridgeInfo.GetSideSize(nodeId) - 1);
            totalBias += Mathf.Clamp(projected, -14f, 14f) * componentScale * 0.45f;
        }

        if (nodeTopologyInfoById != null &&
            nodeTopologyInfoById.TryGetValue(nodeId, out var nodeInfo) &&
            nodeInfo.IsArticulation &&
            placedNeighborCount > 0)
        {
            hasTopologySignal = true;
            placedNeighborCentroid /= placedNeighborCount;
            var outward = placedNeighborCentroid - centroid;
            var move = (Vector2)candidateRoot - placedNeighborCentroid;
            var projected = outward.sqrMagnitude > 0.001f
                ? Vector2.Dot(move, outward.normalized)
                : move.magnitude;
            totalBias += Mathf.Clamp(projected, -12f, 12f) * nodeInfo.Priority * 0.25f;
        }

        return hasTopologySignal ? totalBias : 0f;
    }

    private static Vector2 ComputePlacedRootCentroid(Dictionary<string, RoomPlacement> placedRooms)
    {
        if (placedRooms == null || placedRooms.Count == 0)
            return Vector2.zero;

        var sum = Vector2.zero;
        var count = 0;
        foreach (var room in placedRooms.Values)
        {
            if (room == null)
                continue;
            sum += room.Root;
            count++;
        }

        return count > 0 ? sum / count : Vector2.zero;
    }

    private List<MapGraphAsset.NodeData> BuildChainBfsOrder(MapGraphChainBuilder.Chain chain, Dictionary<string, RoomPlacement> placed)
    {
        var result = new List<MapGraphAsset.NodeData>();
        var frontier = new List<MapGraphAsset.NodeData>();
        var seen = new HashSet<string>();

        foreach (var node in chain.Nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.id))
                continue;
            var hasPlacedNeighbor = graphAsset.GetEdgesFor(node.id).Any(e =>
                placed.ContainsKey(e.fromNodeId) || placed.ContainsKey(e.toNodeId));
            if (hasPlacedNeighbor)
            {
                frontier.Add(node);
                seen.Add(node.id);
            }
        }

        SortNodeFrontier(frontier);

        if (frontier.Count == 0 && chain.Nodes.Count > 0)
        {
            var first = chain.Nodes
                .Where(n => n != null && !string.IsNullOrEmpty(n.id))
                .OrderByDescending(GetTopologyPriority)
                .ThenByDescending(GetNodeDegree)
                .FirstOrDefault();
            if (first != null)
            {
                frontier.Add(first);
                seen.Add(first.id);
            }
        }

        while (frontier.Count > 0)
        {
            var n = frontier[0];
            frontier.RemoveAt(0);
            result.Add(n);
            foreach (var e in graphAsset.GetEdgesFor(n.id))
            {
                var otherId = e.fromNodeId == n.id ? e.toNodeId : e.fromNodeId;
                if (string.IsNullOrEmpty(otherId))
                    continue;
                var other = chain.Nodes.FirstOrDefault(nd => nd != null && nd.id == otherId);
                if (other == null || seen.Contains(other.id))
                    continue;
                frontier.Add(other);
                seen.Add(other.id);
            }
            SortNodeFrontier(frontier);
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

    private void SortNodeFrontier(List<MapGraphAsset.NodeData> frontier)
    {
        if (frontier == null || frontier.Count <= 1)
            return;

        frontier.Sort((a, b) =>
        {
            var topoCmp = GetTopologyPriority(b).CompareTo(GetTopologyPriority(a));
            if (topoCmp != 0)
                return topoCmp;

            var degreeA = GetNodeDegree(a);
            var degreeB = GetNodeDegree(b);
            var degreeCmp = degreeB.CompareTo(degreeA);
            if (degreeCmp != 0)
                return degreeCmp;

            return string.CompareOrdinal(a?.id, b?.id);
        });
    }

    private float GetTopologyPriority(MapGraphAsset.NodeData node)
    {
        if (node == null || string.IsNullOrEmpty(node.id) || nodeTopologyInfoById == null)
            return 0f;
        return nodeTopologyInfoById.TryGetValue(node.id, out var info) ? info.Priority : 0f;
    }

    private int GetNodeDegree(MapGraphAsset.NodeData node)
    {
        if (node == null || string.IsNullOrEmpty(node.id) || neighborLookup == null)
            return 0;
        return neighborLookup.TryGetValue(node.id, out var neighbors) ? neighbors.Count : 0;
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
