// Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.EnergyCache.cs
using System.Collections.Generic;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private sealed class EnergyCache
    {
        public float OverlapPenaltySum { get; set; }
        public float DistancePenaltySum { get; set; }
        public int NodeCount { get; }
        public RoomPlacement[] PlacementsByIndex { get; }
        public bool[] IsPlaced { get; }
        public float[] PairPenalty { get; }
        public float[] EdgePenalty { get; }
        public float TotalEnergy => OverlapWeight * OverlapPenaltySum + DistanceWeight * DistancePenaltySum;

        public EnergyCache(
            int nodeCount,
            RoomPlacement[] placementsByIndex,
            bool[] isPlaced,
            float overlapPenaltySum,
            float distancePenaltySum,
            float[] pairPenalty,
            float[] edgePenalty)
        {
            NodeCount = Mathf.Max(0, nodeCount);
            PlacementsByIndex = placementsByIndex ?? new RoomPlacement[NodeCount];
            IsPlaced = isPlaced ?? new bool[NodeCount];
            OverlapPenaltySum = overlapPenaltySum;
            DistancePenaltySum = distancePenaltySum;
            PairPenalty = pairPenalty ?? new float[PairArrayLength(NodeCount)];
            EdgePenalty = edgePenalty ?? new float[PairArrayLength(NodeCount)];
        }
    }

    private static int PairArrayLength(int n) => n <= 1 ? 0 : (n * (n - 1)) / 2;

    private static int PairIndex(int a, int b, int n)
    {
        // Packed upper-triangle indexing for unordered pairs (a,b), a!=b.
        // This avoids Dictionary allocations in SA inner loops.
        if (a == b || n <= 1)
            return -1;
        if (a > b)
        {
            var t = a;
            a = b;
            b = t;
        }
        var rowStart = a * (n - 1) - (a * (a + 1)) / 2;
        return rowStart + (b - a - 1);
    }

    private EnergyCache BuildEnergyCache(Dictionary<string, RoomPlacement> rooms)
    {
        using var _ps = PS(S_BuildEnergyCache);
        // Builds a cache for the current partial placement:
        // - PairPenalty: overlap penalty for all placed node pairs
        // - EdgePenalty: distance penalty only for graph-adjacent pairs (when not touching)
        // Both are stored in the same packed-pair indexing.
        if (rooms == null || nodeIdByIndex == null || nodeIndexById == null)
            return new EnergyCache(0, null, null, 0f, 0f, null, null);

        var nodeCount = nodeIdByIndex.Length;
        var placementsByIndex = new RoomPlacement[nodeCount];
        var isPlaced = new bool[nodeCount];
        foreach (var kv in rooms)
        {
            if (kv.Value == null)
                continue;
            if (!nodeIndexById.TryGetValue(kv.Key, out var idx))
                continue;
            placementsByIndex[idx] = kv.Value;
            isPlaced[idx] = true;
        }

        float overlapSum = 0f;
        var pairPenalty = new float[PairArrayLength(nodeCount)];
        var placedIndices = new List<int>(rooms.Count);
        for (var i = 0; i < nodeCount; i++)
        {
            if (isPlaced[i])
                placedIndices.Add(i);
        }
        for (int pi = 0; pi < placedIndices.Count; pi++)
        {
            var i = placedIndices[pi];
            var a = placementsByIndex[i];
            if (a == null)
                continue;
            for (int pj = pi + 1; pj < placedIndices.Count; pj++)
            {
                var j = placedIndices[pj];
                var b = placementsByIndex[j];
                if (a == null || b == null)
                    continue;
                var p = IntersectionPenalty(a, b);
                var idx = PairIndex(i, j, nodeCount);
                if (idx >= 0)
                    pairPenalty[idx] = p;
                overlapSum += p;
            }
        }

        float distSum = 0f;
        var edgePenalty = new float[PairArrayLength(nodeCount)];
        if (neighborIndicesByIndex != null)
        {
            for (int pi = 0; pi < placedIndices.Count; pi++)
            {
                var i = placedIndices[pi];
                var a = placementsByIndex[i];
                if (a == null)
                    continue;
                var neigh = neighborIndicesByIndex[i];
                if (neigh == null)
                    continue;
                for (var k = 0; k < neigh.Length; k++)
                {
                    var j = neigh[k];
                    if (j <= i)
                        continue;
                    if (j < 0 || j >= nodeCount || !isPlaced[j])
                        continue;
                    var b = placementsByIndex[j];
                    if (b == null)
                        continue;
                    var p = ComputeEdgeDistancePenalty(a, b);
                    var idx = PairIndex(i, j, nodeCount);
                    if (idx >= 0)
                        edgePenalty[idx] = p;
                    distSum += p;
                }
            }
        }

        return new EnergyCache(nodeCount, placementsByIndex, isPlaced, overlapSum, distSum, pairPenalty, edgePenalty);
    }

    private EnergyCache CloneEnergyCache(EnergyCache src)
    {
        if (src == null)
            return new EnergyCache(0, null, null, 0f, 0f, null, null);

        return new EnergyCache(
            src.NodeCount,
            src.PlacementsByIndex != null ? (RoomPlacement[])src.PlacementsByIndex.Clone() : new RoomPlacement[src.NodeCount],
            src.IsPlaced != null ? (bool[])src.IsPlaced.Clone() : new bool[src.NodeCount],
            src.OverlapPenaltySum,
            src.DistancePenaltySum,
            src.PairPenalty != null ? (float[])src.PairPenalty.Clone() : new float[PairArrayLength(src.NodeCount)],
            src.EdgePenalty != null ? (float[])src.EdgePenalty.Clone() : new float[PairArrayLength(src.NodeCount)]);
    }

    private float ComputeEnergyIfAdded(Dictionary<string, RoomPlacement> roomsWithoutAdded, EnergyCache baseCache, RoomPlacement added)
    {
        if (added == null)
            return baseCache?.TotalEnergy ?? 0f;

        float overlapSum = baseCache?.OverlapPenaltySum ?? 0f;
        float distSum = baseCache?.DistancePenaltySum ?? 0f;

        if (baseCache != null && baseCache.NodeCount > 0)
        {
            for (var i = 0; i < baseCache.NodeCount; i++)
            {
                if (!baseCache.IsPlaced[i])
                    continue;
                var other = baseCache.PlacementsByIndex[i];
                if (other == null)
                    continue;
                overlapSum += IntersectionPenalty(added, other);
            }

            if (nodeIndexById != null &&
                nodeIndexById.TryGetValue(added.NodeId, out var addedIndex) &&
                neighborIndicesByIndex != null &&
                addedIndex >= 0 &&
                addedIndex < neighborIndicesByIndex.Length)
            {
                var neigh = neighborIndicesByIndex[addedIndex];
                for (var k = 0; k < neigh.Length; k++)
                {
                    var j = neigh[k];
                    if (j < 0 || j >= baseCache.NodeCount || !baseCache.IsPlaced[j])
                        continue;
                    var other = baseCache.PlacementsByIndex[j];
                    if (other == null)
                        continue;
                    distSum += ComputeEdgeDistancePenalty(added, other);
                }
            }
        }

        return OverlapWeight * overlapSum + DistanceWeight * distSum;
    }

    private void AddPlacementToEnergyCacheInPlace(Dictionary<string, RoomPlacement> roomsWithoutAdded, EnergyCache cache, RoomPlacement added)
    {
        if (cache == null || added == null || roomsWithoutAdded == null)
            return;
        if (nodeIndexById == null || !nodeIndexById.TryGetValue(added.NodeId, out var addedIndex))
            return;
        if (addedIndex < 0 || addedIndex >= cache.NodeCount)
            return;

        cache.PlacementsByIndex[addedIndex] = added;
        cache.IsPlaced[addedIndex] = true;

        var overlapSum = cache.OverlapPenaltySum;
        var distSum = cache.DistancePenaltySum;

        for (var i = 0; i < cache.NodeCount; i++)
        {
            if (i == addedIndex || !cache.IsPlaced[i])
                continue;
            var other = cache.PlacementsByIndex[i];
            if (other == null)
                continue;
            var pIdx = PairIndex(addedIndex, i, cache.NodeCount);
            if (pIdx < 0)
                continue;
            var oldP = cache.PairPenalty[pIdx];
            overlapSum -= oldP;
            var p = IntersectionPenalty(added, other);
            cache.PairPenalty[pIdx] = p;
            overlapSum += p;
        }

        if (neighborIndicesByIndex != null && addedIndex < neighborIndicesByIndex.Length)
        {
            var neigh = neighborIndicesByIndex[addedIndex];
            for (var k = 0; k < neigh.Length; k++)
            {
                var j = neigh[k];
                if (j < 0 || j >= cache.NodeCount || !cache.IsPlaced[j])
                    continue;
                var other = cache.PlacementsByIndex[j];
                if (other == null)
                    continue;
                var eIdx = PairIndex(addedIndex, j, cache.NodeCount);
                if (eIdx < 0)
                    continue;
                var oldD = cache.EdgePenalty[eIdx];
                distSum -= oldD;
                var d = ComputeEdgeDistancePenalty(added, other);
                cache.EdgePenalty[eIdx] = d;
                distSum += d;
            }
        }

        cache.OverlapPenaltySum = overlapSum;
        cache.DistancePenaltySum = distSum;
    }
}
