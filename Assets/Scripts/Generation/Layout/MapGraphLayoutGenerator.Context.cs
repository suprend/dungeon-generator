// Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Context.cs
using System.Collections.Generic;
using UnityEngine;

public sealed partial class MapGraphLayoutGenerator
{
    private sealed class LayoutContext
    {
        public List<MapGraphChainBuilder.Chain> OrderedChains { get; set; }
        public ShapeLibrary ShapeLibrary { get; set; }
        public ConfigurationSpaceLibrary ConfigSpaceLibrary { get; set; }
        public Dictionary<string, List<GameObject>> RoomPrefabLookup { get; set; }
        public HashSet<GameObject> ConnectorPrefabs { get; set; }
        public Dictionary<string, HashSet<string>> NeighborLookup { get; set; }
        public Dictionary<string, int> NodeIndexById { get; set; }
        public string[] NodeIdByIndex { get; set; }
        public int[][] NeighborIndicesByIndex { get; set; }
    }
}
