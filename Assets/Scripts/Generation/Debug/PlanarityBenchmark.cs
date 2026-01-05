// Assets/Scripts/Generation/Debug/PlanarityBenchmark.cs
using System;
using System.Collections.Generic;
using System.Text;
using GraphPlanarityTesting.Graphs.DataStructures;
using GraphPlanarityTesting.PlanarityTesting.BoyerMyrvold;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

[ExecuteAlways]
public sealed class PlanarityBenchmark : MonoBehaviour
{
    public enum Algorithm
    {
        BoyerMyrvold_IsPlanar,
        BoyerMyrvold_TryGetPlanarFaces,
        MapGraphFaceBuilder_TryBuildFaces,
    }

    public enum GraphBuildMode
    {
        OncePerBenchmark,
        RebuildPerRun,
    }

    [Header("Input")]
    [SerializeField] private MapGraphAsset graphAsset;

    [Header("Benchmark")]
    [SerializeField] private Algorithm algorithm = Algorithm.BoyerMyrvold_TryGetPlanarFaces;
    [SerializeField] private GraphBuildMode graphBuildMode = GraphBuildMode.OncePerBenchmark;
    [Min(0)][SerializeField] private int warmupRuns = 1;
    [Min(1)][SerializeField] private int measuredRuns = 5;
    [SerializeField] private bool forceGCCollect = true;
    [SerializeField] private bool logEachRun = false;
    [SerializeField] private bool prettyMultilineLog = true;
    [Min(0)][SerializeField] private int includeRunsInSummary = 0;

    [Header("Options")]
    [SerializeField] private bool ensureGraphIds = true;
    [SerializeField] private bool includeInvalidEdges = false;
    [SerializeField] private bool logFaceBuilderProfiling = false;

