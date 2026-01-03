// Assets/scripts/Generation/Graph/MapGraphLayoutGenerator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Generates planar room layouts using configuration spaces, chain decomposition, and simulated annealing.
/// Produces multiple candidate layouts via backtracking-style stack search (GenerateLayout + AddChain).
/// </summary>
public sealed class MapGraphLayoutGenerator
{
    public sealed class Settings
    {
        public int MaxLayoutsPerChain { get; set; } = 8;
        public int TemperatureSteps { get; set; } = 12;
        public int InnerIterations { get; set; } = 64;
        public float Cooling { get; set; } = 0.65f;
        public int MaxWiggleCandidates { get; set; } = 16;
        public int MaxFallbackCandidates { get; set; } = 128;
        public bool VerboseConfigSpaceLogs { get; set; } = false;
        public int MaxConfigSpaceLogs { get; set; } = 64;
    }

    public sealed class LayoutResult
    {
        public Dictionary<string, RoomPlacement> Rooms { get; }

        public LayoutResult(Dictionary<string, RoomPlacement> rooms)
        {
            Rooms = rooms ?? new Dictionary<string, RoomPlacement>();
        }
    }

    public sealed class RoomPlacement
    {
        public string NodeId { get; }
        public GameObject Prefab { get; }
        public ModuleShape Shape { get; }
        public Vector2Int Root { get; }

        public RoomPlacement(string nodeId, GameObject prefab, ModuleShape shape, Vector2Int root)
        {
            NodeId = nodeId;
            Prefab = prefab;
            Shape = shape;
            Root = root;
        }

        // World-space occupied floor cells for overlap/config checks.
        public HashSet<Vector2Int> WorldCells
        {
            get
            {
                var set = new HashSet<Vector2Int>();
                if (Shape?.FloorCells == null)
                    return set;
                foreach (var c in Shape.FloorCells)
                    set.Add(c + Root);
                return set;
            }
        }

        public HashSet<Vector2Int> WorldWallCells
        {
            get
            {
                var set = new HashSet<Vector2Int>();
                if (Shape?.WallCells == null)
                    return set;
                foreach (var c in Shape.WallCells)
                    set.Add(c + Root);
                return set;
            }
        }
    }

    private readonly System.Random rng;
    private readonly Settings settings;

    private ShapeLibrary shapeLibrary;
    private ConfigurationSpaceLibrary configSpaceLibrary;
    private MapGraphAsset graphAsset;
    private List<MapGraphChainBuilder.Chain> orderedChains;
    private Dictionary<string, List<GameObject>> roomPrefabLookup;

    public MapGraphLayoutGenerator(int? seed = null, Settings settings = null)
    {
        rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        this.settings = settings ?? new Settings();
    }

    public bool TryGenerate(MapGraphAsset graphAsset, TileStampService stamp, out LayoutResult layout, out string error, int? maxLayoutsPerChain = null)
    {
        layout = null;
        error = null;
        if (graphAsset == null)
        {
            error = "Graph asset is null.";
            return false;
        }
        if (stamp == null)
        {
            error = "TileStampService is required for layout generation.";
            return false;
        }

        this.graphAsset = graphAsset;

        if (!MapGraphFaceBuilder.TryBuildFaces(graphAsset, out var faces, out error))
            return false;
        if (!MapGraphChainBuilder.TryBuildChains(graphAsset, faces, out var chains, out error))
            return false;
        orderedChains = chains;

        shapeLibrary = new ShapeLibrary(stamp);
        configSpaceLibrary = new ConfigurationSpaceLibrary(shapeLibrary);
        configSpaceLibrary.SetDebug(settings.VerboseConfigSpaceLogs, settings.MaxConfigSpaceLogs);

        roomPrefabLookup = BuildRoomPrefabLookup(graphAsset);
        if (roomPrefabLookup.Count == 0)
        {
            error = "No room prefabs available for layout generation.";
            return false;
        }

        var connectorPrefabs = new HashSet<GameObject>();
        foreach (var edge in graphAsset.Edges)
        {
            var conn = edge?.connectionType ?? graphAsset.DefaultConnectionType;
            if (conn?.prefabs == null) continue;
            foreach (var p in conn.prefabs)
                if (p != null) connectorPrefabs.Add(p);
        }

        // Precompute shapes and configuration spaces for all room prefabs
        foreach (var prefab in roomPrefabLookup.Values.SelectMany(x => x).Distinct())
        {
            if (!shapeLibrary.TryGetShape(prefab, out _, out error))
                return false;
        }
        var prefabList = roomPrefabLookup.Values.SelectMany(x => x).Distinct().ToList();
        for (int i = 0; i < prefabList.Count; i++)
        {
            for (int j = 0; j < prefabList.Count; j++)
            {
                if (connectorPrefabs.Contains(prefabList[i]) && connectorPrefabs.Contains(prefabList[j]))
                    continue;
                if (!configSpaceLibrary.TryGetSpace(prefabList[i], prefabList[j], out _, out error))
                    return false;
            }
        }

        var initialLayout = new LayoutState(new Dictionary<string, RoomPlacement>(), 0);
        var stack = new Stack<LayoutState>();
        stack.Push(initialLayout);

        while (stack.Count > 0)
        {
            var state = stack.Pop();
            if (state.ChainIndex >= orderedChains.Count)
            {
                if (IsGloballyValid(state.Rooms))
                {
                    layout = new LayoutResult(state.Rooms);
                    return true;
                }
                continue;
            }

            var chain = orderedChains[state.ChainIndex];
            var maxLayouts = Mathf.Max(1, maxLayoutsPerChain ?? settings.MaxLayoutsPerChain);
            var expansions = AddChain(state, chain, maxLayouts);
            foreach (var exp in expansions)
            {
                stack.Push(new LayoutState(exp.Rooms, state.ChainIndex + 1));
            }
        }

        error = "Failed to generate layout for all chains.";
        return false;
    }

