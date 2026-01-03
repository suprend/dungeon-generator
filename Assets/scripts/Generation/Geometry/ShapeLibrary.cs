// Assets/scripts/Generation/Geometry/ShapeLibrary.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds and caches discrete shapes for module prefabs (rooms/connectors) using tilemaps and sockets.
/// </summary>
public sealed class ShapeLibrary
{
    private readonly TileStampService stamp;
    private readonly Dictionary<GameObject, ModuleShape> shapeCache = new();

    public ShapeLibrary(TileStampService stamp)
    {
        this.stamp = stamp;
    }

    /// <summary>
    /// Returns cached shape or builds a new one for the prefab.
    /// </summary>
    public bool TryGetShape(GameObject prefab, out ModuleShape shape, out string error)
    {
        shape = null;
        error = null;

        if (prefab == null)
        {
            error = "Prefab is null.";
            return false;
        }

        if (shapeCache.TryGetValue(prefab, out shape))
            return true;

        if (stamp == null)
        {
            error = "ShapeLibrary requires a valid TileStampService.";
            return false;
        }

        var inst = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        var meta = inst ? inst.GetComponent<ModuleMetaBase>() : null;
        if (meta == null)
        {
            if (inst) Object.Destroy(inst);
            error = $"Prefab {prefab.name} is missing ModuleMetaBase.";
            return false;
        }

        meta.ResetUsed();
        AlignToCell(inst.transform, Vector3Int.zero);

        var rootCell = stamp.CellFromWorld(meta.transform.position);
        var floorCells = new HashSet<Vector2Int>();
        var wallCells = new HashSet<Vector2Int>();
        foreach (var c in stamp.CollectModuleFloorCells(meta))
            floorCells.Add(new Vector2Int(c.x - rootCell.x, c.y - rootCell.y));
        foreach (var c in stamp.CollectModuleWallCells(meta))
            wallCells.Add(new Vector2Int(c.x - rootCell.x, c.y - rootCell.y));

        // Many prefabs paint "floor" tiles under walls for visuals. For layout/overlap purposes,
        // treat any cell that has a wall tile as non-floor, except the socket bite cell(s).
        floorCells.ExceptWith(wallCells);

        var sockets = new List<ShapeSocket>();
        if (meta.Sockets != null)
        {
            foreach (var sock in meta.Sockets)
            {
                if (sock == null) continue;
                var sockCell = stamp.CellFromWorld(sock.transform.position);
                var localCell = new Vector2Int(sockCell.x - rootCell.x, sockCell.y - rootCell.y);
                // Strict "1-tile bite" uses overlap on the socket cell.
                // Rooms often have a wall tile at the socket location (door is carved later),
                // so treat the socket cell as solid/floor for configuration space purposes.
                floorCells.Add(localCell);
                var contact = BuildContactStrip(localCell, sock.Side, sock.Width);
                sockets.Add(new ShapeSocket(sock.Side, sock.Width, localCell, contact));
            }
        }

        shape = new ModuleShape(floorCells, wallCells, sockets);
        shapeCache[prefab] = shape;
        Object.Destroy(inst);
        return true;
    }

    private void AlignToCell(Transform moduleRoot, Vector3Int targetCell)
    {
        if (moduleRoot == null || stamp == null)
            return;
        moduleRoot.position = stamp.WorldFromCell(targetCell);
    }

    private HashSet<Vector2Int> BuildContactStrip(Vector2Int socketCell, DoorSide side, int width)
    {
        var res = new HashSet<Vector2Int>();
        // Strict 1-tile bite: only the socket cell itself can overlap.
        res.Add(socketCell);
        return res;
    }
}
