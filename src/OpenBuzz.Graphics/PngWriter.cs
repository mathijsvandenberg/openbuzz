using System.Buffers.Binary;
using System.IO.Compression;

namespace OpenBuzz.Graphics;

/// <summary>
/// Minimal RGBA8888 PNG encoder. Written by hand so the project stays free of
/// image-library dependencies; ZLibStream supplies the only hard part.
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static void Write(string path, uint[] rgba, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        Write(fs, rgba, width, height);
    }

    public static void Write(Stream stream, uint[] rgba, int width, int height)
    {
        stream.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;    // bit depth
        ihdr[9] = 6;    // colour type: truecolour with alpha
        ihdr[10] = 0;   // deflate
        ihdr[11] = 0;   // no filtering beyond per-scanline
        ihdr[12] = 0;   // no interlace
        WriteChunk(stream, "IHDR", ihdr);

        // Each scanline is prefixed with its filter byte; 0 means "none".
        var raw = new byte[height * (1 + width * 4)];
        int p = 0;
        for (int y = 0; y < height; y++)
        {
            raw[p++] = 0;
            for (int x = 0; x < width; x++)
            {
                uint c = rgba[y * width + x];
                raw[p++] = (byte)c;
                raw[p++] = (byte)(c >> 8);
                raw[p++] = (byte)(c >> 16);
                raw[p++] = (byte)(c >> 24);
            }
        }

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(raw);

        WriteChunk(stream, "IDAT", compressed.ToArray());
        WriteChunk(stream, "IEND", []);
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        Span<byte> tag = [(byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3]];
        stream.Write(tag);
        stream.Write(data);

        uint crc = Crc32.Compute(tag, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }
}

internal static class Crc32
{
    private static readonly uint[] Table = Build();

    private static uint[] Build()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte x in a) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (byte x in b) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
