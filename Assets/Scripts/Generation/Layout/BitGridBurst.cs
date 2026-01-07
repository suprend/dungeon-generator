using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public static unsafe class BitGridBurst
{
    [BurstCompile]
    public static void AndShifted(
        ulong* fixedBits, int fixedWords, int fixedH,
        ulong* movingBits, int movingWords, int movingH,
        int sx, int sy)
    {
        var yStartMoving = math.max(0, -sy);
        var yEndMoving = math.min(movingH - 1, fixedH - 1 - sy);

        if (yStartMoving > yEndMoving)
        {
            // No overlap, clear everything
            // Note: We assume fixedBits length is fixedWords * fixedH
            // But here we only have pointer and dimensions.
            // We should be careful about size, but typically we clear everything if no overlap.
            // For safety in raw pointer context, we'll iterate.
            var len = fixedWords * fixedH;
            for (int i = 0; i < len; i++)
                fixedBits[i] = 0;
            return;
        }

        var yMinValid = yStartMoving + sy;
        var yMaxValid = yEndMoving + sy;

        // Clear rows before valid range
        if (yMinValid > 0)
        {
            long len = (long)yMinValid * fixedWords;
            for (long i = 0; i < len; i++)
                fixedBits[i] = 0;
        }

        // Clear rows after valid range
        if (yMaxValid < fixedH - 1)
        {
            long start = (long)(yMaxValid + 1) * fixedWords;
            long totalLen = (long)fixedWords * fixedH;
            long len = totalLen - start;
            for (long i = 0; i < len; i++)
                fixedBits[start + i] = 0;
        }

        if (sx == 0)
        {
            for (int yM = yStartMoving; yM <= yEndMoving; yM++)
            {
                var yF = yM + sy;
                long fixedRow = (long)yF * fixedWords;
                long movingRow = (long)yM * movingWords;
                var n = math.min(fixedWords, movingWords);

                for (int i = 0; i < n; i++)
                    fixedBits[fixedRow + i] &= movingBits[movingRow + i];
                
                for (int i = n; i < fixedWords; i++)
                    fixedBits[fixedRow + i] = 0;
            }
        }
        else if (sx > 0)
        {
            var wordShift = sx >> 6;
            var bitShift = sx & 63;
            var invBitShift = 64 - bitShift;

            for (int yM = yStartMoving; yM <= yEndMoving; yM++)
            {
                var yF = yM + sy;
                long fixedRow = (long)yF * fixedWords;
                long movingRow = (long)yM * movingWords;

                if (wordShift > 0)
                {
                    var clearLen = math.min(wordShift, fixedWords);
                    // Clear prefix of the row
                    var dst = fixedBits + fixedRow;
                    for(int k=0; k<clearLen; k++) dst[k] = 0;
                }

                var iFStart = math.max(0, wordShift);
                var iFEnd = math.min(fixedWords - 1, wordShift + movingWords - 1);

                for (int iF = iFStart; iF <= iFEnd; iF++)
                {
                    var src = iF - wordShift;
                    // Read strict, bounds checked by loop logic, but movingBits should be valid.
                    var moved = movingBits[movingRow + src] << bitShift;
                    if (bitShift != 0 && src - 1 >= 0)
                        moved |= movingBits[movingRow + (src - 1)] >> invBitShift;

                    fixedBits[fixedRow + iF] &= moved;
                }

                if (iFEnd < fixedWords - 1)
                {
                     var startClear = fixedRow + iFEnd + 1;
                     var countClear = fixedWords - 1 - iFEnd;
                     var dst = fixedBits + startClear;
                     for(int k=0; k<countClear; k++) dst[k] = 0;
                }
            }
        }
        else
        {
            var s = -sx;
            var wordShift = s >> 6;
            var bitShift = s & 63;
            var invBitShift = 64 - bitShift;

            for (int yM = yStartMoving; yM <= yEndMoving; yM++)
            {
                var yF = yM + sy;
                long fixedRow = (long)yF * fixedWords;
                long movingRow = (long)yM * movingWords;

                var iFEnd = math.min(fixedWords - 1, movingWords - 1 - wordShift);

                for (int iF = 0; iF <= iFEnd; iF++)
                {
                    var src = iF + wordShift;
                    var moved = movingBits[movingRow + src] >> bitShift;
                    if (bitShift != 0 && src + 1 < movingWords)
                        moved |= movingBits[movingRow + (src + 1)] << invBitShift;

                    fixedBits[fixedRow + iF] &= moved;
                }

                if (iFEnd < fixedWords - 1)
                {
                     var startClear = fixedRow + iFEnd + 1;
                     var countClear = fixedWords - 1 - iFEnd;
                     var dst = fixedBits + startClear;
                     for(int k=0; k<countClear; k++) dst[k] = 0;
                }
            }
        }
    }

    /// <summary>
    /// Burst-optimized extraction of bit positions from a BitGrid.
    /// Extracts (x,y) coordinates of all set bits and writes them to output arrays.
    /// </summary>
    [BurstCompile]
    public static int ExtractBitPositions(
        ulong* bits,
        int wordsPerRow,
        int height,
        int minX,
        int minY,
        int* outputX,
        int* outputY,
        int maxCapacity)
    {
        int count = 0;

        for (int r = 0; r < height; r++)
        {
            int y = minY + r;
            long rowStart = (long)r * wordsPerRow;

            for (int w = 0; w < wordsPerRow; w++)
            {
                ulong word = bits[rowStart + w];
                if (word == 0) continue;

                int baseX = minX + (w << 6);

                while (word != 0)
                {
                    if (count >= maxCapacity)
                        return count;

                    var tz = math.tzcnt(word);
                    outputX[count] = baseX + tz;
                    outputY[count] = y;
                    count++;

                    word &= word - 1;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Burst-optimized version of CountOverlapsShifted.
    /// Counts total overlapping bits between fixed and moving grids.
    /// </summary>
    [BurstCompile]
    public static int CountOverlapsShifted(
        ulong* fixedBits, int fixedWords, int fixedH,
        ulong* movingBits, int movingWords, int movingH,
        int sx, int sy)
    {
        var yStartMoving = math.max(0, -sy);
        var yEndMoving = math.min(movingH - 1, fixedH - 1 - sy);

        if (yStartMoving > yEndMoving)
            return 0;

        var total = 0;

        if (sx == 0)
        {
            for (int yM = yStartMoving; yM <= yEndMoving; yM++)
            {
                var yF = yM + sy;
                long fixedRow = (long)yF * fixedWords;
                long movingRow = (long)yM * movingWords;
                var n = math.min(fixedWords, movingWords);

                for (int i = 0; i < n; i++)
                {
                    var overlap = fixedBits[fixedRow + i] & movingBits[movingRow + i];
                    total += math.countbits(overlap);
                }
            }
            return total;
        }

        if (sx > 0)
        {
            var wordShift = sx >> 6;
            var bitShift = sx & 63;
            var invBitShift = 64 - bitShift;

            for (int yM = yStartMoving; yM <= yEndMoving; yM++)
            {
                var yF = yM + sy;
                long fixedRow = (long)yF * fixedWords;
                long movingRow = (long)yM * movingWords;

                var iFStart = math.max(0, wordShift);
                var iFEnd = math.min(fixedWords - 1, wordShift + movingWords - 1);

                for (int iF = iFStart; iF <= iFEnd; iF++)
                {
                    var src = iF - wordShift;
                    var moved = movingBits[movingRow + src] << bitShift;
                    if (bitShift != 0 && src - 1 >= 0)
                        moved |= movingBits[movingRow + (src - 1)] >> invBitShift;

                    var overlap = fixedBits[fixedRow + iF] & moved;
                    total += math.countbits(overlap);
                }
            }
            return total;
        }
        else
        {
            var s = -sx;
            var wordShift = s >> 6;
            var bitShift = s & 63;
            var invBitShift = 64 - bitShift;

            for (int yM = yStartMoving; yM <= yEndMoving; yM++)
            {
                var yF = yM + sy;
                long fixedRow = (long)yF * fixedWords;
                long movingRow = (long)yM * movingWords;

                var iFEnd = math.min(fixedWords - 1, movingWords - 1 - wordShift);

                for (int iF = 0; iF <= iFEnd; iF++)
                {
                    var src = iF + wordShift;
                    var moved = movingBits[movingRow + src] >> bitShift;
                    if (bitShift != 0 && src + 1 < movingWords)
                        moved |= movingBits[movingRow + (src + 1)] << invBitShift;

                    var overlap = fixedBits[fixedRow + iF] & moved;
                    total += math.countbits(overlap);
                }
            }
            return total;
        }
    }

    [BurstCompile]
    public static int CountIllegalOverlapsShifted(
        ulong* fixedBits, int fixedWords, int fixedH, Vector2Int* fixedMin,
        ulong* movingBits, int movingWords, int movingH,
        int sx, int sy,
        Vector2Int* fixedRoot,
        AllowedWorldCells* allowedWorld,
        bool earlyStopAtTwo,
        out Vector2Int lastIllegalWorld)
    {
        lastIllegalWorld = default;
        var yStartMoving = math.max(0, -sy);
        var yEndMoving = math.min(movingH - 1, fixedH - 1 - sy);

        if (yStartMoving > yEndMoving)
            return 0;

        var illegal = 0;

        if (sx == 0)
        {
            for (int yM = yStartMoving; yM <= yEndMoving; yM++)
            {
                var yF = yM + sy;
                long fixedRow = (long)yF * fixedWords;
                long movingRow = (long)yM * movingWords;
                var n = math.min(fixedWords, movingWords);

                for (int i = 0; i < n; i++)
                {
                    var overlap = fixedBits[fixedRow + i] & movingBits[movingRow + i];
                    if (overlap == 0)
                        continue;
                   
                    if (!AccumulateIllegal(overlap, i, yF, *fixedMin, fixedWords, *fixedRoot, *allowedWorld, earlyStopAtTwo, ref illegal, ref lastIllegalWorld))
                        return illegal;
                }
            }
        }
        else if (sx > 0)
        {
            var wordShift = sx >> 6;
            var bitShift = sx & 63;
            var invBitShift = 64 - bitShift;

            for (int yM = yStartMoving; yM <= yEndMoving; yM++)
            {
                var yF = yM + sy;
                long fixedRow = (long)yF * fixedWords;
                long movingRow = (long)yM * movingWords;

                var iFStart = math.max(0, wordShift);
                var iFEnd = math.min(fixedWords - 1, wordShift + movingWords - 1);

                for (int iF = iFStart; iF <= iFEnd; iF++)
                {
                    var src = iF - wordShift;
                    var moved = movingBits[movingRow + src] << bitShift;
                    if (bitShift != 0 && src - 1 >= 0)
                        moved |= movingBits[movingRow + (src - 1)] >> invBitShift;

                    var overlap = fixedBits[fixedRow + iF] & moved;
                    if (overlap == 0)
                        continue;

                     if (!AccumulateIllegal(overlap, iF, yF, *fixedMin, fixedWords, *fixedRoot, *allowedWorld, earlyStopAtTwo, ref illegal, ref lastIllegalWorld))
                        return illegal;
                }
            }
        }
        else
        {
             var s = -sx;
            var wordShift = s >> 6;
            var bitShift = s & 63;
             var invBitShift = 64 - bitShift;

            for (int yM = yStartMoving; yM <= yEndMoving; yM++)
            {
                var yF = yM + sy;
                long fixedRow = (long)yF * fixedWords;
                long movingRow = (long)yM * movingWords;

                var iFEnd = math.min(fixedWords - 1, movingWords - 1 - wordShift);

                for (int iF = 0; iF <= iFEnd; iF++)
                {
                    var src = iF + wordShift;
                    var moved = movingBits[movingRow + src] >> bitShift;
                    if (bitShift != 0 && src + 1 < movingWords)
                        moved |= movingBits[movingRow + (src + 1)] << invBitShift;

                    var overlap = fixedBits[fixedRow + iF] & moved;
                    if (overlap == 0)
                        continue;

                     if (!AccumulateIllegal(overlap, iF, yF, *fixedMin, fixedWords, *fixedRoot, *allowedWorld, earlyStopAtTwo, ref illegal, ref lastIllegalWorld))
                        return illegal;
                }
            }
        }

        return illegal;
    }

    private static bool AccumulateIllegal(
        ulong overlapWord,
        int fixedWordIndex,
        int fixedRowIndex,
        Vector2Int fixedMin,
        int fixedWords,
        Vector2Int fixedRoot,
        AllowedWorldCells allowedWorld,
        bool earlyStopAtTwo,
        ref int illegal,
        ref Vector2Int lastIllegalWorld)
    {
        var y = fixedMin.y + fixedRowIndex;
        var baseX = fixedMin.x + (fixedWordIndex << 6);
        var w = overlapWord;
        
        while (w != 0)
        {
            var bit = math.tzcnt(w);
            var x = baseX + bit;
            var world = fixedRoot + new Vector2Int(x, y);
            
            // Check AllowedWorldCells logic manually or call method
            // Since AllowedWorldCells is a struct and passed by value, and contains simple logic, we can call it.
            // But for Burst safety, ensure everything it calls is Burst compatible.
            // AllowedWorldCells.Contains uses only Vector2Int arithmetic. Should be fine.
            if (allowedWorld.IsEmpty || !allowedWorld.Contains(world))
            {
                illegal++;
                lastIllegalWorld = world;
                if (earlyStopAtTwo && illegal > 1)
                    return false;
            }
            w &= w - 1;
        }
        return true;
    }
}
