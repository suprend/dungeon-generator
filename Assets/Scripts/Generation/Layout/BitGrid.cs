using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public sealed class BitGrid
{
    public Vector2Int Min { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int WordsPerRow { get; private set; }
    public ulong[] Bits { get; private set; }
    public ulong LastWordMask { get; private set; }

    public BitGrid(Vector2Int min, int width, int height, int wordsPerRow, ulong[] bits, ulong lastWordMask)
    {
        Min = min;
        Width = width;
        Height = height;
        WordsPerRow = wordsPerRow;
        Bits = bits;
        LastWordMask = lastWordMask;
    }

    public static BitGrid Build(HashSet<Vector2Int> cells, Vector2Int min, Vector2Int max)
    {
        if (cells == null || cells.Count == 0)
            return null;
        var width = max.x - min.x + 1;
        var height = max.y - min.y + 1;
        if (width <= 0 || height <= 0)
            return null;

        var wordsPerRow = (width + 63) >> 6;
        long totalWords = (long)wordsPerRow * height;
        if (totalWords > 100_000_000) // 100M ulongs = 800MB. Sanity check.
             return null;
        var bits = new ulong[(int)totalWords];
        var rem = width & 63;
        var lastMask = rem == 0 ? ulong.MaxValue : ((1UL << rem) - 1UL);

        foreach (var c in cells)
        {
            var rx = c.x - min.x;
            var ry = c.y - min.y;
            if ((uint)rx >= (uint)width || (uint)ry >= (uint)height)
                continue;
            var word = rx >> 6;
            var bit = rx & 63;
            bits[(long)ry * wordsPerRow + word] |= 1UL << bit;
        }

        if (lastMask != ulong.MaxValue)
        {
            for (int y = 0; y < height; y++)
            {
                var idx = (long)y * wordsPerRow + (wordsPerRow - 1);
                bits[idx] &= lastMask;
            }
        }

        return new BitGrid(min, width, height, wordsPerRow, bits, lastMask);
    }

    public bool ContainsLocal(Vector2Int localCell)
    {
        var rx = localCell.x - Min.x;
        var ry = localCell.y - Min.y;
        if ((uint)rx >= (uint)Width || (uint)ry >= (uint)Height)
            return false;
        var idx = (long)ry * WordsPerRow + (rx >> 6);
        return (Bits[idx] & (1UL << (rx & 63))) != 0;
    }

    // Optimized PopCount using Unity.Mathematics
    public static int PopCount(ulong x)
    {
        return math.countbits(x);
    }

    // Optimized TrailingZeroCount using Unity.Mathematics
    public static int TrailingZeroCount(ulong x)
    {
        return math.tzcnt(x);
    }

    /// <summary>
    /// Fast lookup to check if a position is set in the grid.
    /// </summary>
    public bool IsSet(Vector2Int pos)
    {
        // Bounds check
        int x = pos.x - Min.x;
        int y = pos.y - Min.y;
        
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return false;
        
        // Calculate bit position
        int wordIdx = x >> 6;        // x / 64
        int bitIdx = x & 63;         // x % 64
        
        if (wordIdx >= WordsPerRow)
            return false;
        
        // Check bit
        long idx = (long)y * WordsPerRow + wordIdx;
        return (Bits[idx] & (1UL << bitIdx)) != 0;
    }

    public int CountOverlapsShifted(BitGrid moving, Vector2Int shiftMovingToFixed)
    {
        if (moving == null || Height == 0 || Width == 0 || moving.Height == 0 || moving.Width == 0)
            return 0;

        unsafe
        {
            fixed (ulong* fixedPtr = Bits)
            fixed (ulong* movingPtr = moving.Bits)
            {
                return BitGridBurst.CountOverlapsShifted(
                    fixedPtr, WordsPerRow, Height,
                    movingPtr, moving.WordsPerRow, moving.Height,
                    shiftMovingToFixed.x, shiftMovingToFixed.y);
            }
        }
    }

    public int CountIllegalOverlapsShifted(
        BitGrid moving,
        Vector2Int shiftMovingToFixed,
        Vector2Int fixedRoot,
        AllowedWorldCells allowedWorld,
        bool earlyStopAtTwo,
        out Vector2Int lastIllegalWorld)
    {
        lastIllegalWorld = default;
        if (moving == null || Height == 0 || Width == 0 || moving.Height == 0 || moving.Width == 0)
            return 0;

        var fixedMin = Min;
        unsafe
        {
            fixed (ulong* fixedPtr = Bits)
            fixed (ulong* movingPtr = moving.Bits)
            {
                return BitGridBurst.CountIllegalOverlapsShifted(
                    fixedPtr, WordsPerRow, Height, &fixedMin,
                    movingPtr, moving.WordsPerRow, moving.Height,
                    shiftMovingToFixed.x, shiftMovingToFixed.y,
                    &fixedRoot, &allowedWorld, earlyStopAtTwo,
                    out lastIllegalWorld);
            }
        }
    }



    /// <summary>
    /// Modifies this BitGrid by AND-ing it with another BitGrid shifted.
    /// Used for filtering candidates.
    /// Bits in 'this' are preserved ONLY if the corresponding bit in 'other' (shifted by 'shiftMovingToFixed') is also set.
    /// </summary>
    public void AndShifted(BitGrid moving, Vector2Int shiftMovingToFixed)
    {
        if (moving == null)
        {
            System.Array.Clear(Bits, 0, Bits.Length);
            return;
        }

        unsafe
        {
            fixed (ulong* fixedPtr = Bits)
            fixed (ulong* movingPtr = moving.Bits)
            {
                BitGridBurst.AndShifted(
                    fixedPtr, WordsPerRow, Height,
                    movingPtr, moving.WordsPerRow, moving.Height,
                    shiftMovingToFixed.x, shiftMovingToFixed.y);
            }
        }
    }
    
    public void CopyFrom(BitGrid other)
    {
        if (other == null)
            return;

        Min = other.Min;
        Width = other.Width;
        Height = other.Height;
        WordsPerRow = other.WordsPerRow;
        LastWordMask = other.LastWordMask;

        if (Bits == null || Bits.Length < other.Bits.Length)
        {
            Bits = new ulong[other.Bits.Length];
        }
        
        System.Array.Copy(other.Bits, Bits, other.Bits.Length);
    }

    public BitGrid Clone()
    {
        var newBits = (ulong[])Bits.Clone();
        return new BitGrid(Min, Width, Height, WordsPerRow, newBits, LastWordMask);
    }
}
