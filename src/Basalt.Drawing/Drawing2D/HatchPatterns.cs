namespace System.Drawing.Drawing2D;

/// <summary>
/// The 8x8 bit pattern behind every HatchStyle: bit 7 (the high bit) of row 0
/// is column 0, bit 0 is column 7, one foreground pixel per set bit.
/// </summary>
/// <remarks>
/// None of these has been compared pixel by pixel with GDI+ on Windows. The
/// six classic styles (Horizontal, Vertical, ForwardDiagonal,
/// BackwardDiagonal, Cross, DiagonalCross) are unambiguous in shape but not in
/// which row or column of the tile carries the line; the rest are drawn to
/// look like their names, the Percent* family with an ordered-dither table,
/// and a few (Shingle, Trellis) reuse the closest pattern. They wait for the
/// reference images from Windows.
/// </remarks>
internal static class HatchPatterns
{
    // The classic 8x8 Bayer ordered-dithering matrix: spreads a given count of
    // foreground pixels as evenly as possible across the tile, which is what
    // the Percent* styles are meant to look like.
    private static readonly int[,] Bayer =
    {
        { 0, 48, 12, 60, 3, 51, 15, 63 },
        { 32, 16, 44, 28, 35, 19, 47, 31 },
        { 8, 56, 4, 52, 11, 59, 7, 55 },
        { 40, 24, 36, 20, 43, 27, 39, 23 },
        { 2, 50, 14, 62, 1, 49, 13, 61 },
        { 34, 18, 46, 30, 33, 17, 45, 29 },
        { 10, 58, 6, 54, 9, 57, 5, 53 },
        { 42, 26, 38, 22, 41, 25, 37, 21 },
    };

    public static byte[] Rows(HatchStyle style) => (int)style switch
    {
        0 => Lines(row: 3), // Horizontal (and Min, the same value).
        1 => Columns(col: 3), // Vertical.
        2 => ForwardDiagonal(thickness: 1, offset: 0),
        3 => BackwardDiagonal(thickness: 1, offset: 0),
        4 => Cross(), // Cross, LargeGrid and Max all share this value.
        5 => Or(ForwardDiagonal(1, 0), BackwardDiagonal(1, 0)), // DiagonalCross.
        6 => Percent(5),
        7 => Percent(10),
        8 => Percent(20),
        9 => Percent(25),
        10 => Percent(30),
        11 => Percent(40),
        12 => Percent(50),
        13 => Percent(60),
        14 => Percent(70),
        15 => Percent(75),
        16 => Percent(80),
        17 => Percent(90),
        18 => BackwardDiagonal(1, 0), // LightDownwardDiagonal.
        19 => ForwardDiagonal(1, 0), // LightUpwardDiagonal.
        20 => BackwardDiagonal(2, 0), // DarkDownwardDiagonal.
        21 => ForwardDiagonal(2, 0), // DarkUpwardDiagonal.
        22 => BackwardDiagonal(3, 0), // WideDownwardDiagonal.
        23 => ForwardDiagonal(3, 0), // WideUpwardDiagonal.
        24 => Columns(1), // LightVertical.
        25 => Lines(1), // LightHorizontal.
        26 => Or(Columns(1), Columns(5)), // NarrowVertical.
        27 => Or(Lines(1), Lines(5)), // NarrowHorizontal.
        28 => Or(Or(Columns(1), Columns(2)), Or(Columns(5), Columns(6))), // DarkVertical.
        29 => Or(Or(Lines(1), Lines(2)), Or(Lines(5), Lines(6))), // DarkHorizontal.
        30 => HalfTile(BackwardDiagonal(1, 0), topHalf: true), // DashedDownwardDiagonal.
        31 => HalfTile(ForwardDiagonal(1, 0), topHalf: true), // DashedUpwardDiagonal.
        32 => LeftHalf(Lines(3)), // DashedHorizontal: the left half of the row only.
        33 => HalfTile(Columns(3), topHalf: true), // DashedVertical (top half of the column only).
        34 => Dots([(1, 1), (3, 5), (5, 2), (6, 6), (0, 4), (4, 0)]), // SmallConfetti.
        35 => Blocks([(1, 1), (5, 4)], size: 2), // LargeConfetti.
        36 => ZigZag(),
        37 => Wave(),
        38 => Or(ForwardDiagonal(1, 0), ForwardDiagonal(1, 4)), // DiagonalBrick.
        39 => HorizontalBrick(),
        40 => Or(ForwardDiagonal(1, 0), BackwardDiagonal(1, 2)), // Weave.
        41 => Plaid(),
        42 => Dots([(3, 3)]), // Divot.
        43 => Or(Dotted(Columns(3)), Dotted(Lines(3))), // DottedGrid.
        44 => Diamond(radius: 3, outlineOnly: true, dotted: true), // DottedDiamond.
        45 => HorizontalBrick(), // Shingle: the closest built-in approximation is a brick course.
        46 => Or(ForwardDiagonal(1, 0), BackwardDiagonal(1, 0)), // Trellis: the diagonal lattice, same as DiagonalCross.
        47 => Blocks([(1, 1), (1, 5), (5, 1), (5, 5)], size: 2), // Sphere.
        48 => SmallGrid(),
        49 => Checkerboard(cell: 2), // SmallCheckerBoard.
        50 => Checkerboard(cell: 4), // LargeCheckerBoard.
        51 => Diamond(radius: 3, outlineOnly: true, dotted: false), // OutlinedDiamond.
        52 => Diamond(radius: 3, outlineOnly: false, dotted: false), // SolidDiamond.
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown HatchStyle."),
    };

