using System.Buffers.Binary;

namespace OpenBuzz.Audio;

/// How the two channels are arranged inside a sector's audio payload.
public enum VgpLayout
{
    /// First half of the payload is channel 0, second half channel 1.
    SplitHalves,
    /// 16-byte ADPCM blocks alternate between channels.
    BlockInterleaved,
    /// A single channel: all 144 blocks of the sector are one continuous stream.
    Mono,
}

public sealed record VgpSectorCheck(int Sector, int Channel, short Expected1, short Expected2, short Actual1, short Actual2)
{
    public bool Matches => Expected1 == Actual1 && Expected2 == Actual2;
}

public sealed record VgpProbe(
    int Sectors,
    int TrailingBytes,
    bool MarkerOk,
    VgpLayout Layout,
    int SplitMatches,
    int InterleavedMatches,
    int MonoMatches,
    int Checked)
{
    public int BestMatches => Math.Max(MonoMatches, Math.Max(SplitMatches, InterleavedMatches));
    public bool Confident => Checked > 0 && BestMatches == Checked;
}

/// <summary>
/// A .vgp is a stream of 2336-byte sectors: 2304 bytes of Sony 4-bit ADPCM
/// followed by a 32-byte trailer. There is no file header at all — no magic, no
/// sample rate, no channel count.
///
/// The trailer is what makes the format tractable. Its first eight bytes are
/// four little-endian int16s holding the ADPCM filter history for two channels,
/// so a correct decode must reproduce them exactly. That turns channel layout
/// from a guess into a checkable property, and <see cref="Probe"/> uses it to
/// pick the layout rather than relying on a heuristic.
/// </summary>
public static class VgpFile
{
    public const int SectorSize = 2336;
    public const int AudioBytes = 2304;
    public const int TrailerBytes = 32;
    public const int BlocksPerSector = AudioBytes / VagAdpcm.BlockSize;   // 144
    public const int Channels = 2;

    /// Constant found in the last two bytes of every trailer seen so far.
    public const ushort TrailerMarker = 0x002C;

    /// <summary>
    /// Sample rate is not stored anywhere in the stream. The disc's headered
    /// .vag files declare 11025/22050/44100; song clips sound like full-rate
    /// music, so this is the working default until confirmed by ear.
    /// </summary>
    public const int DefaultSampleRate = 44100;

    public static int SectorCount(long length) => (int)(length / SectorSize);

    private static short TrailerHistory(ReadOnlySpan<byte> file, int sector, int index) =>
        BinaryPrimitives.ReadInt16LittleEndian(file[(sector * SectorSize + AudioBytes + index * 2)..]);

    private static ushort TrailerMark(ReadOnlySpan<byte> file, int sector) =>
        BinaryPrimitives.ReadUInt16LittleEndian(file[(sector * SectorSize + AudioBytes + 30)..]);

    /// <summary>
    /// Works out the channel layout by decoding sectors under each candidate and
    /// comparing the resulting filter history against the value the next
    /// trailer records.
    /// </summary>
    public static VgpProbe Probe(byte[] data)
    {
        int sectors = SectorCount(data.LongLength);
        bool markerOk = true;
        for (int s = 0; s < Math.Min(sectors, 32); s++)
            if (TrailerMark(data, s) != TrailerMarker) markerOk = false;

        int split = 0, inter = 0, mono = 0, checkedCount = 0;

        // Trailer of sector s records the state entering sector s, so the
        // history after decoding sector s must equal the trailer of s+1.
        for (int s = 0; s < Math.Min(sectors - 1, 24); s++)
        {
            foreach (var layout in new[] { VgpLayout.SplitHalves, VgpLayout.BlockInterleaved, VgpLayout.Mono })
            {
                int channels = ChannelsFor(layout);
                bool all = true;
                for (int ch = 0; ch < channels; ch++)
                {
                    var st = LoadState(data, s, ch);
                    DecodeSectorChannel(data, s, ch, layout, ref st, null);
                    short a1 = Sat(st.Hist1), a2 = Sat(st.Hist2);
                    if (a1 != TrailerHistory(data, s + 1, ch * 2) || a2 != TrailerHistory(data, s + 1, ch * 2 + 1))
                        all = false;
                }
                if (all)
                {
                    if (layout == VgpLayout.SplitHalves) split++;
                    else if (layout == VgpLayout.BlockInterleaved) inter++;
                    else mono++;
                }
            }
            checkedCount++;
        }

        var chosen = mono >= split && mono >= inter ? VgpLayout.Mono
                   : inter > split ? VgpLayout.BlockInterleaved
                   : VgpLayout.SplitHalves;
        return new VgpProbe(sectors, (int)(data.LongLength % SectorSize), markerOk, chosen,
                            split, inter, mono, checkedCount);
    }

    private static VagAdpcm.State LoadState(ReadOnlySpan<byte> data, int sector, int channel) => new()
    {
        Hist1 = TrailerHistory(data, sector, channel * 2),
        Hist2 = TrailerHistory(data, sector, channel * 2 + 1),
    };

    private static short Sat(double v) => (short)Math.Clamp(Math.Round(v), short.MinValue, short.MaxValue);

    /// Decodes one channel of one sector, optionally writing samples out.
    public static int ChannelsFor(VgpLayout layout) => layout == VgpLayout.Mono ? 1 : 2;

    private static void DecodeSectorChannel(ReadOnlySpan<byte> data, int sector, int channel,
                                            VgpLayout layout, ref VagAdpcm.State st, short[]? dst)
    {
        int baseOffset = sector * SectorSize;
        int channels = ChannelsFor(layout);
        int perChannel = BlocksPerSector / channels;
        Span<short> tmp = stackalloc short[VagAdpcm.SamplesPerBlock];

        for (int i = 0; i < perChannel; i++)
        {
            int block = layout switch
            {
                VgpLayout.SplitHalves => channel * perChannel + i,
                VgpLayout.BlockInterleaved => i * channels + channel,
                _ => i,
            };

            var src = data.Slice(baseOffset + block * VagAdpcm.BlockSize, VagAdpcm.BlockSize);
            VagAdpcm.DecodeBlock(src, tmp, ref st);

            if (dst is not null)
                for (int n = 0; n < VagAdpcm.SamplesPerBlock; n++)
                    dst[(i * VagAdpcm.SamplesPerBlock + n) * channels + channel] = tmp[n];
        }
    }

    /// <summary>Decodes the whole file to interleaved stereo PCM.</summary>
    public static short[] Decode(byte[] data, VgpLayout layout, out int channels)
    {
        channels = ChannelsFor(layout);
        int sectors = SectorCount(data.LongLength);
        int perSector = BlocksPerSector * VagAdpcm.SamplesPerBlock;   // 4032 samples per sector, all channels

        var pcm = new short[sectors * perSector];
        var sectorBuf = new short[perSector];
        var states = new VagAdpcm.State[channels];

        for (int s = 0; s < sectors; s++)
        {
            for (int ch = 0; ch < channels; ch++)
                DecodeSectorChannel(data, s, ch, layout, ref states[ch], sectorBuf);

            sectorBuf.CopyTo(pcm, s * perSector);
        }

        return pcm;
    }
}
