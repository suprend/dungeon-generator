using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public sealed class PlayerPartyController : MonoBehaviour
{
    [SerializeField] private KeyCode previousCharacterKey = KeyCode.Q;
    [SerializeField] private KeyCode nextCharacterKey = KeyCode.E;
    [SerializeField] private float companionTrailSpacing = 1.1f;
    [SerializeField] private float trailPointSpacing = 0.25f;
    [SerializeField] private float companionTeleportSeparation = 0.8f;
    [SerializeField] private float companionTeleportSearchRadius = 3f;
    [SerializeField] private float companionTeleportNavMeshSampleRadius = 0.45f;
    private bool allowCompanionTeleportToExploredRooms;

    private readonly List<PartyMemberRuntime> members = new();
    private readonly HashSet<string> exploredRoomNodeIds = new();
    private readonly List<Vector2> activeTrailPoints = new();
    private int activeMemberIndex;
    private GeneratedLevelRuntime generatedLevelRuntime;
    private PlayerRoomTracker playerRoomTracker;
    private PartyMemberRuntime trailOwner;

    public PartyMemberRuntime ActiveMember =>
        activeMemberIndex >= 0 && activeMemberIndex < members.Count
            ? members[activeMemberIndex]
            : null;
    public bool AllowCompanionTeleportToExploredRooms => allowCompanionTeleportToExploredRooms;

    public void Initialize(IReadOnlyList<GameObject> memberInstances)
    {
        members.Clear();
        exploredRoomNodeIds.Clear();
        activeMemberIndex = 0;

        if (memberInstances != null)
        {
            for (var i = 0; i < memberInstances.Count; i++)
            {
                var memberObject = memberInstances[i];
                if (memberObject == null)
                    continue;

                var member = memberObject.GetComponent<PartyMemberRuntime>();
                if (member == null)
                {
                    Debug.LogError($"[PlayerPartyController] Party member prefab '{memberObject.name}' is missing {nameof(PartyMemberRuntime)}.", memberObject);
                    continue;
                }

                member.Initialize(this);
                members.Add(member);
            }
        }

        activeMemberIndex = FindFirstLivingMemberIndex();
        if (activeMemberIndex < 0)
            activeMemberIndex = 0;

        ResetActiveTrail();
        ApplyPartyState();
        SyncAnchorTransform();
        RefreshExploredRooms();
    }

    private void Update()
    {
        CacheRuntimeReferences();
        RefreshExploredRooms();

        if (members.Count == 0)
            return;

        if (Input.GetKeyDown(previousCharacterKey))
            SelectRelative(-1);

        if (Input.GetKeyDown(nextCharacterKey))
            SelectRelative(1);

        ApplyPartyState();
        SyncAnchorTransform();
    }

    private void LateUpdate()
    {
        SyncAnchorTransform();
    }

    private void OnDestroy()
    {
        AttachTracker(null);

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i] != null)
                Destroy(members[i].gameObject);
        }

        members.Clear();
    }

    public void HandleMemberDeath(PartyMemberRuntime member)
    {
        if (member == null)
            return;

        if (!HasLivingMembers())
        {
            PlayerDeathRestartMenu.Show();
            return;
        }

        if (member == ActiveMember)
            SelectRelative(1);
        else
            ApplyPartyState();
    }

    public static Vector2 GetFormationOffset(int index)
    {
        return index switch
        {
            0 => new Vector2(0f, 0f),
            1 => new Vector2(-1.5f, -1.25f),
            2 => new Vector2(1.5f, -1.25f),
            3 => new Vector2(0f, -2.2f),
            _ => Vector2.zero
        };
    }

    public static Vector2 GetSpawnOffset(int index)
    {
        return index switch
        {
            0 => new Vector2(0f, 0f),
            1 => new Vector2(-0.8f, -0.65f),
            2 => new Vector2(0.8f, -0.65f),
            3 => new Vector2(0f, -1.2f),
            _ => Vector2.zero
        };
    }

    public void SetGeneratedLevelRuntime(GeneratedLevelRuntime runtime)
    {
        generatedLevelRuntime = runtime;
        CacheRuntimeReferences();
        RefreshExploredRooms();
    }

    public void SetAllowCompanionTeleportToExploredRooms(bool allowTeleport)
    {
        allowCompanionTeleportToExploredRooms = allowTeleport;
    }

    public void TeleportParty(Vector3 spawnPosition)
    {
        RemoveDeadMembers();

        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member == null)
                continue;

            member.TeleportTo(spawnPosition + (Vector3)GetSpawnOffset(i));
        }

        ResetActiveTrail();
        ApplyPartyState();
        transform.position = spawnPosition;
        RefreshExploredRooms();
    }

    public int TeleportCompanionsToActiveMember(GeneratedRoomInfo requiredRoom = null)
    {
        if (ActiveMember == null || !ActiveMember.IsAlive)
            activeMemberIndex = FindFirstLivingMemberIndex();

        var activeMember = ActiveMember;
        if (activeMember == null)
            return 0;

        var anchor = activeMember.transform.position;
        var occupiedPositions = new List<Vector3> { anchor };
        var teleportedCount = 0;
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member == null || member == activeMember || !member.IsAlive)
                continue;

            if (!TryGetCompanionTeleportPosition(anchor, requiredRoom, occupiedPositions, i, out var teleportPosition))
                teleportPosition = anchor;

            member.TeleportTo(teleportPosition);
            occupiedPositions.Add(teleportPosition);
            teleportedCount++;
        }

        ResetActiveTrail();
        ApplyPartyState();
        SyncAnchorTransform();
        RefreshExploredRooms();
        return teleportedCount;
    }

    private bool TryGetCompanionTeleportPosition(
        Vector3 anchor,
        GeneratedRoomInfo requiredRoom,
        List<Vector3> occupiedPositions,
        int memberIndex,
        out Vector3 teleportPosition)
    {
        var preferredOffset = GetSpawnOffset(memberIndex);
        if (preferredOffset.sqrMagnitude <= 0.0001f)
            preferredOffset = GetFormationOffset(memberIndex);

        if (TryAcceptTeleportCandidate(anchor + (Vector3)preferredOffset, requiredRoom, occupiedPositions, out teleportPosition))
            return true;

        var maxRadius = Mathf.Max(0.5f, companionTeleportSearchRadius);
        var minRadius = Mathf.Max(0.35f, companionTeleportSeparation);
        var radiusStep = Mathf.Max(0.25f, companionTeleportSeparation * 0.5f);
        var angleOffset = memberIndex * 47f;

        for (var radius = minRadius; radius <= maxRadius; radius += radiusStep)
        {
            var samples = Mathf.Max(8, Mathf.CeilToInt(radius * 8f));
            for (var sample = 0; sample < samples; sample++)
            {
                var angle = (angleOffset + sample * (360f / samples)) * Mathf.Deg2Rad;
                var candidate = anchor + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                if (TryAcceptTeleportCandidate(candidate, requiredRoom, occupiedPositions, out teleportPosition))
                    return true;
            }
        }

        teleportPosition = anchor;
        return false;
    }

    private bool TryAcceptTeleportCandidate(
        Vector3 candidate,
        GeneratedRoomInfo requiredRoom,
        List<Vector3> occupiedPositions,
        out Vector3 teleportPosition)
    {
        teleportPosition = candidate;

        if (!NavMesh.SamplePosition(
                candidate,
                out var hit,
                Mathf.Max(0.05f, companionTeleportNavMeshSampleRadius),
                NavMesh.AllAreas))
        {
            return false;
        }

        teleportPosition = hit.position;
        teleportPosition.z = candidate.z;

        if (!IsTeleportPositionInRequiredRoom(teleportPosition, requiredRoom))
            return false;

        var minSqrDistance = Mathf.Max(0.05f, companionTeleportSeparation);
        minSqrDistance *= minSqrDistance;
        for (var i = 0; i < occupiedPositions.Count; i++)
        {
            if (((Vector2)occupiedPositions[i] - (Vector2)teleportPosition).sqrMagnitude < minSqrDistance)
                return false;
        }

        return true;
    }

    private bool IsTeleportPositionInRequiredRoom(Vector3 teleportPosition, GeneratedRoomInfo requiredRoom)
    {
        if (requiredRoom == null)
            return true;
        if (generatedLevelRuntime == null)
            return false;
        if (!generatedLevelRuntime.TryGetRoomAtWorldPosition(teleportPosition, out var roomInfo) || roomInfo == null)
            return false;

        return string.Equals(roomInfo.NodeId, requiredRoom.NodeId, System.StringComparison.Ordinal);
    }

    public bool TryGetSafeTeleportPosition(Vector3 desiredWorldPosition, out Vector3 safeWorldPosition)
    {
        safeWorldPosition = desiredWorldPosition;

        CacheRuntimeReferences();
        RefreshExploredRooms();

        if (generatedLevelRuntime == null)
            return false;
        if (!generatedLevelRuntime.TryGetRoomAtWorldPosition(desiredWorldPosition, out var roomInfo) || roomInfo == null)
            return false;
        if (string.IsNullOrEmpty(roomInfo.NodeId) || !exploredRoomNodeIds.Contains(roomInfo.NodeId))
            return false;

        return generatedLevelRuntime.TryGetNearestFloorWorldPosition(roomInfo, desiredWorldPosition, out safeWorldPosition);
    }

    private void SelectRelative(int direction)
    {
        if (members.Count == 0)
            return;

        for (var step = 1; step <= members.Count; step++)
        {
            var candidateIndex = (activeMemberIndex + direction * step + members.Count) % members.Count;
            var candidate = members[candidateIndex];
            if (candidate == null || !candidate.IsAlive)
                continue;

            activeMemberIndex = candidateIndex;
            ResetActiveTrail();
            ApplyPartyState();
            SyncAnchorTransform();
            return;
        }
    }

    private void ApplyPartyState()
    {
        var activeMember = ActiveMember;

        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member == null)
                continue;

            var isActive = member == activeMember && member.IsAlive;
            member.SetPlayerControlled(isActive);
        }

        if (activeMember == null || !activeMember.IsAlive)
            return;

        UpdateActiveTrail(activeMember);

        for (var step = 1; step < members.Count; step++)
        {
            var memberIndex = (activeMemberIndex + step) % members.Count;
            var member = members[memberIndex];
            if (member == null || !member.IsAlive)
                continue;

            var targetPosition = GetTrailPosition(step * companionTrailSpacing);
            member.TickCompanionAI(targetPosition);
        }
    }

    private bool HasLivingMembers()
    {
        return FindFirstLivingMemberIndex() >= 0;
    }

    private void RemoveDeadMembers()
    {
        var activeBeforeCleanup = ActiveMember;

        for (var i = members.Count - 1; i >= 0; i--)
        {
            var member = members[i];
            if (member != null && member.IsAlive)
                continue;

            members.RemoveAt(i);
            if (member != null)
                Destroy(member.gameObject);
        }

        if (activeBeforeCleanup != null && activeBeforeCleanup.IsAlive)
            activeMemberIndex = members.IndexOf(activeBeforeCleanup);
        if (activeMemberIndex < 0 || activeMemberIndex >= members.Count)
            activeMemberIndex = FindFirstLivingMemberIndex();
    }

    private int FindFirstLivingMemberIndex()
    {
        for (var i = 0; i < members.Count; i++)
        {
            if (members[i] != null && members[i].IsAlive)
                return i;
        }

        return -1;
    }

    private void SyncAnchorTransform()
    {
        var activeMember = ActiveMember;
        if (activeMember == null)
            return;

        transform.position = activeMember.transform.position;
    }

    private void CacheRuntimeReferences()
    {
        if (generatedLevelRuntime == null)
            generatedLevelRuntime = GetComponent<GeneratedLevelRuntime>();

        if (playerRoomTracker == null)
            AttachTracker(GetComponent<PlayerRoomTracker>());
    }

    private void AttachTracker(PlayerRoomTracker tracker)
    {
        if (playerRoomTracker != null)
            playerRoomTracker.EnteredRoom -= HandleEnteredRoom;

        playerRoomTracker = tracker;

        if (playerRoomTracker != null)
            playerRoomTracker.EnteredRoom += HandleEnteredRoom;
    }

    private void HandleEnteredRoom(GeneratedRoomInfo roomInfo)
    {
        RegisterExploredRoom(roomInfo);
    }

    private void RefreshExploredRooms()
    {
        if (playerRoomTracker == null)
            return;

        RegisterExploredRoom(playerRoomTracker.CurrentRoom);
        RegisterExploredRoom(playerRoomTracker.LastKnownRoom);
    }

    private void RegisterExploredRoom(GeneratedRoomInfo roomInfo)
    {
        if (roomInfo == null || string.IsNullOrEmpty(roomInfo.NodeId))
            return;

        exploredRoomNodeIds.Add(roomInfo.NodeId);
    }

    private void ResetActiveTrail()
    {
        trailOwner = null;
        activeTrailPoints.Clear();
    }

    private void UpdateActiveTrail(PartyMemberRuntime activeMember)
    {
        if (activeMember == null)
        {
            ResetActiveTrail();
            return;
        }

        var activePosition = (Vector2)activeMember.transform.position;
        if (trailOwner != activeMember)
        {
            trailOwner = activeMember;
            activeTrailPoints.Clear();
            activeTrailPoints.Add(activePosition);
            return;
        }

        if (activeTrailPoints.Count == 0)
        {
            activeTrailPoints.Add(activePosition);
            return;
        }

        var minPointSpacing = Mathf.Max(0.05f, trailPointSpacing);
        if (Vector2.Distance(activeTrailPoints[0], activePosition) >= minPointSpacing)
            activeTrailPoints.Insert(0, activePosition);
        else
            activeTrailPoints[0] = activePosition;

        var requiredTrailLength = Mathf.Max(minPointSpacing, companionTrailSpacing) * Mathf.Max(2, members.Count + 1);
        var accumulatedDistance = 0f;

        for (var i = 1; i < activeTrailPoints.Count; i++)
        {
            accumulatedDistance += Vector2.Distance(activeTrailPoints[i - 1], activeTrailPoints[i]);
            if (accumulatedDistance <= requiredTrailLength)
                continue;

            activeTrailPoints.RemoveRange(i, activeTrailPoints.Count - i);
            break;
        }
    }

    private Vector2 GetTrailPosition(float trailingDistance)
    {
        var activeMember = ActiveMember;
        if (activeMember == null)
            return Vector2.zero;

        if (activeTrailPoints.Count == 0)
            return activeMember.transform.position;

        var remainingDistance = Mathf.Max(0f, trailingDistance);

        for (var i = 1; i < activeTrailPoints.Count; i++)
        {
            var from = activeTrailPoints[i - 1];
            var to = activeTrailPoints[i];
            var segmentLength = Vector2.Distance(from, to);

            if (segmentLength <= 0.0001f)
                continue;

            if (remainingDistance <= segmentLength)
                return Vector2.Lerp(from, to, remainingDistance / segmentLength);

            remainingDistance -= segmentLength;
        }

        return activeTrailPoints[activeTrailPoints.Count - 1];
    }
}
