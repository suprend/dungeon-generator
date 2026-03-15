using UnityEngine;
using UnityEngine.Events;
using System;

public sealed class PlayerRoomTracker : MonoBehaviour
{
    [System.Serializable]
    public sealed class RoomChangedEvent : UnityEvent<string> { }

    [SerializeField] private GeneratedLevelRuntime generatedLevelRuntime;
    [SerializeField] private float pollIntervalSeconds = 0.1f;

    [SerializeField] private string currentRoomNodeId;
    [SerializeField] private string previousRoomNodeId;
    [SerializeField] private string lastKnownRoomNodeId;

    [SerializeField] private RoomChangedEvent onEnteredRoom = new();
    [SerializeField] private RoomChangedEvent onExitedRoom = new();

    private float nextPollTime;
    private GeneratedRoomInfo currentRoom;
    private GeneratedRoomInfo previousRoom;
    private GeneratedRoomInfo lastKnownRoom;

    public string CurrentRoomNodeId => currentRoomNodeId;
    public string PreviousRoomNodeId => previousRoomNodeId;
    public string LastKnownRoomNodeId => lastKnownRoomNodeId;
    public GeneratedRoomInfo CurrentRoom => currentRoom;
    public GeneratedRoomInfo PreviousRoom => previousRoom;
    public GeneratedRoomInfo LastKnownRoom => lastKnownRoom;
    public RoomChangedEvent OnEnteredRoom => onEnteredRoom;
    public RoomChangedEvent OnExitedRoom => onExitedRoom;
    public event Action<GeneratedRoomInfo> EnteredRoom;
    public event Action<GeneratedRoomInfo> ExitedRoom;

    private void OnEnable()
    {
        nextPollTime = 0f;
        RefreshRoomNow();
    }

    public void SetGeneratedLevelRuntime(GeneratedLevelRuntime runtime)
    {
        generatedLevelRuntime = runtime;
        RefreshRoomNow();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextPollTime)
            return;

        nextPollTime = Time.unscaledTime + Mathf.Max(0.01f, pollIntervalSeconds);
        RefreshRoomNow();
    }

    public void RefreshRoomNow()
    {
        if (generatedLevelRuntime == null)
            return;

        generatedLevelRuntime.TryGetRoomAtWorldPosition(transform.position, out var resolvedRoom);
        if (ReferenceEquals(resolvedRoom, currentRoom))
            return;

        previousRoom = currentRoom;
        currentRoom = resolvedRoom;
        if (currentRoom != null)
            lastKnownRoom = currentRoom;

        previousRoomNodeId = previousRoom?.NodeId ?? string.Empty;
        currentRoomNodeId = currentRoom?.NodeId ?? string.Empty;
        lastKnownRoomNodeId = lastKnownRoom?.NodeId ?? string.Empty;

        if (previousRoom != null)
        {
            onExitedRoom.Invoke(previousRoom.NodeId);
            ExitedRoom?.Invoke(previousRoom);
        }
        if (currentRoom != null)
        {
            onEnteredRoom.Invoke(currentRoom.NodeId);
            EnteredRoom?.Invoke(currentRoom);
        }
    }
}
