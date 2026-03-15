using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class RoomEnemySpawnConfig : MonoBehaviour
{
    [SerializeField] private bool enableEnemySpawns = true;
    [SerializeField] private bool excludeFromDefaultSpawning;
    [SerializeField] private List<EnemySpawnPoint> spawnPoints = new();
    [SerializeField] private List<EnemySpawnLayout> layouts = new();

    public bool EnableEnemySpawns => enableEnemySpawns;
    public bool ExcludeFromDefaultSpawning => excludeFromDefaultSpawning;
    public IReadOnlyList<EnemySpawnPoint> SpawnPoints => spawnPoints;
    public IReadOnlyList<EnemySpawnLayout> Layouts => layouts;

    private void OnValidate()
    {
        spawnPoints ??= new List<EnemySpawnPoint>();
        layouts ??= new List<EnemySpawnLayout>();

        for (var i = 0; i < spawnPoints.Count; i++)
        {
            var point = spawnPoints[i];
            if (point == null)
            {
                spawnPoints[i] = new EnemySpawnPoint();
                point = spawnPoints[i];
            }

            if (string.IsNullOrWhiteSpace(point.Id) && point.Point != null)
                point.Id = point.Point.name;
        }

        for (var i = 0; i < layouts.Count; i++)
        {
            var layout = layouts[i];
            if (layout == null)
            {
                layouts[i] = new EnemySpawnLayout();
                layout = layouts[i];
            }

            if (string.IsNullOrWhiteSpace(layout.Id))
                layout.Id = $"layout_{i + 1}";
        }
    }

    public bool TryResolveSpawnPoint(string spawnPointId, out EnemySpawnPoint point)
    {
        point = null;
        if (string.IsNullOrWhiteSpace(spawnPointId) || spawnPoints == null)
            return false;

        for (var i = 0; i < spawnPoints.Count; i++)
        {
            var candidate = spawnPoints[i];
            if (candidate == null || !candidate.Enabled)
                continue;

            if (string.Equals(candidate.GetResolvedId(), spawnPointId, StringComparison.Ordinal))
            {
                point = candidate;
                return true;
            }
        }

        return false;
    }

    public EnemySpawnLayout PickRandomLayout(System.Random rng)
    {
        if (!enableEnemySpawns || excludeFromDefaultSpawning || layouts == null || layouts.Count == 0)
            return null;

        var totalWeight = 0;
        for (var i = 0; i < layouts.Count; i++)
        {
            var layout = layouts[i];
            if (layout == null || !layout.IsEnabled)
                continue;
            totalWeight += Mathf.Max(1, layout.Weight);
        }

        if (totalWeight <= 0)
            return null;

        var roll = rng != null ? rng.Next(totalWeight) : UnityEngine.Random.Range(0, totalWeight);
        for (var i = 0; i < layouts.Count; i++)
        {
            var layout = layouts[i];
            if (layout == null || !layout.IsEnabled)
                continue;

            roll -= Mathf.Max(1, layout.Weight);
            if (roll < 0)
                return layout;
        }

        return null;
    }
}

[Serializable]
public sealed class EnemySpawnPoint
{
    [SerializeField] private string id;
    [SerializeField] private Transform point;
    [SerializeField] private float facingDegrees;
    [SerializeField] private bool enabled = true;

    public string Id
    {
        get => id;
        set => id = value;
    }

    public Transform Point => point;
    public float FacingDegrees => facingDegrees;
    public bool Enabled => enabled;

    public string GetResolvedId()
    {
        return !string.IsNullOrWhiteSpace(id) ? id : point != null ? point.name : string.Empty;
    }
}

[Serializable]
public sealed class EnemySpawnLayout
{
    [SerializeField] private string id;
    [SerializeField] private int weight = 1;
    [SerializeField] private bool isEnabled = true;
    [SerializeField] private List<EnemySpawnEntry> entries = new();

    public string Id
    {
        get => id;
        set => id = value;
    }

    public int Weight => Mathf.Max(1, weight);
    public bool IsEnabled => isEnabled;
    public IReadOnlyList<EnemySpawnEntry> Entries => entries;
}

[Serializable]
public sealed class EnemySpawnEntry
{
    [SerializeField] private string spawnPointId;
    [SerializeField] private GameObject enemyPrefab;

    public string SpawnPointId => spawnPointId;
    public GameObject EnemyPrefab => enemyPrefab;
}
