using UnityEngine;
using UnityEngine.Tilemaps;

public enum DoorSide { North = 0, East = 1, South = 2, West = 3 }

// Маркер точки подключения на границе модуля (комнаты/перехода)
public class DoorSocket : MonoBehaviour
{
    public DoorSide Side;
    [Tooltip("Пока что ширина проёма не поддерживается; всегда 1 тайл.")]
    public int Width = 1;

    [Tooltip("Optional: id of a DoorSocketSpan this socket belongs to. Currently used for authoring/diagnostics; the solver does not enforce a 'one socket per span' constraint automatically.")]
    public string SpanId = "";

    [Min(1)]
    [Tooltip("Connector-only: how many tiles the room is allowed to bite/carve into the connector (1 = only the bite cell).")]
    public int BiteDepth = 1;

    private void OnValidate()
    {
        Width = 1;
        BiteDepth = Mathf.Max(1, BiteDepth);
        SpanId ??= "";
    }

    private void OnDrawGizmos()
    {
        if (!enabled)
            return;
        if (!IsConnectorSocket())
            return;
        DrawBiteDepthGizmos(selected: false);
    }

    private void OnDrawGizmosSelected()
    {
        if (!enabled)
            return;
        if (!IsConnectorSocket())
            return;
        DrawBiteDepthGizmos(selected: true);
    }

    private bool IsConnectorSocket()
    {
        return GetComponentInParent<ConnectorMeta>() != null;
    }

    private void DrawBiteDepthGizmos(bool selected)
    {
        var (stepX, stepY) = GetLocalCellSteps();
        var outwardLocal = Side switch
        {
            DoorSide.North => stepY,
            DoorSide.South => -stepY,
            DoorSide.East => stepX,
            DoorSide.West => -stepX,
            _ => stepY
        };
        if (outwardLocal == Vector3.zero)
            outwardLocal = Vector3.up;

        // "Bite" direction is inward into the connector (opposite of outward normal).
        var inwardWorld = transform.TransformVector(-outwardLocal);

        var alpha = selected ? 0.9f : 0.35f;
        Gizmos.color = new Color(1f, 0.45f, 0.1f, alpha);
        var depth = Mathf.Max(1, BiteDepth);
        var lastIndex = depth - 1;
        var lineLength = lastIndex > 0 ? lastIndex : 0.5f;
        Gizmos.DrawLine(transform.position, transform.position + inwardWorld * lineLength);

        Gizmos.color = new Color(0.2f, 0.8f, 1f, selected ? 0.95f : 0.4f);
        var size = selected ? 0.085f : 0.07f;
        for (int i = 0; i < depth; i++)
        {
            var p = transform.position + inwardWorld * i;
            Gizmos.DrawSphere(p, size);
        }
    }

    private (Vector3 stepX, Vector3 stepY) GetLocalCellSteps()
    {
        var tilemap = GetComponentInParent<Tilemap>();
        if (tilemap == null)
            tilemap = GetComponentInChildren<Tilemap>();

        if (tilemap == null)
            return (Vector3.right, Vector3.up);

        var worldDx = tilemap.GetCellCenterWorld(Vector3Int.right) - tilemap.GetCellCenterWorld(Vector3Int.zero);
        var worldDy = tilemap.GetCellCenterWorld(Vector3Int.up) - tilemap.GetCellCenterWorld(Vector3Int.zero);
        var localDx = transform.InverseTransformVector(worldDx);
        var localDy = transform.InverseTransformVector(worldDy);

        if (localDx == Vector3.zero) localDx = Vector3.right;
        if (localDy == Vector3.zero) localDy = Vector3.up;
        return (localDx, localDy);
    }
}