    private static byte[] Lines(int row)
    {
        var rows = new byte[8];
        rows[row] = 0xFF;
        return rows;
    }

    private static byte[] Columns(int col)
    {
        var rows = new byte[8];
        var bit = (byte)(0x80 >> col);
        for (var r = 0; r < 8; r++) rows[r] = bit;
        return rows;
    }

    private static byte[] Cross()
    {
        var rows = Columns(3);
        rows[3] = 0xFF;
        return rows;
    }

    /// <summary>A "\" diagonal: row r lights columns r, r+1, ... up to thickness, wrapping at 8.</summary>
    private static byte[] BackwardDiagonal(int thickness, int offset)
    {
        var rows = new byte[8];
        for (var r = 0; r < 8; r++)
        {
            byte value = 0;
            for (var t = 0; t < thickness; t++) value |= (byte)(0x80 >> ((r + offset + t) % 8));
            rows[r] = value;
        }

        return rows;
    }

    /// <summary>A "/" diagonal: row r lights columns 7-r, going down as thickness widens.</summary>
    private static byte[] ForwardDiagonal(int thickness, int offset)
    {
        var rows = new byte[8];
        for (var r = 0; r < 8; r++)
        {
            byte value = 0;
            for (var t = 0; t < thickness; t++) value |= (byte)(0x80 >> ((7 - r + offset + t + 8) % 8));
            rows[r] = value;
        }

        return rows;
    }

    private static byte[] Or(byte[] a, byte[] b)
    {
        var rows = new byte[8];
        for (var r = 0; r < 8; r++) rows[r] = (byte)(a[r] | b[r]);
        return rows;
    }

    /// <summary>Keeps only the top half (rows 0-3) of a pattern, to turn a solid line into a dashed one.</summary>
    private static byte[] HalfTile(byte[] pattern, bool topHalf)
    {
        var rows = new byte[8];
        for (var r = 0; r < 8; r++) rows[r] = (topHalf ? r < 4 : r >= 4) ? pattern[r] : (byte)0;
        return rows;
    }

    /// <summary>Keeps only the left half (columns 0-3) of a pattern, to turn a solid row into a dashed one.</summary>
    private static byte[] LeftHalf(byte[] pattern)
    {
        var rows = new byte[8];
        for (var r = 0; r < 8; r++) rows[r] = (byte)(pattern[r] & 0xF0);
        return rows;
    }

    /// <summary>Thins a pattern to every other pixel along its own bits, turning a line into dots.</summary>
    private static byte[] Dotted(byte[] pattern)
    {
        var rows = new byte[8];
        for (var r = 0; r < 8; r++)
        {
            byte value = 0;
            for (var c = 0; c < 8; c++)
            {
                var bit = (byte)(0x80 >> c);
                if ((pattern[r] & bit) != 0 && (r + c) % 2 == 0) value |= bit;
            }

            rows[r] = value;
        }

        return rows;
    }

