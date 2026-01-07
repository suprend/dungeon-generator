// Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Profiler.cs
using UnityEngine.Profiling;

public sealed partial class MapGraphLayoutGenerator
{
    // Unity Profiler sample names (constant strings => no per-call allocations).
    private const string S_TryGenerate = "Layout.TryGenerate";
    private const string S_AddChain = "Layout.AddChain";
    private const string S_AddChainSmall = "Layout.AddChainSmall";
    private const string S_AddChain_InitLayout = "Layout.AddChain.InitLayout";
    private const string S_InitLayout_CloneRooms = "Layout.InitLayout.CloneRooms";
    private const string S_InitLayout_CloneCache = "Layout.InitLayout.CloneCache";
    private const string S_InitLayout_BuildOrder = "Layout.InitLayout.BuildOrder";
    private const string S_InitLayout_NodeLoop = "Layout.InitLayout.NodeLoop";
    private const string S_InitLayout_GetPrefabs = "Layout.InitLayout.GetPrefabs";
    private const string S_InitLayout_Shuffle = "Layout.InitLayout.Shuffle";
    private const string S_InitLayout_TryGetShape = "Layout.InitLayout.TryGetShape";
    private const string S_InitLayout_FindCandidates = "Layout.InitLayout.FindCandidates";
    private const string S_InitLayout_ScoreCandidates = "Layout.InitLayout.ScoreCandidates";
    private const string S_InitLayout_AddPlacement = "Layout.InitLayout.AddPlacement";
    private const string S_ComputeEnergyIfAdded = "Layout.ComputeEnergyIfAdded";
    private const string S_ComputeEnergyIfAdded_Overlaps = "Layout.ComputeEnergyIfAdded.Overlaps";
    private const string S_ComputeEnergyIfAdded_EdgeDistances = "Layout.ComputeEnergyIfAdded.EdgeDistances";
    private const string S_AddChain_BuildEnergyCache = "Layout.AddChain.BuildEnergyCache";
    private const string S_AddChain_SA = "Layout.AddChain.SA";
    private const string S_AddChain_OutputCheck = "Layout.AddChain.OutputCheck";
    private const string S_AddChain_Snapshot = "Layout.AddChain.Snapshot";
    private const string S_AddChain_AcceptReject = "Layout.AddChain.AcceptReject";
    private const string S_TryPerturbInPlace = "Layout.TryPerturbInPlace";
    private const string S_Perturb_SelectTarget = "Layout.Perturb.SelectTarget";
    private const string S_Perturb_SelectPrefab = "Layout.Perturb.SelectPrefab";
    private const string S_Perturb_GenerateCandidates = "Layout.Perturb.GenerateCandidates";
    private const string S_Perturb_ApplyMove = "Layout.Perturb.ApplyMove";
    private const string S_FindPositionCandidates = "Layout.FindPositionCandidates";
    private const string S_WiggleCandidates = "Layout.WiggleCandidates";
    private const string S_UpdateEnergyCacheInPlace = "Layout.UpdateEnergyCacheInPlace";
    private const string S_ComputeEnergy = "Layout.ComputeEnergy";
    private const string S_BuildEnergyCache = "Layout.BuildEnergyCache";
    private const string S_IntersectionPenalty = "Layout.IntersectionPenalty";
    private const string S_IntersectionPenalty_Bitset = "Layout.IntersectionPenalty.Bitset";
    private const string S_IntersectionPenalty_Bitset_BiteAllowance = "Layout.IntersectionPenalty.Bitset.BiteAllowance";
    private const string S_IntersectionPenalty_Bitset_FloorFloor = "Layout.IntersectionPenalty.Bitset.FloorFloor";
    private const string S_IntersectionPenalty_Bitset_AWall_BFloor = "Layout.IntersectionPenalty.Bitset.AWall_BFloor";
    private const string S_IntersectionPenalty_Bitset_BWall_AFloor = "Layout.IntersectionPenalty.Bitset.BWall_AFloor";
    private const string S_IntersectionPenalty_Bitset_Allowed = "Layout.IntersectionPenalty.Bitset.Allowed";
    private const string S_IntersectionPenalty_Hashset = "Layout.IntersectionPenalty.Hashset";
    private const string S_ComputeEdgeDistancePenalty = "Layout.ComputeEdgeDistancePenalty";
    private const string S_TryValidateLayout = "Layout.TryValidateLayout";
    private const string S_CountOverlapShifted = "Layout.CountOverlapShifted";
    private const string S_BuildFacesChains = "Layout.BuildFacesChains";
    private const string S_BuildRoomPrefabLookup = "Layout.BuildRoomPrefabLookup";
    private const string S_BuildNeighborLookup = "Layout.BuildNeighborLookup";
    private const string S_BuildConnectorPrefabSet = "Layout.BuildConnectorPrefabSet";
    private const string S_WarmupShapes = "Layout.WarmupShapes";
    private const string S_WarmupConfigSpaces = "Layout.WarmupConfigSpaces";
    private const string S_WarmupBiteOffsets = "Layout.WarmupBiteOffsets";
    private const string S_StackSearch = "Layout.StackSearch";
    private const string S_StackSearch_Pop = "Layout.StackSearch.Pop";
    private const string S_StackSearch_ValidateGlobal = "Layout.StackSearch.ValidateGlobal";
    private const string S_StackSearch_ExpandChain = "Layout.StackSearch.ExpandChain";
    private const string S_StackSearch_Push = "Layout.StackSearch.Push";
    private const string S_TryValidateGlobal = "Layout.TryValidateGlobal";
    private const string S_GetBitsets = "Layout.GetBitsets";
    private const string S_ComputeWorldBounds = "Layout.ComputeWorldBounds";
    private const string S_GetBiteAllowance = "Layout.GetBiteAllowance";
    private const string S_GetRoomPrefabs = "Layout.GetRoomPrefabs";
    private const string S_AddPlacementToCache = "Layout.AddPlacementToCache";
    private const string S_CloneRoomsDeep = "Layout.CloneRoomsDeep";
    private const string S_UndoMove = "Layout.UndoMove";


    private static ProfilerSample PS(string name) => new(name);
    private static ProfilerSample PSIf(bool enabled, string name) => new(name, enabled);

    private readonly struct ProfilerSample : System.IDisposable
    {
        private readonly bool active;

        public ProfilerSample(string name) : this(name, true) { }

        public ProfilerSample(string name, bool enabled)
        {
            active = enabled;
            if (active)
                Profiler.BeginSample(name);
        }

        public void Dispose()
        {
            if (active)
                Profiler.EndSample();
        }
    }
}
