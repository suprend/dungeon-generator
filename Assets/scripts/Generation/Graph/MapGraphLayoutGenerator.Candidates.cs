// Assets/scripts/Generation/Graph/MapGraphLayoutGenerator.Candidates.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private List<Vector2Int> FindPositionCandidates(string nodeId, GameObject prefab, ModuleShape shape, Dictionary<string, RoomPlacement> placed, bool allowExistingRoot = false)
    {
        var result = new List<Vector2Int>();
        if (shape == null || prefab == null)
            return result;

        var neighborRoots = new List<(RoomPlacement placement, ConfigurationSpace space)>();
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
            neighborRoots.Add((neighbor, space));
        }

        if (neighborRoots.Count == 0)
        {
            if (allowExistingRoot && placed.TryGetValue(nodeId, out var existing))
                result.Add(existing.Root);

            if (result.Count == 0)
            {
                if (placed.Count == 0)
                {
                    result.Add(Vector2Int.zero);
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
                                result.Add(new Vector2Int(dx * spacing, dy * spacing));
                                if (result.Count >= Mathf.Max(8, settings.MaxFallbackCandidates / 8))
                                    break;
                            }
                            if (result.Count >= Mathf.Max(8, settings.MaxFallbackCandidates / 8))
                                break;
                        }
                        if (result.Count >= Mathf.Max(8, settings.MaxFallbackCandidates / 8))
                            break;
                    }
                }
            }
            return result;
        }

        var baseNeighbor = neighborRoots[0];
        var candidates = baseNeighbor.space.Offsets
            .Select(off => baseNeighbor.placement.Root + off)
            .ToList();

        var totalOffsets = candidates.Count;

        for (int i = 1; i < neighborRoots.Count; i++)
        {
            var next = neighborRoots[i];
            candidates = candidates
                .Where(pos => next.space.Contains(pos - next.placement.Root))
                .ToList();
            if (candidates.Count == 0)
                break;
        }

        foreach (var pos in candidates)
            result.Add(pos);

        if (result.Count == 0)
        {
            var scored = new Dictionary<Vector2Int, int>();
            foreach (var (neighbor, space) in neighborRoots)
            {
                foreach (var off in space.Offsets)
                {
                    var pos = neighbor.Root + off;
                    if (!scored.TryGetValue(pos, out var count))
                        count = 0;
                    scored[pos] = count + 1;
                }
            }

            var maxSatisfied = scored.Count > 0 ? scored.Values.Max() : 0;
            foreach (var kv in scored.Where(kv => kv.Value == maxSatisfied).OrderBy(_ => rng.Next()))
            {
                result.Add(kv.Key);
                if (result.Count >= Mathf.Max(1, settings.MaxFallbackCandidates)) break;
            }
        }

        if (result.Count == 0 && allowExistingRoot && placed.TryGetValue(nodeId, out var existingPlacement))
            result.Add(existingPlacement.Root);

        if (result.Count == 0)
        {
            var neighborInfo = string.Join("; ", neighborRoots.Select(n => $"{n.placement.NodeId}:{n.space.Offsets.Count}"));
            Debug.LogWarning($"[LayoutGenerator] No position candidates for node {nodeId} prefab {prefab.name}. Offsets before overlap: {totalOffsets}. Neighbors: {neighborInfo}");
        }

        return result;
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

        var cells = candidate.WorldCells;
        foreach (var other in placed.Values)
        {
            if (other == null)
                continue;
            if (other.NodeId == candidate.NodeId)
                continue;

            var overlapCount = CountOverlapCells(cells, other.WorldCells, out _);
            if (overlapCount == 0)
                continue;

            if (IsAllowedBiteOverlap(candidate, other, overlapCount))
                continue;

            return true;
        }
        return false;
    }
}

