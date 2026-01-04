// Assets/scripts/Generation/Graph/MapGraphLayoutGenerator.SA.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private readonly struct PairPenaltyChange
    {
        public (string a, string b) Key { get; }
        public bool HadOld { get; }
        public float OldValue { get; }

        public PairPenaltyChange((string a, string b) key, bool hadOld, float oldValue)
        {
            Key = key;
            HadOld = hadOld;
            OldValue = oldValue;
        }
    }

    private readonly struct EdgePenaltyChange
    {
        public (string a, string b) Key { get; }
        public bool HadOld { get; }
        public float OldValue { get; }

        public EdgePenaltyChange((string a, string b) key, bool hadOld, float oldValue)
        {
            Key = key;
            HadOld = hadOld;
            OldValue = oldValue;
        }
    }

    private readonly struct MoveUndo
    {
        public string NodeId { get; }
        public RoomPlacement OldPlacement { get; }
        public float OldOverlapSum { get; }
        public float OldDistanceSum { get; }

        public MoveUndo(string nodeId, RoomPlacement oldPlacement, float oldOverlapSum, float oldDistanceSum)
        {
            NodeId = nodeId;
            OldPlacement = oldPlacement;
            OldOverlapSum = oldOverlapSum;
            OldDistanceSum = oldDistanceSum;
        }
    }

    private List<LayoutState> AddChain(LayoutState baseState, MapGraphChainBuilder.Chain chain, int maxLayouts)
    {
        if (profiling != null)
            profiling.AddChainCalls++;

        var generated = new List<LayoutState>();
        if (chain == null || chain.Nodes == null || chain.Nodes.Count == 0)
        {
            lastFailureDetail = "Chain is null/empty.";
            return generated;
        }

        if (chain.Nodes.Count <= 2)
            return AddChainSmall(baseState, chain, maxLayouts);

        var initStart = profiling != null ? NowTicks() : 0;
        var current = GetInitialLayout(baseState, chain);
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
        var energyCache = current.EnergyCache ?? BuildEnergyCache(rooms);
        float temperature = EstimateInitialTemperature(chain, rooms);

        var bestEnergy = energyCache.TotalEnergy;
        var bestRoomsSnapshot = settings.DebugNoLayouts ? new Dictionary<string, RoomPlacement>(rooms) : null;

        var chainNodeIds = chain.Nodes
            .Where(n => n != null && !string.IsNullOrEmpty(n.id))
            .Select(n => n.id)
            .ToList();
        var movableNodeIds = chainNodeIds.Where(id => rooms.ContainsKey(id)).ToList();

        var pairChanges = new List<PairPenaltyChange>(Mathf.Max(4, rooms.Count));
        var edgeChanges = new List<EdgePenaltyChange>(Mathf.Max(4, rooms.Count));
        var touchedEdgeKeys = new HashSet<(string a, string b)>();
        var positionCandidates = new List<Vector2Int>(Mathf.Max(16, settings.MaxFallbackCandidates));
        var wiggleCandidates = new List<Vector2Int>(Mathf.Max(16, settings.MaxWiggleCandidates));

        var saStart = profiling != null ? NowTicks() : 0;
        for (int t = 0; t < Mathf.Max(1, settings.TemperatureSteps); t++)
        {
            for (int i = 0; i < Mathf.Max(1, settings.InnerIterations); i++)
            {
                var currentEnergy = energyCache.TotalEnergy;
                pairChanges.Clear();
                edgeChanges.Clear();
                touchedEdgeKeys.Clear();
                if (!TryPerturbInPlace(rooms, energyCache, movableNodeIds, positionCandidates, wiggleCandidates, pairChanges, edgeChanges, touchedEdgeKeys, out var undo))
                    continue;

                var perturbedEnergy = energyCache.TotalEnergy;
                var delta = perturbedEnergy - currentEnergy;

                if (ShouldValidateForOutput(energyCache) && DifferentEnough(rooms, generated, chain) && IsValidLayout(rooms))
                {
                    var snapshotRooms = new Dictionary<string, RoomPlacement>(rooms);
                    var snapshotCache = BuildEnergyCache(snapshotRooms);
                    var snapshot = new LayoutState(snapshotRooms, baseState.ChainIndex, snapshotCache);
                    generated.Add(snapshot);
                    if (profiling != null)
                        profiling.CandidateLayoutsAccepted++;
                    if (generated.Count >= maxLayouts)
                    {
                        if (profiling != null)
                            profiling.SaLoopTicks += NowTicks() - saStart;
                        return generated;
                    }
                }

                if (delta < 0f)
                    continue;

                var p = Mathf.Exp(-delta / Mathf.Max(0.0001f, temperature));
                if (rng.NextDouble() >= p)
                    UndoMove(rooms, energyCache, undo, pairChanges, edgeChanges);

                if (settings.DebugNoLayouts)
                {
                    var eNow = energyCache.TotalEnergy;
                    if (eNow < bestEnergy)
                    {
                        bestEnergy = eNow;
                        bestRoomsSnapshot = new Dictionary<string, RoomPlacement>(rooms);
                    }
                }
            }

            temperature *= Mathf.Clamp(settings.Cooling, 0.01f, 0.999f);
        }
        if (profiling != null)
            profiling.SaLoopTicks += NowTicks() - saStart;

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

    private void DebugNoLayoutsDump(MapGraphChainBuilder.Chain chain, Dictionary<string, RoomPlacement> bestRooms)
    {
        if (bestRooms == null)
            return;

        var cache = BuildEnergyCache(bestRooms);
        var chainIds = chain?.Nodes != null ? string.Join(",", chain.Nodes.Select(n => n?.id)) : "<null>";

        var ok = TryValidateLayout(bestRooms, out var validateError);
        Debug.LogWarning(
            $"[LayoutGenerator][no-layout] chain=[{chainIds}] bestEnergy={cache.TotalEnergy:0.0} overlapSum={cache.OverlapPenaltySum:0.0} distSum={cache.DistancePenaltySum:0.0} " +
            $"validate={(ok ? "OK" : validateError)}");

        // Top overlap contributors.
        var topPairs = cache.PairPenalty
            .Where(kv => kv.Value > 0.0001f)
            .OrderByDescending(kv => kv.Value)
            .Take(Mathf.Clamp(settings.DebugNoLayoutsTopPairs, 0, 32))
            .ToList();

        for (int i = 0; i < topPairs.Count; i++)
        {
            var kv = topPairs[i];
            var aId = kv.Key.a;
            var bId = kv.Key.b;
            if (!bestRooms.TryGetValue(aId, out var a) || !bestRooms.TryGetValue(bId, out var b))
                continue;
            var delta = b.Root - a.Root;
            var detail = TryGetFirstIllegalOverlapDetail(a, b, out var worldCell, out var kind)
                ? $" firstIllegal={kind}@{worldCell}"
                : string.Empty;
            Debug.LogWarning($"[LayoutGenerator][no-layout] overlap#{i + 1} {aId}<->{bId} penalty={kv.Value:0.0} delta={delta}{detail}");
        }

        // Edges that are not currently satisfied.
        if (graphAsset != null)
        {
            var badEdges = new List<string>();
            foreach (var edge in graphAsset.Edges)
            {
                if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                    continue;
                if (!bestRooms.TryGetValue(edge.fromNodeId, out var a) || !bestRooms.TryGetValue(edge.toNodeId, out var b))
                    continue;

                var touch = RoomsTouchEitherWay(a, b);
                if (touch)
                {
                    var conn = IsConnector(a.Prefab) ? a : b;
                    var room = ReferenceEquals(conn, a) ? b : a;
                    if (!TryFindBiteDepth(conn, room, out _, out _, out _, out _))
                        badEdges.Add($"{edge.fromNodeId}->{edge.toNodeId}: touch=YES bite=NO");
                    continue;
                }

                var delta = b.Root - a.Root;
                var csOk = configSpaceLibrary != null &&
                           configSpaceLibrary.TryGetSpace(a.Prefab, b.Prefab, out var space, out _) &&
                           space != null && space.Contains(delta);
                badEdges.Add($"{edge.fromNodeId}->{edge.toNodeId}: touch=NO cs={(csOk ? "YES" : "NO")} delta={delta}");
                if (badEdges.Count >= Mathf.Clamp(settings.DebugNoLayoutsTopEdges, 0, 64))
                    break;
            }

            for (int i = 0; i < badEdges.Count; i++)
                Debug.LogWarning($"[LayoutGenerator][no-layout] edge#{i + 1} {badEdges[i]}");

            // Extra: log socket/span counts for any high-degree non-connector nodes (useful for star graphs).
            var degrees = new Dictionary<string, int>();
            foreach (var e in graphAsset.Edges)
            {
                if (e == null || string.IsNullOrEmpty(e.fromNodeId) || string.IsNullOrEmpty(e.toNodeId))
                    continue;
                degrees.TryGetValue(e.fromNodeId, out var da);
                degrees[e.fromNodeId] = da + 1;
                degrees.TryGetValue(e.toNodeId, out var db);
                degrees[e.toNodeId] = db + 1;
            }

            foreach (var kv in degrees.OrderByDescending(kv => kv.Value))
            {
                if (kv.Value < 3)
                    break;
                if (!bestRooms.TryGetValue(kv.Key, out var p) || p?.Prefab == null)
                    continue;
                if (IsConnector(p.Prefab))
                    continue;

                var sockets = p.Prefab.GetComponentsInChildren<DoorSocket>(true) ?? Array.Empty<DoorSocket>();
                if (sockets.Length == 0)
                {
                    Debug.LogWarning($"[LayoutGenerator][no-layout] highDegree node={kv.Key} degree={kv.Value} sockets=0 prefab={p.Prefab.name}");
                    continue;
                }

                var bySide = sockets
                    .Where(s => s != null)
                    .GroupBy(s => s.Side)
                    .Select(g => $"{g.Key}:{g.Count()}")
                    .ToList();
                var uniqueSpans = sockets
                    .Where(s => s != null && !string.IsNullOrEmpty(s.SpanId))
                    .Select(s => s.SpanId)
                    .Distinct()
                    .Count();
                Debug.LogWarning(
                    $"[LayoutGenerator][no-layout] highDegree node={kv.Key} degree={kv.Value} prefab={p.Prefab.name} " +
                    $"sockets={sockets.Length} sides=[{string.Join(",", bySide)}] uniqueSpanIds={uniqueSpans}");
            }
        }
    }

    private bool TryGetFirstIllegalOverlapDetail(RoomPlacement a, RoomPlacement b, out Vector2Int worldCell, out string kind)
    {
        worldCell = default;
        kind = null;
        if (a?.Shape == null || b?.Shape == null)
            return false;

        var aFloor = a.Shape.FloorCells;
        var bFloor = b.Shape.FloorCells;
        var aWall = a.Shape.WallCells;
        var bWall = b.Shape.WallCells;
        if (aFloor == null || bFloor == null || aWall == null || bWall == null)
            return false;

        var deltaBA = b.Root - a.Root;
        TryGetBiteAllowance(a, b, out var allowedFloor, out var allowedWallA, out var allowedWallB);

        // floor-floor
        var count = CountOverlapShifted(aFloor, bFloor, deltaBA, allowedFloor, a.Root, out var cell, earlyStopAtTwo: true);
        if (count > 0)
        {
            worldCell = cell;
            kind = "floor-floor";
            return true;
        }

        // aWall on bFloor
        count = CountOverlapShifted(aWall, bFloor, deltaBA, allowedWallA, a.Root, out cell, earlyStopAtTwo: true);
        if (count > 0)
        {
            worldCell = cell;
            kind = $"wall({a.NodeId})-floor({b.NodeId})";
            return true;
        }

        // bWall on aFloor
        var deltaAB = a.Root - b.Root;
        count = CountOverlapShifted(bWall, aFloor, deltaAB, allowedWallB, b.Root, out cell, earlyStopAtTwo: true);
        if (count > 0)
        {
            worldCell = cell;
            kind = $"wall({b.NodeId})-floor({a.NodeId})";
            return true;
        }

        return false;
    }

    private List<LayoutState> AddChainSmall(LayoutState baseState, MapGraphChainBuilder.Chain chain, int maxLayouts)
    {
        if (profiling != null)
            profiling.AddChainSmallCalls++;

        var generated = new List<LayoutState>();
        if (chain == null || chain.Nodes == null || chain.Nodes.Count == 0)
            return generated;

        // Singleton nodes have no constraints and only add combinatorial explosion; return a single best placement.
        if (chain.Edges == null || chain.Edges.Count == 0)
        {
            var rooms = new Dictionary<string, RoomPlacement>(baseState.Rooms);
            var cache = baseState.EnergyCache ?? BuildEnergyCache(rooms);

            var node = chain.Nodes.FirstOrDefault(n => n != null && !string.IsNullOrEmpty(n.id));
            if (node == null)
                return generated;

            if (!rooms.ContainsKey(node.id))
            {
                var prefabs = GetRoomPrefabs(node);
                prefabs.Shuffle(rng);
                var singlePositionCandidates = new List<Vector2Int>(Mathf.Max(16, settings.MaxFallbackCandidates));

                RoomPlacement bestPlacement = null;
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
                        rooms[node.id] = new RoomPlacement(node.id, prefab, shape, pos);
                        var e = ComputeEnergy(rooms);
                        if (e < bestEnergy)
                        {
                            bestEnergy = e;
                            bestPlacement = rooms[node.id];
                        }
                        rooms.Remove(node.id);
                    }
                }

                if (bestPlacement == null)
                    return generated;

                rooms[node.id] = bestPlacement;
                cache = BuildEnergyCache(rooms);
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
        var rooms = new Dictionary<string, RoomPlacement>(baseState.Rooms);
        var order = BuildChainBfsOrder(chain, rooms);
        var positionCandidates = new List<Vector2Int>(Mathf.Max(16, settings.MaxFallbackCandidates));
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
                FindPositionCandidates(node.id, prefab, shape, rooms, positionCandidates);
                for (var i = 0; i < positionCandidates.Count; i++)
                {
                    var pos = positionCandidates[i];
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

    private bool TryPerturbInPlace(
        Dictionary<string, RoomPlacement> rooms,
        EnergyCache energyCache,
        List<string> movableNodeIds,
        List<Vector2Int> positionCandidates,
        List<Vector2Int> wiggleCandidates,
        List<PairPenaltyChange> pairChanges,
        List<EdgePenaltyChange> edgeChanges,
        HashSet<(string a, string b)> touchedEdgeKeys,
        out MoveUndo undo)
    {
        undo = default;
        if (rooms == null || rooms.Count == 0 || energyCache == null)
            return false;
        if (movableNodeIds == null || movableNodeIds.Count == 0)
            return false;

        var targetId = movableNodeIds[rng.Next(movableNodeIds.Count)];
        if (!rooms.TryGetValue(targetId, out var targetPlacement) || targetPlacement == null)
            return false;
        var pChange = Mathf.Clamp01(settings.ChangePrefabProbability);
        var changeShape = rng.NextDouble() < pChange;

        var prefabs = GetRoomPrefabs(graphAsset.GetNodeById(targetId));
        if (prefabs.Count == 0)
            return false;

        GameObject newPrefab = targetPlacement.Prefab;
        ModuleShape newShape = targetPlacement.Shape;
        if (changeShape && prefabs.Count > 1)
        {
            newPrefab = prefabs[rng.Next(prefabs.Count)];
            if (!shapeLibrary.TryGetShape(newPrefab, out newShape, out _))
                return false;
        }

        if (rng.NextDouble() < 0.5)
        {
            FindPositionCandidates(targetId, newPrefab, newShape, rooms, positionCandidates, allowExistingRoot: !changeShape);
        }
        else
        {
            WiggleCandidates(targetId, newPrefab, newShape, rooms, wiggleCandidates);
            if (wiggleCandidates.Count == 0)
                FindPositionCandidates(targetId, newPrefab, newShape, rooms, positionCandidates, allowExistingRoot: !changeShape);
        }

        var candidates = wiggleCandidates.Count > 0 ? wiggleCandidates : positionCandidates;
        if (candidates.Count == 0)
            return false;

        var newRoot = candidates[rng.Next(candidates.Count)];
        var oldPlacement = targetPlacement;
        undo = new MoveUndo(targetId, oldPlacement, energyCache.OverlapPenaltySum, energyCache.DistancePenaltySum);

        rooms[targetId] = new RoomPlacement(targetId, newPrefab, newShape, newRoot);
        UpdateEnergyCacheInPlace(rooms, energyCache, targetId, pairChanges, edgeChanges, touchedEdgeKeys);

        return true;
    }

    private void WiggleCandidates(string nodeId, GameObject prefab, ModuleShape shape, Dictionary<string, RoomPlacement> placed, List<Vector2Int> result)
    {
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

        // If we have 2+ already-placed neighbors, prefer candidate roots that satisfy config-space constraints for two edges at once:
        // P = A.Root + deltaA, where deltaA ∈ CS(A->U) and (P - B.Root) ∈ CS(B->U).
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

                // Iterate the smaller offset set for speed.
                if ((a.space?.Offsets?.Count ?? 0) > (b.space?.Offsets?.Count ?? 0))
                {
                    var tmp = a;
                    a = b;
                    b = tmp;
                }

                var seen = 0;
                foreach (var off in a.space.Offsets)
                {
                    var pos = a.placement.Root + off;
                    if (!b.space.Contains(pos - b.placement.Root))
                        continue;

                    // Reservoir-sample candidates without shuffling large lists.
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

                // If intersection is non-empty, prefer it.
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

        // Fallback: pick a random already-placed neighbor and sample from its configuration-space.
        var idx = rng.Next(neighborRootsScratch.Count);
        var baseNeighbor = neighborRootsScratch[idx];
        var seenBase = 0;
        foreach (var off in baseNeighbor.space.Offsets)
        {
            var pos = baseNeighbor.placement.Root + off;
            seenBase++;
            if (result != null)
            {
                if (result.Count < limit)
                    result.Add(pos);
                else
                {
                    var j = rng.Next(seenBase);
                    if (j < limit)
                        result[j] = pos;
                }
            }
        }

        if (profiling != null)
        {
            profiling.WiggleCandidatesCalls++;
            profiling.WiggleCandidatesTicks += NowTicks() - start;
        }
    }

    private bool ShouldValidateForOutput(EnergyCache cache)
    {
        if (cache == null)
            return false;

        // Only pay the expensive O(n^2) validation cost when the cached energy indicates a full solution:
        // - zero illegal overlaps
        // - all currently-placed edges are touching (distance penalty == 0)
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

    private float EstimateInitialTemperature(MapGraphChainBuilder.Chain chain, Dictionary<string, RoomPlacement> rooms)
    {
        float avgArea = 1f;
        var sizes = rooms.Values.Select(r => r.Shape?.SolidCells?.Count ?? 1).ToList();
        if (sizes.Count > 0)
            avgArea = (float)sizes.Average();
        return Mathf.Max(1f, avgArea * 0.25f);
    }

    private void UpdateEnergyCacheInPlace(
        Dictionary<string, RoomPlacement> rooms,
        EnergyCache energyCache,
        string changedId,
        List<PairPenaltyChange> pairChanges,
        List<EdgePenaltyChange> edgeChanges,
        HashSet<(string a, string b)> touchedEdgeKeys)
    {
        if (rooms == null || energyCache == null || string.IsNullOrEmpty(changedId))
            return;
        if (!rooms.TryGetValue(changedId, out var changedAfter) || changedAfter == null)
            return;

        var overlapSum = energyCache.OverlapPenaltySum;
        var distSum = energyCache.DistancePenaltySum;

        foreach (var otherId in rooms.Keys)
        {
            if (otherId == changedId)
                continue;
            if (!rooms.TryGetValue(otherId, out var otherPlacement) || otherPlacement == null)
                continue;

            var key = PairKey(changedId, otherId);
            var hadOld = energyCache.PairPenalty.TryGetValue(key, out var oldP);
            if (hadOld) overlapSum -= oldP;
            pairChanges.Add(new PairPenaltyChange(key, hadOld, oldP));

            var newP = IntersectionPenalty(changedAfter, otherPlacement);
            energyCache.PairPenalty[key] = newP;
            overlapSum += newP;
        }

        if (graphAsset != null)
        {
            foreach (var edge in graphAsset.GetEdgesFor(changedId))
            {
                if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                    continue;

                var aId = edge.fromNodeId;
                var bId = edge.toNodeId;
                var key = PairKey(aId, bId);
                if (!touchedEdgeKeys.Add(key))
                    continue;

                if (!rooms.TryGetValue(aId, out var a) || !rooms.TryGetValue(bId, out var b))
                    continue;

                var hadOld = energyCache.EdgePenalty.TryGetValue(key, out var oldD);
                if (hadOld) distSum -= oldD;
                edgeChanges.Add(new EdgePenaltyChange(key, hadOld, oldD));

                var newD = ComputeEdgeDistancePenalty(a, b);
                energyCache.EdgePenalty[key] = newD;
                distSum += newD;
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
        if (rooms == null || energyCache == null || string.IsNullOrEmpty(undo.NodeId))
            return;

        rooms[undo.NodeId] = undo.OldPlacement;
        energyCache.OverlapPenaltySum = undo.OldOverlapSum;
        energyCache.DistancePenaltySum = undo.OldDistanceSum;

        if (pairChanges != null)
        {
            for (int i = 0; i < pairChanges.Count; i++)
            {
                var ch = pairChanges[i];
                if (ch.HadOld)
                    energyCache.PairPenalty[ch.Key] = ch.OldValue;
                else
                    energyCache.PairPenalty.Remove(ch.Key);
            }
        }

        if (edgeChanges != null)
        {
            for (int i = 0; i < edgeChanges.Count; i++)
            {
                var ch = edgeChanges[i];
                if (ch.HadOld)
                    energyCache.EdgePenalty[ch.Key] = ch.OldValue;
                else
                    energyCache.EdgePenalty.Remove(ch.Key);
            }
        }
    }
}
