// Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Annealing.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private readonly struct PairPenaltyChange
    {
        public int OtherIndex { get; }
        public float OldValue { get; }

        public PairPenaltyChange(int otherIndex, float oldValue)
        {
            OtherIndex = otherIndex;
            OldValue = oldValue;
        }
    }

    private readonly struct EdgePenaltyChange
    {
        public int OtherIndex { get; }
        public float OldValue { get; }

        public EdgePenaltyChange(int otherIndex, float oldValue)
        {
            OtherIndex = otherIndex;
            OldValue = oldValue;
        }
    }

    private readonly struct MoveUndo
    {
        public string NodeId { get; }
        public int NodeIndex { get; }
        public RoomPlacement Placement { get; }
        public GameObject OldPrefab { get; }
        public ModuleShape OldShape { get; }
        public Vector2Int OldRoot { get; }
        public float OldOverlapSum { get; }
        public float OldDistanceSum { get; }
        public float OldTopologyPenalty { get; }

        public MoveUndo(
            string nodeId,
            int nodeIndex,
            RoomPlacement placement,
            GameObject oldPrefab,
            ModuleShape oldShape,
            Vector2Int oldRoot,
            float oldOverlapSum,
            float oldDistanceSum,
            float oldTopologyPenalty)
        {
            NodeId = nodeId;
            NodeIndex = nodeIndex;
            Placement = placement;
            OldPrefab = oldPrefab;
            OldShape = oldShape;
            OldRoot = oldRoot;
            OldOverlapSum = oldOverlapSum;
            OldDistanceSum = oldDistanceSum;
            OldTopologyPenalty = oldTopologyPenalty;
        }
    }

    private float EstimateInitialTemperature(MapGraphChainBuilder.Chain chain, Dictionary<string, RoomPlacement> rooms)
    {
        float avgArea = 1f;
        var sizes = rooms.Values.Select(r => r.Shape?.SolidCells?.Count ?? 1).ToList();
        if (sizes.Count > 0)
            avgArea = (float)sizes.Average();
        return Mathf.Max(1f, avgArea * 0.25f);
    }

    private bool TryPerturbInPlace(
        Dictionary<string, RoomPlacement> rooms,
        EnergyCache energyCache,
        List<int> movableNodeIndices,
        List<Vector2Int> positionCandidates,
        List<Vector2Int> wiggleCandidates,
        List<PairPenaltyChange> pairChanges,
        List<EdgePenaltyChange> edgeChanges,
        out MoveUndo undo)
    {
        using var _ps = PS(S_TryPerturbInPlace);
        undo = default;
        if (rooms == null || rooms.Count == 0 || energyCache == null)
            return false;
        if (movableNodeIndices == null || movableNodeIndices.Count == 0)
            return false;

        int targetIndex;
        RoomPlacement targetPlacement;
        string targetId;
        using (PS(S_Perturb_SelectTarget))
        {
            var useConflictSelection =
                settings != null &&
                settings.UseConflictDrivenTargetSelection &&
                energyCache.OverlapByNode != null &&
                energyCache.EdgeByNode != null &&
                energyCache.OverlapByNode.Length == energyCache.NodeCount &&
                energyCache.EdgeByNode.Length == energyCache.NodeCount;

            if (!useConflictSelection ||
                movableNodeIndices.Count < 2 ||
                rng.NextDouble() < Mathf.Clamp01(settings.TargetSelectionExplorationProbability))
            {
                targetIndex = movableNodeIndices[rng.Next(movableNodeIndices.Count)];
            }
            else
            {
                var k = Mathf.Clamp(settings.TargetSelectionTournamentK, 2, 8);
                k = Mathf.Min(k, movableNodeIndices.Count);

                var bestIdx = -1;
                var bestScore = 0f;
                for (var t = 0; t < k; t++)
                {
                    var cand = movableNodeIndices[rng.Next(movableNodeIndices.Count)];
                    if (cand < 0 || cand >= energyCache.NodeCount)
                        continue;
                    var score = OverlapWeight * energyCache.OverlapByNode[cand] + DistanceWeight * energyCache.EdgeByNode[cand];
                    if (bestIdx < 0 || score > bestScore)
                    {
                        bestIdx = cand;
                        bestScore = score;
                    }
                }

                targetIndex = bestIdx >= 0 && bestScore > 0f
                    ? bestIdx
                    : movableNodeIndices[rng.Next(movableNodeIndices.Count)];
            }

            if (targetIndex < 0 || targetIndex >= energyCache.NodeCount)
                return false;
            targetPlacement = energyCache.PlacementsByIndex[targetIndex];
            if (targetPlacement == null)
                return false;
            targetId = nodeIdByIndex != null && targetIndex < nodeIdByIndex.Length ? nodeIdByIndex[targetIndex] : null;
            if (string.IsNullOrEmpty(targetId))
                return false;
        }

        var pChange = Mathf.Clamp01(settings.ChangePrefabProbability);
        var changeShape = rng.NextDouble() < pChange;
        var oldTopologyPenalty = ComputeLocalTopologyPenalty(energyCache, targetIndex);

        List<GameObject> prefabs;
        GameObject newPrefab;
        ModuleShape newShape;
        using (PS(S_Perturb_SelectPrefab))
        {
            MapGraphAsset.NodeData node = null;
            if (nodeById != null)
                nodeById.TryGetValue(targetId, out node);
            var type = node != null && node.roomType != null ? node.roomType : graphAsset?.DefaultRoomType;
            if (type == null || prefabsByRoomType == null || !prefabsByRoomType.TryGetValue(type, out prefabs))
                return false;
            if (prefabs.Count == 0)
                return false;

            newPrefab = targetPlacement.Prefab;
            newShape = targetPlacement.Shape;
            if (changeShape && prefabs.Count > 1)
            {
                newPrefab = prefabs[rng.Next(prefabs.Count)];
                if (!shapeLibrary.TryGetShape(newPrefab, out newShape, out _))
                    return false;
            }
        }

        using (PS(S_Perturb_GenerateCandidates))
        {
            var wiggleProbability = settings != null ? Mathf.Clamp01(settings.WiggleProbability) : 0.5f;
            if (rng.NextDouble() < wiggleProbability)
            {
                WiggleCandidates(targetIndex, newPrefab, energyCache, wiggleCandidates);
                if (wiggleCandidates.Count == 0)
                    FindPositionCandidates(
                        targetIndex,
                        newPrefab,
                        newShape,
                        energyCache,
                        positionCandidates,
                        allowExistingRoot: !changeShape,
                        existingRoot: targetPlacement.Root);
            }
            else
            {
                FindPositionCandidates(
                    targetIndex,
                    newPrefab,
                    newShape,
                    energyCache,
                    positionCandidates,
                    allowExistingRoot: !changeShape,
                    existingRoot: targetPlacement.Root);
            }
        }

        var candidates = wiggleCandidates.Count > 0 ? wiggleCandidates : positionCandidates;
        if (candidates.Count == 0)
            return false;

        using (PS(S_Perturb_ApplyMove))
        {
            var oldPrefab = targetPlacement.Prefab;
            var oldShape = targetPlacement.Shape;
            var oldRoot = targetPlacement.Root;

            var newRoot = candidates[rng.Next(candidates.Count)];
            if (newRoot == oldRoot && ReferenceEquals(newPrefab, oldPrefab) && ReferenceEquals(newShape, oldShape))
            {
                // Avoid paying UpdateEnergyCacheInPlace for no-op perturbations.
                // Resample a few times (handles duplicates / existingRoot in candidates).
                var resamples = Mathf.Min(4, candidates.Count);
                for (var r = 0; r < resamples; r++)
                {
                    newRoot = candidates[rng.Next(candidates.Count)];
                    if (newRoot != oldRoot)
                        break;
                }

                if (newRoot == oldRoot && ReferenceEquals(newPrefab, oldPrefab) && ReferenceEquals(newShape, oldShape))
                    return false;
            }

        undo = new MoveUndo(
            targetId,
            targetIndex,
            targetPlacement,
            oldPrefab,
            oldShape,
            oldRoot,
            energyCache.OverlapPenaltySum,
            energyCache.DistancePenaltySum,
            oldTopologyPenalty);

            // Mutate in place to avoid per-step allocations in SA.
            targetPlacement.Prefab = newPrefab;
            targetPlacement.Shape = newShape;
            targetPlacement.Root = newRoot;
            UpdateEnergyCacheInPlace(energyCache, targetIndex, pairChanges, edgeChanges);
        }

        return true;
    }

    private void WiggleCandidates(string nodeId, GameObject prefab, ModuleShape shape, Dictionary<string, RoomPlacement> placed, List<Vector2Int> result)
    {
        using var _ps = PS(S_WiggleCandidates);
        var start = profiling != null ? NowTicks() : 0;
        result?.Clear();
        void Finish()
        {
            if (profiling != null)
            {
                profiling.WiggleCandidatesCalls++;
                profiling.WiggleCandidatesTicks += NowTicks() - start;
            }
        }
        if (string.IsNullOrEmpty(nodeId) || prefab == null || placed == null)
        {
            Finish();
            return;
        }

        neighborRootsScratch.Clear();
        foreach (var e in graphAsset.GetEdgesFor(nodeId))
        {
            var otherId = e.fromNodeId == nodeId ? e.toNodeId : e.fromNodeId;
            if (string.IsNullOrEmpty(otherId))
                continue;
            if (!placed.TryGetValue(otherId, out var neighbor) || neighbor == null)
                continue;
            if (!configSpaceLibrary.TryGetSpace(neighbor.Prefab, prefab, out var space, out _))
                continue;
            if (space == null || space.IsEmpty)
                continue;
            neighborRootsScratch.Add((neighbor, space));
        }

        if (neighborRootsScratch.Count == 0)
        {
            Finish();
            return;
        }

        var limit = Mathf.Max(1, settings.MaxWiggleCandidates);
        var useSampling = settings != null && settings.UseRejectionSamplingCandidates;

        if (neighborRootsScratch.Count >= 2)
        {
            var best1 = -1;
            var best2 = -1;
            var c1 = int.MaxValue;
            var c2 = int.MaxValue;
            for (var i = 0; i < neighborRootsScratch.Count; i++)
            {
                var c = neighborRootsScratch[i].space?.Offsets?.Count ?? int.MaxValue;
                if (c < c1)
                {
                    best2 = best1;
                    c2 = c1;
                    best1 = i;
                    c1 = c;
                }
                else if (c < c2)
                {
                    best2 = i;
                    c2 = c;
                }
            }

            if (best1 >= 0 && best2 >= 0)
            {
                var a = neighborRootsScratch[best1];
                var b = neighborRootsScratch[best2];

                if ((a.space?.Offsets?.Count ?? 0) > (b.space?.Offsets?.Count ?? 0))
                {
                    var tmp = a;
                    a = b;
                    b = tmp;
                }

                var offsA = a.space.OffsetsList;
                if (offsA != null && offsA.Count > 0)
                {
                    if (useSampling)
                    {
                        // Keep this bounded: WiggleCandidates is called very frequently in SA.
                        // Oversample a little, but don't do huge rejection loops when acceptance rate is low.
                        var attempts = Mathf.Min(offsA.Count, Mathf.Clamp(limit * 16, 64, 1024));
                        for (var it = 0; it < attempts && (result == null || result.Count < limit); it++)
                        {
                            var pos = a.placement.Root + offsA[rng.Next(offsA.Count)];
                            var delta = pos - b.placement.Root;

                            var ok = false;
                            if (b.space.Grid != null)
                                ok = b.space.Grid.IsSet(delta);
                            else if (b.space != null)
                                ok = b.space.Contains(delta);

                            if (ok)
                                result?.Add(pos);
                        }
                    }
                    else
                    {
                        var seen = 0;
                        for (var idxA = 0; idxA < offsA.Count; idxA++)
                        {
                            var pos = a.placement.Root + offsA[idxA];
                            var delta = pos - b.placement.Root;

                            // Fast BitGrid lookup instead of HashSet
                            if (b.space.Grid == null || !b.space.Grid.IsSet(delta))
                                continue;

                            seen++;
                            if (result != null)
                            {
                                if (result.Count < limit)
                                    result.Add(pos);
                                else
                                {
                                    var j = rng.Next(seen);
                                    if (j < limit)
                                        result[j] = pos;
                                }
                            }
                        }
                    }
                }

                if (result != null && result.Count > 0)
                {
                    Finish();
                    return;
                }
            }
        }

        var idx = rng.Next(neighborRootsScratch.Count);
        var baseNeighbor = neighborRootsScratch[idx];
        var offs = baseNeighbor.space.OffsetsList;
        if (result != null && offs != null && offs.Count > 0)
        {
            if (!useSampling || offs.Count <= limit)
            {
                for (var i = 0; i < offs.Count; i++)
                    result.Add(baseNeighbor.placement.Root + offs[i]);
            }
            else
            {
                // No rejection needed here (single neighbor) — just take a few random offsets.
                for (var i = 0; i < limit; i++)
                    result.Add(baseNeighbor.placement.Root + offs[rng.Next(offs.Count)]);
            }
        }

        Finish();
    }

    private void WiggleCandidates(int nodeIndex, GameObject prefab, EnergyCache cache, List<Vector2Int> result)
    {
        using var _ps = PS(S_WiggleCandidates);
        var start = profiling != null ? NowTicks() : 0;
        result?.Clear();
        void Finish()
        {
            if (profiling != null)
            {
                profiling.WiggleCandidatesCalls++;
                profiling.WiggleCandidatesTicks += NowTicks() - start;
            }
        }
        if (prefab == null || cache == null || neighborIndicesByIndex == null || nodeIndex < 0 || nodeIndex >= neighborIndicesByIndex.Length)
        {
            Finish();
            return;
        }

        neighborRootsScratch.Clear();
        var neigh = neighborIndicesByIndex[nodeIndex];
        for (var k = 0; k < neigh.Length; k++)
        {
            var otherIndex = neigh[k];
            if (otherIndex < 0 || otherIndex >= cache.NodeCount || !cache.IsPlaced[otherIndex])
                continue;
            var neighbor = cache.PlacementsByIndex[otherIndex];
            if (neighbor == null)
                continue;
            if (!configSpaceLibrary.TryGetSpace(neighbor.Prefab, prefab, out var space, out _))
                continue;
            if (space == null || space.IsEmpty)
                continue;
            neighborRootsScratch.Add((neighbor, space));
        }

        if (neighborRootsScratch.Count == 0)
        {
            Finish();
            return;
        }

        var limit = Mathf.Max(1, settings.MaxWiggleCandidates);
        var useSampling = settings != null && settings.UseRejectionSamplingCandidates;

        if (neighborRootsScratch.Count >= 2)
        {
            var best1 = -1;
            var best2 = -1;
            var c1 = int.MaxValue;
            var c2 = int.MaxValue;
            for (var i = 0; i < neighborRootsScratch.Count; i++)
            {
                var c = neighborRootsScratch[i].space?.Offsets?.Count ?? int.MaxValue;
                if (c < c1)
                {
                    best2 = best1;
                    c2 = c1;
                    best1 = i;
                    c1 = c;
                }
                else if (c < c2)
                {
                    best2 = i;
                    c2 = c;
                }
            }

            if (best1 >= 0 && best2 >= 0)
            {
                var a = neighborRootsScratch[best1];
                var b = neighborRootsScratch[best2];

                if ((a.space?.Offsets?.Count ?? 0) > (b.space?.Offsets?.Count ?? 0))
                {
                    var tmp = a;
                    a = b;
                    b = tmp;
                }

                var offsA = a.space.OffsetsList;
                if (offsA != null && offsA.Count > 0)
                {
                    if (useSampling)
                    {
                        // Keep this bounded: WiggleCandidates is called very frequently in SA.
                        // Oversample a little, but don't do huge rejection loops when acceptance rate is low.
                        var attempts = Mathf.Min(offsA.Count, Mathf.Clamp(limit * 16, 64, 1024));
                        for (var it = 0; it < attempts && (result == null || result.Count < limit); it++)
                        {
                            var pos = a.placement.Root + offsA[rng.Next(offsA.Count)];
                            var delta = pos - b.placement.Root;

                            var ok = false;
                            if (b.space.Grid != null)
                                ok = b.space.Grid.IsSet(delta);
                            else if (b.space != null)
                                ok = b.space.Contains(delta);

                            if (ok)
                                result?.Add(pos);
                        }
                    }
                    else
                    {
                        var seen = 0;
                        for (var idxA = 0; idxA < offsA.Count; idxA++)
                        {
                            var pos = a.placement.Root + offsA[idxA];
                            var delta = pos - b.placement.Root;

                            // Fast BitGrid lookup instead of HashSet
                            if (b.space.Grid == null || !b.space.Grid.IsSet(delta))
                                continue;

                            seen++;
                            if (result != null)
                            {
                                if (result.Count < limit)
                                    result.Add(pos);
                                else
                                {
                                    var j = rng.Next(seen);
                                    if (j < limit)
                                        result[j] = pos;
                                }
                            }
                        }
                    }
                }

                if (result != null && result.Count > 0)
                {
                    Finish();
                    return;
                }
            }
        }

        var idx = rng.Next(neighborRootsScratch.Count);
        var baseNeighbor = neighborRootsScratch[idx];
        var offs = baseNeighbor.space.OffsetsList;
        if (result != null && offs != null && offs.Count > 0)
        {
            if (!useSampling || offs.Count <= limit)
            {
                for (var i = 0; i < offs.Count; i++)
                    result.Add(baseNeighbor.placement.Root + offs[i]);
            }
            else
            {
                // No rejection needed here (single neighbor) — just take a few random offsets.
                for (var i = 0; i < limit; i++)
                    result.Add(baseNeighbor.placement.Root + offs[rng.Next(offs.Count)]);
            }
        }

        Finish();
    }

    private void UpdateEnergyCacheInPlace(
        EnergyCache energyCache,
        int changedIndex,
        List<PairPenaltyChange> pairChanges,
        List<EdgePenaltyChange> edgeChanges)
    {
        using var _ps = PS(S_UpdateEnergyCacheInPlace);
        if (energyCache == null || changedIndex < 0 || changedIndex >= energyCache.NodeCount)
            return;
        var changedAfter = energyCache.PlacementsByIndex[changedIndex];
        if (changedAfter == null)
            return;

        var overlapSum = energyCache.OverlapPenaltySum;
        var distSum = energyCache.DistancePenaltySum;

        static bool BoundsOverlap(Vector2Int aMin, Vector2Int aMax, Vector2Int bMin, Vector2Int bMax)
        {
            if (aMax.x < bMin.x || bMax.x < aMin.x)
                return false;
            if (aMax.y < bMin.y || bMax.y < aMin.y)
                return false;
            return true;
        }

        ComputeWorldBounds(
            changedAfter,
            out energyCache.FloorMinW[changedIndex],
            out energyCache.FloorMaxW[changedIndex],
            out energyCache.WallMinW[changedIndex],
            out energyCache.WallMaxW[changedIndex]);

        var aFloorMinW = energyCache.FloorMinW[changedIndex];
        var aFloorMaxW = energyCache.FloorMaxW[changedIndex];
        var aWallMinW = energyCache.WallMinW[changedIndex];
        var aWallMaxW = energyCache.WallMaxW[changedIndex];

        for (var pi = 0; pi < energyCache.PlacedCount; pi++)
        {
            var otherIndex = energyCache.PlacedIndices[pi];
            if (otherIndex == changedIndex || otherIndex < 0 || otherIndex >= energyCache.NodeCount || !energyCache.IsPlaced[otherIndex])
                continue;
            var otherPlacement = energyCache.PlacementsByIndex[otherIndex];
            if (otherPlacement == null)
                continue;

            var pIdx = PairIndex(changedIndex, otherIndex, energyCache.NodeCount);
            if (pIdx < 0)
                continue;
            var oldP = energyCache.PairPenalty[pIdx];
            var bFloorMinW = energyCache.FloorMinW[otherIndex];
            var bFloorMaxW = energyCache.FloorMaxW[otherIndex];
            var bWallMinW = energyCache.WallMinW[otherIndex];
            var bWallMaxW = energyCache.WallMaxW[otherIndex];

            var checkFloorFloor = BoundsOverlap(aFloorMinW, aFloorMaxW, bFloorMinW, bFloorMaxW);
            var checkAWallBFloor = BoundsOverlap(aWallMinW, aWallMaxW, bFloorMinW, bFloorMaxW);
            var checkBWallAFloor = BoundsOverlap(bWallMinW, bWallMaxW, aFloorMinW, aFloorMaxW);
            var maybeAnyOverlap = checkFloorFloor || checkAWallBFloor || checkBWallAFloor;

            var newP = (!maybeAnyOverlap && oldP == 0f) ? 0f : (maybeAnyOverlap ? IntersectionPenalty(changedAfter, otherPlacement) : 0f);
            if (newP == oldP)
                continue;

            pairChanges.Add(new PairPenaltyChange(otherIndex, oldP));
            energyCache.PairPenalty[pIdx] = newP;
            var dP = newP - oldP;
            overlapSum += dP;
            if (energyCache.OverlapByNode != null && energyCache.OverlapByNode.Length == energyCache.NodeCount)
            {
                energyCache.OverlapByNode[changedIndex] += dP;
                energyCache.OverlapByNode[otherIndex] += dP;
            }
        }

        if (neighborIndicesByIndex != null && changedIndex < neighborIndicesByIndex.Length)
        {
            var neigh = neighborIndicesByIndex[changedIndex];
            for (var k = 0; k < neigh.Length; k++)
            {
                var otherIndex = neigh[k];
                if (otherIndex < 0 || otherIndex >= energyCache.NodeCount || !energyCache.IsPlaced[otherIndex])
                    continue;
                var otherPlacement = energyCache.PlacementsByIndex[otherIndex];
                if (otherPlacement == null)
                    continue;
                var eIdx = PairIndex(changedIndex, otherIndex, energyCache.NodeCount);
                if (eIdx < 0)
                    continue;
                var oldD = energyCache.EdgePenalty[eIdx];
                var newD = ComputeEdgeDistancePenalty(changedAfter, otherPlacement);
                if (newD == oldD)
                    continue;

                edgeChanges.Add(new EdgePenaltyChange(otherIndex, oldD));
                energyCache.EdgePenalty[eIdx] = newD;
                var dD = newD - oldD;
                distSum += dD;
                if (energyCache.EdgeByNode != null && energyCache.EdgeByNode.Length == energyCache.NodeCount)
                {
                    energyCache.EdgeByNode[changedIndex] += dD;
                    energyCache.EdgeByNode[otherIndex] += dD;
                }
            }
        }

        energyCache.OverlapPenaltySum = overlapSum;
        energyCache.DistancePenaltySum = distSum;
    }

    private float ComputeLocalTopologyPenalty(EnergyCache energyCache, int changedIndex)
    {
        if (settings == null || !settings.UseBridgeExpansionBias || energyCache == null)
            return 0f;
        if (changedIndex < 0 || changedIndex >= energyCache.NodeCount || !energyCache.IsPlaced[changedIndex])
            return 0f;
        if (bridgeInfoByEdge == null || bridgeInfoByEdge.Count == 0)
            return 0f;
        if (neighborIndicesByIndex == null || changedIndex >= neighborIndicesByIndex.Length)
            return 0f;

        var sum = 0f;
        var seenPairs = new HashSet<int>();

        AddIncidentBridgePenalties(energyCache, changedIndex, seenPairs, ref sum);

        var directNeighbors = neighborIndicesByIndex[changedIndex];
        if (directNeighbors != null)
        {
            for (var i = 0; i < directNeighbors.Length; i++)
            {
                var neighborIndex = directNeighbors[i];
                if (neighborIndex < 0 || neighborIndex >= energyCache.NodeCount || !energyCache.IsPlaced[neighborIndex])
                    continue;
                AddIncidentBridgePenalties(energyCache, neighborIndex, seenPairs, ref sum);
            }
        }

        return TopologyWeight * sum;
    }

    private void AddIncidentBridgePenalties(EnergyCache energyCache, int centerIndex, HashSet<int> seenPairs, ref float sum)
    {
        if (energyCache == null || seenPairs == null || centerIndex < 0 || centerIndex >= energyCache.NodeCount)
            return;
        if (neighborIndicesByIndex == null || centerIndex >= neighborIndicesByIndex.Length)
            return;

        var neighbors = neighborIndicesByIndex[centerIndex];
        if (neighbors == null)
            return;

        for (var i = 0; i < neighbors.Length; i++)
        {
            var otherIndex = neighbors[i];
            if (otherIndex < 0 || otherIndex >= energyCache.NodeCount || !energyCache.IsPlaced[otherIndex])
                continue;

            var pairIndex = PairIndex(centerIndex, otherIndex, energyCache.NodeCount);
            if (pairIndex < 0 || !seenPairs.Add(pairIndex))
                continue;

            sum += ComputeBridgeTopologyPenalty(energyCache, centerIndex, otherIndex);
        }
    }

    private float ComputeBridgeTopologyPenalty(EnergyCache energyCache, int aIndex, int bIndex)
    {
        if (energyCache == null || aIndex < 0 || bIndex < 0 || aIndex >= energyCache.NodeCount || bIndex >= energyCache.NodeCount)
            return 0f;

        var a = energyCache.PlacementsByIndex[aIndex];
        var b = energyCache.PlacementsByIndex[bIndex];
        if (a == null || b == null)
            return 0f;

        if (!bridgeInfoByEdge.TryGetValue(MapGraphKey.NormalizeKey(a.NodeId, b.NodeId), out var bridgeInfo))
            return 0f;

        return ComputeBridgeEndpointPenalty(energyCache, aIndex, bIndex, bridgeInfo.GetSideSize(b.NodeId)) +
               ComputeBridgeEndpointPenalty(energyCache, bIndex, aIndex, bridgeInfo.GetSideSize(a.NodeId));
    }

    private float ComputeBridgeEndpointPenalty(EnergyCache energyCache, int centerIndex, int bridgeOtherIndex, int oppositeComponentSize)
    {
        if (energyCache == null || centerIndex < 0 || bridgeOtherIndex < 0 || centerIndex >= energyCache.NodeCount || bridgeOtherIndex >= energyCache.NodeCount)
            return 0f;

        var centerPlacement = energyCache.PlacementsByIndex[centerIndex];
        var otherPlacement = energyCache.PlacementsByIndex[bridgeOtherIndex];
        if (centerPlacement == null || otherPlacement == null)
            return 0f;

        if (!TryGetLocalNeighborCentroid(energyCache, centerIndex, bridgeOtherIndex, out var localCentroid))
            return 0f;

        var center = (Vector2)centerPlacement.Root;
        var desiredOutward = center - localCentroid;
        if (desiredOutward.sqrMagnitude <= 0.001f)
            return 0f;

        var bridgeDir = (Vector2)otherPlacement.Root - center;
        if (bridgeDir.sqrMagnitude <= 0.001f)
            return 0f;

        var inwardProjection = Vector2.Dot(bridgeDir, -desiredOutward.normalized);
        if (inwardProjection <= 0f)
            return 0f;

        var scale = 1f + 0.12f * Mathf.Min(12, Mathf.Max(0, oppositeComponentSize - 1));
        if (nodeTopologyInfoById != null && nodeIdByIndex != null && centerIndex < nodeIdByIndex.Length)
        {
            var centerNodeId = nodeIdByIndex[centerIndex];
            if (!string.IsNullOrEmpty(centerNodeId) && nodeTopologyInfoById.TryGetValue(centerNodeId, out var info))
                scale += info.Priority * 0.05f;
        }

        return Mathf.Clamp(inwardProjection, 0f, 12f) * scale;
    }

    private bool TryGetLocalNeighborCentroid(EnergyCache energyCache, int centerIndex, int excludedNeighborIndex, out Vector2 centroid)
    {
        centroid = Vector2.zero;
        if (energyCache == null || centerIndex < 0 || centerIndex >= energyCache.NodeCount)
            return false;
        if (neighborIndicesByIndex == null || centerIndex >= neighborIndicesByIndex.Length)
            return false;

        var neighbors = neighborIndicesByIndex[centerIndex];
        if (neighbors == null || neighbors.Length == 0)
            return false;

        var sum = Vector2.zero;
        var count = 0;
        for (var i = 0; i < neighbors.Length; i++)
        {
            var neighborIndex = neighbors[i];
            if (neighborIndex == excludedNeighborIndex || neighborIndex < 0 || neighborIndex >= energyCache.NodeCount || !energyCache.IsPlaced[neighborIndex])
                continue;

            var placement = energyCache.PlacementsByIndex[neighborIndex];
            if (placement == null)
                continue;

            sum += placement.Root;
            count++;
        }

        if (count <= 0)
            return false;

        centroid = sum / count;
        return true;
    }

    private void UndoMove(
        Dictionary<string, RoomPlacement> rooms,
        EnergyCache energyCache,
        MoveUndo undo,
        List<PairPenaltyChange> pairChanges,
        List<EdgePenaltyChange> edgeChanges)
    {
        using var _ps = PS(S_UndoMove);
        if (rooms == null || energyCache == null || string.IsNullOrEmpty(undo.NodeId) || undo.Placement == null)
            return;

        // Revert placement fields in place; dictionaries/arrays already reference the same object.
        undo.Placement.Prefab = undo.OldPrefab;
        undo.Placement.Shape = undo.OldShape;
        undo.Placement.Root = undo.OldRoot;

        if (undo.NodeIndex >= 0 && undo.NodeIndex < energyCache.NodeCount)
        {
            ComputeWorldBounds(
                undo.Placement,
                out energyCache.FloorMinW[undo.NodeIndex],
                out energyCache.FloorMaxW[undo.NodeIndex],
                out energyCache.WallMinW[undo.NodeIndex],
                out energyCache.WallMaxW[undo.NodeIndex]);
        }
        energyCache.OverlapPenaltySum = undo.OldOverlapSum;
        energyCache.DistancePenaltySum = undo.OldDistanceSum;

        if (pairChanges != null)
        {
            for (int i = 0; i < pairChanges.Count; i++)
            {
                var ch = pairChanges[i];
                var otherIndex = ch.OtherIndex;
                var pIdx = PairIndex(undo.NodeIndex, otherIndex, energyCache.NodeCount);
                if (pIdx < 0 || pIdx >= energyCache.PairPenalty.Length)
                    continue;

                var newP = energyCache.PairPenalty[pIdx];
                var oldP = ch.OldValue;
                var dP = oldP - newP;
                if (energyCache.OverlapByNode != null && energyCache.OverlapByNode.Length == energyCache.NodeCount)
                {
                    energyCache.OverlapByNode[undo.NodeIndex] += dP;
                    if (otherIndex >= 0 && otherIndex < energyCache.NodeCount)
                        energyCache.OverlapByNode[otherIndex] += dP;
                }
                energyCache.PairPenalty[pIdx] = oldP;
            }
        }

        if (edgeChanges != null)
        {
            for (int i = 0; i < edgeChanges.Count; i++)
            {
                var ch = edgeChanges[i];
                var otherIndex = ch.OtherIndex;
                var eIdx = PairIndex(undo.NodeIndex, otherIndex, energyCache.NodeCount);
                if (eIdx < 0 || eIdx >= energyCache.EdgePenalty.Length)
                    continue;

                var newD = energyCache.EdgePenalty[eIdx];
                var oldD = ch.OldValue;
                var dD = oldD - newD;
                if (energyCache.EdgeByNode != null && energyCache.EdgeByNode.Length == energyCache.NodeCount)
                {
                    energyCache.EdgeByNode[undo.NodeIndex] += dD;
                    if (otherIndex >= 0 && otherIndex < energyCache.NodeCount)
                        energyCache.EdgeByNode[otherIndex] += dD;
                }
                energyCache.EdgePenalty[eIdx] = oldD;
            }
        }
    }
}
