// Assets/scripts/Generation/Graph/MapGraphLevelSolver.AssignmentSolver.cs
using System;
using System.Collections.Generic;

public partial class MapGraphLevelSolver
{
    private bool TrySolveChain(int chainIndex)
    {
        if (chainIndex >= orderedChains.Count)
            return true;

        return TryAssignNodeInChain(chainIndex, 0);
    }

    private bool TryAssignNodeInChain(int chainIndex, int nodeIndex)
    {
        var chain = orderedChains[chainIndex];
        if (nodeIndex >= chain.Nodes.Count)
        {
            var depthBeforeEdges = state.Depth;
            if (!TryAssignAvailableEdgesForChain(chain, chainIndex))
            {
                state.RollbackToDepth(depthBeforeEdges);
                return false;
            }

            if (TrySolveChain(chainIndex + 1))
                return true;

            state.RollbackToDepth(depthBeforeEdges);
            return false;
        }

        var node = chain.Nodes[nodeIndex];
        if (node == null || string.IsNullOrEmpty(node.id) || state.NodeRooms.ContainsKey(node.id))
            return TryAssignNodeInChain(chainIndex, nodeIndex + 1);

        var roomCandidates = new List<RoomTypeAsset>(GatherRoomCandidates(node));
        roomCandidates.Shuffle(rng);
        var depthBeforeNode = state.Depth;

        foreach (var candidate in roomCandidates)
        {
            if (!state.PushNode(node.id, candidate, chainIndex))
                continue;

            if (!TryAssignAvailableEdgesForChain(chain, chainIndex))
            {
                state.RollbackToDepth(depthBeforeNode);
                continue;
            }

            if (TryAssignNodeInChain(chainIndex, nodeIndex + 1))
                return true;

            state.RollbackToDepth(depthBeforeNode);
        }

        return false;
    }

    private IEnumerable<RoomTypeAsset> GatherRoomCandidates(MapGraphAsset.NodeData node)
    {
        if (node.roomType != null)
            return new[] { node.roomType };
        if (graphAsset.DefaultRoomType != null)
            return new[] { graphAsset.DefaultRoomType };
        return Array.Empty<RoomTypeAsset>();
    }

    private bool TryAssignAvailableEdgesForChain(MapGraphChainBuilder.Chain chain, int chainIndex)
    {
        if (chain?.Edges == null)
            return true;

        foreach (var edge in chain.Edges)
        {
            if (edge == null || string.IsNullOrEmpty(edge.fromNodeId) || string.IsNullOrEmpty(edge.toNodeId))
                continue;

            if (!state.NodeRooms.ContainsKey(edge.fromNodeId) || !state.NodeRooms.ContainsKey(edge.toNodeId))
                continue;

            var key = MapGraphKey.NormalizeKey(edge.fromNodeId, edge.toNodeId);
            if (state.EdgeConnections.ContainsKey(key))
                continue;

            var connection = edge.connectionType != null ? edge.connectionType : graphAsset.DefaultConnectionType;
            if (connection == null)
                return false;

            if (!state.PushEdge(key, connection, chainIndex))
                return false;
        }

        return true;
    }
}

