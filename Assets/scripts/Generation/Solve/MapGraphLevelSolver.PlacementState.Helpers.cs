// Assets/scripts/Generation/Graph/MapGraphLevelSolver.PlacementState.Helpers.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;

public partial class MapGraphLevelSolver
{
    private sealed partial class PlacementState
    {
        private void AlignToCell(Transform moduleRoot, Vector3Int targetCell)
        {
            if (!moduleRoot || stamp == null) return;
            moduleRoot.position = stamp.WorldFromCell(targetCell);
        }

        private void AlignSocketToCell(Transform moduleRoot, DoorSocket socket, Vector3Int targetCell)
        {
            if (!moduleRoot || !socket || stamp == null) return;
            var currentCell = stamp.CellFromWorld(socket.transform.position);
            var delta = stamp.WorldFromCell(targetCell) - stamp.WorldFromCell(currentCell);
            moduleRoot.position += delta;
        }

        private IEnumerable<DoorSocket> GetMatchingSockets(IEnumerable<DoorSocket> sockets, DoorSide side, int? width)
        {
            if (sockets == null) yield break;
            foreach (var socket in sockets)
            {
                if (socket == null) continue;
                if (side != socket.Side) continue;
                if (width.HasValue && NormalizeWidth(socket.Width) != NormalizeWidth(width.Value)) continue;
                yield return socket;
            }
        }

        private static int NormalizeWidth(int width) => 1;

        private Vector3Int GraphPosToCell(Vector2 graphPos)
        {
            return new Vector3Int(Mathf.RoundToInt(graphPos.x), Mathf.RoundToInt(graphPos.y), 0);
        }

