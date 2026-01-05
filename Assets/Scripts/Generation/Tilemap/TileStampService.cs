using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

public class TileStampService
{
    private readonly Grid grid;
    private readonly Tilemap floorMap;
    private readonly Tilemap wallMap;

    public TileStampService(Grid grid, Tilemap floorMap, Tilemap wallMap)
    {
        this.grid = grid;
        this.floorMap = floorMap;
        this.wallMap = wallMap;
    }

    public Grid Grid => grid;
    public Tilemap FloorMap => floorMap;
    public Tilemap WallMap => wallMap;

    public Vector3 WorldFromCell(Vector3Int cell) => grid ? grid.GetCellCenterWorld(cell) : (Vector3)cell;
    public Vector3Int CellFromWorld(Vector3 world) => grid ? grid.WorldToCell(world) : Vector3Int.FloorToInt(world);

    public void ClearMaps()
    {
        if (floorMap) floorMap.ClearAllTiles();
        if (wallMap) wallMap.ClearAllTiles();
    }

    public List<Vector3Int> CollectModuleFloorCells(ModuleMetaBase module)
    {
        var result = new List<Vector3Int>();
        if (!module || grid == null) return result;
        var tilemaps = module.GetComponentsInChildren<Tilemap>();
        if (tilemaps == null || tilemaps.Length == 0) return result;
        foreach (var src in tilemaps)
        {
            if (src == null || !src.gameObject.name.ToLower().Contains("floor")) continue;
            var bounds = src.cellBounds;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                var c = new Vector3Int(x, y, 0);
                if (src.GetTile(c) == null) continue;
                var world = src.GetCellCenterWorld(c);
                var dstCell = grid.WorldToCell(world);
                result.Add(dstCell);
            }
        }
        return result;
    }

    public List<Vector3Int> CollectModuleWallCells(ModuleMetaBase module)
    {
        var result = new List<Vector3Int>();
        if (!module || grid == null) return result;
        var tilemaps = module.GetComponentsInChildren<Tilemap>();
        if (tilemaps == null || tilemaps.Length == 0) return result;
        foreach (var src in tilemaps)
        {
            if (src == null || !src.gameObject.name.ToLower().Contains("wall")) continue;
            var bounds = src.cellBounds;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                var c = new Vector3Int(x, y, 0);
                if (src.GetTile(c) == null) continue;
                var world = src.GetCellCenterWorld(c);
                var dstCell = grid.WorldToCell(world);
                result.Add(dstCell);
            }
        }
        return result;
    }

    public void StampModuleFloor(ModuleMetaBase module, HashSet<Vector3Int> allowedCells = null)
    {
        if (!module || floorMap == null || grid == null) return;
        var tilemaps = module.GetComponentsInChildren<Tilemap>();
        if (tilemaps == null || tilemaps.Length == 0) return;
        foreach (var src in tilemaps)
        {
            if (src == null || !src.gameObject.name.ToLower().Contains("floor")) continue;
            var bounds = src.cellBounds;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                var c = new Vector3Int(x, y, 0);
                var tile = src.GetTile(c);
                if (tile == null) continue;
                var world = src.GetCellCenterWorld(c);
                var dstCell = grid.WorldToCell(world);
                if (allowedCells != null && !allowedCells.Contains(dstCell)) continue;
                PasteTile(src, c, floorMap, dstCell);
                if (wallMap) wallMap.SetTile(dstCell, null);
            }
        }
    }

    public void StampModuleWalls(ModuleMetaBase module, HashSet<Vector3Int> overrideCells = null, HashSet<Vector3Int> allowedCells = null)
    {
        if (!module || wallMap == null || grid == null) return;
        var tilemaps = module.GetComponentsInChildren<Tilemap>();
        if (tilemaps == null || tilemaps.Length == 0) return;
        foreach (var src in tilemaps)
        {
            if (src == null || !src.gameObject.name.ToLower().Contains("wall")) continue;
            var bounds = src.cellBounds;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                var c = new Vector3Int(x, y, 0);
                var tile = src.GetTile(c);
                if (tile == null) continue;
                var world = src.GetCellCenterWorld(c);
                var dstCell = grid.WorldToCell(world);
                if (allowedCells != null && !allowedCells.Contains(dstCell)) continue;
                if (floorMap != null && floorMap.HasTile(dstCell)) continue;
                bool canOverride = overrideCells != null && overrideCells.Contains(dstCell);
                if (!canOverride && wallMap.HasTile(dstCell)) continue;
                PasteTile(src, c, wallMap, dstCell);
            }
        }
    }

    public void DisableRenderers(Transform t)
    {
        foreach (var r in t.GetComponentsInChildren<TilemapRenderer>(true)) r.enabled = false;
        foreach (var r in t.GetComponentsInChildren<SpriteRenderer>(true)) r.enabled = false;
    }

    private void PasteTile(Tilemap src, Vector3Int srcCell, Tilemap dst, Vector3Int dstCell)
    {
        var tile = src.GetTile(srcCell);
        if (tile == null) return;
        dst.SetTile(dstCell, tile);
        dst.SetTileFlags(dstCell, TileFlags.None);
        dst.SetTransformMatrix(dstCell, src.GetTransformMatrix(srcCell));
        dst.SetColor(dstCell, src.GetColor(srcCell));
    }
}
