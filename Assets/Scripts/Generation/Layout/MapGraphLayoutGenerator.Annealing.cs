// Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Annealing.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private readonly struct PairPenaltyChange
    {
        public int Index { get; }
        public float OldValue { get; }

        public PairPenaltyChange(int index, float oldValue)
        {
            Index = index;
            OldValue = oldValue;
        }
    }

    private readonly struct EdgePenaltyChange
    {
        public int Index { get; }
        public float OldValue { get; }

        public EdgePenaltyChange(int index, float oldValue)
        {
            Index = index;
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

        public MoveUndo(
            string nodeId,
            int nodeIndex,
            RoomPlacement placement,
            GameObject oldPrefab,
            ModuleShape oldShape,
            Vector2Int oldRoot,
            float oldOverlapSum,
            float oldDistanceSum)
        {
            NodeId = nodeId;
            NodeIndex = nodeIndex;
            Placement = placement;
            OldPrefab = oldPrefab;
            OldShape = oldShape;
            OldRoot = oldRoot;
            OldOverlapSum = oldOverlapSum;
            OldDistanceSum = oldDistanceSum;
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
            targetIndex = movableNodeIndices[rng.Next(movableNodeIndices.Count)];
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
            if (rng.NextDouble() < 0.5)
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
            else
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
        }

        var candidates = wiggleCandidates.Count > 0 ? wiggleCandidates : positionCandidates;
        if (candidates.Count == 0)
            return false;

        using (PS(S_Perturb_ApplyMove))
        {
            var newRoot = candidates[rng.Next(candidates.Count)];
            undo = new MoveUndo(
                targetId,
                targetIndex,
                targetPlacement,
                targetPlacement.Prefab,
                targetPlacement.Shape,
                targetPlacement.Root,
                energyCache.OverlapPenaltySum,
                energyCache.DistancePenaltySum);

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
        if (string.IsNullOrEmpty(nodeId) || prefab == null || placed == null)
            return;

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
            return;

        var limit = Mathf.Max(1, settings.MaxWiggleCandidates);

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

                var seen = 0;
                var offsA = a.space.OffsetsList;
                for (var idxA = 0; idxA < offsA.Count; idxA++)
                {
                    var pos = a.placement.Root + offsA[idxA];
                    if (!b.space.Contains(pos - b.placement.Root))
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

                if (result != null && result.Count > 0)
                {
                    if (profiling != null)
                    {
                        profiling.WiggleCandidatesCalls++;
                        profiling.WiggleCandidatesTicks += NowTicks() - start;
                    }
                    return;
                }
            }
        }

        var idx = rng.Next(neighborRootsScratch.Count);
        var baseNeighbor = neighborRootsScratch[idx];
        var offs = baseNeighbor.space.OffsetsList;
        if (result != null && offs.Count > 0)
        {
            if (offs.Count <= limit)
            {
                for (var i = 0; i < offs.Count; i++)
                    result.Add(baseNeighbor.placement.Root + offs[i]);
            }
            else
            {
                var seen = 0;
                for (var i = 0; i < offs.Count; i++)
                {
                    var pos = baseNeighbor.placement.Root + offs[i];
                    seen++;
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

        if (profiling != null)
        {
            profiling.WiggleCandidatesCalls++;
            profiling.WiggleCandidatesTicks += NowTicks() - start;
        }
    }

    private void WiggleCandidates(int nodeIndex, GameObject prefab, EnergyCache cache, List<Vector2Int> result)
    {
        using var _ps = PS(S_WiggleCandidates);
        var start = profiling != null ? NowTicks() : 0;
        result?.Clear();
        if (prefab == null || cache == null || neighborIndicesByIndex == null || nodeIndex < 0 || nodeIndex >= neighborIndicesByIndex.Length)
            return;

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
            return;

        var limit = Mathf.Max(1, settings.MaxWiggleCandidates);

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

                var seen = 0;
                var offsA = a.space.OffsetsList;
                for (var idxA = 0; idxA < offsA.Count; idxA++)
                {
                    var pos = a.placement.Root + offsA[idxA];
                    if (!b.space.Contains(pos - b.placement.Root))
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

                if (result != null && result.Count > 0)
                {
                    if (profiling != null)
                    {
                        profiling.WiggleCandidatesCalls++;
                        profiling.WiggleCandidatesTicks += NowTicks() - start;
                    }
                    return;
                }
            }
        }

        var idx = rng.Next(neighborRootsScratch.Count);
        var baseNeighbor = neighborRootsScratch[idx];
        var offs = baseNeighbor.space.OffsetsList;
        if (result != null && offs.Count > 0)
        {
            if (offs.Count <= limit)
            {
                for (var i = 0; i < offs.Count; i++)
                    result.Add(baseNeighbor.placement.Root + offs[i]);
            }
            else
            {
                var seen = 0;
                for (var i = 0; i < offs.Count; i++)
                {
                    var pos = baseNeighbor.placement.Root + offs[i];
                    seen++;
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

        if (profiling != null)
        {
            profiling.WiggleCandidatesCalls++;
            profiling.WiggleCandidatesTicks += NowTicks() - start;
        }
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

            pairChanges.Add(new PairPenaltyChange(pIdx, oldP));
            energyCache.PairPenalty[pIdx] = newP;
            overlapSum += newP - oldP;
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

                edgeChanges.Add(new EdgePenaltyChange(eIdx, oldD));
                energyCache.EdgePenalty[eIdx] = newD;
                distSum += newD - oldD;
            }
        }

        energyCache.OverlapPenaltySum = overlapSum;
        energyCache.DistancePenaltySum = distSum;
    }

    private void UndoMove(
        Dictionary<string, RoomPlacement> rooms,
        EnergyCache energyCache,
        MoveUndo undo,
        List<PairPenaltyChange> pairChanges,
        List<EdgePenaltyChange> edgeChanges)
    {
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
                if (ch.Index >= 0 && ch.Index < energyCache.PairPenalty.Length)
                    energyCache.PairPenalty[ch.Index] = ch.OldValue;
            }
        }

        if (edgeChanges != null)
        {
            for (int i = 0; i < edgeChanges.Count; i++)
            {
                var ch = edgeChanges[i];
                if (ch.Index >= 0 && ch.Index < energyCache.EdgePenalty.Length)
                    energyCache.EdgePenalty[ch.Index] = ch.OldValue;
            }
        }
    }
}