    [ContextMenu("Run Planarity Benchmark")]
    public void RunBenchmark()
    {
        if (graphAsset == null)
        {
            UnityEngine.Debug.LogWarning("[PlanarityBenchmark] No graphAsset assigned.");
            return;
        }

        if (measuredRuns <= 0)
        {
            UnityEngine.Debug.LogWarning("[PlanarityBenchmark] measuredRuns must be >= 1.");
            return;
        }

        if (ensureGraphIds)
            graphAsset.EnsureIds();

        var v = 0;
        var eAdded = 0;
        var eDup = 0;
        var eSkipped = 0;

        UndirectedAdjacencyListGraph<string> graphOnce = null;
        if (algorithm != Algorithm.MapGraphFaceBuilder_TryBuildFaces && graphBuildMode == GraphBuildMode.OncePerBenchmark)
            graphOnce = BuildGraph(graphAsset, includeInvalidEdges, out v, out eAdded, out eDup, out eSkipped);

        if (forceGCCollect)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        // Warmup (JIT + internal caches).
        for (int i = 0; i < warmupRuns; i++)
        {
            if (algorithm == Algorithm.MapGraphFaceBuilder_TryBuildFaces)
            {
                var prev = MapGraphFaceBuilder.LogProfiling;
                MapGraphFaceBuilder.SetDebug(logFaceBuilderProfiling);
                MapGraphFaceBuilder.TryBuildFaces(graphAsset, out _, out _);
                MapGraphFaceBuilder.SetDebug(prev);
            }
            else
            {
                var graph = graphBuildMode == GraphBuildMode.RebuildPerRun
                    ? BuildGraph(graphAsset, includeInvalidEdges, out _, out _, out _, out _)
                    : graphOnce;
                RunPlanarity(algorithm, graph, out _, out _);
            }
        }

        var timesMs = new List<double>(measuredRuns);
        bool lastPlanar = false;
        int lastFaces = 0;

        for (int i = 0; i < measuredRuns; i++)
        {
            if (forceGCCollect)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            var sw = Stopwatch.StartNew();

            if (algorithm == Algorithm.MapGraphFaceBuilder_TryBuildFaces)
            {
                var prev = MapGraphFaceBuilder.LogProfiling;
                MapGraphFaceBuilder.SetDebug(logFaceBuilderProfiling);
                var ok = MapGraphFaceBuilder.TryBuildFaces(graphAsset, out var faces, out _);
                MapGraphFaceBuilder.SetDebug(prev);
                lastPlanar = ok;
                lastFaces = faces?.Count ?? 0;
            }
            else
            {
                UndirectedAdjacencyListGraph<string> graph;
                if (graphBuildMode == GraphBuildMode.RebuildPerRun)
                {
                    graph = BuildGraph(graphAsset, includeInvalidEdges, out v, out eAdded, out eDup, out eSkipped);
                }
                else
                {
                    graph = graphOnce;
                }

                RunPlanarity(algorithm, graph, out lastPlanar, out lastFaces);
            }

            sw.Stop();
            var ms = sw.Elapsed.TotalMilliseconds;
            timesMs.Add(ms);

            if (logEachRun)
                UnityEngine.Debug.Log($"[PlanarityBenchmark] run {i + 1,2}/{measuredRuns,2}: {ms,7:0.0} ms | planar={lastPlanar} faces={lastFaces}");
        }

        var stats = ComputeStats(timesMs);

        var graphInfo = algorithm == Algorithm.MapGraphFaceBuilder_TryBuildFaces
            ? $"nodes={graphAsset.Nodes?.Count ?? 0} edges={graphAsset.Edges?.Count ?? 0}"
            : $"vertices={v} edgesAdded={eAdded} dupEdgesCaught={eDup} skippedEdges={eSkipped}";

        if (!prettyMultilineLog)
        {
            UnityEngine.Debug.Log(
                $"[PlanarityBenchmark] graph={graphAsset.name} algo={algorithm} build={graphBuildMode} {graphInfo} " +
                $"warmup={warmupRuns} runs={measuredRuns} gc={(forceGCCollect ? "on" : "off")} " +
                $"ms[min={stats.Min:0.0} p50={stats.P50:0.0} p90={stats.P90:0.0} avg={stats.Avg:0.0} max={stats.Max:0.0}] " +
                $"std={stats.StdDev:0.0} result=planar:{lastPlanar} faces:{lastFaces}");
            return;
        }

        var sb = new StringBuilder(512);
        sb.AppendLine("[PlanarityBenchmark]");
        sb.AppendLine($"  Graph      : {graphAsset.name} ({graphInfo})");
        sb.AppendLine($"  Algorithm  : {algorithm}");
        sb.AppendLine($"  Build mode : {graphBuildMode} ({(graphBuildMode == GraphBuildMode.RebuildPerRun ? "includes graph build time" : "algorithm only")})");
        sb.AppendLine($"  Runs       : warmup={warmupRuns} measured={measuredRuns} gc={(forceGCCollect ? "on" : "off")}");
        sb.AppendLine($"  Result     : planar={lastPlanar} faces={lastFaces}");
        sb.AppendLine($"  Time (ms)  : min={stats.Min:0.0}  p50={stats.P50:0.0}  p90={stats.P90:0.0}  avg={stats.Avg:0.0}  max={stats.Max:0.0}  std={stats.StdDev:0.0}");

        if (includeRunsInSummary > 0)
        {
            var count = Mathf.Min(includeRunsInSummary, timesMs.Count);
            sb.Append("  Samples   : ");
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(timesMs[i].ToString("0.0"));
            }
            if (count < timesMs.Count)
                sb.Append(", ...");
            sb.AppendLine();
        }

