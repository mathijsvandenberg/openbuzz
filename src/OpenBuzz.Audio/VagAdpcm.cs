namespace OpenBuzz.Audio;

/// <summary>
/// Sony PS2 4-bit ADPCM ("VAG"). Audio is a chain of 16-byte blocks: a
/// predictor/shift byte, a flag byte, then 14 bytes holding 28 nibbles.
/// </summary>
public static class VagAdpcm
{
    public const int BlockSize = 16;
    public const int SamplesPerBlock = 28;

    private static readonly double[] F0 = [0.0, 60.0 / 64.0, 115.0 / 64.0, 98.0 / 64.0, 122.0 / 64.0];
    private static readonly double[] F1 = [0.0, 0.0, -52.0 / 64.0, -55.0 / 64.0, -60.0 / 64.0];

    /// Per-channel filter history; carry it across blocks of the same channel.
    public struct State
    {
        public double Hist1, Hist2;
    }

    [Flags]
    public enum BlockFlag : byte
    {
        None = 0,
        LoopEnd = 1,
        LoopRegion = 2,
        LoopStart = 4,
    }

    public static byte FlagOf(ReadOnlySpan<byte> block) => block[1];

    /// True when the block marks the end of the stream (flag 1 or 7).
    public static bool IsTerminator(ReadOnlySpan<byte> block) => block[1] is 1 or 7;

    /// <summary>Decodes one 16-byte block into 28 samples.</summary>
    public static void DecodeBlock(ReadOnlySpan<byte> block, Span<short> dst, ref State st)
    {
        int shift = block[0] & 0x0F;
        int predictor = (block[0] >> 4) & 0x0F;
        if (predictor > 4) predictor = 0;   // out-of-range predictors occur in padding; treat as flat

        double f0 = F0[predictor], f1 = F1[predictor];

        for (int i = 0; i < 14; i++)
        {
            byte packed = block[2 + i];
            for (int half = 0; half < 2; half++)
            {
                int nibble = half == 0 ? packed & 0x0F : packed >> 4;

                // Sign-extend the nibble into the top of a 16-bit word, then scale.
                int s = nibble << 12;
                if ((s & 0x8000) != 0) s |= unchecked((int)0xFFFF0000);

                double sample = (s >> shift) + st.Hist1 * f0 + st.Hist2 * f1;
                st.Hist2 = st.Hist1;
                st.Hist1 = sample;

                dst[i * 2 + half] = (short)Math.Clamp(Math.Round(sample), short.MinValue, short.MaxValue);
            }
        }
    }

    /// <summary>
    /// Decodes a contiguous run of blocks belonging to a single channel.
    /// </summary>
    public static short[] DecodeChannel(ReadOnlySpan<byte> data, int startBlock, int blockStride, int blockCount)
    {
        var pcm = new short[blockCount * SamplesPerBlock];
        var st = new State();
        int written = 0;

        for (int i = 0; i < blockCount; i++)
        {
            int offset = (startBlock + i * blockStride) * BlockSize;
            if (offset + BlockSize > data.Length) break;
            DecodeBlock(data.Slice(offset, BlockSize), pcm.AsSpan(written, SamplesPerBlock), ref st);
            written += SamplesPerBlock;
        }

        return written == pcm.Length ? pcm : pcm[..written];
    }
}
