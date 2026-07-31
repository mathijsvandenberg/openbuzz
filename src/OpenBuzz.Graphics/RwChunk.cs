using System.Buffers.Binary;
using System.Text;

namespace OpenBuzz.Graphics;

/// RenderWare chunk type ids, only the handful these files use.
public enum RwType : uint
{
    Struct = 0x01,
    String = 0x02,
    Extension = 0x03,
    TextureNative = 0x15,
    TextureDictionary = 0x16,
}

/// <summary>
/// A RenderWare stream chunk: 12-byte header of type, payload size and library
/// version, followed by the payload.
/// </summary>
public readonly record struct RwChunk(RwType Type, int Size, uint LibraryId, int DataOffset)
{
    public const int HeaderSize = 12;

    public int End => DataOffset + Size;

    public static RwChunk Read(ReadOnlySpan<byte> data, int offset) => new(
        (RwType)BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]),
        BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 4)..]),
        BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 8)..]),
        offset + HeaderSize);

    /// Enumerates sibling chunks starting at <paramref name="offset"/>.
    public static IEnumerable<RwChunk> Walk(byte[] data, int offset, int end)
    {
        while (offset + HeaderSize <= end)
        {
            var chunk = Read(data, offset);
            if (chunk.Size < 0 || chunk.End > data.Length) yield break;
            yield return chunk;
            offset = chunk.End;
        }
    }

    public static string ReadString(ReadOnlySpan<byte> data, RwChunk chunk)
    {
        var span = data.Slice(chunk.DataOffset, chunk.Size);
        int nul = span.IndexOf((byte)0);
        return Encoding.Latin1.GetString(nul >= 0 ? span[..nul] : span);
    }
}
