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
        public int PlacedCount { get; set; }
        public int[] PlacedIndices { get; }
        public RoomPlacement[] PlacementsByIndex { get; }
        public bool[] IsPlaced { get; }
        public Vector2Int[] FloorMinW { get; }
        public Vector2Int[] FloorMaxW { get; }
        public Vector2Int[] WallMinW { get; }
        public Vector2Int[] WallMaxW { get; }
        public float[] PairPenalty { get; }
        public float[] EdgePenalty { get; }
        public float TotalEnergy => OverlapWeight * OverlapPenaltySum + DistanceWeight * DistancePenaltySum;

        public EnergyCache(
            int nodeCount,
            int[] placedIndices,
            int placedCount,
            RoomPlacement[] placementsByIndex,
            bool[] isPlaced,
            Vector2Int[] floorMinW,
            Vector2Int[] floorMaxW,
            Vector2Int[] wallMinW,
            Vector2Int[] wallMaxW,
            float overlapPenaltySum,
            float distancePenaltySum,
            float[] pairPenalty,
            float[] edgePenalty)
        {
            NodeCount = Mathf.Max(0, nodeCount);
            PlacedIndices = placedIndices ?? new int[NodeCount];
            PlacedCount = Mathf.Clamp(placedCount, 0, PlacedIndices.Length);
            PlacementsByIndex = placementsByIndex ?? new RoomPlacement[NodeCount];
            IsPlaced = isPlaced ?? new bool[NodeCount];
            FloorMinW = floorMinW ?? new Vector2Int[NodeCount];
            FloorMaxW = floorMaxW ?? new Vector2Int[NodeCount];
            WallMinW = wallMinW ?? new Vector2Int[NodeCount];
            WallMaxW = wallMaxW ?? new Vector2Int[NodeCount];
            OverlapPenaltySum = overlapPenaltySum;
            DistancePenaltySum = distancePenaltySum;
            PairPenalty = pairPenalty ?? new float[PairArrayLength(NodeCount)];
            EdgePenalty = edgePenalty ?? new float[PairArrayLength(NodeCount)];
        }
    }

    private static int PairArrayLength(int n) => n <= 1 ? 0 : (n * (n - 1)) / 2;

    private static void ComputeWorldBounds(
        RoomPlacement placement,
        out Vector2Int floorMinW,
        out Vector2Int floorMaxW,
        out Vector2Int wallMinW,
        out Vector2Int wallMaxW)
    {
        if (placement?.Shape == null)
        {
            floorMinW = default;
            floorMaxW = default;
            wallMinW = default;
            wallMaxW = default;
            return;
        }

        floorMinW = placement.Shape.Min + placement.Root;
        floorMaxW = placement.Shape.Max + placement.Root;
        wallMinW = placement.Shape.WallMin + placement.Root;
        wallMaxW = placement.Shape.WallMax + placement.Root;
    }

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
            return new EnergyCache(0, null, 0, null, null, null, null, null, null, 0f, 0f, null, null);

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

        var floorMinW = new Vector2Int[nodeCount];
        var floorMaxW = new Vector2Int[nodeCount];
        var wallMinW = new Vector2Int[nodeCount];
        var wallMaxW = new Vector2Int[nodeCount];

        float overlapSum = 0f;
        var pairPenalty = new float[PairArrayLength(nodeCount)];
        var placedIndices = new List<int>(rooms.Count);
        for (var i = 0; i < nodeCount; i++)
        {
            if (isPlaced[i])
            {
                placedIndices.Add(i);
                ComputeWorldBounds(placementsByIndex[i], out floorMinW[i], out floorMaxW[i], out wallMinW[i], out wallMaxW[i]);
            }
        }
        var placedArray = new int[nodeCount];
        for (int k = 0; k < placedIndices.Count; k++)
            placedArray[k] = placedIndices[k];
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

        return new EnergyCache(
            nodeCount,
            placedArray,
            placedIndices.Count,
            placementsByIndex,
            isPlaced,
            floorMinW,
            floorMaxW,
            wallMinW,
            wallMaxW,
            overlapSum,
            distSum,
            pairPenalty,
            edgePenalty);
    }

    private EnergyCache CloneEnergyCache(EnergyCache src)
    {
        if (src == null)
            return new EnergyCache(0, null, 0, null, null, null, null, null, null, 0f, 0f, null, null);

        return new EnergyCache(
            src.NodeCount,
            src.PlacedIndices != null ? (int[])src.PlacedIndices.Clone() : new int[src.NodeCount],
            src.PlacedCount,
            src.PlacementsByIndex != null ? (RoomPlacement[])src.PlacementsByIndex.Clone() : new RoomPlacement[src.NodeCount],
            src.IsPlaced != null ? (bool[])src.IsPlaced.Clone() : new bool[src.NodeCount],
            src.FloorMinW != null ? (Vector2Int[])src.FloorMinW.Clone() : new Vector2Int[src.NodeCount],
            src.FloorMaxW != null ? (Vector2Int[])src.FloorMaxW.Clone() : new Vector2Int[src.NodeCount],
            src.WallMinW != null ? (Vector2Int[])src.WallMinW.Clone() : new Vector2Int[src.NodeCount],
            src.WallMaxW != null ? (Vector2Int[])src.WallMaxW.Clone() : new Vector2Int[src.NodeCount],
            src.OverlapPenaltySum,
            src.DistancePenaltySum,
            src.PairPenalty != null ? (float[])src.PairPenalty.Clone() : new float[PairArrayLength(src.NodeCount)],
            src.EdgePenalty != null ? (float[])src.EdgePenalty.Clone() : new float[PairArrayLength(src.NodeCount)]);
    }

    private float ComputeEnergyIfAdded(Dictionary<string, RoomPlacement> roomsWithoutAdded, EnergyCache baseCache, RoomPlacement added)
    {
        var deepProfile = settings != null && settings.LogLayoutProfiling;
        using var _ps = PSIf(deepProfile, S_ComputeEnergyIfAdded);

        if (added == null)
            return baseCache?.TotalEnergy ?? 0f;

        float overlapSum = baseCache?.OverlapPenaltySum ?? 0f;
        float distSum = baseCache?.DistancePenaltySum ?? 0f;

        if (baseCache != null && baseCache.NodeCount > 0)
        {
            using (PSIf(deepProfile, S_ComputeEnergyIfAdded_Overlaps))
            {
                for (var pi = 0; pi < baseCache.PlacedCount; pi++)
                {
                    var i = baseCache.PlacedIndices[pi];
                    if (i < 0 || i >= baseCache.NodeCount || !baseCache.IsPlaced[i])
                        continue;
                    var other = baseCache.PlacementsByIndex[i];
                    if (other == null)
                        continue;
                    overlapSum += IntersectionPenalty(added, other);
                }
            }

            if (nodeIndexById != null &&
                nodeIndexById.TryGetValue(added.NodeId, out var addedIndex) &&
                neighborIndicesByIndex != null &&
                addedIndex >= 0 &&
                addedIndex < neighborIndicesByIndex.Length)
            {
                using (PSIf(deepProfile, S_ComputeEnergyIfAdded_EdgeDistances))
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
        }

        return OverlapWeight * overlapSum + DistanceWeight * distSum;
    }

    private float ComputeEnergyIfAddedAt(string addedNodeId, GameObject addedPrefab, ModuleShape addedShape, Vector2Int addedRoot, EnergyCache baseCache)
    {
        var deepProfile = settings != null && settings.LogLayoutProfiling;
        using var _ps = PSIf(deepProfile, S_ComputeEnergyIfAdded);

        if (addedShape == null || addedPrefab == null || string.IsNullOrEmpty(addedNodeId))
            return baseCache?.TotalEnergy ?? 0f;

        float overlapSum = baseCache?.OverlapPenaltySum ?? 0f;
        float distSum = baseCache?.DistancePenaltySum ?? 0f;

        if (baseCache != null && baseCache.NodeCount > 0)
        {
            using (PSIf(deepProfile, S_ComputeEnergyIfAdded_Overlaps))
            {
                for (var pi = 0; pi < baseCache.PlacedCount; pi++)
                {
                    var i = baseCache.PlacedIndices[pi];
                    if (i < 0 || i >= baseCache.NodeCount || !baseCache.IsPlaced[i])
                        continue;
                    var other = baseCache.PlacementsByIndex[i];
                    if (other == null)
                        continue;
                    overlapSum += IntersectionPenaltyRaw(
                        addedNodeId,
                        addedPrefab,
                        addedShape,
                        addedRoot,
                        other.NodeId,
                        other.Prefab,
                        other.Shape,
                        other.Root);
                }
            }

            if (nodeIndexById != null &&
                nodeIndexById.TryGetValue(addedNodeId, out var addedIndex) &&
                neighborIndicesByIndex != null &&
                addedIndex >= 0 &&
                addedIndex < neighborIndicesByIndex.Length)
            {
                using (PSIf(deepProfile, S_ComputeEnergyIfAdded_EdgeDistances))
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
                        distSum += ComputeEdgeDistancePenaltyRaw(
                            addedPrefab,
                            addedShape,
                            addedRoot,
                            other.Prefab,
                            other.Shape,
                            other.Root);
                    }
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

        var wasPlaced = cache.IsPlaced[addedIndex];
        cache.PlacementsByIndex[addedIndex] = added;
        cache.IsPlaced[addedIndex] = true;
        ComputeWorldBounds(added, out cache.FloorMinW[addedIndex], out cache.FloorMaxW[addedIndex], out cache.WallMinW[addedIndex], out cache.WallMaxW[addedIndex]);
        if (!wasPlaced && cache.PlacedCount < cache.PlacedIndices.Length)
            cache.PlacedIndices[cache.PlacedCount++] = addedIndex;

        var overlapSum = cache.OverlapPenaltySum;
        var distSum = cache.DistancePenaltySum;

        for (var pi = 0; pi < cache.PlacedCount; pi++)
        {
            var i = cache.PlacedIndices[pi];
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
