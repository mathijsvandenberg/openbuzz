using System.Buffers.Binary;
using System.Text;

namespace OpenBuzz.Graphics;

/// One glyph: its rectangle in the atlas, in normalised UV, plus its advance.
public sealed record RwGlyph(float U0, float V0, float U1, float V1, float Advance);

/// <summary>
/// A bitmap font from a `Font*.rp2` stream.
///
/// The stream is an ordinary RenderWare chunk stream, but the font chunks sit
/// after the texture dictionary and use a Buzz-specific id (0x199), each one
/// preceded by a CHUNKGROUPSTART carrying its name as a STRING. The name is the
/// one the scripts ask for: `GenericData.lua` sets QuestionFontName to
/// "GeneralLarge", ClipboardTitleFontName to "ClipboardSmall", and so on.
///
/// The font chunk's declared size is wrong - it reads 3111 for fonts whose data
/// is 19,513 bytes - so the extent is computed from the header instead, which
/// lands exactly on the following CHUNKGROUPEND for all six.
/// </summary>
public sealed class RwFont
{
    /// Buzz's font chunk id.
    public const uint ChunkId = 0x199;

    private const uint GroupStart = 0x29;
    private const uint StringId = 0x02;
    private const int HeaderSize = 0x24;
    private const int GlyphRecord = 21;   // 5 floats, then one always-zero byte
    private const int NameField = 32;

    /// The script-facing name, e.g. "GeneralLarge".
    public required string Name { get; init; }

    /// The atlas this font draws from, e.g. "SynBol18.png".
    public required string TextureName { get; init; }

    /// <summary>
    /// Line height in pixels at native size. Note this is the *interior*
    /// height: the glyph cell is one pixel taller, and rows in the atlas are
    /// spaced <see cref="LineStep"/> apart.
    /// </summary>
    public required float LineHeight { get; init; }

    /// <summary>
    /// Cell height in pixels - the distance between atlas rows, and the right
    /// spacing between lines of text.
    ///
    /// Every rectangle in the table sits on a half-texel, because the UVs
    /// address texel centres: the first texel of a cell is at
    /// <c>u0 * width - 0.5</c> and the last at <c>u1 * width - 0.5</c>, so the
    /// cell spans one more pixel than the difference between them.
    /// </summary>
    public float LineStep => LineHeight + 1f;

    /// <summary>
    /// How far the pen moves for this glyph, in pixels at native size. Cells
    /// tile the atlas exactly at this stride, so it is also the cell width.
    /// </summary>
    public float AdvanceOf(RwGlyph glyph) => glyph.Advance * LineHeight + 1f;

    public required RwGlyph[] Glyphs { get; init; }

    /// Character code to glyph index; 0xFFFF where the font has no glyph.
    public required ushort[] CharMap { get; init; }

    /// Character codes are biased by this before indexing <see cref="CharMap"/>.
    public required int CharBias { get; init; }

    public bool TryGetGlyph(char ch, out RwGlyph glyph)
    {
        if (TryGetIndex(ch, out int i)) { glyph = Glyphs[i]; return true; }
        glyph = default!;
        return false;
    }

    /// The glyph index for a character, or false where the font has none.
    public bool TryGetIndex(char ch, out int index)
    {
        index = -1;
        int i = ch + CharBias;
        if (i < 0 || i >= CharMap.Length) return false;

        ushort g = CharMap[i];
        if (g == 0xFFFF || g >= Glyphs.Length) return false;

        index = g;
        return true;
    }

    /// Width of a string in pixels at native size. Unmapped characters take a
    /// quarter of a line height, which is what the missing-glyph gap looks like.
    public float Measure(string text)
    {
        float w = 0;
        foreach (char ch in text)
            w += TryGetGlyph(ch, out var g) ? AdvanceOf(g) : LineHeight * 0.25f;
        return w;
    }

    public static List<RwFont> Load(string path) => ParseAll(File.ReadAllBytes(path));

    public static List<RwFont> ParseAll(byte[] d)
    {
        var fonts = new List<RwFont>();
        string pending = "";

        int o = 0;
        while (o + 12 <= d.Length)
        {
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o));
            int size = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(o + 4));
            int data = o + 12;

            if (id == GroupStart && size >= 16 && data + size <= d.Length)
            {
                // Payload is a count followed by a STRING chunk holding the
                // name of the font that comes next.
                int s = data + 4;
                if (BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(s)) == StringId)
                {
                    int len = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(s + 4));
                    if (len > 0 && s + 12 + len <= d.Length) pending = ReadFixedString(d, s + 12, len);
                }
                o = data + size;
                continue;
            }

            if (id == ChunkId)
            {
                fonts.Add(Parse(d, data, pending, out int end));
                pending = "";
                o = end;
                continue;
            }

            if (size < 0 || data + size > d.Length) break;
            o = data + size;
        }

        return fonts;
    }

    private static RwFont Parse(byte[] d, int at, string name, out int end)
    {
        // +0x08 line height, +0x1C glyph count, +0x20 character bias.
        float lineHeight = BitConverter.ToSingle(d, at + 0x08);
        int glyphCount = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(at + 0x1C));
        int bias = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(at + 0x20));

        // The map always runs from -128 to the bias, so it holds bias + 128
        // entries. Characters are biased into it, which is how the high Latin-1
        // letters land below the ASCII block.
        int mapCount = bias + 128;
        var map = new ushort[mapCount];
        for (int i = 0; i < mapCount; i++)
            map[i] = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(at + HeaderSize + i * 2));

        int table = at + HeaderSize + mapCount * 2;
        var glyphs = new RwGlyph[glyphCount];
        for (int i = 0; i < glyphCount; i++)
        {
            int r = table + i * GlyphRecord;
            glyphs[i] = new RwGlyph(
                BitConverter.ToSingle(d, r),
                BitConverter.ToSingle(d, r + 4),
                BitConverter.ToSingle(d, r + 8),
                BitConverter.ToSingle(d, r + 12),
                BitConverter.ToSingle(d, r + 16));
        }

        // A count, then the atlas name in a fixed 32-byte field.
        int nameAt = table + glyphCount * GlyphRecord + 4;
        end = nameAt + NameField;

        return new RwFont
        {
            Name = name,
            TextureName = ReadFixedString(d, nameAt, NameField),
            LineHeight = lineHeight,
            Glyphs = glyphs,
            CharMap = map,
            CharBias = bias,
        };
    }

    private static string ReadFixedString(byte[] d, int at, int len)
    {
        var span = d.AsSpan(at, Math.Min(len, d.Length - at));
        int nul = span.IndexOf((byte)0);
        return Encoding.Latin1.GetString(nul >= 0 ? span[..nul] : span);
    }
}
