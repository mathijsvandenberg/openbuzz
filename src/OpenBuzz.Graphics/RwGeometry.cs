using System.Buffers.Binary;

namespace OpenBuzz.Graphics;

/// A triangle as RenderWare stores it, already reordered to winding order.
public readonly record struct RwTriangle(ushort A, ushort B, ushort C, ushort Material);

/// <summary>
/// One mesh out of a `.rp2` clump.
///
/// RenderWare geometry comes in two shapes and the format word says which. Bit
/// 24 is the "native" flag: when it is clear the vertex data sits in the
/// GEOMETRY chunk's own STRUCT as plain float arrays, and when it is set the
/// STRUCT is a 40-byte stub and the real data lives in a platform-specific
/// extension - on PS2, VU1 DMA chains.
///
/// This reads the plain form, which is what all 253 non-native geometries use,
/// including every character costume in the game. The 715 native ones are not
/// handled yet and report <see cref="IsNative"/>.
/// </summary>
public sealed class RwGeometry
{
    // Format flags, from librw's Geometry::Flags.
    public const int FlagTriStrip = 0x01;
    public const int FlagPositions = 0x02;
    public const int FlagTextured = 0x04;
    public const int FlagPrelit = 0x08;
    public const int FlagNormals = 0x10;
    public const int FlagTextured2 = 0x80;

    public required bool IsNative { get; init; }
    public required int Flags { get; init; }
    public required int NumUVs { get; init; }
    public required int VertexCount { get; init; }

    /// xyz per vertex.
    public float[] Positions { get; init; } = [];

    /// xyz per vertex, empty when the geometry carries none.
    public float[] Normals { get; init; } = [];

    /// uv per vertex, first channel only.
    public float[] TexCoords { get; init; } = [];

    /// RGBA per vertex, packed little-endian.
    public uint[] Colours { get; init; } = [];

    public RwTriangle[] Triangles { get; init; } = [];

    /// Texture name per material index, empty string where a material has none.
    public string[] MaterialTextures { get; init; } = [];

    public static RwGeometry Parse(byte[] d, RwNode geometry)
    {
        var children = RwStream.Parse(d.AsSpan(geometry.DataOffset, geometry.Size).ToArray(), 0);
        int structAt = geometry.DataOffset + RwStream.HeaderSize;

        int p = structAt;
        uint format = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p));
        int triangleCount = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p + 4));
        int vertexCount = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p + 8));
        p += 16;   // the fourth word is the morph target count, always 1 here

        int flags = (int)(format & 0xFFFF);
        int numUVs = (int)((format >> 16) & 0xFF);
        bool native = ((format >> 24) & 0xFF) != 0;

        var textures = ReadMaterialTextures(d, geometry);

        if (native)
        {
            return new RwGeometry
            {
                IsNative = true, Flags = flags, NumUVs = numUVs,
                VertexCount = vertexCount, MaterialTextures = textures,
            };
        }

        // This build writes no ambient/specular/diffuse trio - the struct sizes
        // only add up without it, exactly, for all 253 plain geometries.
        var colours = Array.Empty<uint>();
        if ((flags & FlagPrelit) != 0)
        {
            colours = new uint[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                colours[i] = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p + i * 4));
            p += vertexCount * 4;
        }

        var uvs = Array.Empty<float>();
        if ((flags & (FlagTextured | FlagTextured2)) != 0)
        {
            // Only the first channel is kept; the rest are skipped.
            uvs = new float[vertexCount * 2];
            for (int i = 0; i < uvs.Length; i++) uvs[i] = BitConverter.ToSingle(d, p + i * 4);
            p += numUVs * vertexCount * 8;
        }

        var triangles = new RwTriangle[triangleCount];
        for (int i = 0; i < triangleCount; i++)
        {
            int t = p + i * 8;
            // Stored as vertex2, vertex1, material, vertex3.
            triangles[i] = new RwTriangle(
                BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(t + 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(t)),
                BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(t + 6)),
                BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(t + 4)));
        }
        p += triangleCount * 8;

        p += 16;   // morph target bounding sphere
        bool hasVertices = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p)) != 0;
        bool hasNormals = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p + 4)) != 0;
        p += 8;

        var positions = Array.Empty<float>();
        if (hasVertices)
        {
            positions = new float[vertexCount * 3];
            for (int i = 0; i < positions.Length; i++) positions[i] = BitConverter.ToSingle(d, p + i * 4);
            p += vertexCount * 12;
        }

        var normals = Array.Empty<float>();
        if (hasNormals)
        {
            normals = new float[vertexCount * 3];
            for (int i = 0; i < normals.Length; i++) normals[i] = BitConverter.ToSingle(d, p + i * 4);
        }

        return new RwGeometry
        {
            IsNative = false,
            Flags = flags,
            NumUVs = numUVs,
            VertexCount = vertexCount,
            Positions = positions,
            Normals = normals,
            TexCoords = uvs,
            Colours = colours,
            Triangles = triangles,
            MaterialTextures = textures,
        };
    }

    /// Pulls the texture name out of each MATERIAL in the geometry's MATLIST.
    private static string[] ReadMaterialTextures(byte[] d, RwNode geometry)
    {
        var matList = geometry.Children.FirstOrDefault(c => c.Id == RwId.MatList);
        if (matList is null) return [];

        var names = new List<string>();
        foreach (var material in matList.Children.Where(c => c.Id == RwId.Material))
        {
            var texture = material.Children.FirstOrDefault(c => c.Id == RwId.Texture);
            var name = texture?.Children.FirstOrDefault(c => c.Id == RwId.String);
            names.Add(name is null ? "" : RwStream.ReadString(d, name));
        }
        return [.. names];
    }

    /// Every geometry in a stream, in file order.
    public static List<RwGeometry> LoadAll(byte[] data) =>
        [.. RwStream.Flatten(RwStream.Parse(data))
                    .Where(n => n.Id == RwId.Geometry)
                    .Select(n => Parse(data, n))];
}
