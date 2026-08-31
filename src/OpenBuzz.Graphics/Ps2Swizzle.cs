namespace OpenBuzz.Graphics;

/// <summary>
/// The PS2 GS stores palettised textures in a block/column interleave rather
/// than row order, so indices have to be un-shuffled before they mean anything.
/// </summary>
public static class Ps2Swizzle
{
    // The block/column, bit-permutation, seriation and annealing models that
    // used to live here were all attempts to infer the swizzle from the images.
    // Each scored better without being right. librw's own transform replaced
    // them - see RwSwizzle - and this file keeps only the two pieces that were
    // never in doubt.
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
