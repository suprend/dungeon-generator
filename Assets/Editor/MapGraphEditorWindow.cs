// Assets/Editor/MapGraphEditorWindow.cs
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class MapGraphEditorWindow : EditorWindow
{
    private MapGraphView graphView;
    private Toolbar toolbar;
    private ObjectField graphField;
    private MapGraphAsset currentGraph;

    private MapNodeInspector nodeInspector;
    private MapGraphAsset.NodeData selectedNode;

    [MenuItem("Window/Generation/Map Graph Editor")]
    public static void ShowWindow()
    {
        var window = GetWindow<MapGraphEditorWindow>();
        window.titleContent = new GUIContent("Map Graph Editor");
    }

    private void OnEnable()
    {
        ConstructToolbar();
        ConstructBody();
        RefreshTitle();
        if (currentGraph != null)
            graphView.PopulateView(currentGraph);
    }

    private void OnDisable()
    {
        rootVisualElement.Clear();
    }

    private void ConstructToolbar()
    {
        toolbar = new Toolbar();
        graphField = new ObjectField("Graph Asset")
        {
            objectType = typeof(MapGraphAsset),
            allowSceneObjects = false
        };
        graphField.RegisterValueChangedCallback(evt =>
        {
            currentGraph = evt.newValue as MapGraphAsset;
            graphView.PopulateView(currentGraph);
            RefreshTitle();
            OnNodeSelected(null);
        });
        toolbar.Add(graphField);

        var addNodeButton = new Button(() =>
        {
            if (currentGraph == null) return;
            graphView.CreateNode(currentGraph, graphView.GetCenterPosition());
        })
        { text = "Add Node" };
        toolbar.Add(addNodeButton);

        var frameButton = new Button(() => graphView.FrameAll())
        { text = "Frame All" };
        toolbar.Add(frameButton);

        toolbar.Add(new Label("Shift+Drag from node to connect"));

        toolbar.style.flexShrink = 0f;
        rootVisualElement.Add(toolbar);
    }

    private void ConstructBody()
    {
        var body = new VisualElement { style = { flexGrow = 1f, flexDirection = FlexDirection.Row } };
        graphView = new MapGraphView();
        graphView.OnNodeSelected += OnNodeSelected;
        graphView.style.flexGrow = 1f;
        body.Add(graphView);

        nodeInspector = new MapNodeInspector();
        nodeInspector.OnLabelChanged += HandleLabelChanged;
        nodeInspector.OnRoomTypeChanged += HandleRoomTypeChanged;
        nodeInspector.OnNotesChanged += HandleNotesChanged;
        nodeInspector.OnDeleteRequested += HandleDeleteRequested;
        body.Add(nodeInspector);
        rootVisualElement.Add(body);

        OnNodeSelected(null);
    }

    private void RefreshTitle()
    {
        if (currentGraph)
            titleContent = new GUIContent($"Map Graph: {currentGraph.name}");
        else
            titleContent = new GUIContent("Map Graph Editor");
    }

    private void OnNodeSelected(MapGraphAsset.NodeData node)
    {
        selectedNode = node;
        nodeInspector?.SetNode(node);
    }

    private void HandleLabelChanged(string newLabel)
    {
        if (selectedNode == null || currentGraph == null) return;
        Undo.RecordObject(currentGraph, "Edit node label");
        selectedNode.label = newLabel;
        graphView.RefreshNode(selectedNode);
        EditorUtility.SetDirty(currentGraph);
    }

    private void HandleRoomTypeChanged(RoomTypeAsset roomType)
    {
        if (selectedNode == null || currentGraph == null) return;
        Undo.RecordObject(currentGraph, "Edit node room type");
        selectedNode.roomType = roomType;
        EditorUtility.SetDirty(currentGraph);
    }

    private void HandleNotesChanged(string notes)
    {
        if (selectedNode == null || currentGraph == null) return;
        Undo.RecordObject(currentGraph, "Edit node notes");
        selectedNode.notes = notes;
        EditorUtility.SetDirty(currentGraph);
    }

    private void HandleDeleteRequested()
    {
        if (selectedNode == null || currentGraph == null) return;
        Undo.RecordObject(currentGraph, "Delete node");
        currentGraph.RemoveNode(selectedNode);
        EditorUtility.SetDirty(currentGraph);
        graphView.PopulateView(currentGraph);
        OnNodeSelected(null);
    }
}
