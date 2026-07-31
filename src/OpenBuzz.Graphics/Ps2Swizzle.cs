namespace OpenBuzz.Graphics;

/// <summary>
/// The PS2 GS stores palettised textures in a block/column interleave rather
/// than row order, so indices have to be un-shuffled before they mean anything.
/// </summary>
public static class Ps2Swizzle
{
    /// <summary>Undoes the PSMT8 (8-bit indexed) interleave.</summary>
    public static byte[] UnswizzlePsmt8(ReadOnlySpan<byte> src, int width, int height)
    {
        var dst = new byte[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int blockLocation = (y & ~0x0F) * width + (x & ~0x0F) * 2;
                int swapSelector = (((y + 2) >> 2) & 0x01) * 4;
                int posY = (((y & ~3) >> 1) + (y & 1)) & 0x07;
                int columnLocation = posY * width * 2 + ((x + swapSelector) & 0x07) * 4;
                int byteSelector = ((y >> 1) & 1) + ((x >> 2) & 2);

                int index = blockLocation + columnLocation + byteSelector;
                dst[y * width + x] = index < src.Length ? src[index] : (byte)0;
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