    private List<LayoutState> AddChain(LayoutState baseState, MapGraphChainBuilder.Chain chain, int maxLayouts)
    {
        var generated = new List<LayoutState>();
        if (chain == null || chain.Nodes == null || chain.Nodes.Count == 0)
            return generated;

        var current = GetInitialLayout(baseState, chain);
        if (current == null)
        {
            Debug.LogWarning($"[LayoutGenerator] GetInitialLayout returned null for chain [{string.Join(",", chain.Nodes.Select(n => n?.id))}]");
            return generated;
        }

        if (IsValidLayout(current.Rooms) && DifferentEnough(current, generated, chain))
        {
            generated.Add(current);
            if (generated.Count >= maxLayouts)
                return generated;
        }

        var temperature = EstimateInitialTemperature(chain, current);
        var temperatureSteps = Mathf.Max(1, settings.TemperatureSteps);
        var innerIterations = Mathf.Max(1, settings.InnerIterations);
        var cooling = Mathf.Clamp(settings.Cooling, 0.01f, 0.999f);

        for (int t = 0; t < temperatureSteps; t++)
        {
            if (generated.Count >= maxLayouts)
                break;

            for (int i = 0; i < innerIterations; i++)
            {
                var perturbed = PerturbLayout(current, chain);
                if (perturbed != null && IsValidLayout(perturbed.Rooms))
                {
                    if (DifferentEnough(perturbed, generated, chain))
                        generated.Add(perturbed);
                }

                var currentEnergy = ComputeEnergy(current.Rooms);
                var perturbedEnergy = perturbed != null ? ComputeEnergy(perturbed.Rooms) : float.MaxValue;
                var delta = perturbedEnergy - currentEnergy;
                if (delta < 0 || rng.NextDouble() < Math.Exp(-delta / temperature))
                    current = perturbed ?? current;
            }

            temperature *= cooling;
        }

        if (generated.Count == 0)
            Debug.LogWarning($"[LayoutGenerator] AddChain produced 0 layouts for chain [{string.Join(",", chain.Nodes.Select(n => n?.id))}]");

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
                Debug.LogWarning($"[LayoutGenerator] No candidates for node {node.id} in chain [{string.Join(",", chain.Nodes.Select(n => n?.id))}]");
                return null;
            }

