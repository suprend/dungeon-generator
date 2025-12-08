// MapBuilder.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

public class MapBuilder : MonoBehaviour
{
    [Header("Prefabs pools")]
    public GameObject[] roomPrefabs;        // префабы комнат (с RoomMeta)
    public GameObject[] connectorPrefabs;   // префабы переходов (с ConnectorMeta)

    [Header("Global Tilemap layers")]
    public Grid targetGrid;                 // общий Grid
    public Tilemap floorMap;                // общий пол
    public Tilemap wallMap;                 // общие стены

    [Header("Generation params")]
    public int maxRooms = 10;               // лимит добавляемых комнат (без стартовой)
    public int attemptsPerFrontier = 6;     // попыток подобрать (connector, room) на один сокет

    // Walls are always taken from prefabs; missing walls are not filled algorithmically.

    [Header("Debug")]
    public bool verboseLogs = false;

    // Constraints (fixed configuration):
    // - Allow Frontier From Connectors: OFF
    // - Forbid Floor Overlap Except Anchors: ON
    // - Forbid Room Wall Overlap: ON
    private const bool allowFrontierFromConnectors = false;
    private const bool forbidFloorOverlapExceptAnchors = true;
    private const bool forbidRoomWallOverlap = true;
    // Допуск выхода от якоря убран: перекрытие пола разрешено только в поперечнике k на клетке якоря

    private readonly List<FrontierEntry> frontier = new();
    private System.Random rng;
    private TileStampService stamp;
    private PlacementValidator validator;

    [Serializable]
    public class FrontierEntry
    {
        public ModuleMetaBase owner; // модуль, владелец сокета (обычно комната)
        public DoorSocket socket;    // сам сокет в инстансе
        public DoorSide side;        // сторона
        public int width;            // k
        public Vector3Int cell;      // клетка сокета (в targetGrid)
    }

    void Awake()
    {
        rng = new System.Random(UnityEngine.Random.Range(int.MinValue, int.MaxValue));
        stamp = new TileStampService(targetGrid, floorMap, wallMap);
        validator = new PlacementValidator(floorMap, wallMap, stamp);
        Build(new Vector3Int(0, 0, 0));
    }

    // Полезные утилиты вынесены в DoorSideExtensions / CollectionExtensions

    // Построение карты
    public void Build(Vector3Int startCell)
    {
        stamp.ClearMaps();
        frontier.Clear();
        // 1. Выбрать стартовую комнату
        var startRoomPrefab = PickRandom(roomPrefabs);
        if (!startRoomPrefab) { Debug.LogWarning("[MapBuilder] No roomPrefabs assigned"); return; }
        var startRoom = Instantiate(startRoomPrefab, stamp.WorldFromCell(startCell), Quaternion.identity, targetGrid ? targetGrid.transform : null)
                        .GetComponent<RoomMeta>();
        if (!startRoom) { Debug.LogWarning("[MapBuilder] Start prefab has no RoomMeta"); return; }
        startRoom.ResetUsed();

        // Штамп пола и стен
        stamp.StampModuleFloor(startRoom);
        stamp.StampModuleWalls(startRoom);
        stamp.DisableRenderers(startRoom.transform);

        // Собрать фронтир
        AddFreeSocketsToFrontier(startRoom);

        int roomsPlaced = 0;
        int guard = 10000; // защита от бесконечного цикла
        while (frontier.Count > 0 && roomsPlaced < maxRooms && guard-- > 0)
        {
            var a = PopRandom(frontier);
            if (a == null || a.owner == null || a.socket == null) continue;
            if (verboseLogs) Debug.Log($"[MapBuilder] Grow from {a.owner.name} side={a.side} k={a.width}");

            if (TryGrowFrom(a, out var newRoom))
            {
                roomsPlaced++;
                if (newRoom) AddFreeSocketsToFrontier(newRoom);
            }
        }
    }

    // Попытка нарастить карту из сокета фронтира: anchor + connector + room
    bool TryGrowFrom(FrontierEntry a, out RoomMeta placedRoom)
    {
        placedRoom = null;
        var anchorMeta = a.owner;
        var anchorSide = a.side;
        var k = NormalizeWidth(a.width);
        var anchorCell = a.cell;

        // Зафиксируем использование конкретного сокета у якоря
        if (!anchorMeta.TryUse(a.socket))
            return false;

        // Подбор коннектора: достаточно иметь хотя бы один сокет на Opp(anchorSide) с шириной k
        var connectorCandidates = PrefabsWithSocket<ConnectorMeta>(connectorPrefabs, anchorSide.Opposite(), k).ToList();
        connectorCandidates.Shuffle(rng);

        int attempts = 0;
        foreach (var connectorPrefab in connectorCandidates)
        {
            if (attempts++ >= attemptsPerFrontier) break;

            if (TryConnectViaConnector(connectorPrefab, anchorMeta, anchorSide, k, anchorCell, out placedRoom))
                return true;
        }

        if (verboseLogs)
            Debug.Log($"[MapBuilder] No connector matched side={anchorSide} k={k}. Check ConnectorMeta sockets and Width.");

        return false;
    }

