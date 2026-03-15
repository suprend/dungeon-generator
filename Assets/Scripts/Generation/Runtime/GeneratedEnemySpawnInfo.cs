using System;
using UnityEngine;

[Serializable]
public sealed class GeneratedEnemySpawnInfo
{
    [SerializeField] private string spawnPointId;
    [SerializeField] private string enemyPrefabName;
    [SerializeField] private Vector3 spawnWorldPosition;
    [SerializeField] private float facingDegrees;
    [SerializeField] private bool enabled;
    [SerializeField] private GameObject enemyPrefab;

    public string SpawnPointId => spawnPointId;
    public string EnemyPrefabName => enemyPrefabName;
    public Vector3 SpawnWorldPosition => spawnWorldPosition;
    public float FacingDegrees => facingDegrees;
    public bool Enabled => enabled;
    public GameObject EnemyPrefab => enemyPrefab;

    public GeneratedEnemySpawnInfo(
        string spawnPointId,
        GameObject enemyPrefab,
        Vector3 spawnWorldPosition,
        float facingDegrees,
        bool enabled)
    {
        this.spawnPointId = spawnPointId ?? string.Empty;
        this.enemyPrefab = enemyPrefab;
        enemyPrefabName = enemyPrefab != null ? enemyPrefab.name : string.Empty;
        this.spawnWorldPosition = spawnWorldPosition;
        this.facingDegrees = facingDegrees;
        this.enabled = enabled;
    }
}
