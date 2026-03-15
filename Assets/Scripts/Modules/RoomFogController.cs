using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class RoomFogController : MonoBehaviour
{
    [SerializeField] private GeneratedLevelRuntime generatedLevelRuntime;
    [SerializeField] private Tilemap fogMap;
    [SerializeField] private TileBase fogTile;
    [SerializeField] private bool coverConnectors = true;
    [SerializeField] private bool coverWalls = true;
    [SerializeField] private float revealDurationSeconds = 0.35f;
    [SerializeField] private float revealSoftnessWorld = 1.5f;
    [SerializeField] private string revealShaderName = "Custom/RoomFogReveal2D";

    private readonly Dictionary<string, List<Vector3Int>> fogCellsByRoom = new();
    private readonly HashSet<string> revealedRooms = new();
    private readonly Dictionary<string, Coroutine> revealRoutines = new();
    private readonly Dictionary<string, GameObject> activeOverlays = new();
    private readonly Dictionary<string, Material> activeMaterials = new();
    private PlayerRoomTracker playerRoomTracker;

    private void OnEnable()
    {
        if (generatedLevelRuntime != null)
            generatedLevelRuntime.GeneratedRoomsChanged += HandleGeneratedRoomsChanged;
        if (generatedLevelRuntime != null)
            generatedLevelRuntime.PlayerSpawned += HandlePlayerSpawned;

        TryAttachSpawnedPlayerTracker();

        HandleGeneratedRoomsChanged();
        if (playerRoomTracker != null && playerRoomTracker.CurrentRoom != null)
            RevealRoom(playerRoomTracker.CurrentRoom, playerRoomTracker.transform.position, instant: false);
    }

    private void OnDisable()
    {
        if (generatedLevelRuntime != null)
            generatedLevelRuntime.GeneratedRoomsChanged -= HandleGeneratedRoomsChanged;
        if (generatedLevelRuntime != null)
            generatedLevelRuntime.PlayerSpawned -= HandlePlayerSpawned;

        AttachTracker(null);

        foreach (var routine in revealRoutines.Values)
        {
            if (routine != null)
                StopCoroutine(routine);
        }
        revealRoutines.Clear();
        ClearActiveRevealObjects();
    }

    private void HandleGeneratedRoomsChanged()
    {
        RebuildFog();
    }

    private void HandlePlayerSpawned(GameObject playerInstance)
    {
        if (playerInstance == null)
            return;

        playerInstance.TryGetComponent<PlayerRoomTracker>(out var spawnedTracker);
        AttachTracker(spawnedTracker);

        if (playerRoomTracker != null && playerRoomTracker.CurrentRoom != null)
            RevealRoom(playerRoomTracker.CurrentRoom, playerRoomTracker.transform.position, instant: false);
    }

    private void HandleEnteredRoom(GeneratedRoomInfo roomInfo)
    {
        RevealRoom(roomInfo, playerRoomTracker != null ? playerRoomTracker.transform.position : Vector3.zero, instant: false);
    }

    private void TryAttachSpawnedPlayerTracker()
    {
        if (generatedLevelRuntime == null || generatedLevelRuntime.SpawnedPlayerInstance == null)
            return;

        generatedLevelRuntime.SpawnedPlayerInstance.TryGetComponent<PlayerRoomTracker>(out var spawnedTracker);
        AttachTracker(spawnedTracker);
    }

    private void AttachTracker(PlayerRoomTracker tracker)
    {
        if (playerRoomTracker != null)
            playerRoomTracker.EnteredRoom -= HandleEnteredRoom;

        playerRoomTracker = tracker;

        if (playerRoomTracker != null)
            playerRoomTracker.EnteredRoom += HandleEnteredRoom;
    }

    public void RebuildFog()
    {
        if (fogMap == null)
            return;

        fogMap.ClearAllTiles();
        fogCellsByRoom.Clear();
        revealedRooms.Clear();
        ClearActiveRevealObjects();

        if (generatedLevelRuntime == null || generatedLevelRuntime.LastGeneratedRooms == null)
            return;

        for (var i = 0; i < generatedLevelRuntime.LastGeneratedRooms.Count; i++)
        {
            var room = generatedLevelRuntime.LastGeneratedRooms[i];
            if (room == null)
                continue;
            if (room.IsConnector && !coverConnectors)
                continue;

            var cells = new HashSet<Vector3Int>();
            if (room.FloorCells != null)
            {
                foreach (var cell in room.FloorCells)
                    cells.Add(cell);
            }
            if (coverWalls && room.WallCells != null)
            {
                foreach (var cell in room.WallCells)
                    cells.Add(cell);
            }

            if (cells.Count == 0)
                continue;

            var list = new List<Vector3Int>(cells.Count);
            foreach (var cell in cells)
            {
                list.Add(cell);
                if (fogTile != null)
                    fogMap.SetTile(cell, fogTile);
            }

            fogCellsByRoom[room.NodeId] = list;
        }
    }

    public void RevealRoom(GeneratedRoomInfo roomInfo, Vector3 revealOriginWorld, bool instant)
    {
        if (roomInfo == null || string.IsNullOrEmpty(roomInfo.NodeId))
            return;
        if (revealedRooms.Contains(roomInfo.NodeId))
            return;
        if (!fogCellsByRoom.TryGetValue(roomInfo.NodeId, out var cells) || cells == null || cells.Count == 0)
            return;

        if (instant || revealDurationSeconds <= 0f)
        {
            ClearFogCells(cells);
            revealedRooms.Add(roomInfo.NodeId);
            return;
        }

        if (revealRoutines.TryGetValue(roomInfo.NodeId, out var running) && running != null)
            StopCoroutine(running);
        CleanupRevealObjects(roomInfo.NodeId);

        revealRoutines[roomInfo.NodeId] = StartCoroutine(RevealRoomRoutine(roomInfo.NodeId, cells, revealOriginWorld));
    }

    private IEnumerator RevealRoomRoutine(string roomNodeId, List<Vector3Int> cells, Vector3 revealOriginWorld)
    {
        ClearFogCells(cells);

        var overlay = CreateOverlayTilemap(roomNodeId, cells);
        if (overlay == null)
        {
            revealedRooms.Add(roomNodeId);
            revealRoutines.Remove(roomNodeId);
            yield break;
        }

        var renderer = overlay.GetComponent<TilemapRenderer>();
        var shader = Shader.Find(revealShaderName);
        if (renderer == null || shader == null)
        {
            Destroy(overlay);
            revealedRooms.Add(roomNodeId);
            revealRoutines.Remove(roomNodeId);
            yield break;
        }

        var runtimeMaterial = new Material(shader);
        renderer.material = runtimeMaterial;
        activeOverlays[roomNodeId] = overlay;
        activeMaterials[roomNodeId] = runtimeMaterial;

        runtimeMaterial.SetVector("_RevealOrigin", new Vector4(revealOriginWorld.x, revealOriginWorld.y, 0f, 0f));
        runtimeMaterial.SetFloat("_RevealSoftness", Mathf.Max(0.05f, revealSoftnessWorld));

        var targetRadius = ComputeTargetRadius(cells, revealOriginWorld);
        var duration = Mathf.Max(0.01f, revealDurationSeconds);
        var elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            var t = Mathf.Clamp01(elapsed / duration);
            runtimeMaterial.SetFloat("_RevealRadius", Mathf.Lerp(0f, targetRadius, t));
            yield return null;
        }

        runtimeMaterial.SetFloat("_RevealRadius", targetRadius);
        revealedRooms.Add(roomNodeId);
        CleanupRevealObjects(roomNodeId);
        revealRoutines.Remove(roomNodeId);
    }

    private GameObject CreateOverlayTilemap(string roomNodeId, List<Vector3Int> cells)
    {
        if (fogMap == null || fogTile == null)
            return null;

        var overlay = new GameObject($"FogReveal_{roomNodeId}");
        var overlayTransform = overlay.transform;
        overlayTransform.SetParent(fogMap.transform.parent, false);
        overlayTransform.localPosition = Vector3.zero;
        overlayTransform.localRotation = Quaternion.identity;
        overlayTransform.localScale = Vector3.one;

        var tilemap = overlay.AddComponent<Tilemap>();
        var renderer = overlay.AddComponent<TilemapRenderer>();
        renderer.sortingLayerID = fogMap.GetComponent<TilemapRenderer>().sortingLayerID;
        renderer.sortingOrder = fogMap.GetComponent<TilemapRenderer>().sortingOrder;

        for (var i = 0; i < cells.Count; i++)
            tilemap.SetTile(cells[i], fogTile);

        return overlay;
    }

    private void ClearFogCells(List<Vector3Int> cells)
    {
        if (fogMap == null)
            return;

        for (var i = 0; i < cells.Count; i++)
            fogMap.SetTile(cells[i], null);
    }

    private float ComputeTargetRadius(List<Vector3Int> cells, Vector3 originWorld)
    {
        var radius = 0f;
        for (var i = 0; i < cells.Count; i++)
        {
            var world = fogMap.GetCellCenterWorld(cells[i]);
            radius = Mathf.Max(radius, Vector2.Distance(originWorld, world));
        }

        return radius + revealSoftnessWorld + 0.5f;
    }

    private void ClearActiveRevealObjects()
    {
        var roomIds = new List<string>(activeOverlays.Keys);
        for (var i = 0; i < roomIds.Count; i++)
            CleanupRevealObjects(roomIds[i]);
    }

    private void CleanupRevealObjects(string roomNodeId)
    {
        if (activeMaterials.TryGetValue(roomNodeId, out var material) && material != null)
            Destroy(material);
        if (activeOverlays.TryGetValue(roomNodeId, out var overlay) && overlay != null)
            Destroy(overlay);

        activeMaterials.Remove(roomNodeId);
        activeOverlays.Remove(roomNodeId);
    }
}