    bool TryConnectViaConnector(GameObject connectorPrefab, ModuleMetaBase anchorMeta, DoorSide anchorSide, int anchorWidth, Vector3Int anchorCell, out RoomMeta placedRoom)
    {
        placedRoom = null;
        var connectorInst = Instantiate(connectorPrefab, Vector3.zero, Quaternion.identity, targetGrid ? targetGrid.transform : null)
                           .GetComponent<ConnectorMeta>();
        connectorInst.ResetUsed();
        var connInitialPos = connectorInst.transform.position;

        var s1List = GetMatchingSockets(connectorInst.Sockets, anchorSide.Opposite(), anchorWidth).ToList();
        s1List.Shuffle(rng);

        foreach (var s1 in s1List)
        {
            connectorInst.transform.position = connInitialPos;
            AlignSocketToCell(connectorInst.transform, s1, anchorCell);

            var otherSockets = connectorInst.Sockets.Where(s => s && s != s1).ToList();
            otherSockets.Shuffle(rng);

            foreach (var s2 in otherSockets)
            {
                var s2Cell = stamp.CellFromWorld(s2.transform.position);

                if (forbidFloorOverlapExceptAnchors)
                {
                    var allowed = validator.AllowedWidthStrip(anchorCell, s1.Side, s1.Width);
                    if (validator.OverlapsExistingFloorExcept(connectorInst, allowed))
                    {
                        if (verboseLogs) Debug.Log("[MapBuilder] Connector floor would overlap existing floor beyond anchor; skipping");
                        continue;
                    }
                }

                if (TryPlaceRoom(connectorInst, anchorMeta, s1, s2, s2Cell, out placedRoom))
                    return true;
            }
        }

        Destroy(connectorInst.gameObject);
        return false;
    }

    bool TryPlaceRoom(ConnectorMeta connectorInst, ModuleMetaBase anchorMeta, DoorSocket s1, DoorSocket s2, Vector3Int s2Cell, out RoomMeta placedRoom)
    {
        placedRoom = null;
        int roomWidth = NormalizeWidth(s2.Width);
        var targetSide = s2.Side.Opposite();
        var roomCandidates = PrefabsWithSocket<RoomMeta>(roomPrefabs, targetSide, roomWidth).ToList();
        roomCandidates.Shuffle(rng);

        foreach (var roomPrefab in roomCandidates)
        {
            var roomInst = Instantiate(roomPrefab, Vector3.zero, Quaternion.identity, targetGrid ? targetGrid.transform : null)
                           .GetComponent<RoomMeta>();
            roomInst.ResetUsed();

            var bSock = GetMatchingSockets(roomInst.Sockets, targetSide, roomWidth).FirstOrDefault();
            if (!bSock)
            {
                Destroy(roomInst.gameObject);
                continue;
            }

            AlignSocketToCell(roomInst.transform, bSock, s2Cell);

            if (forbidFloorOverlapExceptAnchors)
            {
                var connectorCells = stamp.CollectModuleFloorCells(connectorInst);
                if (validator.OverlapsExistingFloorExcept(roomInst, new HashSet<Vector3Int>(connectorCells) { s2Cell }))
                {
                    Destroy(roomInst.gameObject);
                    if (verboseLogs) Debug.Log("[MapBuilder] Room floor would overlap existing/connector floor beyond s2; skipping room");
                    continue;
                }
            }

            if (forbidRoomWallOverlap)
            {
                var connectorCells = stamp.CollectModuleFloorCells(connectorInst);
                if (validator.OverlapsExistingWallsExcept(roomInst, new HashSet<Vector3Int>(connectorCells)))
                {
                    Destroy(roomInst.gameObject);
                    if (verboseLogs) Debug.Log("[MapBuilder] Room walls would intersect existing walls; skipping room");
                    continue;
                }
            }

            if (forbidRoomWallOverlap)
            {
                var allowedReplace = new HashSet<Vector3Int>(stamp.CollectModuleWallCells(anchorMeta));
                foreach (var wc in stamp.CollectModuleWallCells(roomInst)) allowedReplace.Add(wc);
                if (validator.OverlapsExistingWallsExcept(connectorInst, allowedReplace))
                {
                    Destroy(roomInst.gameObject);
                    if (verboseLogs) Debug.Log("[MapBuilder] Connector walls would intersect existing walls outside anchor/room walls; skipping room");
                    continue;
                }
            }

            stamp.StampModuleFloor(connectorInst);
            stamp.StampModuleFloor(roomInst);
            var allowedReplaceWalls = new HashSet<Vector3Int>(stamp.CollectModuleWallCells(anchorMeta));
            foreach (var wc in stamp.CollectModuleWallCells(roomInst)) allowedReplaceWalls.Add(wc);
            stamp.StampModuleWalls(connectorInst, allowedReplaceWalls);
            stamp.StampModuleWalls(roomInst);

            stamp.DisableRenderers(connectorInst.transform);
            stamp.DisableRenderers(roomInst.transform);

            connectorInst.TryUse(s1);
            connectorInst.TryUse(s2);
            roomInst.TryUse(bSock);

            AddFreeSocketsToFrontier(connectorInst);
            placedRoom = roomInst;
            return true;
        }

        return false;
    }