        UnityEngine.Debug.Log(sb.ToString());
    }

    private static void RunPlanarity(Algorithm algorithm, IGraph<string> graph, out bool planar, out int faces)
    {
        faces = 0;
        if (graph == null)
        {
            planar = false;
            return;
        }

        var planarity = new BoyerMyrvold<string>();
        switch (algorithm)
        {
            case Algorithm.BoyerMyrvold_IsPlanar:
                planar = planarity.IsPlanar(graph);
                return;
            case Algorithm.BoyerMyrvold_TryGetPlanarFaces:
                planar = planarity.TryGetPlanarFaces(graph, out var planarFaces);
                faces = planarFaces?.Faces?.Count ?? 0;
                return;
            default:
                planar = false;
                return;
        }
    }

    private static UndirectedAdjacencyListGraph<string> BuildGraph(
        MapGraphAsset asset,
        bool includeInvalidEdges,
        out int vertices,
        out int edgesAdded,
        out int duplicateEdgesCaught,
        out int skippedEdges)
    {
        vertices = 0;
        edgesAdded = 0;
        duplicateEdgesCaught = 0;
        skippedEdges = 0;

        var graph = new UndirectedAdjacencyListGraph<string>();
        var nodeIds = new HashSet<string>();

        if (asset?.Nodes != null)
        {
            foreach (var n in asset.Nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.id))
                    continue;
                if (!nodeIds.Add(n.id))
                    continue;
                graph.AddVertex(n.id);
                vertices++;
            }
        }

        if (asset?.Edges == null)
            return graph;

        foreach (var e in asset.Edges)
        {
            if (e == null || string.IsNullOrEmpty(e.fromNodeId) || string.IsNullOrEmpty(e.toNodeId))
            {
                if (!includeInvalidEdges) { skippedEdges++; continue; }
                continue;
            }

            if (!nodeIds.Contains(e.fromNodeId) || !nodeIds.Contains(e.toNodeId))
            {
                if (!includeInvalidEdges) { skippedEdges++; continue; }
                continue;
            }

            try
            {
                graph.AddEdge(e.fromNodeId, e.toNodeId);
                edgesAdded++;
            }
            catch (ArgumentException)
            {
                duplicateEdgesCaught++;
            }
        }

        return graph;
    }

    private readonly struct Stats
    {
        public double Min { get; }
        public double Max { get; }
        public double Avg { get; }
        public double P50 { get; }
        public double P90 { get; }
        public double StdDev { get; }

        public Stats(double min, double max, double avg, double p50, double p90, double stdDev)
        {
            Min = min;
            Max = max;
            Avg = avg;
            P50 = p50;
            P90 = p90;
            StdDev = stdDev;
        }
    }

    private static Stats ComputeStats(List<double> samples)
    {
        if (samples == null || samples.Count == 0)
            return new Stats(0, 0, 0, 0, 0, 0);

        var min = double.MaxValue;
        var max = double.MinValue;
        var sum = 0.0;
        foreach (var t in samples)
        {
            sum += t;
            if (t < min) min = t;
            if (t > max) max = t;
        }
        var avg = sum / samples.Count;

        var varianceSum = 0.0;
        foreach (var t in samples)
        {
            var d = t - avg;
            varianceSum += d * d;
        }
        var std = Math.Sqrt(varianceSum / samples.Count);

        var sorted = new List<double>(samples);
        sorted.Sort();
        var p50 = PercentileSorted(sorted, 0.50);
        var p90 = PercentileSorted(sorted, 0.90);

        return new Stats(min, max, avg, p50, p90, std);
    }

    private static double PercentileSorted(List<double> sortedAscending, double percentile01)
    {
        if (sortedAscending == null || sortedAscending.Count == 0)
            return 0;

        percentile01 = Math.Max(0, Math.Min(1, percentile01));
        var n = sortedAscending.Count;
        if (n == 1)
            return sortedAscending[0];

        var pos = (n - 1) * percentile01;
        var i = (int)pos;
        var frac = pos - i;
        if (i >= n - 1)
            return sortedAscending[n - 1];
        return sortedAscending[i] + (sortedAscending[i + 1] - sortedAscending[i]) * frac;
    }
}
