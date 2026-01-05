// Assets/scripts/Generation/Graph/MapGraphAssignmentState.cs
using System.Collections.Generic;

internal sealed class MapGraphAssignmentState
{
    private readonly Stack<Frame> frames = new();
    public Dictionary<string, RoomTypeAsset> NodeRooms { get; } = new();
    public Dictionary<(string, string), ConnectionTypeAsset> EdgeConnections { get; } = new();

    public int Depth => frames.Count;

    public void Clear()
    {
        frames.Clear();
        NodeRooms.Clear();
        EdgeConnections.Clear();
    }

    public bool PushNode(string nodeId, RoomTypeAsset room, int faceIndex)
    {
        if (NodeRooms.ContainsKey(nodeId))
            return NodeRooms[nodeId] == room;

        NodeRooms[nodeId] = room;
        frames.Push(Frame.ForNode(faceIndex, nodeId, room));
        return true;
    }

    public bool PushEdge((string, string) edgeKey, ConnectionTypeAsset connection, int faceIndex)
    {
        if (EdgeConnections.ContainsKey(edgeKey))
            return EdgeConnections[edgeKey] == connection;

        EdgeConnections[edgeKey] = connection;
        frames.Push(Frame.ForEdge(faceIndex, edgeKey, connection));
        return true;
    }

    public void RollbackToDepth(int depth)
    {
        while (frames.Count > depth)
        {
            var frame = frames.Pop();
            switch (frame.Type)
            {
                case FrameType.Node:
                    if (frame.NodeId != null)
                        NodeRooms.Remove(frame.NodeId);
                    break;
                case FrameType.Edge:
                    EdgeConnections.Remove(frame.EdgeKey);
                    break;
            }
        }
    }

    private readonly struct Frame
    {
        public readonly int FaceIndex;
        public readonly FrameType Type;
        public readonly string NodeId;
        public readonly RoomTypeAsset Room;
        public readonly (string, string) EdgeKey;
        public readonly ConnectionTypeAsset Connection;

        private Frame(int faceIndex, FrameType type, string nodeId, RoomTypeAsset room, (string, string) edgeKey, ConnectionTypeAsset connection)
        {
            FaceIndex = faceIndex;
            Type = type;
            NodeId = nodeId;
            Room = room;
            EdgeKey = edgeKey;
            Connection = connection;
        }

        public static Frame ForNode(int faceIndex, string nodeId, RoomTypeAsset room) =>
            new(faceIndex, FrameType.Node, nodeId, room, default, null);

        public static Frame ForEdge(int faceIndex, (string, string) edgeKey, ConnectionTypeAsset connection) =>
            new(faceIndex, FrameType.Edge, null, null, edgeKey, connection);
    }

    private enum FrameType
    {
        Node,
        Edge
    }
}

