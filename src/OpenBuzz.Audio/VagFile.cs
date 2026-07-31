using System.Buffers.Binary;
using System.Text;

namespace OpenBuzz.Audio;

/// <summary>
/// A .vag file: 48-byte big-endian header followed by ADPCM blocks. These carry
/// their own sample rate, which is what makes them useful for calibrating the
/// headerless .vgp streams.
/// </summary>
public sealed class VagFile
{
    public const int HeaderSize = 0x30;

    public int Version { get; init; }
    public int DataSize { get; init; }
    public int SampleRate { get; init; }
    public string Name { get; init; } = "";
    public byte[] Data { get; init; } = [];

    public static bool LooksLikeVag(ReadOnlySpan<byte> d) =>
        d.Length >= HeaderSize && d[0] == 'V' && d[1] == 'A' && d[2] == 'G' && (d[3] == 'p' || d[3] == 'i');

    public static VagFile Parse(byte[] d)
    {
        if (!LooksLikeVag(d)) throw new InvalidDataException("Not a VAG file.");

        int dataSize = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(0x0C));
        int rate = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(0x10));

        int available = d.Length - HeaderSize;
        if (dataSize <= 0 || dataSize > available) dataSize = available;

        var name = Encoding.Latin1.GetString(d, 0x20, 16).TrimEnd('\0', ' ');

        return new VagFile
        {
            Version = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(0x04)),
            DataSize = dataSize,
            SampleRate = rate,
            Name = name,
            Data = d[HeaderSize..(HeaderSize + dataSize)],
        };
    }

    public short[] DecodeMono() =>
        VagAdpcm.DecodeChannel(Data, 0, 1, Data.Length / VagAdpcm.BlockSize);
}
