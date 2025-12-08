// ModuleMetaBase.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Базовый мета-компонент для модулей: комнаты и переходы
public abstract class ModuleMetaBase : MonoBehaviour
{
    [Header("Socket points on module boundary")]
    public DoorSocket[] Sockets;

    // Трекинг занятости по конкретным сокетам (а не по стороне)
    private readonly HashSet<DoorSocket> usedSockets = new();

    public bool Has(DoorSide side) => Sockets != null && Sockets.Any(s => s && s.Side == side);
    public DoorSocket Get(DoorSide side) => Sockets?.FirstOrDefault(s => s && s.Side == side);

    public bool IsUsed(DoorSocket socket) => socket != null && usedSockets.Contains(socket);

    public bool TryUse(DoorSocket socket)
    {
        if (socket == null) return false;
        if (!Sockets.Contains(socket)) return false;
        if (usedSockets.Contains(socket)) return false;
        usedSockets.Add(socket);
        return true;
    }

    // Вспомогательные совместимые методы по стороне (используют первый попавшийся сокет на стороне)
    public bool IsUsed(DoorSide side)
    {
        var s = Get(side);
        return s != null && usedSockets.Contains(s);
    }

    public bool TryUse(DoorSide side)
    {
        var s = Get(side);
        return TryUse(s);
    }

    // Сброс использованных сокетов (на случай реюза префаба в редакторе)
    public void ResetUsed()
    {
        usedSockets.Clear();
    }
}
