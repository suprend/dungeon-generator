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
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Debug Actions", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Build (Use Current Settings)"))
                    RunBuildForSelection(clearOverride: null);

                if (GUILayout.Button("Build (Force Clear)"))
                    RunBuildForSelection(clearOverride: true);
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

