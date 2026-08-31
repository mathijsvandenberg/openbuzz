using System.Buffers.Binary;

namespace OpenBuzz.Graphics;

/// <summary>
/// Reads PS2 native geometry: the vertex data as VU1 DMA chains.
///
/// Ported from rwtools, src/ps2native.cpp - readPs2NativeData, readData and
/// deleteOverlapping - because the layout is not something to infer. It is a
/// DMA program, and the vertex format varies from block to block.
///
/// The shape of it: each split is a chain of 16-byte tags. Section A tags point
/// at one block covering the whole split, section B tags carry per-block vertex
/// data inline. Consecutive blocks overlap by two vertices so the triangle strip
/// continues across them, and the overlap is trimmed when a tag says the block
/// was not the last.
/// </summary>
public static class RwPs2Native
{
    private const float NormalScale = 1f / 128f;
    private const float VertexScalePrelit = 1f / 128f;
    private const float VertexScale = 1f / 1024f;
    private const float UvScale = 1f / 4096f;

    public sealed class Mesh
    {
        public List<float> Positions { get; } = [];
        public List<float> Normals { get; } = [];
        public List<float> TexCoords { get; } = [];
        public List<uint> Colours { get; } = [];

        /// Four weights per vertex, with the bone index packed into the low
        /// bits of each weight's float representation.
        public List<float> Weights { get; } = [];
        public List<byte> BoneIndices { get; } = [];

        /// One triangle strip per split, in the order the splits appear.
        public List<List<int>> Strips { get; } = [];

        public List<string> Unknown { get; } = [];
    }

    /// <param name="splitIndexCounts">Index count per split, from BINMESH.</param>
    public static Mesh Read(byte[] d, int dataStart, int dataEnd, int[] splitIndexCounts, bool prelit, int numUVs)
    {
        var mesh = new Mesh();

        // STRUCT header, then the platform id.
        int p = dataStart + RwStream.HeaderSize + 4;
        int index = 0;

        foreach (int splitIndices in splitIndexCounts)
        {
            if (p + 8 > dataEnd) break;

            int splitSize = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p));
            p += 8;                       // the second word is hasNoSectionAData

            int blockStart = p;
            int end = Math.Min(p + splitSize, dataEnd);
            var strip = new List<int>();
            var typesRead = new List<uint>();

            bool sectionALast = false, sectionBLast = false, dataAread = false;

