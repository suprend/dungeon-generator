using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(GraphMapBuilder))]
[RequireComponent(typeof(GeneratedLevelRuntime))]
[RequireComponent(typeof(LevelEnemyController))]
public sealed class DungeonRunController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GraphMapBuilder graphMapBuilder;
    [SerializeField] private GeneratedLevelRuntime generatedLevelRuntime;
    [SerializeField] private LevelEnemyController levelEnemyController;
    [SerializeField] private DungeonRunHud runHud;
    [SerializeField] private GameObject exitPortalPrefab;
    [SerializeField] private bool spawnExitIfMissing;

    [Header("Run")]
    [SerializeField] private int baseSeed = 1000;
    [SerializeField] private int seedStep = 9973;
    [SerializeField] private int currentLevel = 1;
    [SerializeField] private float multiplierPerLevel = 0.5f;
    [SerializeField] private float enemyStatsMultiplierPerLevel = 0.25f;
    [SerializeField] private bool buildFirstLevelOnStart;

    private readonly System.Random roomRng = new();
    private GameObject currentExitPortal;
    private int score;
    private bool isChangingLevel;

    public int CurrentLevel => Mathf.Max(1, currentLevel);
    public int Score => Mathf.Max(0, score);
    public float ScoreMultiplier => 1f + (CurrentLevel - 1) * Mathf.Max(0f, multiplierPerLevel);
    public float EnemyStatsMultiplier => 1f + (CurrentLevel - 1) * Mathf.Max(0f, enemyStatsMultiplierPerLevel);

    private void Awake()
    {
        CacheComponents();
        if (generatedLevelRuntime != null)
            generatedLevelRuntime.PreserveSpawnedPlayerOnRebuild = true;
    }

    private void Reset()
    {
        CacheComponents();
    }

    private void OnEnable()
    {
        CacheComponents();

        if (generatedLevelRuntime != null)
            generatedLevelRuntime.GeneratedRoomsChanged += HandleGeneratedRoomsChanged;
        if (levelEnemyController != null)
            levelEnemyController.EnemyKilled += HandleEnemyKilled;
    }

    private void OnDisable()
    {
        if (generatedLevelRuntime != null)
            generatedLevelRuntime.GeneratedRoomsChanged -= HandleGeneratedRoomsChanged;
        if (levelEnemyController != null)
            levelEnemyController.EnemyKilled -= HandleEnemyKilled;
    }

    private void Start()
    {
        RefreshHud();

        if (buildFirstLevelOnStart)
            BuildCurrentLevel();
        else if (spawnExitIfMissing && generatedLevelRuntime != null && generatedLevelRuntime.LastGeneratedRooms.Count > 0 && currentExitPortal == null)
            SpawnExitPortal();
    }

    public void GoToNextLevel()
    {
        if (isChangingLevel)
            return;

        isChangingLevel = true;
        currentLevel = Mathf.Max(1, currentLevel + 1);
        ClearLevelRuntimeObjects();
        BuildCurrentLevel();
        if (generatedLevelRuntime != null && !generatedLevelRuntime.TryMoveSpawnedPlayerToStartRoom())
            Debug.LogWarning("[DungeonRunController] Could not move player to the new level start room.");
        RefreshHud();
        isChangingLevel = false;
    }

    private void BuildCurrentLevel()
    {
        if (graphMapBuilder == null)
            return;

        graphMapBuilder.randomSeed = ComputeLevelSeed(CurrentLevel);
        graphMapBuilder.Build();
    }

    private int ComputeLevelSeed(int levelIndex)
    {
        unchecked
        {
            return baseSeed + Mathf.Max(1, levelIndex) * seedStep;
        }
    }

    private void HandleGeneratedRoomsChanged()
    {
        DestroyCurrentExitPortal();
        if (spawnExitIfMissing)
            SpawnExitPortal();
        RefreshHud();
    }

    private void HandleEnemyKilled(GameObject enemyInstance, int baseScore)
    {
        var gained = Mathf.RoundToInt(Mathf.Max(0, baseScore) * ScoreMultiplier);
        score = Mathf.Max(0, score + gained);
        RefreshHud();
    }

    private void SpawnExitPortal()
    {
        if (generatedLevelRuntime == null || generatedLevelRuntime.GraphMapBuilder == null)
            return;

        var rooms = generatedLevelRuntime.LastGeneratedRooms;
        if (rooms == null || rooms.Count == 0)
            return;

        var candidates = new List<GeneratedRoomInfo>();
        for (var i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            if (room == null || room.IsConnector || room.IsStartRoom || room.FloorCells == null || room.FloorCells.Count == 0)
                continue;

            candidates.Add(room);
        }

        if (candidates.Count == 0)
            return;

        var selectedRoom = candidates[roomRng.Next(candidates.Count)];
        var position = PickPortalPosition(selectedRoom);
        currentExitPortal = CreateExitPortal(position);
    }

    private Vector3 PickPortalPosition(GeneratedRoomInfo room)
    {
        var grid = generatedLevelRuntime.GraphMapBuilder.targetGrid;
        if (grid == null)
            return room.SpawnWorldPosition;

        var center = room.CellBounds.center;
        var bestCell = default(Vector3Int);
        var bestSqrDistance = float.MaxValue;
        var foundAny = false;

        foreach (var floorCell in room.FloorCells)
        {
            var sqrDistance = ((Vector3)floorCell - center).sqrMagnitude;
            if (sqrDistance >= bestSqrDistance)
                continue;

            bestSqrDistance = sqrDistance;
            bestCell = floorCell;
            foundAny = true;
        }

        return foundAny ? grid.GetCellCenterWorld(bestCell) : room.SpawnWorldPosition;
    }

    private GameObject CreateExitPortal(Vector3 position)
    {
        GameObject portal;
        if (exitPortalPrefab != null)
        {
            portal = Instantiate(exitPortalPrefab, position, Quaternion.identity, GetRuntimeObjectParent());
        }
        else
        {
            portal = new GameObject("LevelExitPortal");
            portal.transform.SetParent(GetRuntimeObjectParent(), false);
            portal.transform.position = position;

            var spriteRenderer = portal.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = CreateFallbackPortalSprite();
            spriteRenderer.color = new Color(0.25f, 0.9f, 1f, 0.95f);
            spriteRenderer.sortingOrder = 80;
        }

        if (!portal.TryGetComponent<LevelExitPortal>(out var exitPortal))
            exitPortal = portal.AddComponent<LevelExitPortal>();
        exitPortal.Initialize(this);

        if (!portal.TryGetComponent<LevelRuntimeObject>(out _))
            portal.AddComponent<LevelRuntimeObject>();

        var collider2D = portal.GetComponent<BoxCollider2D>();
        if (collider2D != null)
        {
            collider2D.isTrigger = true;
            if (collider2D.size.sqrMagnitude <= 0.0001f)
                collider2D.size = Vector2.one;
        }

        return portal;
    }

    private Transform GetRuntimeObjectParent()
    {
        if (graphMapBuilder != null && graphMapBuilder.targetGrid != null)
            return graphMapBuilder.targetGrid.transform;

        return transform;
    }

    private void DestroyCurrentExitPortal()
    {
        if (currentExitPortal != null)
            Destroy(currentExitPortal);

        currentExitPortal = null;
    }

    private void ClearLevelRuntimeObjects()
    {
        DestroyCurrentExitPortal();

        var runtimeObjects = FindObjectsOfType<LevelRuntimeObject>();
        for (var i = 0; i < runtimeObjects.Length; i++)
        {
            if (runtimeObjects[i] == null || IsSpawnedPlayerObject(runtimeObjects[i].transform))
                continue;

            Destroy(runtimeObjects[i].gameObject);
        }
    }

    private bool IsSpawnedPlayerObject(Transform candidate)
    {
        if (candidate == null || generatedLevelRuntime == null || generatedLevelRuntime.SpawnedPlayerInstance == null)
            return false;

        var playerTransform = generatedLevelRuntime.SpawnedPlayerInstance.transform;
        return candidate == playerTransform || candidate.IsChildOf(playerTransform);
    }

    private void RefreshHud()
    {
        if (runHud == null)
            runHud = GetComponentInChildren<DungeonRunHud>();
        if (runHud == null)
            runHud = gameObject.AddComponent<DungeonRunHud>();

        runHud.SetValues(CurrentLevel, Score, ScoreMultiplier);
    }

    private void CacheComponents()
    {
        if (graphMapBuilder == null)
            graphMapBuilder = GetComponent<GraphMapBuilder>();
        if (generatedLevelRuntime == null)
            generatedLevelRuntime = GetComponent<GeneratedLevelRuntime>();
        if (levelEnemyController == null)
            levelEnemyController = GetComponent<LevelEnemyController>();
        if (runHud == null)
            runHud = GetComponentInChildren<DungeonRunHud>();
    }

    private static Sprite CreateFallbackPortalSprite()
    {
        var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false)
        {
            name = "FallbackLevelExitPortal",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        var clear = new Color(0f, 0f, 0f, 0f);
        var fill = Color.white;
        var center = new Vector2(7.5f, 7.5f);
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                var distance = Vector2.Distance(new Vector2(x, y), center);
                texture.SetPixel(x, y, distance <= 6.5f && distance >= 3.5f ? fill : clear);
            }
        }

        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0f, 0f, 16f, 16f), new Vector2(0.5f, 0.5f), 16f);
    }
}
