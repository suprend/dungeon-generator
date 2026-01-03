// Assets/Editor/PlanarityBenchmarkEditor.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlanarityBenchmark))]
public sealed class PlanarityBenchmarkEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Run Planarity Benchmark"))
            {
                var bench = (PlanarityBenchmark)target;
                bench.RunBenchmark();
            }
        }
    }
}
#endif

