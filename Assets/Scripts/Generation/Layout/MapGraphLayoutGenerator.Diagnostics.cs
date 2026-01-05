// Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Diagnostics.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
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
        var topPairs = new List<(int a, int b, float p)>();
        var topLimit = Mathf.Clamp(settings.DebugNoLayoutsTopPairs, 0, 32);
        if (topLimit > 0 && cache.NodeCount > 1)
        {
            for (var a = 0; a < cache.NodeCount; a++)
            {
                if (!cache.IsPlaced[a])
                    continue;
                for (var b = a + 1; b < cache.NodeCount; b++)
                {
                    if (!cache.IsPlaced[b])
                        continue;
                    var idx = PairIndex(a, b, cache.NodeCount);
                    if (idx < 0)
                        continue;
                    var p = cache.PairPenalty[idx];
                    if (p <= 0.0001f)
                        continue;
                    topPairs.Add((a, b, p));
                }
            }
            topPairs.Sort((x, y) => y.p.CompareTo(x.p));
            if (topPairs.Count > topLimit)
                topPairs.RemoveRange(topLimit, topPairs.Count - topLimit);
        }

        for (int i = 0; i < topPairs.Count; i++)
        {
            var kv = topPairs[i];
            var aId = nodeIdByIndex != null && kv.a >= 0 && kv.a < nodeIdByIndex.Length ? nodeIdByIndex[kv.a] : kv.a.ToString();
            var bId = nodeIdByIndex != null && kv.b >= 0 && kv.b < nodeIdByIndex.Length ? nodeIdByIndex[kv.b] : kv.b.ToString();
            if (!bestRooms.TryGetValue(aId, out var a) || !bestRooms.TryGetValue(bId, out var b))
                continue;
            var delta = b.Root - a.Root;
            var detail = TryGetFirstIllegalOverlapDetail(a, b, out var worldCell, out var kind)
                ? $" firstIllegal={kind}@{worldCell}"
                : string.Empty;
            Debug.LogWarning($"[LayoutGenerator][no-layout] overlap#{i + 1} {aId}<->{bId} penalty={kv.p:0.0} delta={delta}{detail}");
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

        var count = CountOverlapShifted(aFloor, bFloor, deltaBA, allowedFloor, a.Root, out var cell, earlyStopAtTwo: true);
        if (count > 0)
        {
            worldCell = cell;
            kind = "floor-floor";
            return true;
        }

        count = CountOverlapShifted(aWall, bFloor, deltaBA, allowedWallA, a.Root, out cell, earlyStopAtTwo: true);
        if (count > 0)
        {
            worldCell = cell;
            kind = $"wall({a.NodeId})-floor({b.NodeId})";
            return true;
        }

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
}
