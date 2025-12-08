// DoorSocket.cs
using UnityEngine;

public enum DoorSide { North = 0, East = 1, South = 2, West = 3 }

// Маркер точки подключения на границе модуля (комнаты/перехода)
public class DoorSocket : MonoBehaviour
{
    public DoorSide Side;
    [Tooltip("Ширина проёма в тайлах (рекомендуется нечётная, напр. 3)")]
    public int Width = 3;
}

