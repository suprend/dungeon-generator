// Assets/Editor/MapEdge.cs
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

public class MapEdge : GraphElement
{
    private readonly MapNodeView fromNode;
    private readonly MapNodeView toNode;
    private readonly MapGraphView ownerView;
    private readonly Button deleteButton;
    private readonly Button settingsButton;

    public MapGraphAsset.EdgeData EdgeData { get; }

    private const float HitThreshold = 8f;

    public MapEdge(MapGraphAsset.EdgeData data, MapNodeView from, MapNodeView to, MapGraphView owner)
    {
        EdgeData = data;
        fromNode = from;
        toNode = to;
        ownerView = owner;
        capabilities |= Capabilities.Deletable | Capabilities.Selectable;
        pickingMode = PickingMode.Position;
        generateVisualContent += OnGenerateVisualContent;
        fromNode.OnPositionChanged += OnNodePositionChanged;
        toNode.OnPositionChanged += OnNodePositionChanged;
        RegisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);

        deleteButton = new Button(() => ownerView?.RemoveEdge(this))
        {
            text = "×",
            style =
            {
                position = Position.Absolute,
                width = 18,
                height = 18,
                unityTextAlign = TextAnchor.MiddleCenter
            }
        };
        deleteButton.tooltip = "Remove edge";
        Add(deleteButton);

        settingsButton = new Button(() =>
        {
            var world = settingsButton.worldBound;
            ownerView?.ShowEdgeSettings(this, world);
        })
        {
            text = "⚙",
            style =
            {
                position = Position.Absolute,
                width = 20,
                height = 20,
                unityTextAlign = TextAnchor.MiddleCenter
            }
        };
        settingsButton.tooltip = "Edit connection settings";
        Add(settingsButton);
    }

    private void OnNodePositionChanged(MapNodeView _)
    {
        MarkDirtyRepaint();
        UpdateButtonPositions(fromNode.GetPosition().center, toNode.GetPosition().center);
    }

    private void OnDetachedFromPanel(DetachFromPanelEvent evt)
    {
        if (fromNode != null)
            fromNode.OnPositionChanged -= OnNodePositionChanged;
        if (toNode != null)
            toNode.OnPositionChanged -= OnNodePositionChanged;
        UnregisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);
    }

    private void OnGenerateVisualContent(MeshGenerationContext context)
    {
        var painter = context.painter2D;
        painter.lineWidth = 2f;
        painter.strokeColor = Color.white;
        var start = fromNode.GetPosition().center;
        var end = toNode.GetPosition().center;
        painter.BeginPath();
        painter.MoveTo(start);
        painter.LineTo(end);
        painter.Stroke();
        UpdateButtonPositions(start, end);
    }

    public override bool ContainsPoint(Vector2 localPoint)
    {
        var start = fromNode.GetPosition().center;
        var end = toNode.GetPosition().center;
        return DistanceToSegment(localPoint, start, end) <= HitThreshold;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var t = Vector2.Dot(point - a, ab) / Vector2.Dot(ab, ab);
        t = Mathf.Clamp01(t);
        var projection = a + ab * t;
        return Vector2.Distance(point, projection);
    }

    private void UpdateButtonPositions(Vector2 start, Vector2 end)
    {
        var mid = (start + end) * 0.5f;
        if (deleteButton != null)
        {
            deleteButton.style.left = mid.x - deleteButton.resolvedStyle.width * 0.5f;
            deleteButton.style.top = mid.y - deleteButton.resolvedStyle.height - 4f;
        }
        if (settingsButton != null)
        {
            settingsButton.style.left = mid.x - settingsButton.resolvedStyle.width * 0.5f;
            settingsButton.style.top = mid.y + 4f;
        }
    }
}

internal class EdgeSettingsPopup : PopupWindowContent
{
    private readonly MapGraphAsset.EdgeData edgeData;
    private readonly MapGraphAsset graphAsset;

    public EdgeSettingsPopup(MapGraphAsset.EdgeData edgeData, MapGraphAsset graphAsset)
    {
        this.edgeData = edgeData;
        this.graphAsset = graphAsset;
    }

    public override Vector2 GetWindowSize() => new Vector2(240, 110);

    public override void OnGUI(Rect rect)
    {
        if (edgeData == null || graphAsset == null) return;

        EditorGUI.BeginChangeCheck();
        var newType = (ConnectionTypeAsset)EditorGUILayout.ObjectField(
            "Connection Type",
            edgeData.connectionType ?? graphAsset.DefaultConnectionType,
            typeof(ConnectionTypeAsset),
            false);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(graphAsset, "Edit edge settings");
            edgeData.connectionType = newType;
            EditorUtility.SetDirty(graphAsset);
        }
    }
}
