// Assets/scripts/Generation/Graph/MapGraphLayoutGenerator.SA.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private List<LayoutState> AddChain(LayoutState baseState, MapGraphChainBuilder.Chain chain, int maxLayouts)
    {
        var generated = new List<LayoutState>();
        if (chain == null || chain.Nodes == null || chain.Nodes.Count == 0)
        {
            lastFailureDetail = "Chain is null/empty.";
            return generated;
        }

        var current = GetInitialLayout(baseState, chain);
        if (current == null)
        {
            var chainIds = string.Join(",", chain.Nodes.Select(n => n?.id));
            lastFailureDetail = $"GetInitialLayout returned null for chain [{chainIds}]";
            Debug.LogWarning($"[LayoutGenerator] {lastFailureDetail}");
            return generated;
        }

        float temperature = EstimateInitialTemperature(chain, current);

        for (int t = 0; t < Mathf.Max(1, settings.TemperatureSteps); t++)
        {
            for (int i = 0; i < Mathf.Max(1, settings.InnerIterations); i++)
            {
                var perturbed = PerturbLayout(current, chain);
                var currentEnergy = current.EnergyCache != null ? current.EnergyCache.TotalEnergy : ComputeEnergy(current.Rooms);
                var perturbedEnergy = perturbed != null
                    ? (perturbed.EnergyCache != null ? perturbed.EnergyCache.TotalEnergy : ComputeEnergy(perturbed.Rooms))
                    : float.MaxValue;
                var delta = perturbedEnergy - currentEnergy;

                if (perturbed != null && ShouldValidateForOutput(perturbed) && IsValidLayout(perturbed.Rooms))
                {
                    if (DifferentEnough(perturbed, generated, chain))
                    {
                        generated.Add(perturbed);
                        if (generated.Count >= maxLayouts)
                            return generated;
                    }
                }

                if (delta < 0f)
                {
                    if (perturbed != null)
                        current = perturbed;
                }
                else
                {
                    var p = Mathf.Exp(-delta / Mathf.Max(0.0001f, temperature));
                    if (rng.NextDouble() < p && perturbed != null)
                        current = perturbed;
                }
            }

            temperature *= Mathf.Clamp(settings.Cooling, 0.01f, 0.999f);
        }

        if (generated.Count == 0)
        {
            var chainIds = string.Join(",", chain.Nodes.Select(n => n?.id));
            lastFailureDetail = $"AddChain produced 0 layouts for chain [{chainIds}]";
            Debug.LogWarning($"[LayoutGenerator] {lastFailureDetail}");
        }

        return generated;
    }

    private LayoutState GetInitialLayout(LayoutState baseState, MapGraphChainBuilder.Chain chain)
    {
        var rooms = new Dictionary<string, RoomPlacement>(baseState.Rooms);
        var order = BuildChainBfsOrder(chain, rooms);
        foreach (var node in order)
        {
            if (node == null || string.IsNullOrEmpty(node.id))
                continue;
            if (rooms.ContainsKey(node.id))
                continue;

            var prefabs = GetRoomPrefabs(node);
            prefabs.Shuffle(rng);

            RoomPlacement bestPlacement = null;
            float bestEnergy = float.MaxValue;
            foreach (var prefab in prefabs)
            {
                if (!shapeLibrary.TryGetShape(prefab, out var shape, out _))
                    continue;
                var candidates = FindPositionCandidates(node.id, prefab, shape, rooms);
                foreach (var pos in candidates)
                {
                    var placement = new RoomPlacement(node.id, prefab, shape, pos);
                    rooms[node.id] = placement;
                    var energy = ComputeEnergy(rooms);
                    if (energy < bestEnergy)
                    {
                        bestEnergy = energy;
                        bestPlacement = placement;
                    }
                    rooms.Remove(node.id);
                }
            }

            if (bestPlacement == null)
            {
                var chainIds = string.Join(",", chain.Nodes.Select(n => n?.id));
                lastFailureDetail = $"No candidates for node {node.id} in chain [{chainIds}]";
                Debug.LogWarning($"[LayoutGenerator] {lastFailureDetail}");
                return null;
            }

            rooms[node.id] = bestPlacement;
        }

        return new LayoutState(rooms, baseState.ChainIndex, BuildEnergyCache(rooms));
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
                if (string.IsNullOrEmpty(otherId)) continue;
                var other = chain.Nodes.FirstOrDefault(nd => nd != null && nd.id == otherId);
                if (other == null || seen.Contains(other.id)) continue;
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

    private LayoutState PerturbLayout(LayoutState state, MapGraphChainBuilder.Chain chain)
    {
        var chainNodes = chain.Nodes.Where(n => n != null && !string.IsNullOrEmpty(n.id)).Select(n => n.id).ToList();
        var movable = chainNodes.Where(id => state.Rooms.ContainsKey(id)).ToList();
        if (movable.Count == 0)
            return null;

        var targetId = movable[rng.Next(movable.Count)];
        var rooms = new Dictionary<string, RoomPlacement>(state.Rooms);
        var targetPlacement = rooms[targetId];
        var changeShape = rng.NextDouble() < 0.35;

        var prefabs = GetRoomPrefabs(graphAsset.GetNodeById(targetId));
        if (prefabs.Count == 0)
            return null;

        GameObject newPrefab = targetPlacement.Prefab;
        ModuleShape newShape = targetPlacement.Shape;
        if (changeShape && prefabs.Count > 1)
        {
            newPrefab = prefabs[rng.Next(prefabs.Count)];
            if (!shapeLibrary.TryGetShape(newPrefab, out newShape, out _))
                return null;
        }

        List<Vector2Int> candidates;
        if (rng.NextDouble() < 0.5)
        {
            candidates = FindPositionCandidates(targetId, newPrefab, newShape, rooms, allowExistingRoot: !changeShape);
        }
        else
        {
            candidates = WiggleCandidates(targetId, newPrefab, newShape, rooms);
            if (candidates.Count == 0)
                candidates = FindPositionCandidates(targetId, newPrefab, newShape, rooms, allowExistingRoot: !changeShape);
        }
        if (candidates.Count == 0)
            return null;

        var newRoot = candidates[rng.Next(candidates.Count)];
        rooms[targetId] = new RoomPlacement(targetId, newPrefab, newShape, newRoot);

        var baseCache = state.EnergyCache ?? BuildEnergyCache(state.Rooms);
        var newCache = baseCache != null ? UpdateEnergyCacheForMove(baseCache, state.Rooms, rooms, targetId) : null;
        return new LayoutState(rooms, state.ChainIndex, newCache);
    }

    private List<Vector2Int> WiggleCandidates(string nodeId, GameObject prefab, ModuleShape shape, Dictionary<string, RoomPlacement> placed)
    {
        var result = new List<Vector2Int>();
        var neighbors = graphAsset.GetEdgesFor(nodeId)
            .Select(e => e.fromNodeId == nodeId ? e.toNodeId : e.fromNodeId)
            .Where(id => !string.IsNullOrEmpty(id) && placed.ContainsKey(id))
            .ToList();
        if (neighbors.Count == 0)
            return result;

        var neighborId = neighbors[rng.Next(neighbors.Count)];
        var neighbor = placed[neighborId];
        if (!configSpaceLibrary.TryGetSpace(neighbor.Prefab, prefab, out var space, out _))
            return result;
        foreach (var off in space.Offsets.OrderBy(_ => rng.Next()))
        {
            var pos = neighbor.Root + off;
            result.Add(pos);
            if (result.Count >= Mathf.Max(1, settings.MaxWiggleCandidates)) break;
        }
        return result;
    }

    private bool ShouldValidateForOutput(LayoutState candidate)
    {
        if (candidate?.EnergyCache == null)
            return false;

        // Only pay the expensive O(n^2) validation cost when the cached energy indicates a full solution:
        // - zero illegal overlaps
        // - all currently-placed edges are touching (distance penalty == 0)
        return candidate.EnergyCache.OverlapPenaltySum <= 0.0001f &&
               candidate.EnergyCache.DistancePenaltySum <= 0.0001f;
    }

    private bool DifferentEnough(LayoutState candidate, List<LayoutState> existing, MapGraphChainBuilder.Chain chain)
    {
        foreach (var other in existing)
        {
            if (!DiffersOnChain(candidate.Rooms, other.Rooms, chain))
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
            if (!hasA) continue;
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

    private float EstimateInitialTemperature(MapGraphChainBuilder.Chain chain, LayoutState state)
    {
        float avgArea = 1f;
        var sizes = state.Rooms.Values.Select(r => r.Shape?.SolidCells?.Count ?? 1).ToList();
        if (sizes.Count > 0)
            avgArea = (float)sizes.Average();
        return Mathf.Max(1f, avgArea * 0.25f);
    }
}
