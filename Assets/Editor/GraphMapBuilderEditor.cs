// Assets/Editor/GraphMapBuilderEditor.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

[CustomEditor(typeof(GraphMapBuilder))]
[CanEditMultipleObjects]
public sealed class GraphMapBuilderEditor : Editor
{
    private static bool foldGraph = true;
    private static bool foldTargets = true;
    private static bool foldRun = true;
    private static bool foldLayout = true;
    private static bool foldDiagnostics = true;

    private readonly struct LayoutPreset
    {
        public string Name { get; }
        public int MaxLayoutsPerChain { get; }
        public int TemperatureSteps { get; }
        public int InnerIterations { get; }
        public float Cooling { get; }
        public float ChangePrefabProbability { get; }
        public int MaxWiggleCandidates { get; }
        public int MaxFallbackCandidates { get; }

        public LayoutPreset(
            string name,
            int maxLayoutsPerChain,
            int temperatureSteps,
            int innerIterations,
            float cooling,
            float changePrefabProbability,
            int maxWiggleCandidates,
            int maxFallbackCandidates)
        {
            Name = name;
            MaxLayoutsPerChain = maxLayoutsPerChain;
            TemperatureSteps = temperatureSteps;
            InnerIterations = innerIterations;
            Cooling = cooling;
            ChangePrefabProbability = changePrefabProbability;
            MaxWiggleCandidates = maxWiggleCandidates;
            MaxFallbackCandidates = maxFallbackCandidates;
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawGraphSection();
        DrawTargetsSection();
        DrawRunSection();
        DrawLayoutSection();
        DrawDiagnosticsSection();

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Debug Actions", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Build (Use Current Settings)"))
                    RunBuildForSelection(clearOverride: null);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear Tilemaps Only"))
                    RunClearForSelection();
            }

            if (!EditorApplication.isPlaying)
                EditorGUILayout.HelpBox("Runs in Edit Mode (no Play needed). This will modify Tilemaps and may create/destroy prefab instances depending on your settings.", MessageType.Info);
        }
    }

    private void DrawGraphSection()
    {
        foldGraph = EditorGUILayout.Foldout(foldGraph, "Graph", true);
        if (!foldGraph) return;

        using (new EditorGUILayout.VerticalScope("box"))
        {
            DrawProp(nameof(GraphMapBuilder.graph), "Graph Asset", "Logical graph to generate from.");
        }
    }

    private void DrawTargetsSection()
    {
        foldTargets = EditorGUILayout.Foldout(foldTargets, "Target Tilemaps", true);
        if (!foldTargets) return;

        using (new EditorGUILayout.VerticalScope("box"))
        {
            DrawProp(nameof(GraphMapBuilder.targetGrid), "Grid", "Target Grid used for world↔cell conversion.");
            DrawProp(nameof(GraphMapBuilder.floorMap), "Floor Tilemap", "Tilemap to stamp floor tiles into.");
            DrawProp(nameof(GraphMapBuilder.wallMap), "Wall Tilemap", "Tilemap to stamp wall tiles into (optional but recommended).");
        }
    }

    private void DrawRunSection()
    {
        foldRun = EditorGUILayout.Foldout(foldRun, "Run", true);
        if (!foldRun) return;

        using (new EditorGUILayout.VerticalScope("box"))
        {
            DrawProp(nameof(GraphMapBuilder.runOnStart), "Run On Start", "Automatically run Build() on Awake().");
            DrawProp(nameof(GraphMapBuilder.clearOnRun), "Clear On Run", "Clear tilemaps before stamping.");
            DrawProp(nameof(GraphMapBuilder.destroyPlacedInstances), "Destroy Placed Instances", "After stamping, destroy instantiated prefabs (keeps only tiles).");

            EditorGUILayout.Space(4);
            DrawProp(nameof(GraphMapBuilder.placementTimeLimitSeconds), "Placement Time Limit (s)", "Time limit for placement/stamping stage. 0 = unlimited.");
            DrawProp(nameof(GraphMapBuilder.randomSeed), "Seed", "0 = random per attempt; non-zero = deterministic.");
            DrawProp(nameof(GraphMapBuilder.layoutAttempts), "Layout Attempts", "How many different seeds to try when Seed==0.");
            DrawProp(nameof(GraphMapBuilder.verboseLogs), "Verbose Logs", "Extra logs from solver and placement.");
        }
    }

    private void DrawLayoutSection()
    {
        foldLayout = EditorGUILayout.Foldout(foldLayout, "Layout (SA)", true);
        if (!foldLayout) return;

        using (new EditorGUILayout.VerticalScope("box"))
        {
            DrawLayoutPresets();

            DrawProp(nameof(GraphMapBuilder.maxLayoutsPerChain), "Max Layouts Per Chain", "How many alternative layouts to keep per chain expansion (higher = more backtracking, slower).");
            DrawProp(nameof(GraphMapBuilder.temperatureSteps), "Temperature Steps", "SA outer loop iterations (higher = slower, explores more).");
            DrawProp(nameof(GraphMapBuilder.innerIterations), "Inner Iterations", "SA inner loop iterations per temperature step.");
            DrawProp(nameof(GraphMapBuilder.cooling), "Cooling", "Temperature multiplier per step (lower = cools faster).");
            DrawProp(nameof(GraphMapBuilder.changePrefabProbability), "Change Prefab Probability", "Chance to change prefab during SA perturbation (higher explores more, lower is faster/more stable).");

            EditorGUILayout.Space(4);
            DrawProp(nameof(GraphMapBuilder.maxWiggleCandidates), "Max Wiggle Candidates", "Limit for CS-based move candidates (fast path).");
            DrawProp(nameof(GraphMapBuilder.maxFallbackCandidates), "Max Fallback Candidates", "Limit for non-CS candidates (slow fallback).");
        }
    }

    private void DrawLayoutPresets()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Applies only SA parameters (does not change Layout Attempts or debug flags).", EditorStyles.wordWrappedMiniLabel);

            var presets = new[]
            {
                new LayoutPreset(
                    name: "Fast",
                    maxLayoutsPerChain: 4,
                    temperatureSteps: 12,
                    innerIterations: 48,
                    cooling: 0.60f,
                    changePrefabProbability: 0.15f,
                    maxWiggleCandidates: 12,
                    maxFallbackCandidates: 96),
                new LayoutPreset(
                    name: "Balanced",
                    maxLayoutsPerChain: 8,
                    temperatureSteps: 24,
                    innerIterations: 128,
                    cooling: 0.65f,
                    changePrefabProbability: 0.35f,
                    maxWiggleCandidates: 16,
                    maxFallbackCandidates: 256),
                new LayoutPreset(
                    name: "Thorough",
                    maxLayoutsPerChain: 12,
                    temperatureSteps: 32,
                    innerIterations: 192,
                    cooling: 0.70f,
                    changePrefabProbability: 0.45f,
                    maxWiggleCandidates: 24,
                    maxFallbackCandidates: 384),
            };

            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < presets.Length; i++)
                {
                    var p = presets[i];
                    if (GUILayout.Button(p.Name))
                        ApplyLayoutPresetToSelection(p);
                }
            }
        }
    }

    private void ApplyLayoutPresetToSelection(LayoutPreset preset)
    {
        foreach (var t in targets)
        {
            if (t is not GraphMapBuilder builder || builder == null)
                continue;

            Undo.RecordObject(builder, $"Apply Layout Preset: {preset.Name}");
            builder.maxLayoutsPerChain = preset.MaxLayoutsPerChain;
            builder.temperatureSteps = preset.TemperatureSteps;
            builder.innerIterations = preset.InnerIterations;
            builder.cooling = preset.Cooling;
            builder.changePrefabProbability = preset.ChangePrefabProbability;
            builder.maxWiggleCandidates = preset.MaxWiggleCandidates;
            builder.maxFallbackCandidates = preset.MaxFallbackCandidates;
            MarkDirty(builder);
        }
    }

    private void DrawDiagnosticsSection()
    {
        foldDiagnostics = EditorGUILayout.Foldout(foldDiagnostics, "Diagnostics", true);
        if (!foldDiagnostics) return;

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Config Space (CS)", EditorStyles.boldLabel);
            DrawProp(nameof(GraphMapBuilder.logConfigSpaceSizeSummary), "Log CS Size Summary", "Logs config-space sizes for prefab pairs during warmup.");
            if (GetBool(nameof(GraphMapBuilder.logConfigSpaceSizeSummary)))
                DrawProp(nameof(GraphMapBuilder.maxConfigSpaceSizePairs), "Max CS Pairs", "How many largest pairs to include in the summary.");
            DrawProp(nameof(GraphMapBuilder.verboseConfigSpaceLogs), "Verbose CS Logs", "Logs individual CS accept/reject decisions (can be noisy).");
            if (GetBool(nameof(GraphMapBuilder.verboseConfigSpaceLogs)))
                DrawProp(nameof(GraphMapBuilder.maxConfigSpaceLogs), "Max CS Logs", "Hard cap for verbose CS logs.");

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Layout Generator", EditorStyles.boldLabel);
            DrawProp(nameof(GraphMapBuilder.logLayoutProfiling), "Log Layout Profiling", "Logs time breakdown for layout generation.");

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Failure Debug", EditorStyles.boldLabel);
            DrawProp(nameof(GraphMapBuilder.debugNoLayouts), "Debug No Layouts", "If a chain produces 0 layouts, logs diagnostics for the best SA state.");
            if (GetBool(nameof(GraphMapBuilder.debugNoLayouts)))
            {
                DrawProp(nameof(GraphMapBuilder.debugNoLayoutsTopPairs), "Top Overlap Pairs", "How many top overlap-contributor pairs to print.");
                DrawProp(nameof(GraphMapBuilder.debugNoLayoutsTopEdges), "Top Problem Edges", "How many problematic edges to print.");
            }
        }
    }

    private void DrawProp(string propertyName, string label, string tooltip)
    {
        var prop = serializedObject.FindProperty(propertyName);
        if (prop == null)
            return;
        EditorGUILayout.PropertyField(prop, new GUIContent(label, tooltip));
    }

    private bool GetBool(string propertyName)
    {
        var prop = serializedObject.FindProperty(propertyName);
        if (prop == null || prop.propertyType != SerializedPropertyType.Boolean)
            return false;
        // If multiple objects are selected with mixed values, treat as disabled to avoid confusing conditional UI.
        return !prop.hasMultipleDifferentValues && prop.boolValue;
    }

    private void RunBuildForSelection(bool? clearOverride)
    {
        foreach (var t in targets)
        {
            if (t is not GraphMapBuilder builder || builder == null)
                continue;

            var originalClear = builder.clearOnRun;
            if (clearOverride.HasValue)
                builder.clearOnRun = clearOverride.Value;

            try
            {
                var sw = Stopwatch.StartNew();
                builder.Build();
                sw.Stop();

                UnityEngine.Debug.Log($"[GraphMapBuilder] Build invoked ({(EditorApplication.isPlaying ? "Play" : "Edit")} Mode) in {sw.Elapsed.TotalMilliseconds:0.0}ms on '{builder.gameObject.name}'.");
            }
            finally
            {
                if (clearOverride.HasValue)
                    builder.clearOnRun = originalClear;
                MarkDirty(builder);
            }
        }
    }

    private void RunClearForSelection()
    {
        foreach (var t in targets)
        {
            if (t is not GraphMapBuilder builder || builder == null)
                continue;

            if (builder.targetGrid == null || builder.floorMap == null)
            {
                UnityEngine.Debug.LogWarning("[GraphMapBuilder] Target Grid and Floor Map are required to clear tilemaps.");
                continue;
            }

            var sw = Stopwatch.StartNew();
            var stamp = new TileStampService(builder.targetGrid, builder.floorMap, builder.wallMap);
            stamp.ClearMaps();
            sw.Stop();

            UnityEngine.Debug.Log($"[GraphMapBuilder] Cleared tilemaps in {sw.Elapsed.TotalMilliseconds:0.0}ms on '{builder.gameObject.name}'.");
            MarkDirty(builder);
        }
    }

    private static void MarkDirty(GraphMapBuilder builder)
    {
        if (builder == null)
            return;

        if (!EditorApplication.isPlaying)
        {
            EditorUtility.SetDirty(builder);
            if (builder.floorMap != null) EditorUtility.SetDirty(builder.floorMap);
            if (builder.wallMap != null) EditorUtility.SetDirty(builder.wallMap);
            EditorSceneManager.MarkSceneDirty(builder.gameObject.scene);
        }
    }
}
#endif
