using System.Text;

namespace OpenBuzz.Graphics;

/// <summary>
/// RenderWare chunk ids, taken from librw's <c>src/rwbase.h</c> rather than
/// recalled. Core ids are the plugin id directly; the Criterion ones carry a
/// vendor field in the high bits.
/// </summary>
public static class RwId
{
    public const uint Struct = 0x01;
    public const uint String = 0x02;
    public const uint Extension = 0x03;
    public const uint Camera = 0x05;
    public const uint Texture = 0x06;
    public const uint Material = 0x07;
    public const uint MatList = 0x08;
    public const uint World = 0x0B;
    public const uint Matrix = 0x0D;
    public const uint FrameList = 0x0E;
    public const uint Geometry = 0x0F;
    public const uint Clump = 0x10;
    public const uint Light = 0x12;
    public const uint Atomic = 0x14;
    public const uint TextureNative = 0x15;
    public const uint TexDictionary = 0x16;
    public const uint Image = 0x18;
    public const uint GeometryList = 0x1A;
    public const uint AnimAnimation = 0x1B;
    public const uint RightToRender = 0x1F;
    public const uint UvAnimDict = 0x2B;

    // Criterion Toolkit plugins.
    public const uint Skin = 0x0116;
    public const uint HAnim = 0x011E;
    public const uint UserData = 0x011F;
    public const uint MatFx = 0x0120;
    public const uint Pds = 0x0131;
    public const uint Adc = 0x0134;
    public const uint UvAnimation = 0x0135;

    // Criterion World plugins.
    public const uint Mesh = 0x050E;
    public const uint NativeData = 0x0510;
    public const uint VertexFormat = 0x0511;

    public static string Name(uint id) => id switch
    {
        Struct => "STRUCT",
        String => "STRING",
        Extension => "EXTENSION",
        Camera => "CAMERA",
        Texture => "TEXTURE",
        Material => "MATERIAL",
        MatList => "MATLIST",
        World => "WORLD",
        Matrix => "MATRIX",
        FrameList => "FRAMELIST",
        Geometry => "GEOMETRY",
        Clump => "CLUMP",
        Light => "LIGHT",
        Atomic => "ATOMIC",
        TextureNative => "TEXTURENATIVE",
        TexDictionary => "TEXDICTIONARY",
        Image => "IMAGE",
        GeometryList => "GEOMETRYLIST",
        AnimAnimation => "ANIMANIMATION",
        RightToRender => "RIGHTTORENDER",
        UvAnimDict => "UVANIMDICT",
        Skin => "SKIN",
        HAnim => "HANIM",
        UserData => "USERDATA",
        MatFx => "MATFX",
        Pds => "PDS",
        Adc => "ADC",
        UvAnimation => "UVANIMATION",
        Mesh => "MESH",
        NativeData => "NATIVEDATA",
        VertexFormat => "VERTEXFMT",
        0x29 => "CHUNKGROUPSTART",
        0x2A => "CHUNKGROUPEND",
        _ => $"0x{id:X}",
    };

    /// <summary>
    /// Whether a chunk's payload is a further chunk stream rather than raw
    /// data. Descending into a leaf would read its bytes as bogus headers.
    /// </summary>
    public static bool HasChildren(uint id) => id is
        Clump or FrameList or GeometryList or Geometry or Atomic or
        MatList or Material or Texture or TexDictionary or
        Extension or Light or Camera or World or UvAnimDict;
}

/// A node in a RenderWare chunk tree.
public sealed record RwNode(uint Id, int Size, uint Version, int DataOffset, int Depth)
{
    public string Name => RwId.Name(Id);
    public int End => DataOffset + Size;
    public List<RwNode> Children { get; } = [];
}

/// <summary>
/// Walks a RenderWare stream (`.rp2`, `.dff`, `.txd`) into a chunk tree.
///
/// These files are ordinary RenderWare streams: a texture dictionary of PS2
/// native textures followed by chunk groups holding clumps and animations. The
/// embedded TEXTURENATIVE payloads are byte-identical in layout to the
/// standalone `.tex` files, so <see cref="Ps2Texture"/> reads them unchanged.
/// </summary>
public static class RwStream
{
    public const int HeaderSize = 12;

    public static List<RwNode> Parse(byte[] data, int maxDepth = 8) =>
        ParseRange(data, 0, data.Length, 0, maxDepth);

    private static List<RwNode> ParseRange(byte[] data, int start, int end, int depth, int maxDepth)
    {
        var nodes = new List<RwNode>();
        int offset = start;

        while (offset + HeaderSize <= end)
        {
            uint id = BitConverter.ToUInt32(data, offset);
            int size = BitConverter.ToInt32(data, offset + 4);
            uint version = BitConverter.ToUInt32(data, offset + 8);
            int dataOffset = offset + HeaderSize;

            // A bad size means we have walked into raw data; stop rather than
            // emit nonsense.
            if (size < 0 || dataOffset + size > end) break;

            var node = new RwNode(id, size, version, dataOffset, depth);
            if (depth < maxDepth && RwId.HasChildren(id) && size >= HeaderSize)
                node.Children.AddRange(ParseRange(data, dataOffset, dataOffset + size, depth + 1, maxDepth));

            nodes.Add(node);
            offset = node.End;
        }

        return nodes;
    }

    public static IEnumerable<RwNode> Flatten(IEnumerable<RwNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }

    /// Reads a STRING chunk's text.
    public static string ReadString(byte[] data, RwNode node)
    {
        var span = data.AsSpan(node.DataOffset, node.Size);
        int nul = span.IndexOf((byte)0);
        return Encoding.Latin1.GetString(nul >= 0 ? span[..nul] : span);
    }
}
