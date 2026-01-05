// Assets/Scripts/Generation/Utils/DoorSideExtensions.cs
using System;
using UnityEngine;

public static class DoorSideExtensions
{
    public static DoorSide Opposite(this DoorSide side)
    {
        return side switch
        {
            DoorSide.North => DoorSide.South,
            DoorSide.South => DoorSide.North,
            DoorSide.East => DoorSide.West,
            DoorSide.West => DoorSide.East,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unexpected door side value.")
        };
    }

    public static Vector3Int PerpendicularAxis(this DoorSide side)
    {
        return side switch
        {
            DoorSide.North or DoorSide.South => new Vector3Int(1, 0, 0),
            DoorSide.East or DoorSide.West => new Vector3Int(0, 1, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unexpected door side value.")
        };
    }

    public static Vector2Int Forward(this DoorSide side)
    {
        return side switch
        {
            DoorSide.North => new Vector2Int(0, 1),
            DoorSide.South => new Vector2Int(0, -1),
            DoorSide.East => new Vector2Int(1, 0),
            DoorSide.West => new Vector2Int(-1, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unexpected door side value.")
        };
    }
}
