namespace OpenBuzz.Graphics;

/// <summary>
/// RenderWare's own PS2 texture de-swizzle, ported from librw
/// (<c>src/ps2/ps2raster.cpp</c>, functions <c>swizzle</c> and
/// <c>unswizzleRaster</c>).
///
/// This replaces six rounds of inference. Every earlier attempt tried to derive
/// the mapping from the images - block/column formulas, bit permutations,
/// seriation, annealing, interleave models - and each produced something that
/// scored better without being right, because the objective functions reward
/// smoothness rather than correctness.
///
/// The real transform is a bit-twiddle over x and y that no permutation of a
/// fixed-size block can express: it works four rows at a time, and the address
/// depends on bits of y XORed into x. That is why every fixed-block model
/// plateaued.
///
/// Note that RenderWare only swizzles when a flag says so, so a raster may
/// legitimately be stored linear.
/// </summary>
public static class RwSwizzle
{
    /// Raster flag meaning "8-bit data is swizzled" (librw Ps2Raster::SWIZZLED8).
    public const int Swizzled8 = 0x2;

    /// Raster flag meaning "4-bit data is swizzled" (Ps2Raster::SWIZZLED4).
    public const int Swizzled4 = 0x4;

    /// <summary>
    /// librw's <c>swizzle(x, y, logw)</c>: the offset of texel (x, y) within its
    /// group of four rows.
    /// </summary>
    private static uint Swizzle(uint x, uint y, int logw)
    {
        // Bits of y perturb x before the rest of the address is built. This
        // coupling is the part no separable row/column model could represent.
        x ^= (((y >> 1) & 1) ^ ((y >> 2) & 1)) << 2;

        uint nx = (x & 7) | ((x >> 1) & ~7u);
        uint ny = (y & 1) | ((y >> 1) & ~1u);
        uint n = ((y >> 1) & 1) | (((x >> 3) & 1) << 1);

        return n | (nx << 2) | (ny << (logw - 1 + 2));
    }

    /// <summary>
    /// Un-swizzles 8-bit indexed data in place-equivalent fashion, returning a
    /// linear row-major buffer.
    /// </summary>
    public static byte[] UnswizzlePsmt8(ReadOnlySpan<byte> src, int width, int height)
    {
        // librw clamps to the minimum transfer size for the format; for a
        // swizzled PSMT8 raster that is 16x4.
        int w = Math.Max(width, 16);
        int h = Math.Max(height, 4);

        int logw = 0;
        for (int i = 1; i < w; i *= 2) logw++;
        uint mask = (1u << (logw + 2)) - 1;

        var dst = new byte[w * h];
        var group = new byte[4 * w];

        for (int y = 0; y + 4 <= h; y += 4)
        {
            int baseOffset = y * w;
            for (int i = 0; i < group.Length; i++)
            {
                int from = baseOffset + i;
                group[i] = from < src.Length ? src[from] : (byte)0;
            }

            for (int i = 0; i < 4; i++)
                for (int x = 0; x < w; x++)
                {
                    uint s = Swizzle((uint)x, (uint)(y + i), logw) & mask;
                    dst[(y + i) * w + x] = s < group.Length ? group[s] : (byte)0;
                }
        }

        if (w == width && h == height) return dst;

        // Crop back if the buffer was widened to the minimum transfer size.
        var cropped = new byte[width * height];
        for (int y = 0; y < height; y++)
            Array.Copy(dst, y * w, cropped, y * width, Math.Min(width, w));
        return cropped;
    }
}
