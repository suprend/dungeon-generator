// Assets/Editor/MapGraphView.cs
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

public class MapGraphView : GraphView
{
    private MapGraphAsset graphAsset;
    private readonly Dictionary<string, MapNodeView> nodeLookup = new();
    private MapNodeView pendingConnectionStart;
    private MapNodeView pendingConnectionTarget;
    private bool isDrawingConnection;
    private ConnectionPreview connectionPreview;
    private bool suppressGraphChanges;
    public event System.Action<MapGraphAsset.NodeData> OnNodeSelected;

    public MapGraphView()
    {
        Insert(0, new GridBackground());
        SetupZoom(0.1f, 1.5f);
        var zoomer = new SmoothZoomer();
        this.AddManipulator(zoomer);
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());

        graphViewChanged = OnGraphViewChanged;
        RegisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.TrickleDown);
        RegisterCallback<MouseUpEvent>(OnGraphMouseUp, TrickleDown.TrickleDown);
        RegisterCallback<MouseMoveEvent>(OnMouseMove);
        RegisterCallback<KeyDownEvent>(OnKeyDown);
    }

    internal MapGraphAsset GraphAsset => graphAsset;

    public void PopulateView(MapGraphAsset asset)
    {
        graphAsset = asset;
        graphAsset?.EnsureIds();
        pendingConnectionStart = null;
        nodeLookup.Clear();
        suppressGraphChanges = true;
        DeleteElements(graphElements.ToList());
        suppressGraphChanges = false;

        if (graphAsset == null)
            return;

        foreach (var node in graphAsset.Nodes)
            CreateNodeView(node);

        foreach (var edge in graphAsset.Edges)
            CreateEdge(edge);
    }

    public void CreateNode(MapGraphAsset asset, Vector2 position)
    {
        if (asset == null) return;
        Undo.RecordObject(asset, "Add graph node");
        var nodeData = asset.CreateNode();
        nodeData.position = position;
        var nodeView = CreateNodeView(nodeData);
        nodeView.SetPosition(new Rect(nodeData.position, MapNodeView.DefaultSize));
        EditorUtility.SetDirty(asset);
    }

    public Vector2 GetCenterPosition()
    {
        var rect = layout;
        if (rect.width <= 0f || rect.height <= 0f)
            return Vector2.zero;
        return contentViewContainer.WorldToLocal(rect.center);
    }

    private void OnMouseDown(MouseDownEvent evt)
    {
        if (graphAsset == null)
            return;

        var targetElement = evt.target as VisualElement;
        if (evt.button == 0 && evt.clickCount == 2)
        {
            if (targetElement is GraphElement && targetElement is not GridBackground)
                return;
            var localPos = GraphToContent(evt.mousePosition) - MapNodeView.DefaultSize * 0.5f;
            CreateNode(graphAsset, localPos);
            evt.StopPropagation();
        }
    }

    private MapNodeView CreateNodeView(MapGraphAsset.NodeData nodeData)
    {
        var nodeView = new MapNodeView(nodeData);
        nodeView.SetPosition(new Rect(nodeData.position, MapNodeView.DefaultSize));
        nodeView.OnNodeDataChanged += () =>
        {
            if (graphAsset != null)
                EditorUtility.SetDirty(graphAsset);
        };
        nodeView.OnRequestRename += (view, newName) =>
        {
            if (graphAsset == null) return;
            Undo.RecordObject(graphAsset, "Rename graph node");
            graphAsset.RenameNode(view.Data, newName);
            view.RefreshFields();
            EditorUtility.SetDirty(graphAsset);
        };
        nodeView.OnPositionChanged += _ => MarkDirtyRepaint();
        nodeView.OnConnectionRequested += BeginConnectionDrag;
        nodeView.OnSelectedCallback += () => OnNodeSelected?.Invoke(nodeData);

        AddElement(nodeView);
        if (!string.IsNullOrEmpty(nodeData.id))
            nodeLookup[nodeData.id] = nodeView;
        return nodeView;
    }

    public void RefreshNode(MapGraphAsset.NodeData nodeData)
    {
        if (nodeData == null || string.IsNullOrEmpty(nodeData.id)) return;
        if (nodeLookup.TryGetValue(nodeData.id, out var view))
        {
            view.RefreshFields();
        }
        else
        {
            nodeLookup[nodeData.id] = CreateNodeView(nodeData);
        }
    }

    private void CreateEdge(MapGraphAsset.EdgeData edgeData)
    {
        if (edgeData == null || string.IsNullOrEmpty(edgeData.fromNodeId) || string.IsNullOrEmpty(edgeData.toNodeId))
            return;
        if (!nodeLookup.TryGetValue(edgeData.fromNodeId, out var fromNode)) return;
        if (!nodeLookup.TryGetValue(edgeData.toNodeId, out var toNode)) return;

        var edge = new MapEdge(edgeData, fromNode, toNode, this);
        AddElement(edge);
        edge.StretchToParentSize();
        edge.SendToBack();
    }

    private void OnMouseMove(MouseMoveEvent evt)
    {
        if (!isDrawingConnection || connectionPreview == null)
            return;

        connectionPreview.SetEnd(GraphToContent(evt.mousePosition));

        var targetElement = evt.target as VisualElement;
        var node = targetElement?.GetFirstAncestorOfType<MapNodeView>();
        if (node != null && node != pendingConnectionStart)
        {
            if (pendingConnectionTarget != node)
            {
                pendingConnectionTarget?.SetPendingConnection(false);
                pendingConnectionTarget = node;
                pendingConnectionTarget.SetPendingConnection(true);
            }
        }
        else if (pendingConnectionTarget != null)
        {
            pendingConnectionTarget.SetPendingConnection(false);
            pendingConnectionTarget = null;
        }

        evt.StopPropagation();
    }

    private void OnGraphMouseUp(MouseUpEvent evt)
    {
        if (!isDrawingConnection)
            return;

        var ve = evt.target as VisualElement;
        var node = ve?.GetFirstAncestorOfType<MapNodeView>();
        if (node != null && pendingConnectionStart != null && node != pendingConnectionStart)
        {
            TryAddEdge(pendingConnectionStart, node);
        }

        EndConnectionDrag();
        evt.StopPropagation();
    }

    private Vector2 GraphToContent(Vector2 graphPosition)
    {
        var world = this.LocalToWorld(graphPosition);
        return contentViewContainer.WorldToLocal(world);
    }

    private GraphViewChange OnGraphViewChanged(GraphViewChange change)
    {
        if (graphAsset == null || suppressGraphChanges)
            return change;

        var requiresReload = false;
        if (change.elementsToRemove != null)
        {
            foreach (var element in change.elementsToRemove)
            {
                switch (element)
                {
                    case MapEdge edge:
                        Undo.RecordObject(graphAsset, "Remove graph edge");
                        graphAsset.RemoveEdge(edge.EdgeData);
                        EditorUtility.SetDirty(graphAsset);
                        break;
                    case MapNodeView nodeView:
                        Undo.RecordObject(graphAsset, "Remove graph node");
                        graphAsset.RemoveNode(nodeView.Data);
                        nodeLookup.Remove(nodeView.Data.id);
                        EditorUtility.SetDirty(graphAsset);
                        requiresReload = true;
                        break;
                }
            }
        }

        if (requiresReload)
            EditorApplication.delayCall += () =>
            {
                if (graphAsset != null)
                    PopulateView(graphAsset);
            };

        return change;
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (evt.target is TextField)
            return;

        if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
        {
            var selectionSnapshot = selection.OfType<GraphElement>().ToList();
            if (selectionSnapshot.Count > 0)
            {
                DeleteElements(selectionSnapshot);
                evt.StopPropagation();
            }
        }
    }

    public void TryAddEdge(MapNodeView from, MapNodeView to)
    {
        if (graphAsset == null || from == null || to == null) return;
        Undo.RecordObject(graphAsset, "Add graph edge");
        var result = graphAsset.AddEdge(from.Data.id, to.Data.id);
        if (result != null)
        {
            CreateEdge(result);
            EditorUtility.SetDirty(graphAsset);
        }
    }

    internal void RemoveEdge(MapEdge edge)
    {
        if (graphAsset == null || edge == null) return;
        Undo.RecordObject(graphAsset, "Remove graph edge");
        graphAsset.RemoveEdge(edge.EdgeData);
        RemoveElement(edge);
        EditorUtility.SetDirty(graphAsset);
    }

    internal void ShowEdgeSettings(MapEdge edge, Rect activatorRect)
    {
        if (graphAsset == null || edge == null) return;
        UnityEditor.PopupWindow.Show(activatorRect, new EdgeSettingsPopup(edge.EdgeData, graphAsset));
    }

    private void BeginConnectionDrag(MapNodeView node)
    {
        if (node == null || isDrawingConnection) return;
        pendingConnectionStart = node;
        node.SetPendingConnection(true);
        isDrawingConnection = true;
        connectionPreview ??= new ConnectionPreview(() => pendingConnectionStart?.GetPosition().center ?? Vector2.zero);
        if (connectionPreview.parent == null)
        {
            Add(connectionPreview);
            connectionPreview.StretchToParentSize();
            connectionPreview.SendToBack();
        }
        connectionPreview.Show();
        connectionPreview.SetEnd(node.GetPosition().center);
    }

    private void EndConnectionDrag()
    {
        isDrawingConnection = false;
        connectionPreview?.Hide();
        if (pendingConnectionTarget != null)
        {
            pendingConnectionTarget.SetPendingConnection(false);
            pendingConnectionTarget = null;
        }
        if (pendingConnectionStart != null)
        {
            pendingConnectionStart.SetPendingConnection(false);
            pendingConnectionStart = null;
        }
    }
}

