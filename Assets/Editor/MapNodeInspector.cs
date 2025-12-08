// Assets/Editor/MapNodeInspector.cs
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class MapNodeInspector : VisualElement
{
    private readonly Label titleLabel;
    private readonly TextField nameField;
    private readonly ObjectField roomTypeField;
    private readonly TextField notesField;
    private readonly Button deleteButton;
    private bool isUpdating;

    public event System.Action<string> OnLabelChanged;
    public event System.Action<RoomTypeAsset> OnRoomTypeChanged;
    public event System.Action<string> OnNotesChanged;
    public event System.Action OnDeleteRequested;

    public MapNodeInspector()
    {
        style.width = 280;
        style.paddingLeft = 8;
        style.paddingRight = 8;
        style.paddingTop = 8;
        style.paddingBottom = 8;
        style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
        style.flexDirection = FlexDirection.Column;

        titleLabel = new Label("Select node");
        titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        Add(titleLabel);

        nameField = new TextField("Name");
        nameField.RegisterValueChangedCallback(evt =>
        {
            if (isUpdating) return;
            OnLabelChanged?.Invoke(evt.newValue);
        });
        Add(nameField);

        roomTypeField = new ObjectField
        {
            objectType = typeof(RoomTypeAsset),
            allowSceneObjects = false
        };
        roomTypeField.label = string.Empty;
        roomTypeField.style.marginLeft = 0;
        roomTypeField.RegisterValueChangedCallback(evt =>
        {
            if (isUpdating) return;
            OnRoomTypeChanged?.Invoke(evt.newValue as RoomTypeAsset);
        });
        Add(CreateStackedField("Room Type", roomTypeField));

        notesField = new TextField("Notes") { multiline = true };
        notesField.RegisterValueChangedCallback(evt =>
        {
            if (isUpdating) return;
            OnNotesChanged?.Invoke(evt.newValue);
        });
        Add(notesField);

        deleteButton = new Button(() =>
        {
            if (isUpdating) return;
            OnDeleteRequested?.Invoke();
        })
        { text = "Delete Node" };
        Add(deleteButton);

        SetNode(null);
    }

    public void SetNode(MapGraphAsset.NodeData node)
    {
        isUpdating = true;
        if (node == null)
        {
            titleLabel.text = "Select node";
            nameField.SetValueWithoutNotify(string.Empty);
            roomTypeField.SetValueWithoutNotify(null);
            notesField.SetValueWithoutNotify(string.Empty);
            nameField.SetEnabled(false);
            roomTypeField.SetEnabled(false);
            notesField.SetEnabled(false);
            deleteButton.SetEnabled(false);
        }
        else
        {
            titleLabel.text = node.label ?? "Node";
            nameField.SetValueWithoutNotify(node.label);
            roomTypeField.SetValueWithoutNotify(node.roomType);
            notesField.SetValueWithoutNotify(node.notes);
            nameField.SetEnabled(true);
            roomTypeField.SetEnabled(true);
            notesField.SetEnabled(true);
            deleteButton.SetEnabled(true);
        }
        isUpdating = false;
    }

    private static VisualElement CreateStackedField(string label, VisualElement field)
    {
        var container = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Column
            }
        };
        var header = new Label(label)
        {
            style =
            {
                marginBottom = 2
            }
        };
        container.Add(header);
        container.Add(field);
        return container;
    }
}
