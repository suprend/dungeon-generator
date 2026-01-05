// Assets/Scripts/Generation/Layout/MapGraphLayoutGenerator.StackSearch.cs
using System.Collections.Generic;

public sealed partial class MapGraphLayoutGenerator
{
    private bool TryRunStackSearch(
        LayoutContext ctx,
        int? maxLayoutsPerChain,
        out LayoutResult layout,
        out string error,
        long tryGenerateStartTicks)
    {
        layout = null;
        error = null;
        if (ctx == null)
        {
            error = "Layout context is null.";
            return false;
        }

        lastFailureDetail = null;

        var initialLayout = new LayoutState(new Dictionary<string, RoomPlacement>(), 0, energyCache: null);
        var stack = new Stack<LayoutState>();
        stack.Push(initialLayout);
        if (profiling != null)
        {
            profiling.StackPushes++;
            profiling.MaxStackDepth = UnityEngine.Mathf.Max(profiling.MaxStackDepth, stack.Count);
        }

        using (PS(S_StackSearch))
        {
            while (stack.Count > 0)
            {
                var state = stack.Pop();
                if (profiling != null)
                    profiling.StackPops++;
                if (state.ChainIndex >= orderedChains.Count)
                {
                    using (PS(S_TryValidateGlobal))
                    {
                        if (TryValidateGlobal(state.Rooms, out var globalError))
                        {
                            layout = new LayoutResult(state.Rooms);
                            if (profiling != null)
                            {
                                profiling.TotalTryGenerateTicks = NowTicks() - tryGenerateStartTicks;
                                LogProfilingSummary(profiling);
                            }
                            return true;
                        }
                        lastFailureDetail = globalError;
                    }
                    continue;
                }

                var chain = orderedChains[state.ChainIndex];
                var maxLayouts = UnityEngine.Mathf.Max(1, maxLayoutsPerChain ?? settings.MaxLayoutsPerChain);
                var expansions = AddChain(state, chain, maxLayouts);
                foreach (var exp in expansions)
                {
                    stack.Push(exp.WithChainIndex(state.ChainIndex + 1));
                    if (profiling != null)
                    {
                        profiling.StackPushes++;
                        profiling.MaxStackDepth = UnityEngine.Mathf.Max(profiling.MaxStackDepth, stack.Count);
                    }
                }
            }
        }

        error = string.IsNullOrEmpty(lastFailureDetail)
            ? "Failed to generate layout for all chains."
            : $"Failed to generate layout for all chains. Last failure: {lastFailureDetail}";

        if (profiling != null)
        {
            profiling.TotalTryGenerateTicks = NowTicks() - tryGenerateStartTicks;
            LogProfilingSummary(profiling);
        }
        return false;
    }
}
