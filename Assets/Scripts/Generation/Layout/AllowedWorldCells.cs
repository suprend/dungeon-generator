using UnityEngine;

public readonly struct AllowedWorldCells
{
    public static AllowedWorldCells None => default;
    // Compact “allowed overlap mask” used in tight loops (layout validation + energy).
    // Modes:
    // - 0: empty
    // - 1: explicit small set (1 or 3 cells)
    // - 2: 3-ray mask in world space:
    //   - center ray (floor) at perp==0
    //   - two tangent rays (walls) at perp==±tangent
    //   k is the distance along the inward vector, constrained to [0..rayMaxK].
    private readonly byte mode; // 0 = none, 1 = explicit set (1 or 3), 2 = ray mask
    private readonly byte count;
    private readonly Vector2Int a;
    private readonly Vector2Int b;
    private readonly Vector2Int c;

    // Ray-mode fields (world cells).
    private readonly Vector2Int rayBase;
    private readonly Vector2Int rayInward;
    private readonly Vector2Int rayTangent;
    private readonly int rayMaxK;
    private readonly byte rayMask; // 1 = floor ray, 2 = wall rays

    public AllowedWorldCells(Vector2Int one)
    {
        mode = 1;
        count = 1;
        a = one;
        b = default;
        c = default;
        rayBase = default;
        rayInward = default;
        rayTangent = default;
        rayMaxK = 0;
        rayMask = 0;
    }

    public AllowedWorldCells(Vector2Int one, Vector2Int two, Vector2Int three)
    {
        mode = 1;
        count = 3;
        a = one;
        b = two;
        c = three;
        rayBase = default;
        rayInward = default;
        rayTangent = default;
        rayMaxK = 0;
        rayMask = 0;
    }

    private AllowedWorldCells(Vector2Int rayBase, Vector2Int rayInward, Vector2Int rayTangent, int rayMaxK, byte rayMask)
    {
        mode = 2;
        count = 0;
        a = default;
        b = default;
        c = default;
        this.rayBase = rayBase;
        this.rayInward = rayInward;
        this.rayTangent = rayTangent;
        this.rayMaxK = rayMaxK;
        this.rayMask = rayMask;
    }

    public static AllowedWorldCells Rays(Vector2Int rayBase, Vector2Int rayInward, Vector2Int rayTangent, int maxKInclusive, byte rayMask)
    {
        if (maxKInclusive < 0 || rayMask == 0)
            return None;
        return new AllowedWorldCells(rayBase, rayInward, rayTangent, maxKInclusive, rayMask);
    }

    public bool Contains(Vector2Int cell)
    {
        if (mode == 0)
            return false;

        if (mode == 1)
        {
            if (count == 0)
                return false;
            if (cell == a)
                return true;
            if (count == 1)
                return false;
            return cell == b || cell == c;
        }

        // Project onto inward axis to get “depth” along the connector bite ray.
        var offset = cell - rayBase;
        var k = offset.x * rayInward.x + offset.y * rayInward.y;
        if (k < 0 || k > rayMaxK)
            return false;
        var perp = offset - rayInward * k;
        if ((rayMask & 1) != 0 && perp == Vector2Int.zero)
            return true;
        if ((rayMask & 2) != 0 && (perp == rayTangent || perp == -rayTangent))
            return true;
        return false;
    }

    public bool IsEmpty => mode == 0;

    public bool TryGetExplicit(out int explicitCount, out Vector2Int cellA, out Vector2Int cellB, out Vector2Int cellC)
    {
        explicitCount = 0;
        cellA = default;
        cellB = default;
        cellC = default;
        if (mode != 1 || count == 0)
            return false;

        explicitCount = count;
        cellA = a;
        cellB = b;
        cellC = c;
        return true;
    }

    public bool TryGetRays(out Vector2Int baseCell, out Vector2Int inward, out Vector2Int tangent, out int maxKInclusive, out byte mask)
    {
        baseCell = default;
        inward = default;
        tangent = default;
        maxKInclusive = -1;
        mask = 0;
        if (mode != 2 || rayMask == 0)
            return false;

        baseCell = rayBase;
        inward = rayInward;
        tangent = rayTangent;
        maxKInclusive = rayMaxK;
        mask = rayMask;
        return true;
    }
}