            while (p < end)
            {
                int before = p;

                // Section A: a pointer to one block covering the whole split.
                bool reachedEnd = false;
                while (!reachedEnd && !sectionALast && p + 16 <= end)
                {
                    byte tag = d[p + 3];
                    uint word1 = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p + 4));
                    uint type = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p + 12));
                    p += 16;

                    switch (tag)
                    {
                        case 0x30:
                            if (!dataAread)
                            {
                                int at = blockStart + (int)word1 * 0x10;
                                if (at >= 0 && at + 16 <= dataEnd)
                                    ReadData(d, at, splitIndices, type, mesh, strip, ref index, prelit, numUVs);
                            }
                            p += 16;      // the pointer tag is followed by a dummy
                            break;

                        case 0x60:
                            sectionALast = true;
                            reachedEnd = true;
                            dataAread = true;
                            break;

                        case 0x10:
                            reachedEnd = true;
                            dataAread = true;
                            break;
                    }
                }

                // Section B: inline vertex data, block by block.
                reachedEnd = false;
                while (!reachedEnd && !sectionBLast && p + 16 <= end)
                {
                    byte tag = d[p + 3];
                    byte count = d[p + 14];
                    byte b11 = d[p + 11], b15 = d[p + 15];
                    uint type = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p + 12));
                    p += 16;

                    switch (tag)
                    {
                        case 0x00:
                        case 0x07:
                            p = ReadData(d, p, count, type, mesh, strip, ref index, prelit, numUVs);
                            typesRead.Add(type);
                            break;

                        case 0x04:
                            if (b11 == 0x11 && (b15 == 0x11 || b15 == 0x06))
                            {
                                p = end;
                                typesRead.Clear();
                                sectionBLast = true;
                            }
                            else if (b11 == 0 && b15 == 0)
                            {
                                // Not the last block: the next one repeats its
                                // final two vertices to keep the strip going.
                                DeleteOverlapping(mesh, strip, typesRead, ref index);
                                typesRead.Clear();
                            }
                            reachedEnd = true;
                            break;
                    }
                }

                if (p <= before) break;   // no progress; refuse to spin
            }

            mesh.Strips.Add(strip);
            p = end;
        }

        return mesh;
    }

    /// Reads one block and returns the position after it, padding included.
    private static int ReadData(byte[] d, int p, int count, uint rawType, Mesh mesh,
                                List<int> strip, ref int index, bool prelit, int numUVs)
    {
        uint type = rawType & 0xFF00FFFF;
        float vertexScale = prelit ? VertexScalePrelit : VertexScale;
        int size;

        switch (type)
        {
            case 0x68008000:                                  // float32 positions
                size = 12;
                if (!Fits(d, p, count, size)) return p;
                for (int j = 0; j < count; j++)
                {
                    int o = p + j * 12;
                    mesh.Positions.Add(BitConverter.ToSingle(d, o));
                    mesh.Positions.Add(BitConverter.ToSingle(d, o + 4));
                    mesh.Positions.Add(BitConverter.ToSingle(d, o + 8));
                    strip.Add(index++);
                }
                break;

            case 0x6D008000:                                  // int16 positions
                size = 8;
                if (!Fits(d, p, count, size)) return p;
                for (int j = 0; j < count; j++)
                {
                    int o = p + j * 8;
                    mesh.Positions.Add(BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(o)) * vertexScale);
                    mesh.Positions.Add(BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(o + 2)) * vertexScale);
                    mesh.Positions.Add(BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(o + 4)) * vertexScale);

                    // 0x8000 restarts the strip, done with two degenerates.
                    if (BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o + 6)) == 0x8000 && index > 0)
                    {
                        strip.Add(index - 1);
                        strip.Add(index - 1);
                    }
                    strip.Add(index++);
                }
                break;

            case 0x64008001:                                  // float32 UVs
                size = 8;
                if (!Fits(d, p, count, size)) return p;
                for (int j = 0; j < count; j++)
                {
                    mesh.TexCoords.Add(BitConverter.ToSingle(d, p + j * 8));
                    mesh.TexCoords.Add(BitConverter.ToSingle(d, p + j * 8 + 4));
                }
                break;

            case 0x6D008001:                                  // int16 UVs, every channel
                size = 4 * Math.Max(numUVs, 1);
                if (!Fits(d, p, count, size)) return p;
                for (int j = 0; j < count; j++)
                {
                    int o = p + j * size;
                    mesh.TexCoords.Add(BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(o)) * UvScale);
                    mesh.TexCoords.Add(BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(o + 2)) * UvScale);
                }
                break;

            case 0x65008001:                                  // int16 UVs, one channel
                size = 4;
                if (!Fits(d, p, count, size)) return p;
                for (int j = 0; j < count; j++)
                {
                    mesh.TexCoords.Add(BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(p + j * 4)) * UvScale);
                    mesh.TexCoords.Add(BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(p + j * 4 + 2)) * UvScale);
                }
                break;

            case 0x6D00C002:                                  // day and night colours
                size = 8;
                if (!Fits(d, p, count, size)) return p;
                for (int j = 0; j < count; j++)
                {
                    int o = p + j * 8;
                    mesh.Colours.Add((uint)(d[o] | (d[o + 2] << 8) | (d[o + 4] << 16) | (d[o + 6] << 24)));
                }
                break;

            case 0x6E00C002:                                  // colours
                size = 4;
                if (!Fits(d, p, count, size)) return p;
                for (int j = 0; j < count; j++)
                    mesh.Colours.Add(BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p + j * 4)));
                break;

            case 0x6E008002:
            case 0x6E008003:                                  // int8 normals, padded to 4
                size = 4;
                if (!Fits(d, p, count, size)) return p;
                for (int j = 0; j < count; j++)
                {
                    int o = p + j * 4;
                    mesh.Normals.Add((sbyte)d[o] * NormalScale);
                    mesh.Normals.Add((sbyte)d[o + 1] * NormalScale);
                    mesh.Normals.Add((sbyte)d[o + 2] * NormalScale);
                }
                break;

            case 0x6A008003:                                  // int8 normals, packed
                size = 3;
                if (!Fits(d, p, count, size)) return p;
                for (int j = 0; j < count; j++)
                {
                    int o = p + j * 3;
                    mesh.Normals.Add((sbyte)d[o] * NormalScale);
                    mesh.Normals.Add((sbyte)d[o + 1] * NormalScale);
                    mesh.Normals.Add((sbyte)d[o + 2] * NormalScale);
                }
                break;

            case 0x6C008004:
            case 0x6C008003:
            case 0x6C008001:                                  // skin weights and bones
                size = 16;
                if (!Fits(d, p, count, size)) return p;
                for (int j = 0; j < count; j++)
                    for (int i = 0; i < 4; i++)
                    {
                        uint bits = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p + j * 16 + i * 4));
                        mesh.Weights.Add(BitConverter.UInt32BitsToSingle(bits));

                        // The bone index rides in the low bits of the weight,
                        // one-based, with zero meaning "no bone". It is a byte
                        // first and one-based second: truncating after the
                        // subtraction instead gives nonsense indices, which
                        // shows up as a few vertices flung across the scene.
                        byte bone = (byte)(bits >> 2);
                        mesh.BoneIndices.Add(bone == 0 ? (byte)0 : (byte)(bone - 1));
                    }
                break;

            default:
                mesh.Unknown.Add($"0x{type:X8}");
                return p;
        }

        int used = count * size;
        return p + used + ((used & 0xF) == 0 ? 0 : 0x10 - (used & 0xF));
    }

    private static bool Fits(byte[] d, int p, int count, int size) => p + count * size <= d.Length;

    /// Trims the two vertices the next block repeats.
    private static void DeleteOverlapping(Mesh mesh, List<int> strip, List<uint> typesRead, ref int index)
    {
        foreach (uint raw in typesRead)
        {
            switch (raw & 0xFF00FFFF)
            {
                case 0x68008000:
                case 0x6D008000:
                    Trim(mesh.Positions, 6);
                    if (strip.Count >= 2) strip.RemoveRange(strip.Count - 2, 2);
                    index -= 2;
                    break;

                case 0x64008001:
                case 0x65008001:
                case 0x6D008001:
                    Trim(mesh.TexCoords, 4);
                    break;

                case 0x6D00C002:
                case 0x6E00C002:
                    Trim(mesh.Colours, 2);
                    break;

                case 0x6E008002:
                case 0x6E008003:
                case 0x6A008003:
                    Trim(mesh.Normals, 6);
                    break;

                case 0x6C008004:
                case 0x6C008003:
                case 0x6C008001:
                    Trim(mesh.Weights, 8);
                    Trim(mesh.BoneIndices, 8);
                    break;
            }
        }
    }

    private static void Trim<T>(List<T> list, int count)
    {
        if (list.Count >= count) list.RemoveRange(list.Count - count, count);
    }

    /// <summary>
    /// Converts a triangle strip to a triangle list, dropping degenerates.
    ///
    /// A strip restart is encoded by repeating a position, not by repeating an
    /// index - the float-position blocks hand out a fresh index for every entry
    /// - so the test has to compare coordinates. That it is the right test is
    /// not a matter of taste: culling this way leaves exactly the triangle count
    /// the geometry header declares, 214 and 851 for the two Angie meshes.
    /// </summary>
    public static IEnumerable<(int A, int B, int C)> Triangulate(List<int> strip, List<float> positions)
    {
        for (int i = 0; i + 2 < strip.Count; i++)
        {
            int a = strip[i], b = strip[i + 1], c = strip[i + 2];
            if (a == b || b == c || a == c) continue;
            if (Same(positions, a, b) || Same(positions, b, c) || Same(positions, a, c)) continue;

            yield return (i & 1) == 0 ? (a, b, c) : (a, c, b);
        }
    }

    private static bool Same(List<float> p, int i, int j)
    {
        if ((i + 1) * 3 > p.Count || (j + 1) * 3 > p.Count) return false;
        return p[i * 3] == p[j * 3] && p[i * 3 + 1] == p[j * 3 + 1] && p[i * 3 + 2] == p[j * 3 + 2];
    }
}