    private static byte[] Dots((int Row, int Col)[] positions)
    {
        var rows = new byte[8];
        foreach (var (row, col) in positions) rows[row] |= (byte)(0x80 >> col);
        return rows;
    }

    private static byte[] Blocks((int Row, int Col)[] origins, int size)
    {
        var rows = new byte[8];
        foreach (var (row, col) in origins)
        {
            for (var dr = 0; dr < size; dr++)
            {
                for (var dc = 0; dc < size; dc++) rows[row + dr] |= (byte)(0x80 >> (col + dc));
            }
        }

        return rows;
    }

    /// <summary>A triangular wave, rising for the first half of the tile and falling for the second.</summary>
    private static byte[] ZigZag()
    {
        var rows = new byte[8];
        for (var row = 0; row < 8; row++)
        {
            var col = row <= 3 ? row : 7 - row;
            rows[row] = (byte)(0x80 >> col);
        }

        return rows;
    }

    /// <summary>A gentle sinusoid, one bit per column instead of per row.</summary>
    private static byte[] Wave()
    {
        int[] rowOfColumn = [1, 0, 0, 1, 2, 3, 3, 2];
        var rows = new byte[8];
        for (var col = 0; col < 8; col++) rows[rowOfColumn[col]] |= (byte)(0x80 >> col);
        return rows;
    }

    private static byte[] HorizontalBrick()
    {
        byte[] rows =
        [
            0xFF, // mortar course
            0x80, 0x80, 0x80, // left joint, half brick tall
            0xFF, // mortar course
            0x08, 0x08, 0x08, // joint offset by half a brick
        ];
        return rows;
    }

    private static byte[] Plaid()
    {
        var rows = new byte[8];
        for (var r = 0; r < 8; r++) rows[r] = r is 1 or 5 ? (byte)0xFF : (byte)0x42;
        return rows;
    }

    private static byte[] SmallGrid()
    {
        var rows = new byte[8];
        for (var r = 0; r < 8; r++) rows[r] = r is 0 or 4 ? (byte)0xFF : (byte)0x88;
        return rows;
    }

    private static byte[] Checkerboard(int cell)
    {
        var rows = new byte[8];
        for (var r = 0; r < 8; r++)
        {
            byte value = 0;
            for (var c = 0; c < 8; c++)
            {
                if (((r / cell) + (c / cell)) % 2 == 0) value |= (byte)(0x80 >> c);
            }

            rows[r] = value;
        }

        return rows;
    }

    /// <summary>A diamond centred on the tile, as an outline, dotted outline, or solid fill.</summary>
    private static byte[] Diamond(int radius, bool outlineOnly, bool dotted)
    {
        const float Center = 3.5f;
        var rows = new byte[8];
        for (var r = 0; r < 8; r++)
        {
            byte value = 0;
            for (var c = 0; c < 8; c++)
            {
                var distance = Math.Abs(r - Center) + Math.Abs(c - Center);
                var onDiamond = outlineOnly ? distance is > 2.5f and < 3.5f : distance <= radius + 0.5f;
                if (onDiamond && (!dotted || (r + c) % 2 == 0)) value |= (byte)(0x80 >> c);
            }

            rows[r] = value;
        }

        return rows;
    }

    private static byte[] Percent(int percent)
    {
        // Spreads round(percent/100 * 64) foreground pixels over the tile,
        // choosing the 64 Bayer cells with the lowest threshold first: the
        // standard ordered-dithering trick for turning a density into a
        // pattern that looks evenly grey at a glance.
        var target = (int)MathF.Round(percent / 100f * 64f);
        var rows = new byte[8];
        for (var r = 0; r < 8; r++)
        {
            byte value = 0;
            for (var c = 0; c < 8; c++)
            {
                if (Bayer[r, c] < target) value |= (byte)(0x80 >> c);
            }

            rows[r] = value;
        }

        return rows;
    }
}
