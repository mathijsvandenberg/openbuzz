namespace OpenBuzz.Graphics;

/// <summary>
/// The PS2 GS stores palettised textures in a block/column interleave rather
/// than row order, so indices have to be un-shuffled before they mean anything.
/// </summary>
public static class Ps2Swizzle
{
    /// <summary>
    /// Undoes the 8-bit index shuffle.
    ///
    /// The permutation is horizontal only: rows are already in the right order,
    /// which is visible in a decoded flag - colour bands stay crisp and there is
    /// no diagonal skew, so the stride is right and pixels never move between
    /// rows. Only x is shuffled, by a permutation of its low five bits:
    /// INCOMPLETE. This is the best permutation found so far, not the answer.
    /// It cuts transitions per row on the flags from 231 to 96, but a correct
    /// decode of flag artwork should be well under 20, so the image is still
    /// visibly wrong.
    ///
    /// The likely reason: this searches only permutations of x that are the same
    /// on every row, and PS2 swizzles are not. The standard PSMT8 formula has a
    /// swap selector of ((y + 2) >> 2) & 1, so the horizontal shuffle alternates
    /// with y. Extending the search to permutations parameterised by low bits of
    /// y is the next step.
    ///
    /// See `obz tex bitperm`. Metric history matters here: mean luminance step
    /// rewarded interleaving and picked a permutation that shredded a flag
    /// emblem into five fragments; longest-run saturated because flat areas keep
    /// one run alive regardless. Counting every colour transition per row is the
    /// metric that finally discriminates, prompted by the Danish flag being red,
    /// white, red - exactly two transitions when correct.
    /// </summary>
    public static readonly int[] LowBitPermutation = [2, 3, 1, 4, 5, 0];

    /// XOR applied after the bit permutation.
    public const int LowBitXor = 1;

    public static byte[] UnswizzlePsmt8(ReadOnlySpan<byte> src, int width, int height)
    {
        int bits = LowBitPermutation.Length;
        int mask = (1 << bits) - 1;

        // The map depends only on x, so build it once per row width.
        var map = new int[width];
        for (int x = 0; x < width; x++)
        {
            int low = x & mask, shuffled = 0;
            for (int b = 0; b < bits; b++)
                if ((low & (1 << b)) != 0)
                    shuffled |= 1 << LowBitPermutation[b];
            map[x] = (x & ~mask) | (shuffled ^ LowBitXor);
        }

        var dst = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int i = row + map[x];
                dst[row + x] = i < src.Length ? src[i] : (byte)0;
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
