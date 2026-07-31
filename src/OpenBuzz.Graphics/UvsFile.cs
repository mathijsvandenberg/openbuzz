using System.Buffers.Binary;
using System.Text;

namespace OpenBuzz.Graphics;

/// A named sub-rectangle of a texture atlas, in normalised UV coordinates.
public readonly record struct UvRect(string Name, float U0, float V0, float U1, float V1)
{
    public float Width => U1 - U0;
    public float Height => V1 - V0;

    /// Pixel bounds within a texture of the given size.
    public (int X, int Y, int W, int H) ToPixels(int textureWidth, int textureHeight) =>
        ((int)MathF.Round(U0 * textureWidth),
         (int)MathF.Round(V0 * textureHeight),
         (int)MathF.Round(Width * textureWidth),
         (int)MathF.Round(Height * textureHeight));
}

/// <summary>
/// A `.uvs` atlas index.
///
/// Header: a name field holding a tag, then a version byte and an entry count.
/// Each entry is a name field followed by four floats — u0, v0, u1, v1.
///
/// A name field is a length byte, then that many bytes of NUL-terminated text,
/// then padding so the text occupies a multiple of four bytes. The padding is
/// measured from the start of the text, not from the file offset, so the floats
/// are frequently not 4-byte aligned within the file.
/// </summary>
public sealed class UvsFile
{
    public required string Tag { get; init; }
    public required int Version { get; init; }
    public required int DeclaredCount { get; init; }
    public required IReadOnlyList<UvRect> Rects { get; init; }

    public static UvsFile Load(string path) => Parse(File.ReadAllBytes(path));

    public static UvsFile Parse(byte[] d)
    {
        int p = 0;
        string tag = ReadNameField(d, ref p);

        int version = p < d.Length ? d[p++] : 0;
        int count = p < d.Length ? d[p++] : 0;

        var rects = new List<UvRect>(count);
        // Parse to the end rather than trusting the count, then report both.
        while (p + 1 < d.Length)
        {
            string name = ReadNameField(d, ref p);
            if (name.Length == 0 || p + 16 > d.Length) break;

            rects.Add(new UvRect(name,
                ReadFloat(d, ref p), ReadFloat(d, ref p),
                ReadFloat(d, ref p), ReadFloat(d, ref p)));
        }

        return new UvsFile { Tag = tag, Version = version, DeclaredCount = count, Rects = rects };
    }

    private static string ReadNameField(byte[] d, ref int p)
    {
        if (p >= d.Length) return "";
        int len = d[p++];
        if (len <= 0 || p + len > d.Length) return "";

        var s = Encoding.Latin1.GetString(d, p, len).TrimEnd('\0');
        // Padding is always at least one byte, so a length already divisible by
        // four still advances a whole extra word.
        p += (len + 4) & ~3;
        return s;
    }

    private static float ReadFloat(byte[] d, ref int p)
    {
        float v = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p)));
        p += 4;
        return v;
    }
}