            rooms[node.id] = bestPlacement;
        }

        return new LayoutState(rooms, baseState.ChainIndex);
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

        // If the chain contains disconnected nodes (e.g., isolated vertices), append them deterministically.
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

        // Either re-sample full candidate set or pick a small random wiggle from config space of a random neighbor.
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

        return new LayoutState(rooms, state.ChainIndex);
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

            // If there are no constraints (isolated node / disconnected component), we still need a few candidate roots
            // to avoid "everything at (0,0)" when multiple components exist.
            if (result.Count == 0)
            {
                if (placed.Count == 0)
                {
                    result.Add(Vector2Int.zero);
                }
                else
                {
                    var spacing = EstimateUnconstrainedSpacing(shape);
                    // Simple expanding square ring around origin, shuffled later by energy tie-break.
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

        // Start from first neighbor's offsets
        var baseNeighbor = neighborRoots[0];
        var candidates = baseNeighbor.space.Offsets
            .Select(off => baseNeighbor.placement.Root + off)
            .ToList();

        var totalOffsets = candidates.Count;

        // Intersect with remaining neighbors
        for (int i = 1; i < neighborRoots.Count; i++)
        {
            var next = neighborRoots[i];
            candidates = candidates
                .Where(pos => next.space.Contains(pos - next.placement.Root))
                .ToList();
            if (candidates.Count == 0)
                break;
        }

        // Important: do NOT filter by overlaps here. Simulated annealing needs the ability to explore
        // temporarily-invalid layouts (overlaps / not-yet-satisfied edges) and reduce energy over time.
        foreach (var pos in candidates)
            result.Add(pos);

        // If strict intersection is empty, fall back to positions that satisfy the maximum number of placed neighbors.
        // This matches the intended "satisfy as many neighbors as possible" behavior from the paper.
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

    private int CountOverlapCells(HashSet<Vector2Int> a, HashSet<Vector2Int> b, out Vector2Int overlapCell)
    {
        overlapCell = default;
        if (a == null || b == null || a.Count == 0 || b.Count == 0)
            return 0;

        var count = 0;
        var iter = a.Count <= b.Count ? a : b;
        var other = ReferenceEquals(iter, a) ? b : a;
        foreach (var c in iter)
        {
            if (!other.Contains(c))
                continue;
            count++;
            overlapCell = c;
            if (count > 1)
                return count;
        }
        return count;
    }

    private bool IsAllowedBiteOverlap(RoomPlacement a, RoomPlacement b, int overlapCount)
    {
        if (a == null || b == null)
            return false;
        if (overlapCount != 1)
            return false;
        if (!AreNeighbors(a.NodeId, b.NodeId))
            return false;
        if (IsConnector(a.Prefab) == IsConnector(b.Prefab))
            return false;
        if (!RoomsTouchEitherWay(a, b))
            return false;
        return true;
    }

    private bool IsConnector(GameObject prefab)
    {
        return prefab != null && prefab.GetComponent<ConnectorMeta>() != null;
    }

    private bool AreNeighbors(string aId, string bId)
    {
        if (graphAsset == null || string.IsNullOrEmpty(aId) || string.IsNullOrEmpty(bId))
            return false;

        return graphAsset.GetEdgesFor(aId).Any(e =>
            e != null &&
            ((e.fromNodeId == aId && e.toNodeId == bId) || (e.fromNodeId == bId && e.toNodeId == aId)));
    }

    private bool IsValidLayout(Dictionary<string, RoomPlacement> rooms)
    {
        // No floor↔floor overlaps, except strict 1-tile bite on satisfied Room↔Corridor edges.
        // Additionally, no walls may overlap another module's floor.
        var list = rooms.Values.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            for (int j = i + 1; j < list.Count; j++)
            {
                var a = list[i];
                var b = list[j];

                var overlapCount = CountOverlapCells(a.WorldCells, b.WorldCells, out var overlapCell);
                var allowedBite = overlapCount == 1 && IsAllowedBiteOverlap(a, b, overlapCount);

                if (overlapCount > 0 && !allowedBite)
                    return false;

                var allowedA = allowedBite ? AllowedWallOnFloorCells(a, b, overlapCell) : null;
                var allowedB = allowedBite ? AllowedWallOnFloorCells(b, a, overlapCell) : null;
                if (HasOverlapExcept(a.WorldWallCells, b.WorldCells, allowedA))
                    return false;
                if (HasOverlapExcept(b.WorldWallCells, a.WorldCells, allowedB))
                    return false;
            }
        }

        // All placed neighbors must touch and must be Room↔Corridor only, with a 1-tile bite.
        foreach (var edge in graphAsset.Edges)
        {
            if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                continue;
            if (!rooms.TryGetValue(edge.fromNodeId, out var a) || !rooms.TryGetValue(edge.toNodeId, out var b))
                continue;
            if (IsConnector(a.Prefab) == IsConnector(b.Prefab))
                return false;
            if (!RoomsTouchEitherWay(a, b))
                return false;

            var overlapCount = CountOverlapCells(a.WorldCells, b.WorldCells, out _);
            if (overlapCount != 1)
                return false;
        }

        return true;
    }

    private bool IsGloballyValid(Dictionary<string, RoomPlacement> rooms)
    {
        if (!IsValidLayout(rooms))
            return false;

        // Connectivity: BFS over touching rooms along graph edges that are satisfied.
        if (rooms.Count == 0)
            return false;

        var start = rooms.Keys.First();
        var visited = new HashSet<string>();
        var q = new Queue<string>();
        visited.Add(start);
        q.Enqueue(start);

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var edge in graphAsset.GetEdgesFor(cur))
            {
                var other = edge.fromNodeId == cur ? edge.toNodeId : edge.fromNodeId;
                if (string.IsNullOrEmpty(other) || !rooms.ContainsKey(other))
                    continue;
                if (!RoomsTouchEitherWay(rooms[cur], rooms[other]))
                    continue;
                if (visited.Add(other))
                    q.Enqueue(other);
            }
        }

        return visited.Count == rooms.Count;
    }

    private bool RoomsTouchEitherWay(RoomPlacement a, RoomPlacement b)
    {
        return RoomsTouch(a, b) || RoomsTouch(b, a);
    }

    private bool RoomsTouch(RoomPlacement a, RoomPlacement b)
    {
        if (a == null || b == null || a.Prefab == null || b.Prefab == null)
            return false;
        if (!configSpaceLibrary.TryGetSpace(a.Prefab, b.Prefab, out var space, out _))
            return false;
        var delta = b.Root - a.Root;
        return space != null && space.Contains(delta);
    }

    private float ComputeEnergy(Dictionary<string, RoomPlacement> rooms)
    {
        const float overlapWeight = 1000f;
        const float distanceWeight = 1f;

        float overlapArea = 0f;
        var list = rooms.Values.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            for (int j = i + 1; j < list.Count; j++)
            {
                overlapArea += IntersectionPenalty(list[i], list[j]);
            }
        }

        float distPenalty = 0f;
        foreach (var edge in graphAsset.Edges)
        {
            if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                continue;
            if (!rooms.TryGetValue(edge.fromNodeId, out var a) || !rooms.TryGetValue(edge.toNodeId, out var b))
                continue;
            if (RoomsTouchEitherWay(a, b))
                continue;
            var da = CenterOf(a);
            var db = CenterOf(b);
            var diff = da - db;
            distPenalty += diff.sqrMagnitude;
        }

        return overlapWeight * overlapArea + distanceWeight * distPenalty;
    }

    private float IntersectionArea(RoomPlacement a, RoomPlacement b)
    {
        if (a == null || b == null)
            return 0f;

        var overlapCount = CountOverlapCells(a.WorldCells, b.WorldCells, out _);
        if (overlapCount == 0)
            return 0f;
        if (IsAllowedBiteOverlap(a, b, overlapCount))
            return 0f;
        return overlapCount;
    }

    private float IntersectionPenalty(RoomPlacement a, RoomPlacement b)
    {
        if (a == null || b == null)
            return 0f;

        var penalty = 0f;

        // Floor↔floor overlaps (except allowed bite).
        var floorOverlapCount = CountOverlapAll(a.WorldCells, b.WorldCells, (HashSet<Vector2Int>)null, out var lastFloorOverlapCell);
        var allowedBite = floorOverlapCount == 1 && IsAllowedBiteOverlap(a, b, 1);
        if (floorOverlapCount > 0 && !allowedBite)
            penalty += floorOverlapCount;

        // Wall↔floor overlaps (except allowed bite cell).
        var allowedA = allowedBite ? AllowedWallOnFloorCells(a, b, lastFloorOverlapCell) : null;
        var allowedB = allowedBite ? AllowedWallOnFloorCells(b, a, lastFloorOverlapCell) : null;
        penalty += CountOverlapAll(a.WorldWallCells, b.WorldCells, allowedA, out _);
        penalty += CountOverlapAll(b.WorldWallCells, a.WorldCells, allowedB, out _);

        return penalty;
    }

    private HashSet<Vector2Int> AllowedWallOnFloorCells(RoomPlacement wallOwner, RoomPlacement floorOwner, Vector2Int biteCell)
    {
        if (wallOwner == null || floorOwner == null)
            return null;

        var allowed = new HashSet<Vector2Int> { biteCell };

        // Carve-mask: connector side-walls adjacent to the entrance may overlap the room's floor.
        // This only applies to connector walls overlapping room floors (not room walls).
        if (!IsConnector(wallOwner.Prefab) || IsConnector(floorOwner.Prefab))
            return allowed;

        if (!TryGetSocketSideAtWorldCell(wallOwner, biteCell, out var side) &&
            !TryGetSocketSideAtWorldCell(floorOwner, biteCell, out side))
        {
            var delta = floorOwner.Root - wallOwner.Root;
            side = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? DoorSide.East : DoorSide.North;
        }

        var tangent = side == DoorSide.North || side == DoorSide.South ? Vector2Int.right : Vector2Int.up;
        allowed.Add(biteCell + tangent);
        allowed.Add(biteCell - tangent);
        return allowed;
    }

    private bool TryGetSocketSideAtWorldCell(RoomPlacement placement, Vector2Int worldCell, out DoorSide side)
    {
        side = default;
        if (placement?.Shape?.Sockets == null)
            return false;

        foreach (var sock in placement.Shape.Sockets)
        {
            if (sock == null)
                continue;
            if (placement.Root + sock.CellOffset != worldCell)
                continue;
            side = sock.Side;
            return true;
        }
        return false;
    }

    private bool HasOverlapExcept(HashSet<Vector2Int> a, HashSet<Vector2Int> b, HashSet<Vector2Int> allowedCells)
    {
        if (a == null || b == null || a.Count == 0 || b.Count == 0)
            return false;
        var iter = a.Count <= b.Count ? a : b;
        var other = ReferenceEquals(iter, a) ? b : a;
        foreach (var c in iter)
        {
            if (!other.Contains(c))
                continue;
            if (allowedCells != null && allowedCells.Contains(c))
                continue;
            return true;
        }
        return false;
    }

    private int CountOverlapAll(HashSet<Vector2Int> a, HashSet<Vector2Int> b, HashSet<Vector2Int> allowedCells, out Vector2Int lastOverlapCell)
    {
        lastOverlapCell = default;
        if (a == null || b == null || a.Count == 0 || b.Count == 0)
            return 0;

        var count = 0;
        var iter = a.Count <= b.Count ? a : b;
        var other = ReferenceEquals(iter, a) ? b : a;
        foreach (var c in iter)
        {
            if (!other.Contains(c))
                continue;
            if (allowedCells != null && allowedCells.Contains(c))
                continue;
            count++;
            lastOverlapCell = c;
        }
        return count;
    }

    private Vector2 CenterOf(RoomPlacement p)
    {
        if (p?.Shape?.FloorCells == null || p.Shape.FloorCells.Count == 0)
            return p?.Root ?? Vector2.zero;
        var sum = Vector2.zero;
        foreach (var c in p.Shape.FloorCells)
            sum += (Vector2)(c + p.Root);
        return sum / p.Shape.FloorCells.Count;
    }

    private List<GameObject> GetRoomPrefabs(MapGraphAsset.NodeData node)
    {
        if (node == null)
            return new List<GameObject>();
        if (roomPrefabLookup.TryGetValue(node.id, out var list))
            return new List<GameObject>(list);
        return new List<GameObject>();
    }

    private Dictionary<string, List<GameObject>> BuildRoomPrefabLookup(MapGraphAsset graph)
    {
        var lookup = new Dictionary<string, List<GameObject>>();
        foreach (var node in graph.Nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.id))
                continue;
            var list = new List<GameObject>();
            var type = node.roomType != null ? node.roomType : graph.DefaultRoomType;
            if (type?.prefabs != null)
            {
                foreach (var p in type.prefabs)
                {
                    if (p != null)
                        list.Add(p);
                }
            }
            lookup[node.id] = list;
        }
        return lookup;
    }

    private sealed class LayoutState
    {
        public Dictionary<string, RoomPlacement> Rooms { get; }
        public int ChainIndex { get; }

        public LayoutState(Dictionary<string, RoomPlacement> rooms, int chainIndex)
        {
            Rooms = rooms ?? new Dictionary<string, RoomPlacement>();
            ChainIndex = chainIndex;
        }
    }
}
