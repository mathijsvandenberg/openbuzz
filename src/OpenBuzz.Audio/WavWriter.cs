using System.Buffers.Binary;

namespace OpenBuzz.Audio;

/// Minimal 16-bit PCM RIFF writer — enough for anything downstream to consume.
public static class WavWriter
{
    public static void Write(string path, short[] interleaved, int channels, int sampleRate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        Write(fs, interleaved, channels, sampleRate);
    }

    public static void Write(Stream stream, short[] interleaved, int channels, int sampleRate)
    {
        int dataBytes = interleaved.Length * 2;
        Span<byte> h = stackalloc byte[44];

        "RIFF"u8.CopyTo(h);
        BinaryPrimitives.WriteInt32LittleEndian(h[4..], 36 + dataBytes);
        "WAVE"u8.CopyTo(h[8..]);
        "fmt "u8.CopyTo(h[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(h[16..], 16);           // PCM chunk size
        BinaryPrimitives.WriteInt16LittleEndian(h[20..], 1);            // format = PCM
        BinaryPrimitives.WriteInt16LittleEndian(h[22..], (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(h[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(h[28..], sampleRate * channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(h[32..], (short)(channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(h[34..], 16);           // bits per sample
        "data"u8.CopyTo(h[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(h[40..], dataBytes);

        stream.Write(h);

        var bytes = new byte[dataBytes];
        Buffer.BlockCopy(interleaved, 0, bytes, 0, dataBytes);
        stream.Write(bytes);
    }
}
