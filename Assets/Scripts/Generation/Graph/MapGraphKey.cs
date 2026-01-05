// Assets/scripts/Generation/Graph/MapGraphKey.cs
public static class MapGraphKey
{
    public static (string, string) NormalizeKey(string a, string b)
    {
        return string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
    }
}

