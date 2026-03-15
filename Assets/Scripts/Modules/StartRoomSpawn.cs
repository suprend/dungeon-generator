using UnityEngine;

public sealed class StartRoomSpawn : MonoBehaviour
{
    [SerializeField] private Transform spawnPoint;

    public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;
}
