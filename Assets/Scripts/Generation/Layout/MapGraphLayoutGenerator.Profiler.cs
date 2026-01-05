// Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.Profiler.cs
using UnityEngine.Profiling;

public sealed partial class MapGraphLayoutGenerator
{
    // Unity Profiler sample names (constant strings => no per-call allocations).
    private const string S_TryGenerate = "Layout.TryGenerate";
    private const string S_AddChain = "Layout.AddChain";
    private const string S_AddChainSmall = "Layout.AddChainSmall";
    private const string S_TryPerturbInPlace = "Layout.TryPerturbInPlace";
    private const string S_FindPositionCandidates = "Layout.FindPositionCandidates";
    private const string S_WiggleCandidates = "Layout.WiggleCandidates";
    private const string S_UpdateEnergyCacheInPlace = "Layout.UpdateEnergyCacheInPlace";
    private const string S_ComputeEnergy = "Layout.ComputeEnergy";
    private const string S_BuildEnergyCache = "Layout.BuildEnergyCache";
    private const string S_IntersectionPenalty = "Layout.IntersectionPenalty";
    private const string S_ComputeEdgeDistancePenalty = "Layout.ComputeEdgeDistancePenalty";
    private const string S_TryValidateLayout = "Layout.TryValidateLayout";
    private const string S_CountOverlapShifted = "Layout.CountOverlapShifted";
    private const string S_BuildFacesChains = "Layout.BuildFacesChains";
    private const string S_BuildRoomPrefabLookup = "Layout.BuildRoomPrefabLookup";
    private const string S_BuildNeighborLookup = "Layout.BuildNeighborLookup";
    private const string S_BuildConnectorPrefabSet = "Layout.BuildConnectorPrefabSet";
    private const string S_WarmupShapes = "Layout.WarmupShapes";
    private const string S_WarmupConfigSpaces = "Layout.WarmupConfigSpaces";
    private const string S_StackSearch = "Layout.StackSearch";
    private const string S_TryValidateGlobal = "Layout.TryValidateGlobal";

    private static ProfilerSample PS(string name) => new(name);

    private readonly struct ProfilerSample : System.IDisposable
    {
        public ProfilerSample(string name)
        {
            Profiler.BeginSample(name);
        }

        public void Dispose()
        {
            Profiler.EndSample();
        }
    }
}
