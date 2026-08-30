namespace OpenBuzz.Graphics;

/// <summary>
/// The PS2 GS stores palettised textures in a block/column interleave rather
/// than row order, so indices have to be un-shuffled before they mean anything.
/// </summary>
public static class Ps2Swizzle
{
    /// <summary>
    /// Within-block ordering that undoes the 8-bit horizontal shuffle:
    /// output pixel i of each 16-pixel block reads source position Order[i].
    ///
    /// Recovered by solving rather than guessing. The permutation is horizontal
    /// only - in a decoded flag the colour bands stay crisp and there is no
    /// diagonal skew, so rows are already right and pixels never move between
    /// them. That makes it a seriation problem: order the 16 positions so that
    /// neighbouring output pixels agree as often as possible, with the cost
    /// between two positions measured from the data as how often they differ.
    ///
    /// The result is stride-4 residue groups - all x congruent to 0 mod 4, then
    /// 2, then 3, then 1 - which is the interleave a byte-selector produces.
    ///
    /// Two unrelated textures agree on it independently: BZ_Language_flags and
    /// BZ_fonts_AardvarkBold both solve to this sequence, the font's coming out
    /// reversed, which is the one ambiguity seriation cannot resolve. Transitions
    /// per row fall from 147 to 64 on the flags.
    ///
    /// Rows are permuted too, which the flags could not reveal - a row displaced
    /// inside a solid colour band looks identical. The medal icons, being smooth,
    /// showed heavy horizontal striping, and solving the vertical axis the same
    /// way gives even rows then odd rows, dropping vertical transitions from 108
    /// to 62. So the shuffle is two-dimensional and separable.
    ///
    /// Four earlier attempts guessed a *shape* - block/column formulas, bit
    /// permutations, a row-dependent swap - and all plateaued. See
    /// docs/texture-format.md.
    /// </summary>
    /// Column order within each 16-pixel group: stride-4 residue groups,
    /// x congruent to 0 mod 4, then 2, then 3, then 1.
    public static readonly int[] ColumnOrder = [0, 4, 8, 12, 2, 6, 10, 14, 15, 11, 7, 3, 13, 9, 5, 1];

    /// Row order within each 16-row band: even rows, then odd rows.
    public static readonly int[] RowOrder = [0, 2, 4, 6, 8, 10, 12, 14, 1, 3, 5, 7, 9, 11, 13, 15];

    public static byte[] UnswizzlePsmt8(ReadOnlySpan<byte> src, int width, int height)
    {
        int n = ColumnOrder.Length;
        var dst = new byte[width * height];

        for (int y = 0; y < height; y++)
        {
            int band = y / n, iy = y % n;
            int srcRow = (band * n + RowOrder[iy]) * width;
            int dstRow = y * width;

            for (int block = 0; block + n <= width; block += n)
                for (int i = 0; i < n; i++)
                {
                    int from = srcRow + block + ColumnOrder[i];
                    dst[dstRow + block + i] = from < src.Length ? src[from] : (byte)0;
                }
        }

        return dst;
    }

    /// <summary>
    /// Undoes the PSMT4 (4-bit indexed) interleave, expanding to one byte per
    /// pixel so callers only deal with one index format.
    /// </summary>
    public static byte[] UnswizzlePsmt4(ReadOnlySpan<byte> src, int width, int height)
    {
        var dst = new byte[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int blockLocation = (y & ~0x0F) * width * 2 + (x & ~0x1F) * 2;
                int swapSelector = (((y + 2) >> 2) & 0x01) * 4;
                int posY = (((y & ~3) >> 1) + (y & 1)) & 0x07;
                int columnLocation = posY * width * 2 + ((x + swapSelector) & 0x07) * 4;
                int byteSelector = ((y >> 1) & 1) + ((x >> 2) & 2);

                int index = blockLocation + columnLocation + byteSelector;
                if (index >= src.Length * 2) { dst[y * width + x] = 0; continue; }

                byte packed = src[index >> 1];
                dst[y * width + x] = (byte)((index & 1) != 0 ? packed >> 4 : packed & 0x0F);
            }
        }

        return dst;
    }

    /// <summary>
    /// Reorders a 256-entry CLUT out of the GS's CSM1 layout: within every
    /// block of 32 entries the second and third groups of 8 are swapped.
    /// 16-entry palettes are stored linearly and need no fixing.
    /// </summary>
    public static void UnshuffleClut256(Span<uint> palette)
    {
        if (palette.Length < 256) return;

        Span<uint> copy = stackalloc uint[256];
        palette[..256].CopyTo(copy);

        for (int i = 0; i < 256; i++)
        {
            int source = (i & 0x18) switch
            {
                0x08 => i + 8,
                0x10 => i - 8,
                _ => i,
            };
            palette[i] = copy[source];
        }
    }

    /// <summary>
    /// PS2 alpha runs 0..128, where 128 is fully opaque. Scaling by 255/128
    /// and clamping restores a normal 0..255 channel.
    /// </summary>
    public static byte ExpandAlpha(byte a) => (byte)Math.Min(255, a * 255 / 128);
}
