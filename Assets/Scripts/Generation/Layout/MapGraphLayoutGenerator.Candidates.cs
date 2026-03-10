// Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Candidates.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private readonly List<(RoomPlacement placement, ConfigurationSpace space)> neighborRootsScratch = new();
    private readonly List<Vector2Int> candidatesScratch = new();
    private readonly Dictionary<Vector2Int, int> scoredScratch = new();
    private readonly BitGrid scratchGrid = new BitGrid(Vector2Int.zero, 0, 0, 0, new ulong[0], 0);

    private void FindPositionCandidates(string nodeId, GameObject prefab, ModuleShape shape, Dictionary<string, RoomPlacement> placed, List<Vector2Int> result, bool allowExistingRoot = false)
    {
        using var _ps = PS(S_FindPositionCandidates);
        var start = profiling != null ? NowTicks() : 0;
        result?.Clear();
        if (shape == null || prefab == null)
            return;

        neighborRootsScratch.Clear();
        foreach (var edge in graphAsset.GetEdgesFor(nodeId))
        {
            var otherId = edge.fromNodeId == nodeId ? edge.toNodeId : edge.fromNodeId;
            if (string.IsNullOrEmpty(otherId))
                continue;
            if (!placed.TryGetValue(otherId, out var neighbor))
                continue;
            if (!configSpaceLibrary.TryGetSpace(neighbor.Prefab, prefab, out var space, out _))
                continue;
            if (space == null || space.IsEmpty)
                continue;
            neighborRootsScratch.Add((neighbor, space));
        }

        if (neighborRootsScratch.Count == 0)
        {
            if (allowExistingRoot && placed.TryGetValue(nodeId, out var existing))
                result?.Add(existing.Root);

            if (result == null || result.Count == 0)
            {
                if (placed.Count == 0)
                {
                    result?.Add(Vector2Int.zero);
                }
                else
                {
                    var spacing = EstimateUnconstrainedSpacing(shape);
                    var rings = Mathf.Clamp(settings.MaxWiggleCandidates, 4, 64) / 4;
                    for (int r = 0; r <= rings; r++)
                    {
                        for (int dx = -r; dx <= r; dx++)
                        {
                            for (int dy = -r; dy <= r; dy++)
                            {
                                if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r)
                                    continue;
                                result?.Add(new Vector2Int(dx * spacing, dy * spacing));
                                if (result != null && result.Count >= Mathf.Max(8, settings.MaxFallbackCandidates / 8))
                                    break;
                            }
                            if (result != null && result.Count >= Mathf.Max(8, settings.MaxFallbackCandidates / 8))
                                break;
                        }
                        if (result != null && result.Count >= Mathf.Max(8, settings.MaxFallbackCandidates / 8))
                            break;
                    }
                }
            }
            return;
        }

        // FAST PATH: Single neighbor - use precomputed OffsetsList directly
        if (neighborRootsScratch.Count == 1)
        {
            var neighbor = neighborRootsScratch[0].placement;
            var space = neighborRootsScratch[0].space;
            var offsets = space.OffsetsList;

            candidatesScratch.Clear();
            candidatesScratch.Capacity = Mathf.Max(candidatesScratch.Capacity, offsets.Count);

            for (int i = 0; i < offsets.Count; i++)
            {
                candidatesScratch.Add(neighbor.Root + offsets[i]);
            }

            if (result != null)
                result.AddRange(candidatesScratch);

            return;
        }

        // SLOW PATH: Multiple neighbors - use BitGrid intersection
        // Optimized intersection using BitGrid
        var baseIndex = 0;
        var bestCount = int.MaxValue;
        for (var i = 0; i < neighborRootsScratch.Count; i++)
        {
            var s = neighborRootsScratch[i].space;
            var c = s?.Offsets?.Count ?? int.MaxValue;
            // Prefer smaller grids for less cloning work
            if (c < bestCount)
            {
                bestCount = c;
                baseIndex = i;
            }
        }

        var baseNeighbor = neighborRootsScratch[baseIndex];
        var baseGrid = baseNeighbor.space.Grid;

        if (baseGrid != null)
        {
            scratchGrid.CopyFrom(baseGrid);
            var baseRoot = baseNeighbor.placement.Root;

            for (int i = 0; i < neighborRootsScratch.Count; i++)
            {
                if (i == baseIndex)
                    continue;
                var next = neighborRootsScratch[i];
                if (next.space.Grid == null)
                {
                    System.Array.Clear(scratchGrid.Bits, 0, scratchGrid.Bits.Length);
                    break;
                }
                var shift = next.placement.Root - baseRoot;
                scratchGrid.AndShifted(next.space.Grid, shift);
            }

            // Extract bit positions - managed loop
            int wordsPerRow = scratchGrid.WordsPerRow;
            ulong[] bits = scratchGrid.Bits;
            int h = scratchGrid.Height;
            int minX = scratchGrid.Min.x;
            int minY = scratchGrid.Min.y;

            candidatesScratch.Clear();
            // Upper bound for the intersection is the smallest neighbor space we picked as the base.
            // Avoid pre-allocating by grid area: large sparse grids would cause huge allocations here.
            if (bestCount > 0 && bestCount != int.MaxValue)
                candidatesScratch.Capacity = Mathf.Max(candidatesScratch.Capacity, bestCount);

            for (int r = 0; r < h; r++)
            {
                long rowStart = (long)r * wordsPerRow;
                int y = minY + r;
                for (int w = 0; w < wordsPerRow; w++)
                {
                    ulong word = bits[rowStart + w];
                    if (word == 0) continue;
                    
                    int baseX = minX + (w << 6);
                    while (word != 0)
                    {
                        var tz = BitGrid.TrailingZeroCount(word);
                        var x = baseX + tz;
                        candidatesScratch.Add(baseRoot + new Vector2Int(x, y));
                        word &= word - 1;
                    }
                }
            }
        }
        else
        {
            // Fallback if BitGrid creation failed (unlikely)
             candidatesScratch.Clear();
             // Logic would go here but with lazy initialization/null check it should be fine.
        }

        if (result != null)
            result.AddRange(candidatesScratch);

        if (result == null || result.Count == 0)
        {
            // Fallback strategies (scoring etc)
            scoredScratch.Clear();
            foreach (var (neighbor, space) in neighborRootsScratch)
            {
                var offsets = space.OffsetsList;
                for (var i = 0; i < offsets.Count; i++)
                {
                    var pos = neighbor.Root + offsets[i];
                    if (!scoredScratch.TryGetValue(pos, out var count))
                        count = 0;
                    scoredScratch[pos] = count + 1;
                }
            }

            var maxSatisfied = 0;
            foreach (var kv in scoredScratch)
                maxSatisfied = Mathf.Max(maxSatisfied, kv.Value);

            candidatesScratch.Clear();
            foreach (var kv in scoredScratch)
            {
                if (kv.Value == maxSatisfied)
                    candidatesScratch.Add(kv.Key);
            }
            candidatesScratch.Shuffle(rng);

            var limit = Mathf.Max(1, settings.MaxFallbackCandidates);
            for (var i = 0; i < candidatesScratch.Count && i < limit; i++)
            {
                result?.Add(candidatesScratch[i]);
            }
        }

        if ((result == null || result.Count == 0) && allowExistingRoot && placed.TryGetValue(nodeId, out var existingPlacement))
            result?.Add(existingPlacement.Root);

        if (result == null || result.Count == 0)
        {
            var neighborInfo = string.Join("; ", neighborRootsScratch.Select(n => $"{n.placement.NodeId}:{n.space.Offsets.Count}"));
            Debug.LogWarning($"[LayoutGenerator] No position candidates for node {nodeId} prefab {prefab.name}. Neighbors: {neighborInfo}");
        }

        if (profiling != null)
        {
            profiling.FindPositionCandidatesCalls++;
            profiling.FindPositionCandidatesTicks += NowTicks() - start;
        }
    }

    private void FindPositionCandidates(
        int nodeIndex,
        GameObject prefab,
        ModuleShape shape,
        EnergyCache cache,
        List<Vector2Int> result,
        bool allowExistingRoot,
        Vector2Int existingRoot)
    {
        using var _ps = PS(S_FindPositionCandidates);
        var start = profiling != null ? NowTicks() : 0;
        result?.Clear();
        void Finish()
        {
            if (profiling != null)
            {
                profiling.FindPositionCandidatesCalls++;
                profiling.FindPositionCandidatesTicks += NowTicks() - start;
            }
        }
        if (shape == null || prefab == null || cache == null || neighborIndicesByIndex == null || nodeIndex < 0 || nodeIndex >= neighborIndicesByIndex.Length)
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

        var baseCount = 0;
        if (allowExistingRoot)
        {
            result?.Add(existingRoot);
            baseCount = result?.Count ?? 0;
        }

        if (neighborRootsScratch.Count == 0)
        {
            if (result == null || result.Count == baseCount)
            {
                var anyPlaced = false;
                for (var i = 0; i < cache.NodeCount; i++)
                {
                    if (cache.IsPlaced[i])
                    {
                        anyPlaced = true;
                        break;
                    }
                }

                if (!anyPlaced)
                {
                    result?.Add(Vector2Int.zero);
                }
                else
                {
                    var spacing = EstimateUnconstrainedSpacing(shape);
                    var rings = Mathf.Clamp(settings.MaxWiggleCandidates, 4, 64) / 4;
                    var center = allowExistingRoot ? existingRoot : Vector2Int.zero;
                    for (int r = 0; r <= rings; r++)
                    {
                        for (int dx = -r; dx <= r; dx++)
                        {
                            for (int dy = -r; dy <= r; dy++)
                            {
                                if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r)
                                    continue;
                                result?.Add(center + new Vector2Int(dx * spacing, dy * spacing));
                                if (result != null && result.Count - baseCount >= Mathf.Max(8, settings.MaxFallbackCandidates / 8))
                                    break;
                            }
                            if (result != null && result.Count - baseCount >= Mathf.Max(8, settings.MaxFallbackCandidates / 8))
                                break;
                        }
                        if (result != null && result.Count - baseCount >= Mathf.Max(8, settings.MaxFallbackCandidates / 8))
                            break;
                    }
                }
            }
            Finish();
            return;
        }

        var useSampling = settings != null && settings.UseRejectionSamplingCandidates;
        var desired = Mathf.Clamp(Mathf.Max(16, settings.MaxWiggleCandidates), 8, 256);
        if (result != null)
            baseCount = result.Count;

        // FAST PATH: Single neighbor.
        if (neighborRootsScratch.Count == 1)
        {
            var neighbor = neighborRootsScratch[0].placement;
            var space = neighborRootsScratch[0].space;
            var offsets = space.OffsetsList;
            if (offsets != null && offsets.Count > 0)
            {
                if (!useSampling || offsets.Count <= desired)
                {
                    for (int i = 0; i < offsets.Count; i++)
                        result?.Add(neighbor.Root + offsets[i]);
                }
                else
                {
                    for (int i = 0; i < desired; i++)
                        result?.Add(neighbor.Root + offsets[rng.Next(offsets.Count)]);
                }
            }
            Finish();
            return;
        }

        // Multiple neighbors:
        // - Exact mode: BitGrid intersection + full enumeration.
        // - Sampling mode: rejection sampling against neighbor spaces (bounded).
        var baseIndex = 0;
        var bestCount = int.MaxValue;
        for (var i = 0; i < neighborRootsScratch.Count; i++)
        {
            var s = neighborRootsScratch[i].space;
            var c = s?.Offsets?.Count ?? int.MaxValue;
            if (c < bestCount)
            {
                bestCount = c;
                baseIndex = i;
            }
        }

        var baseNeighbor = neighborRootsScratch[baseIndex];
        var baseRoot = baseNeighbor.placement.Root;

        if (useSampling)
        {
            var baseOffsets = baseNeighbor.space?.OffsetsList;
            if (baseOffsets != null && baseOffsets.Count > 0)
            {
                // Keep this bounded: this is called often inside SA. Large rejection loops can be worse than scanning.
                // Oversample moderately; if acceptance is low we still return a small set quickly.
                var attempts = Mathf.Min(baseOffsets.Count, Mathf.Clamp(desired * 16 * Mathf.Max(1, neighborRootsScratch.Count), 128, 4096));
                for (var a = 0; a < attempts && (result == null || (result.Count - baseCount) < desired); a++)
                {
                    var candidateRoot = baseRoot + baseOffsets[rng.Next(baseOffsets.Count)];
                    var ok = true;
                    for (var i = 0; i < neighborRootsScratch.Count; i++)
                    {
                        if (i == baseIndex)
                            continue;
                        var n = neighborRootsScratch[i];
                        var delta = candidateRoot - n.placement.Root;
                        var g = n.space?.Grid;
                        if (g != null)
                        {
                            if (!g.IsSet(delta))
                            {
                                ok = false;
                                break;
                            }
                        }
                        else
                        {
                            if (n.space == null || !n.space.Contains(delta))
                            {
                                ok = false;
                                break;
                            }
                        }
                    }
                    if (ok)
                        result?.Add(candidateRoot);
                }
            }

            // If we failed to satisfy all neighbors, fall back to a bounded “satisfy max neighbors” sample.
            if (result == null || result.Count == baseCount)
            {
                scoredScratch.Clear();
                var perNeighbor = Mathf.Clamp(desired * 8, 64, 2048);
                foreach (var (neighbor, space) in neighborRootsScratch)
                {
                    var offsets = space?.OffsetsList;
                    if (offsets == null || offsets.Count == 0)
                        continue;
                    var take = Mathf.Min(perNeighbor, offsets.Count);
                    for (var i = 0; i < take; i++)
                    {
                        var pos = neighbor.Root + offsets[rng.Next(offsets.Count)];
                        if (!scoredScratch.TryGetValue(pos, out var count))
                            count = 0;
                        scoredScratch[pos] = count + 1;
                    }
                }

                var maxSatisfied = 0;
                foreach (var kv in scoredScratch)
                    maxSatisfied = Mathf.Max(maxSatisfied, kv.Value);

                candidatesScratch.Clear();
                foreach (var kv in scoredScratch)
                {
                    if (kv.Value == maxSatisfied)
                        candidatesScratch.Add(kv.Key);
                }
                candidatesScratch.Shuffle(rng);
                for (var i = 0; i < candidatesScratch.Count && i < desired; i++)
                    result?.Add(candidatesScratch[i]);
            }

            Finish();
            return;
        }

        var baseGrid = baseNeighbor.space.Grid;

        if (baseGrid != null)
        {
            scratchGrid.CopyFrom(baseGrid);

            for (int i = 0; i < neighborRootsScratch.Count; i++)
            {
                if (i == baseIndex)
                    continue;
                var next = neighborRootsScratch[i];
                if (next.space.Grid == null)
                {
                     System.Array.Clear(scratchGrid.Bits, 0, scratchGrid.Bits.Length);
                     break;
                }
                var shift = next.placement.Root - baseRoot;
                scratchGrid.AndShifted(next.space.Grid, shift);
            }

            // Extract bit positions - managed loop with pre-capacity
            int wordsPerRow = scratchGrid.WordsPerRow;
            ulong[] bits = scratchGrid.Bits;
            int h = scratchGrid.Height;
            int minX = scratchGrid.Min.x;
            int minY = scratchGrid.Min.y;

            candidatesScratch.Clear();
            // Upper bound for the intersection size is the base space size.
            if (bestCount > 0 && bestCount != int.MaxValue)
                candidatesScratch.Capacity = Mathf.Max(candidatesScratch.Capacity, bestCount);

            for (int r = 0; r < h; r++)
            {
                long rowStart = (long)r * wordsPerRow;
                int y = minY + r;
                for (int w = 0; w < wordsPerRow; w++)
                {
                    ulong word = bits[rowStart + w];
                    if (word == 0) continue;
                    
                    int baseX = minX + (w << 6);
                    while (word != 0)
                    {
                        var tz = BitGrid.TrailingZeroCount(word);
                        var x = baseX + tz;
                        candidatesScratch.Add(baseRoot + new Vector2Int(x, y));
                        word &= word - 1;
                    }
                }
            }
        }
        else
        {
             candidatesScratch.Clear();
        }

        if (result != null)
            result.AddRange(candidatesScratch);

        if (result == null || result.Count == baseCount)
        {
            scoredScratch.Clear();
            foreach (var (neighbor, space) in neighborRootsScratch)
            {
                var offsets = space.OffsetsList;
                for (var i = 0; i < offsets.Count; i++)
                {
                    var pos = neighbor.Root + offsets[i];
                    if (!scoredScratch.TryGetValue(pos, out var count))
                        count = 0;
                    scoredScratch[pos] = count + 1;
                }
            }

            var maxSatisfied = 0;
            foreach (var kv in scoredScratch)
                maxSatisfied = Mathf.Max(maxSatisfied, kv.Value);

            candidatesScratch.Clear();
            foreach (var kv in scoredScratch)
            {
                if (kv.Value == maxSatisfied)
                    candidatesScratch.Add(kv.Key);
            }
            candidatesScratch.Shuffle(rng);

            var limit = Mathf.Max(1, settings.MaxFallbackCandidates);
            for (var i = 0; i < candidatesScratch.Count && i < limit; i++)
            {
                result?.Add(candidatesScratch[i]);
            }
        }

        if ((result == null || result.Count == 0) && allowExistingRoot)
            result?.Add(existingRoot);

        Finish();
    }

    private int EstimateUnconstrainedSpacing(ModuleShape shape)
    {
        if (shape?.FloorCells == null || shape.FloorCells.Count == 0)
            return 8;

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        foreach (var c in shape.FloorCells)
        {
            minX = Mathf.Min(minX, c.x);
            minY = Mathf.Min(minY, c.y);
            maxX = Mathf.Max(maxX, c.x);
            maxY = Mathf.Max(maxY, c.y);
        }
        var w = Mathf.Max(1, maxX - minX + 1);
        var h = Mathf.Max(1, maxY - minY + 1);
        return Mathf.Max(w, h) + 4;
    }

    private bool HasIllegalOverlap(RoomPlacement candidate, Dictionary<string, RoomPlacement> placed)
    {
        if (candidate == null)
            return true;

        var candidateFloor = candidate.Shape?.FloorCells;
        if (candidateFloor == null || candidateFloor.Count == 0)
            return true;

        foreach (var other in placed.Values)
        {
            if (other == null)
                continue;
            if (other.NodeId == candidate.NodeId)
                continue;

            var otherFloor = other.Shape?.FloorCells;
            if (otherFloor == null || otherFloor.Count == 0)
                continue;

            var delta = other.Root - candidate.Root;
            var overlapCount = 0;
            if (settings != null && settings.UseBitsetOverlap)
            {
                var bitsCandidate = GetBitsets(candidate.Shape);
                var bitsOther = GetBitsets(other.Shape);
                if (bitsCandidate?.Floor != null && bitsOther?.Floor != null)
                {
                    var shift = (bitsOther.Floor.Min + delta) - bitsCandidate.Floor.Min;
                    overlapCount = bitsCandidate.Floor.CountIllegalOverlapsShifted(bitsOther.Floor, shift, candidate.Root, AllowedWorldCells.None, earlyStopAtTwo: true, out _);
                }
            }
            else
            {
                overlapCount = CountOverlapShifted(candidateFloor, otherFloor, delta, AllowedWorldCells.None, candidate.Root, out _, earlyStopAtTwo: true);
            }
            if (overlapCount <= 0)
                continue;

            if (overlapCount == 1 && IsAllowedBiteOverlap(candidate, other, 1))
                continue;

            return true;
        }
        return false;
    }
}
