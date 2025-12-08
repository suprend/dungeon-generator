// Assets/Editor/MapGraphAssetEditor.cs
using System;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(MapGraphAsset))]
public class MapGraphAssetEditor : Editor
{
    private SerializedProperty nodesProp;
    private SerializedProperty edgesProp;
    private SerializedProperty defaultRoomTypeProp;
    private SerializedProperty defaultConnectionTypeProp;
    private ReorderableList nodesList;
    private ReorderableList edgesList;

    private void OnEnable()
    {
        nodesProp = serializedObject.FindProperty("nodes");
        edgesProp = serializedObject.FindProperty("edges");
        defaultRoomTypeProp = serializedObject.FindProperty("defaultRoomType");
        defaultConnectionTypeProp = serializedObject.FindProperty("defaultConnectionType");

        nodesList = new ReorderableList(serializedObject, nodesProp, true, true, true, true);
        nodesList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Nodes");
        nodesList.elementHeight = EditorGUIUtility.singleLineHeight * 4.5f + EditorGUIUtility.standardVerticalSpacing * 4;
        nodesList.drawElementCallback = DrawNodeElement;

        edgesList = new ReorderableList(serializedObject, edgesProp, true, true, true, true);
        edgesList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Edges");
        edgesList.elementHeight = EditorGUIUtility.singleLineHeight * 4 + EditorGUIUtility.standardVerticalSpacing * 4;
        edgesList.drawElementCallback = DrawEdgeElement;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(defaultRoomTypeProp, new GUIContent("Default Room Type"));
        EditorGUILayout.PropertyField(defaultConnectionTypeProp, new GUIContent("Default Connection Type"));
        EditorGUILayout.Space();
        nodesList.DoLayoutList();
        EditorGUILayout.Space();
        edgesList.DoLayoutList();
        serializedObject.ApplyModifiedProperties();
    }

    private void DrawNodeElement(Rect rect, int index, bool active, bool focused)
    {
        if (index < 0 || index >= nodesProp.arraySize)
            return;

        var element = nodesProp.GetArrayElementAtIndex(index);
        var labelProp = element.FindPropertyRelative("label");
        var roomTypeProp = element.FindPropertyRelative("roomType");
        var notesProp = element.FindPropertyRelative("notes");

        var lineHeight = EditorGUIUtility.singleLineHeight;
        var spacing = EditorGUIUtility.standardVerticalSpacing;

        rect.y += lineHeight + spacing + 2;
        EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, lineHeight), labelProp, new GUIContent("Label"));
        rect.y += lineHeight + spacing;
        EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, lineHeight), roomTypeProp);
        rect.y += lineHeight + spacing;
        EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, lineHeight), notesProp);
    }

    private void DrawEdgeElement(Rect rect, int index, bool active, bool focused)
    {
        if (index < 0 || index >= edgesProp.arraySize)
            return;

        var element = edgesProp.GetArrayElementAtIndex(index);
        var fromProp = element.FindPropertyRelative("fromNodeId");
        var toProp = element.FindPropertyRelative("toNodeId");
        var connectionProp = element.FindPropertyRelative("connectionType");

        var asset = (MapGraphAsset)target;
        var nodeOptions = asset.Nodes
            .Select((n, i) => FormatNodeLabel(n, i))
            .ToArray();
        var nodeIds = asset.Nodes.Select(n => n.id).ToArray();

        int PopupWithIds(Rect position, string label, SerializedProperty prop)
        {
            var index = Array.IndexOf(nodeIds, prop.stringValue);
            index = EditorGUI.Popup(position, label, index, nodeOptions);
            if (index >= 0 && index < nodeIds.Length)
                prop.stringValue = nodeIds[index];
            return index;
        }

        var lineHeight = EditorGUIUtility.singleLineHeight;
        var spacing = EditorGUIUtility.standardVerticalSpacing;

        PopupWithIds(new Rect(rect.x, rect.y, rect.width, lineHeight), "From Node", fromProp);
        rect.y += lineHeight + spacing;
        PopupWithIds(new Rect(rect.x, rect.y, rect.width, lineHeight), "To Node", toProp);
        rect.y += lineHeight + spacing;
        EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, lineHeight), connectionProp);
}

    private static string FormatNodeLabel(MapGraphAsset.NodeData node, int index)
    {
        var display = string.IsNullOrEmpty(node.label) ? $"Node {index + 1}" : node.label;
        if (!string.IsNullOrEmpty(node.id))
        {
            var suffix = node.id.Length <= 6 ? node.id : node.id.Substring(0, 6);
            display = $"{display} ({suffix})";
        }
        return display;
    }
}
