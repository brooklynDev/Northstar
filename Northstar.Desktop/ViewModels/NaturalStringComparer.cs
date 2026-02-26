using System;
using System.Collections.Generic;

namespace Northstar.Desktop.ViewModels;

internal sealed class NaturalStringComparer : IComparer<string?>
{
    public static readonly NaturalStringComparer OrdinalIgnoreCase = new(StringComparer.OrdinalIgnoreCase);

    private readonly StringComparer _textComparer;

    private NaturalStringComparer(StringComparer textComparer)
    {
        _textComparer = textComparer;
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var ix = 0;
        var iy = 0;

        while (ix < x.Length && iy < y.Length)
        {
            var cx = x[ix];
            var cy = y[iy];
            var xIsDigit = char.IsDigit(cx);
            var yIsDigit = char.IsDigit(cy);

            if (xIsDigit && yIsDigit)
            {
                var numericResult = CompareNumericChunk(x, ref ix, y, ref iy);
                if (numericResult != 0)
                {
                    return numericResult;
                }

                continue;
            }

            var textResult = CompareTextChunk(x, ref ix, y, ref iy);
            if (textResult != 0)
            {
                return textResult;
            }
        }

        return x.Length.CompareTo(y.Length);
    }

    private int CompareTextChunk(string x, ref int ix, string y, ref int iy)
    {
        var startX = ix;
        while (ix < x.Length && !char.IsDigit(x[ix]))
        {
            ix++;
        }

        var startY = iy;
        while (iy < y.Length && !char.IsDigit(y[iy]))
        {
            iy++;
        }

        var chunkX = x[startX..ix];
        var chunkY = y[startY..iy];
        return _textComparer.Compare(chunkX, chunkY);
    }

    private static int CompareNumericChunk(string x, ref int ix, string y, ref int iy)
    {
        var startX = ix;
        while (ix < x.Length && char.IsDigit(x[ix]))
        {
            ix++;
        }

        var startY = iy;
        while (iy < y.Length && char.IsDigit(y[iy]))
        {
            iy++;
        }

        var lenX = ix - startX;
        var lenY = iy - startY;

        var trimX = startX;
        while (trimX < ix && x[trimX] == '0')
        {
            trimX++;
        }

        var trimY = startY;
        while (trimY < iy && y[trimY] == '0')
        {
            trimY++;
        }

        var significantLenX = ix - trimX;
        var significantLenY = iy - trimY;

        if (significantLenX != significantLenY)
        {
            return significantLenX.CompareTo(significantLenY);
        }

        for (var i = 0; i < significantLenX; i++)
        {
            var digitCompare = x[trimX + i].CompareTo(y[trimY + i]);
            if (digitCompare != 0)
            {
                return digitCompare;
            }
        }

        // Same numeric value, prefer fewer total digits (e.g. "1" before "01").
        if (lenX != lenY)
        {
            return lenX.CompareTo(lenY);
        }

        return 0;
    }
}
