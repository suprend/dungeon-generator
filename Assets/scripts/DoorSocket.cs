// DoorSocket.cs
using UnityEngine;

public enum DoorSide { North = 0, East = 1, South = 2, West = 3 }

// Маркер точки подключения на границе модуля (комнаты/перехода)
public class DoorSocket : MonoBehaviour
{
    public DoorSide Side;
    [Tooltip("Пока что ширина проёма не поддерживается; всегда 1 тайл.")]
    public int Width = 1;

    private void OnValidate()
    {
        Width = 1;
    }
}