    // Добавить свободные сокеты модуля в фронтир
    void AddFreeSocketsToFrontier(ModuleMetaBase module)
    {
        if (module == null || module.Sockets == null) return;
        if (!allowFrontierFromConnectors && module is ConnectorMeta) return;
        foreach (var s in module.Sockets)
        {
            if (!s) continue;
            if (module.IsUsed(s)) continue;
            var cell = stamp.CellFromWorld(s.transform.position);
            // Для комнат: сокеты должны быть на стене (т.е. там нет пола)
            if (module is RoomMeta && floorMap != null && floorMap.HasTile(cell)) continue;
            frontier.Add(new FrontierEntry
            {
                owner = module,
                socket = s,
                side = s.Side,
                width = NormalizeWidth(s.Width),
                cell = cell
            });
        }
    }

    // --------------------- ВСПОМОГАТЕЛЬНОЕ ---------------------

    Vector3 WorldFromCell(Vector3Int cell) => stamp.WorldFromCell(cell);
    Vector3Int CellFromWorld(Vector3 world) => stamp.CellFromWorld(world);

    void AlignSocketToCell(Transform moduleRoot, DoorSocket socket, Vector3Int targetCell)
    {
        if (!moduleRoot || !socket) return;
        var currentCell = CellFromWorld(socket.transform.position);
        var delta = WorldFromCell(targetCell) - WorldFromCell(currentCell);
        moduleRoot.position += delta;
    }

    T PickRandom<T>(IList<T> list)
    {
        if (list == null || list.Count == 0) return default;
        return list[rng.Next(list.Count)];
    }

    T PickRandom<T>(T[] array)
    {
        if (array == null || array.Length == 0) return default;
        return array[rng.Next(array.Length)];
    }

    T PopRandom<T>(IList<T> list)
    {
        if (list == null || list.Count == 0) return default;
        int i = rng.Next(list.Count);
        var val = list[i];
        list.RemoveAt(i);
        return val;
    }

    IEnumerable<GameObject> PrefabsWithSocket<TMeta>(GameObject[] prefabsPool, DoorSide side, int width)
        where TMeta : ModuleMetaBase
    {
        if (prefabsPool == null) yield break;
        foreach (var prefab in prefabsPool)
        {
            if (!prefab) continue;
            var meta = prefab.GetComponent<TMeta>();
            if (!meta || meta.Sockets == null) continue;
            if (meta.Sockets.Any(s => MatchesSocket(s, side, width)))
                yield return prefab;
        }
    }

    IEnumerable<DoorSocket> GetMatchingSockets(IEnumerable<DoorSocket> sockets, DoorSide side, int width)
    {
        if (sockets == null) yield break;
        foreach (var socket in sockets)
        {
            if (MatchesSocket(socket, side, width))
                yield return socket;
        }
    }

    static bool MatchesSocket(DoorSocket socket, DoorSide side, int width)
    {
        return socket && socket.Side == side && NormalizeWidth(socket.Width) == NormalizeWidth(width);
    }

    static int NormalizeWidth(int width) => Mathf.Max(1, width);

    // Shuffling is delegated to CollectionExtensions
}
