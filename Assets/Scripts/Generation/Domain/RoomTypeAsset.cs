// Assets/Scripts/Generation/Domain/RoomTypeAsset.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines a logical room type (shop, boss, etc.) with a set of prefab variants.
/// </summary>
[CreateAssetMenu(menuName = "Generation/Room Type", fileName = "RoomType")]
public class RoomTypeAsset : ScriptableObject
{
    [Tooltip("Possible prefab variants for this room type.")]
    public List<GameObject> prefabs = new();

    [Tooltip("Optional metadata for spawning logic.")]
    public string notes;

    public GameObject PickRandomPrefab(System.Random rng)
    {
        if (prefabs == null || prefabs.Count == 0) return null;
        var index = rng != null ? rng.Next(prefabs.Count) : Random.Range(0, prefabs.Count);
        return prefabs[index];
    }
}
