using System.Buffers.Binary;

namespace OpenBuzz.Audio;

/// How the two channels are arranged inside a sector's audio payload.
public enum VgpLayout
{
    /// First half of the payload is channel 0, second half channel 1.
    /// Confirmed by listening: this is what the disc actually uses.
    SplitHalves,
    /// 16-byte ADPCM blocks alternate between channels. Not observed; kept so
    /// the alternative stays testable via --layout.
    BlockInterleaved,
    /// A single channel spanning all 144 blocks of the sector.
    Mono,
}

public sealed record VgpProbe(
    int Sectors,
    int TrailingBytes,
    int BadPredictorBlocks,
    int TotalBlocks,
    ushort FirstTrailerMark)
{
    /// The sector model holds only if the file divides exactly and the audio
    /// region decodes as well-formed ADPCM.
    public bool StructureOk => TrailingBytes == 0 && BadPredictorBlocks == 0;

    public double SecondsAt(int sampleRate) =>
        (double)Sectors * VgpFile.SamplesPerSectorPerChannel / sampleRate;
}

/// <summary>
/// A .vgp is a stream of 2336-byte sectors: 2304 bytes of Sony 4-bit ADPCM
/// followed by a 32-byte trailer. There is no file header of any kind - no
/// magic, no sample rate, no channel count.
///
/// The audio is stereo, with the first 1152 bytes of each sector's payload
/// carrying the left channel and the second 1152 the right
/// (<see cref="VgpLayout.SplitHalves"/>). That was settled by ear, not by
/// analysis: an earlier attempt read the trailer's leading eight bytes as four
/// int16s of per-channel filter history, which would have made layout
/// self-checking, but no layout reproduces those values, so the trailer is
/// something else - most likely a seek record. It is skipped, not decoded.
/// </summary>
public static class VgpFile
{
    public const int SectorSize = 2336;
    public const int AudioBytes = 2304;
    public const int TrailerBytes = 32;
    public const int BlocksPerSector = AudioBytes / VagAdpcm.BlockSize;                  // 144
    public const int SamplesPerSectorPerChannel = BlocksPerSector / 2 * VagAdpcm.SamplesPerBlock; // 2016

    public const VgpLayout DefaultLayout = VgpLayout.SplitHalves;

    /// <summary>
    /// Not stored in the stream. 44100 matches how the clips sound and is the
    /// only plausible candidate consistent with the rest of the disc: the 124
    /// headered .vag files declare 11025, 22050 and 44100, and nothing anywhere
    /// uses 32000 or 48000.
    /// </summary>
    public const int DefaultSampleRate = 44100;

    /// <summary>
    /// Bit in the trailer's last uint16 that appears to mean "single channel".
    /// Across the disc the marker is 0x002C on every SONGCLIP entry and 0x012C
    /// on every NETSPEAK entry, and nothing else - music is stereo, speech mono.
    /// </summary>
    public const ushort MonoMarkerBit = 0x0100;

    public static int SectorCount(long length) => (int)(length / SectorSize);

    public static int ChannelsFor(VgpLayout layout) => layout == VgpLayout.Mono ? 1 : 2;

    /// <summary>Picks a layout from the trailer marker of the first sector.</summary>
    public static VgpLayout LayoutFor(ReadOnlySpan<byte> data)
    {
        if (data.Length < SectorSize) return DefaultLayout;
        ushort mark = BinaryPrimitives.ReadUInt16LittleEndian(data[(AudioBytes + TrailerBytes - 2)..]);
        return (mark & MonoMarkerBit) != 0 ? VgpLayout.Mono : VgpLayout.SplitHalves;
    }

    /// <summary>Checks the file against the sector model without decoding it fully.</summary>
    public static VgpProbe Probe(byte[] data)
    {
        int sectors = SectorCount(data.LongLength);
        int bad = 0, total = 0;

        for (int s = 0; s < sectors; s++)
        {
            int start = s * SectorSize;
            for (int b = 0; b < BlocksPerSector; b++)
            {
                total++;
                // A well-formed block names one of the five ADPCM filters.
                if ((data[start + b * VagAdpcm.BlockSize] >> 4 & 0x0F) > 4) bad++;
            }
        }

        ushort mark = sectors > 0
            ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(AudioBytes + TrailerBytes - 2))
            : (ushort)0;

        return new VgpProbe(sectors, (int)(data.LongLength % SectorSize), bad, total, mark);
    }

    /// <summary>Decodes the whole file to interleaved PCM.</summary>
    public static short[] Decode(byte[] data, VgpLayout layout, out int channels)
    {
        channels = ChannelsFor(layout);
        int sectors = SectorCount(data.LongLength);
        int perSector = BlocksPerSector * VagAdpcm.SamplesPerBlock;   // 4032 samples, all channels

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

    private static void DecodeSectorChannel(ReadOnlySpan<byte> data, int sector, int channel,
                                            VgpLayout layout, ref VagAdpcm.State st, short[] dst)
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

            VagAdpcm.DecodeBlock(data.Slice(baseOffset + block * VagAdpcm.BlockSize, VagAdpcm.BlockSize), tmp, ref st);

            for (int n = 0; n < VagAdpcm.SamplesPerBlock; n++)
                dst[(i * VagAdpcm.SamplesPerBlock + n) * channels + channel] = tmp[n];
        }
    }
}
