// Assets/Scripts/Generation/Geometry/ConfigurationSpaceLibrary.Debug.cs
using UnityEngine;

public sealed partial class ConfigurationSpaceLibrary
{
    public void SetDebug(bool enabled, int maxLogs = 64)
    {
        verbose = enabled;
        maxVerboseLogs = Mathf.Max(0, maxLogs);
    }

    private void LogVerbose(ref int logged, ModuleShape fixedShape, ModuleShape movingShape, ShapeSocket aSock, ShapeSocket bSock, Vector2Int delta, string detail)
    {
        if (!verbose)
            return;
        if (maxVerboseLogs <= 0)
            return;
        if (logged >= maxVerboseLogs)
            return;

        logged++;
        Debug.Log($"[ConfigSpace][dbg] {aSock.Side}@{aSock.CellOffset} vs {bSock.Side}@{bSock.CellOffset} delta={delta} => {detail}");
    }
}
