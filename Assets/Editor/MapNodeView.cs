// Assets/Editor/MapNodeView.cs
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

public class MapNodeView : Node
{
    public static readonly Vector2 DefaultSize = new(200, 150);

    public MapGraphAsset.NodeData Data { get; }

    public System.Action<MapNodeView, string> OnRequestRename;
    public System.Action OnNodeDataChanged;
    public System.Action<MapNodeView> OnPositionChanged;
    public System.Action<MapNodeView> OnConnectionRequested;
    public System.Action OnSelectedCallback;

    private readonly Label titleLabel;

    public MapNodeView(MapGraphAsset.NodeData data)
    {
        Data = data;

        title = string.IsNullOrEmpty(data.label) ? "Room Node" : data.label;

        capabilities &= ~Capabilities.Collapsible;
        titleButtonContainer.Clear();

        titleContainer.style.justifyContent = Justify.Center;
        titleLabel = titleContainer.Q<Label>("title-label");
        if (titleLabel != null)
        {
            titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.text = title;
        }
        else
        {
            var fallback = new Label(title)
            {
                style =
                {
                    unityTextAlign = TextAnchor.MiddleCenter,
                    unityFontStyleAndWeight = FontStyle.Bold
                }
            };
            titleLabel = fallback;
            titleContainer.Add(fallback);
        }

        RegisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.TrickleDown);

        RefreshExpandedState();
    }

    private void OnMouseDown(MouseDownEvent evt)
    {
        if (evt.button == 0 && evt.shiftKey)
        {
            OnConnectionRequested?.Invoke(this);
            evt.StopPropagation();
        }
        else if (evt.button == 0)
        {
            OnSelectedCallback?.Invoke();
        }
    }

    public override void OnSelected()
    {
        base.OnSelected();
        OnSelectedCallback?.Invoke();
    }

    public void RefreshFields()
    {
        title = string.IsNullOrEmpty(Data.label) ? "Room Node" : Data.label;
        if (titleLabel != null)
            titleLabel.text = title;
    }

    public void SetPendingConnection(bool pending)
    {
        titleContainer.style.borderBottomColor = pending ? Color.yellow : Color.clear;
        titleContainer.style.borderBottomWidth = pending ? 2f : 0f;
    }

    public override void SetPosition(Rect newPos)
    {
        base.SetPosition(newPos);
        Data.position = newPos.position;
        OnNodeDataChanged?.Invoke();
        OnPositionChanged?.Invoke(this);
    }
}
