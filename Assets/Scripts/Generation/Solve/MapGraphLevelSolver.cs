// Assets/Scripts/Generation/Solve/MapGraphLevelSolver.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Solver that iterates faces in ascending size and assigns rooms/connections with backtracking.
/// </summary>
public partial class MapGraphLevelSolver
{
    private MapGraphAsset graphAsset;
    private readonly List<MapGraphChainBuilder.Chain> orderedChains = new();
    private readonly MapGraphAssignmentState state = new();
    private readonly System.Random rng = new();
    private ShapeLibrary shapeLibrary;
    private ConfigurationSpaceLibrary configSpaceLibrary;

    public MapGraphLevelSolver(MapGraphAsset graphAsset)
    {
        this.graphAsset = graphAsset;
    }

    public bool TrySolve(out IReadOnlyDictionary<string, RoomTypeAsset> nodeAssignments, out IReadOnlyDictionary<(string,string), ConnectionTypeAsset> edgeAssignments, out string error)
    {
        nodeAssignments = null;
        edgeAssignments = null;
        error = null;
        if (graphAsset == null)
        {
            error = "Graph asset is null.";
            return false;
        }

        
        if (!MapGraphFaceBuilder.TryBuildFaces(graphAsset, out var faces, out error))
            return false;
        if (!MapGraphChainBuilder.TryBuildChains(graphAsset, faces, out var chains, out error))
            return false;

        orderedChains.Clear();
        orderedChains.AddRange(chains);
        state.Clear();

        if (!TrySolveChain(0))
        {
            error = "Unable to place rooms for provided graph.";
            return false;
        }

        nodeAssignments = new Dictionary<string, RoomTypeAsset>(state.NodeRooms);
        edgeAssignments = new Dictionary<(string,string), ConnectionTypeAsset>(state.EdgeConnections);
        return true;
    }
}