        private DoorSide GetApproxSide(Vector2 from, Vector2 to)
        {
            var dir = to - from;
            if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.y))
                return dir.x >= 0 ? DoorSide.East : DoorSide.West;
            return dir.y >= 0 ? DoorSide.North : DoorSide.South;
        }

        private bool TryComputePlacement(ModuleMetaBase meta, GameObject prefab, out Placement placement)
        {
            placement = null;
            if (meta == null || stamp == null)
                return false;

            var rootCell = stamp.CellFromWorld(meta.transform.position);
            var cacheKey = prefab != null ? prefab : meta.gameObject;

            if (!geometryCache.TryGetValue(cacheKey, out var cached))
            {
                var floorCells = stamp.CollectModuleFloorCells(meta);
                var wallCells = stamp.CollectModuleWallCells(meta);
                var floorOffsets = new List<Vector3Int>(floorCells.Count);
                foreach (var c in floorCells)
                    floorOffsets.Add(c - rootCell);
                var wallOffsets = new List<Vector3Int>(wallCells.Count);
                foreach (var c in wallCells)
                    wallOffsets.Add(c - rootCell);
                cached = new GeometryCache(floorOffsets, wallOffsets);
                geometryCache[cacheKey] = cached;
            }

            var floors = new HashSet<Vector3Int>();
            foreach (var off in cached.FloorOffsets)
                floors.Add(rootCell + off);

            var walls = new HashSet<Vector3Int>();
            foreach (var off in cached.WallOffsets)
                walls.Add(rootCell + off);

            // Ignore "floor under wall" cells for overlap purposes; keep bite socket-cells as floor.
            floors.ExceptWith(walls);
            foreach (var s in meta.Sockets ?? Array.Empty<DoorSocket>())
            {
                if (s == null) continue;
                var cell = stamp.CellFromWorld(s.transform.position);
                floors.Add(cell);
            }

            placement = new Placement(meta, floors, walls, prefab, rootCell);
            return true;
        }

        private bool TryGetBlueprint(GameObject prefab, out ModuleBlueprint blueprint, out string error)
        {
            blueprint = null;
            error = null;
            if (prefab == null)
            {
                error = "Prefab is null.";
                return false;
            }

            if (blueprintCache.TryGetValue(prefab, out blueprint))
                return true;

            var inst = UnityEngine.Object.Instantiate(prefab, Vector3.zero, Quaternion.identity, stampWorldParent());
            var meta = inst ? inst.GetComponent<ModuleMetaBase>() : null;
            if (meta == null)
            {
                if (inst) UnityEngine.Object.Destroy(inst);
                error = $"Prefab {prefab.name} has no ModuleMetaBase.";
                return false;
            }

            meta.ResetUsed();
            AlignToCell(meta.transform, Vector3Int.zero);
            var rootCell = stamp.CellFromWorld(meta.transform.position);

            var floorCells = stamp.CollectModuleFloorCells(meta);
            var wallCells = stamp.CollectModuleWallCells(meta);

            var wallOffsetsSet = new HashSet<Vector3Int>();
            foreach (var c in wallCells) wallOffsetsSet.Add(c - rootCell);
            var floorOffsetsSet = new HashSet<Vector3Int>();
            foreach (var c in floorCells) floorOffsetsSet.Add(c - rootCell);

            // Ignore "floor under wall" cells for overlap purposes; keep bite socket-cells as floor.
            floorOffsetsSet.ExceptWith(wallOffsetsSet);

            var sockets = new List<SocketInfo>();
            if (meta.Sockets != null)
            {
                foreach (var s in meta.Sockets)
                {
                    if (s == null) continue;
                    var sockCell = stamp.CellFromWorld(s.transform.position);
                    var off = sockCell - rootCell;
                    sockets.Add(new SocketInfo(s.Side, NormalizeWidth(s.Width), s.BiteDepth, off));
                    floorOffsetsSet.Add(off);
                }
            }

            blueprint = new ModuleBlueprint(floorOffsetsSet.ToList(), wallOffsetsSet.ToList(), sockets);
            blueprintCache[prefab] = blueprint;
            UnityEngine.Object.Destroy(inst);
            return true;
        }

        private void BuildPlacementFromBlueprint(ModuleBlueprint blueprint, Vector3Int rootCell, out HashSet<Vector3Int> floors, out HashSet<Vector3Int> walls)
        {
            floors = new HashSet<Vector3Int>();
            walls = new HashSet<Vector3Int>();
            if (blueprint == null) return;
            foreach (var off in blueprint.FloorOffsets)
                floors.Add(rootCell + off);
            foreach (var off in blueprint.WallOffsets)
                walls.Add(rootCell + off);
        }

        private DoorSocket FindSocketAtCell(IEnumerable<DoorSocket> sockets, SocketInfo target, Vector3Int rootCell, DoorSocket exclude = null)
        {
            if (sockets == null || target == null) return null;
            var wantedCell = rootCell + target.CellOffset;
            foreach (var socket in sockets)
            {
                if (socket == null || socket == exclude) continue;
                if (socket.Side != target.Side) continue;
                if (NormalizeWidth(socket.Width) != NormalizeWidth(target.Width)) continue;
                var cell = stamp.CellFromWorld(socket.transform.position);
                if (cell == wantedCell)
                    return socket;
            }
            return null;
        }

        private List<GameObject> GetConnectorPrefabs(ConnectionTypeAsset connectionType, DoorSide requiredSide, int requiredWidth, out string error)
        {
            error = null;
            if (connectionType == null)
            {
                error = "Connection type is null.";
                return new List<GameObject>();
            }

            var key = (connectionType, requiredSide, NormalizeWidth(requiredWidth));
            if (connectorPrefabCache.TryGetValue(key, out var cached))
                return new List<GameObject>(cached);

            var prefabs = connectionType.prefabs ?? new List<GameObject>();
            var result = new List<GameObject>();
            foreach (var prefab in prefabs)
            {
                if (prefab == null) continue;
                if (!TryGetBlueprint(prefab, out var bp, out _)) continue;
                if (bp.Sockets.Any(s => s.Side == requiredSide && NormalizeWidth(s.Width) == NormalizeWidth(requiredWidth)))
                    result.Add(prefab);
            }

            if (result.Count == 0 && prefabs.Count == 0)
                error = $"Connection type {connectionType.name} has no prefabs.";
            else if (result.Count == 0)
                error = $"No connector prefabs match side {requiredSide} width {NormalizeWidth(requiredWidth)} for {connectionType.name}.";

            connectorPrefabCache[key] = new List<GameObject>(result);
            return result;
        }

        private List<GameObject> GetRoomPrefabs(RoomTypeAsset roomType, DoorSide? requiredSide, int? requiredWidth, out string error)
        {
            error = null;
            if (roomType == null)
            {
                error = "Room type is null.";
                return new List<GameObject>();
            }

            var normalizedWidth = requiredWidth.HasValue ? NormalizeWidth(requiredWidth.Value) : 0;
            var key = (roomType, requiredSide ?? DoorSide.North, normalizedWidth);
            if (roomPrefabCache.TryGetValue(key, out var cached))
                return new List<GameObject>(cached);

            var prefabs = roomType.prefabs ?? new List<GameObject>();
            var result = new List<GameObject>();
            foreach (var prefab in prefabs)
            {
                if (prefab == null) continue;
                if (!TryGetBlueprint(prefab, out var bp, out _)) continue;
                if (!requiredSide.HasValue)
                {
                    result.Add(prefab);
                    continue;
                }

                if (bp.Sockets.Any(s => s.Side == requiredSide.Value && NormalizeWidth(s.Width) == normalizedWidth))
                    result.Add(prefab);
            }

            if (result.Count == 0 && prefabs.Count == 0)
                error = $"Room type {roomType.name} has no prefabs.";
            else if (result.Count == 0)
                error = $"No room prefabs match side {requiredSide} width {normalizedWidth} for type {roomType.name}.";

            roomPrefabCache[key] = new List<GameObject>(result);
            return result;
        }

        private bool HasOverlap(HashSet<Vector3Int> cells, HashSet<Vector3Int> occupied, HashSet<Vector3Int> allowed = null)
        {
            foreach (var c in cells)
            {
                if (allowed != null && allowed.Contains(c)) continue;
                if (occupied.Contains(c)) return true;
            }
            return false;
        }

        private bool IsSocketBlocked(ModuleMetaBase owner, DoorSocket socket)
        {
            if (socket == null)
                return true;
            if (usedSockets.Contains(socket))
                return true;

            var spanId = socket.SpanId;
            if (string.IsNullOrEmpty(spanId))
                return false;

            owner ??= socket.GetComponentInParent<ModuleMetaBase>();
            if (owner == null)
                return false;

            return usedSocketSpans.Contains((owner, spanId));
        }

        private void MarkSocketUsed(DoorSocket socket)
        {
            if (socket == null)
                return;

            usedSockets.Add(socket);

            var spanId = socket.SpanId;
            if (string.IsNullOrEmpty(spanId))
                return;

            var owner = socket.GetComponentInParent<ModuleMetaBase>();
            if (owner != null)
                usedSocketSpans.Add((owner, spanId));
        }

        private void UnmarkSocketUsed(DoorSocket socket)
        {
            if (socket == null)
                return;

            usedSockets.Remove(socket);

            var spanId = socket.SpanId;
            if (string.IsNullOrEmpty(spanId))
                return;

            var owner = socket.GetComponentInParent<ModuleMetaBase>();
            if (owner == null)
                return;

            var key = (owner, spanId);
            if (!usedSocketSpans.Contains(key))
                return;

            // Defensive: only clear the span lock when no other socket from the same span is still marked used.
            foreach (var s in usedSockets)
            {
                if (s == null)
                    continue;
                if (s == socket)
                    continue;
                if (s.SpanId != spanId)
                    continue;
                if (s.GetComponentInParent<ModuleMetaBase>() != owner)
                    continue;
                return;
            }

            usedSocketSpans.Remove(key);
        }

        private static void AddUsedSocketToPlacement(Placement placement, DoorSocket socket)
        {
            if (placement == null || socket == null)
                return;
            if (!placement.UsedSockets.Contains(socket))
                placement.UsedSockets.Add(socket);
        }

        private bool HasConfigSpace(GameObject fixedPrefab, GameObject movingPrefab)
        {
            if (configSpaceLibrary == null || fixedPrefab == null || movingPrefab == null)
                return false;
            if (!configSpaceLibrary.TryGetSpace(fixedPrefab, movingPrefab, out var space, out _))
                return false;
            return space != null && !space.IsEmpty;
        }

        private bool FitsConfigSpace(GameObject fixedPrefab, GameObject movingPrefab, Vector3Int fixedRootCell, Vector3Int movingRootCell)
        {
            if (configSpaceLibrary == null || fixedPrefab == null || movingPrefab == null)
                return false;
            if (!configSpaceLibrary.TryGetSpace(fixedPrefab, movingPrefab, out var space, out _))
                return false;
            var delta = new Vector2Int(movingRootCell.x - fixedRootCell.x, movingRootCell.y - fixedRootCell.y);
            return space.Contains(delta);
        }

        private bool CheckTimeLimit()
        {
            if (maxDurationSeconds <= 0f) return true;
            if (Time.realtimeSinceStartup - startTime <= maxDurationSeconds)
                return true;
            var msg = $"Placement time limit exceeded. Nodes placed {placedNodes.Count}/{totalNodes}, edges placed {placedEdges.Count}/{totalEdges}.";
            if (verboseLogs)
                Debug.Log($"[MapGraphLevelSolver] {msg}");
            LastError ??= msg;
            return false;
        }

        private HashSet<Vector3Int> AllowedWidthStrip(Vector3Int anchorCell, DoorSide side, int width)
        {
            // Width is currently not supported; allow only the socket cell itself.
            return new HashSet<Vector3Int> { anchorCell };
        }

        private void CommitPlacement(string nodeId, Placement placement)
        {
            placementStack.Add(placement);
            foreach (var c in placement.FloorCells) occupiedFloor.Add(c);
            foreach (var c in placement.WallCells) occupiedWall.Add(c);
            foreach (var s in placement.UsedSockets) MarkSocketUsed(s);
            if (placement.EdgeKey.HasValue)
                placedEdges.Add(placement.EdgeKey.Value);
            if (!string.IsNullOrEmpty(nodeId))
                placedNodes[nodeId] = placement;
        }

        private void RollbackToDepth(int depth)
        {
            while (placementStack.Count > depth)
            {
                var p = placementStack[placementStack.Count - 1];
                RemovePlacement(p);
                placementStack.RemoveAt(placementStack.Count - 1);
            }
        }

        public void StampAll(bool disableRenderers = true)
        {
            foreach (var placement in placementStack)
            {
                if (placement.Meta == null) continue;
                stamp.StampModuleFloor(placement.Meta, placement.FloorCells);
                stamp.StampModuleWalls(placement.Meta, overrideCells: null, allowedCells: placement.WallCells);
                if (disableRenderers)
                    stamp.DisableRenderers(placement.Meta.transform);
            }
        }

        public void DestroyPlacedInstances()
        {
            foreach (var placement in placementStack)
            {
                if (placement.Meta != null)
                    UnityEngine.Object.Destroy(placement.Meta.gameObject);
            }
            placementStack.Clear();
            placedNodes.Clear();
            placedEdges.Clear();
            occupiedFloor.Clear();
            occupiedWall.Clear();
            usedSockets.Clear();
            usedSocketSpans.Clear();
        }

        public void Cleanup()
        {
            foreach (var p in placementStack)
            {
                if (p.Meta != null)
                    UnityEngine.Object.Destroy(p.Meta.gameObject);
            }
            placementStack.Clear();
            placedNodes.Clear();
            placedEdges.Clear();
            occupiedFloor.Clear();
            occupiedWall.Clear();
            usedSockets.Clear();
            usedSocketSpans.Clear();
        }

        private void RemovePlacement(Placement p)
        {
            foreach (var kv in placedNodes.Where(kv => kv.Value == p).ToList())
                placedNodes.Remove(kv.Key);
            foreach (var c in p.FloorCells) occupiedFloor.Remove(c);
            foreach (var c in p.WallCells) occupiedWall.Remove(c);
            foreach (var s in p.UsedSockets) UnmarkSocketUsed(s);
            if (p.EdgeKey.HasValue)
                placedEdges.Remove(p.EdgeKey.Value);
            if (p.Meta != null)
                UnityEngine.Object.Destroy(p.Meta.gameObject);
        }

        private void Log(string msg)
        {
            if (verboseLogs)
                Debug.Log($"[MapGraphLevelSolver] {msg}");
            if (string.IsNullOrEmpty(LastError))
                LastError = msg;
        }

        private Transform stampWorldParent()
        {
            var gridField = stamp.GetType().GetField("grid", BindingFlags.NonPublic | BindingFlags.Instance);
            if (gridField == null) return null;
            var grid = gridField.GetValue(stamp) as Grid;
            return grid != null ? grid.transform : null;
        }

        private sealed class Placement
        {
            public ModuleMetaBase Meta { get; }
            public HashSet<Vector3Int> FloorCells { get; }
            public HashSet<Vector3Int> WallCells { get; }
            public List<DoorSocket> UsedSockets { get; }
            public (string, string)? EdgeKey { get; set; }
            public GameObject Prefab { get; }
            public Vector3Int RootCell { get; private set; }

            public Placement(ModuleMetaBase meta, HashSet<Vector3Int> floors, HashSet<Vector3Int> walls, GameObject prefab, Vector3Int rootCell)
            {
                Meta = meta;
                FloorCells = floors ?? new HashSet<Vector3Int>();
                WallCells = walls ?? new HashSet<Vector3Int>();
                UsedSockets = new List<DoorSocket>();
                EdgeKey = null;
                Prefab = prefab;
                RootCell = rootCell;
            }

            public void SetRoot(Vector3Int root)
            {
                RootCell = root;
            }
        }

        private sealed class GeometryCache
        {
            public List<Vector3Int> FloorOffsets { get; }
            public List<Vector3Int> WallOffsets { get; }

            public GeometryCache(List<Vector3Int> floorOffsets, List<Vector3Int> wallOffsets)
            {
                FloorOffsets = floorOffsets ?? new List<Vector3Int>();
                WallOffsets = wallOffsets ?? new List<Vector3Int>();
            }
        }

        private sealed class ModuleBlueprint
        {
            public List<Vector3Int> FloorOffsets { get; }
            public List<Vector3Int> WallOffsets { get; }
            public List<SocketInfo> Sockets { get; }

            public ModuleBlueprint(List<Vector3Int> floorOffsets, List<Vector3Int> wallOffsets, List<SocketInfo> sockets)
            {
                FloorOffsets = floorOffsets ?? new List<Vector3Int>();
                WallOffsets = wallOffsets ?? new List<Vector3Int>();
                Sockets = sockets ?? new List<SocketInfo>();
            }
        }

        private sealed class SocketInfo
        {
            public DoorSide Side { get; }
            public int Width { get; }
            public int BiteDepth { get; }
            public Vector3Int CellOffset { get; }

            public SocketInfo(DoorSide side, int width, int biteDepth, Vector3Int cellOffset)
            {
                Side = side;
                Width = width;
                BiteDepth = Mathf.Max(1, biteDepth);
                CellOffset = cellOffset;
            }
        }
    }
}
