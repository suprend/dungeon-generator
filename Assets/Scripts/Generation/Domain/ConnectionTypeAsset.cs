// Assets/scripts/Generation/ConnectionTypeAsset.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Describes a type of connection/connector between rooms with available prefab variants.
/// </summary>
[CreateAssetMenu(menuName = "Generation/Connection Type", fileName = "ConnectionType")]
public class ConnectionTypeAsset : ScriptableObject
{
    [Tooltip("Possible connector prefabs for this connection type.")]
    public List<GameObject> prefabs = new();

    [Tooltip("Default doorway width (cells) this connector expects.")]
    public int defaultWidth = 1;

    [Tooltip("Optional metadata, e.g., special rules or tags.")]
    public string notes;

    public GameObject PickRandomPrefab(System.Random rng)
    {
        if (prefabs == null || prefabs.Count == 0) return null;
        var index = rng != null ? rng.Next(prefabs.Count) : Random.Range(0, prefabs.Count);
        return prefabs[index];
    }
}