public class SmoothZoomer : MouseManipulator
{
    private const float SmoothFactor = 1.0000001f;
    private readonly float minScale = 0.001f;
    private readonly float maxScale = 1.5f;

    public SmoothZoomer()
    {
        activators.Clear();
    }

    protected override void RegisterCallbacksOnTarget()
    {
        target.RegisterCallback<WheelEvent>(OnWheel);
    }

    protected override void UnregisterCallbacksFromTarget()
    {
        target.UnregisterCallback<WheelEvent>(OnWheel);
    }

    private void OnWheel(WheelEvent evt)
    {
        if (target is GraphView graphView)
        {
            var delta = evt.delta.y;
            var viewTransform = graphView.viewTransform;
            var scale = viewTransform.scale.x;
            var factor = Mathf.Pow(SmoothFactor, delta);
            var newScale = Mathf.Clamp(scale * factor, minScale, maxScale);
            var scaleRatio = newScale / scale;

            var pivot = evt.localMousePosition;
            var position = viewTransform.position + (Vector3)((1 - scaleRatio) * pivot);
            graphView.UpdateViewTransform(position, new Vector3(newScale, newScale, 1f));

            evt.StopPropagation();
        }
    }
}

public class ConnectionPreview : VisualElement
{
    private readonly System.Func<Vector2> startGetter;
    private Vector2 endPoint;

    public ConnectionPreview(System.Func<Vector2> startGetter)
    {
        this.startGetter = startGetter;
        pickingMode = PickingMode.Ignore;
        style.position = Position.Absolute;
        style.display = DisplayStyle.None;
        generateVisualContent += OnGenerateVisualContent;
    }

    public void SetEnd(Vector2 end)
    {
        endPoint = end;
        MarkDirtyRepaint();
    }

    public void Show()
    {
        style.display = DisplayStyle.Flex;
        MarkDirtyRepaint();
    }

    public void Hide()
    {
        style.display = DisplayStyle.None;
    }

    private void OnGenerateVisualContent(MeshGenerationContext context)
    {
        if (style.display == DisplayStyle.None)
            return;

        var painter = context.painter2D;
        painter.lineWidth = 2f;
        painter.strokeColor = new Color(1f, 1f, 1f, 0.5f);
        var start = startGetter != null ? startGetter() : Vector2.zero;
        painter.BeginPath();
        painter.MoveTo(start);
        painter.LineTo(endPoint);
        painter.Stroke();
    }
}
