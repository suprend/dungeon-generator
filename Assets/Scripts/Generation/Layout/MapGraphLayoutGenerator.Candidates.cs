// Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Candidates.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private readonly List<(RoomPlacement placement, ConfigurationSpace space)> neighborRootsScratch = new();
    private readonly List<Vector2Int> candidatesScratch = new();
    private readonly Dictionary<Vector2Int, int> scoredScratch = new();

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

        // Start intersection from the smallest config-space to keep candidate list small.
        var baseIndex = 0;
        var bestCount = int.MaxValue;
        for (var i = 0; i < neighborRootsScratch.Count; i++)
        {
            var c = neighborRootsScratch[i].space?.Offsets?.Count ?? int.MaxValue;
            if (c < bestCount)
            {
                bestCount = c;
                baseIndex = i;
            }
        }

        var baseNeighbor = neighborRootsScratch[baseIndex];
        candidatesScratch.Clear();
        var baseOffsets = baseNeighbor.space.OffsetsList;
        for (var i = 0; i < baseOffsets.Count; i++)
            candidatesScratch.Add(baseNeighbor.placement.Root + baseOffsets[i]);

        var totalOffsets = candidatesScratch.Count;

        for (int i = 0; i < neighborRootsScratch.Count; i++)
        {
            if (i == baseIndex)
                continue;
            var next = neighborRootsScratch[i];
            var write = 0;
            for (int read = 0; read < candidatesScratch.Count; read++)
            {
                var pos = candidatesScratch[read];
                if (next.space.Contains(pos - next.placement.Root))
                    candidatesScratch[write++] = pos;
            }
            if (write < candidatesScratch.Count)
                candidatesScratch.RemoveRange(write, candidatesScratch.Count - write);
            if (candidatesScratch.Count == 0)
                break;
        }

        if (result != null)
            result.AddRange(candidatesScratch);

        if (result == null || result.Count == 0)
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

        if ((result == null || result.Count == 0) && allowExistingRoot && placed.TryGetValue(nodeId, out var existingPlacement))
            result?.Add(existingPlacement.Root);

        if (result == null || result.Count == 0)
        {
            var neighborInfo = string.Join("; ", neighborRootsScratch.Select(n => $"{n.placement.NodeId}:{n.space.Offsets.Count}"));
            Debug.LogWarning($"[LayoutGenerator] No position candidates for node {nodeId} prefab {prefab.name}. Offsets before overlap: {totalOffsets}. Neighbors: {neighborInfo}");
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
        if (shape == null || prefab == null || cache == null || neighborIndicesByIndex == null || nodeIndex < 0 || nodeIndex >= neighborIndicesByIndex.Length)
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
        {
            if (allowExistingRoot)
                result?.Add(existingRoot);

            if (result == null || result.Count == 0)
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

        // Start intersection from the smallest config-space to keep candidate list small.
        var baseIndex = 0;
        var bestCount = int.MaxValue;
        for (var i = 0; i < neighborRootsScratch.Count; i++)
        {
            var c = neighborRootsScratch[i].space?.Offsets?.Count ?? int.MaxValue;
            if (c < bestCount)
            {
                bestCount = c;
                baseIndex = i;
            }
        }

        var baseNeighbor = neighborRootsScratch[baseIndex];
        candidatesScratch.Clear();
        var baseOffsets = baseNeighbor.space.OffsetsList;
        for (var i = 0; i < baseOffsets.Count; i++)
            candidatesScratch.Add(baseNeighbor.placement.Root + baseOffsets[i]);

        for (int i = 0; i < neighborRootsScratch.Count; i++)
        {
            if (i == baseIndex)
                continue;
            var next = neighborRootsScratch[i];
            var write = 0;
            for (int read = 0; read < candidatesScratch.Count; read++)
            {
                var pos = candidatesScratch[read];
                if (next.space.Contains(pos - next.placement.Root))
                    candidatesScratch[write++] = pos;
            }
            if (write < candidatesScratch.Count)
                candidatesScratch.RemoveRange(write, candidatesScratch.Count - write);
            if (candidatesScratch.Count == 0)
                break;
        }

        if (result != null)
            result.AddRange(candidatesScratch);

        if (result == null || result.Count == 0)
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

        if (profiling != null)
        {
            profiling.FindPositionCandidatesCalls++;
            profiling.FindPositionCandidatesTicks += NowTicks() - start;
        }
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
