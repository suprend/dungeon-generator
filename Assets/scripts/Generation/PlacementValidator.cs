using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class PlacementValidator
{
    private readonly Tilemap floorMap;
    private readonly Tilemap wallMap;
    private readonly TileStampService stamp;

    public PlacementValidator(Tilemap floorMap, Tilemap wallMap, TileStampService stamp)
    {
        this.floorMap = floorMap;
        this.wallMap = wallMap;
        this.stamp = stamp;
    }

    public bool OverlapsExistingFloorExcept(ModuleMetaBase module, HashSet<Vector3Int> allowed)
    {
        var cells = stamp.CollectModuleFloorCells(module);
        foreach (var c in cells)
        {
            if (allowed != null && allowed.Contains(c)) continue;
            if (floorMap.HasTile(c)) return true;
        }
        return false;
    }

    public bool OverlapsExistingWallsExcept(ModuleMetaBase module, HashSet<Vector3Int> allowed)
    {
        if (wallMap == null) return false;
        var cells = stamp.CollectModuleWallCells(module);
        foreach (var c in cells)
        {
            if (allowed != null && allowed.Contains(c)) continue;
            if (wallMap.HasTile(c)) return true;
        }
        return false;
    }

    public HashSet<Vector3Int> AllowedWidthStrip(Vector3Int anchorCell, DoorSide side, int width)
    {
        // Width is currently not supported; allow only the socket cell itself.
        return new HashSet<Vector3Int> { anchorCell };
    }
}
