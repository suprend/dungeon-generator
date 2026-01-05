// Assets/Scripts/Generation/Utils/CollectionExtensions.cs
using System;
using System.Collections.Generic;

public static class CollectionExtensions
{
    /// <summary>
    /// Fisher-Yates shuffle that modifies the list in place.
    /// </summary>
    public static void Shuffle<T>(this IList<T> list, Random rng)
    {
        if (list == null) throw new ArgumentNullException(nameof(list));
        if (rng == null) throw new ArgumentNullException(nameof(rng));

        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
