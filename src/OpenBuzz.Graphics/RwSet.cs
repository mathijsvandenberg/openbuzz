namespace OpenBuzz.Graphics;

using System.Buffers.Binary;

/// One named piece of a set: a clump, and the name the stream gave it.
public sealed class RwSetPiece
{
    public required string Name { get; init; }

    /// The clump, or null for a marker that carries only a transform.
    public RwNode? Clump { get; init; }

    /// Where the piece sits, as a 3x3 rotation and a translation, taken from
    /// the root frame of its clump. Identity when the clump has no frame list.
    public float[] Matrix { get; init; } = [1, 0, 0, 0, 1, 0, 0, 0, 1];
    public float[] Translation { get; init; } = [0, 0, 0];

    /// How the piece is meant to be drawn, from the NORM_ group that follows
    /// its name: NORM_DEFAULT or NORM_ADDITIVE_BLENDING. The flares and the
    /// podium glows are additive, and drawn opaque they come out as black
    /// slabs across the set.
    public string Render { get; init; } = "NORM_DEFAULT";

    public bool IsAdditive => Render.Contains("ADDITIVE");

    /// A DUMMYNODE_ marks a spot rather than drawing anything - where a
    /// contestant stands, where the clock hangs. It does carry geometry, but
    /// it is a placeholder and must not be drawn.
    public bool IsMarker => Name.StartsWith("DUMMYNODE_", StringComparison.Ordinal);
}

/// <summary>
/// A set file: the studio, the green room, the prize room.
///
/// Where a costume ships one body three times over in three anonymous clumps,
/// a set ships forty distinct pieces, each preceded by a CHUNKGROUPSTART whose
/// payload is a count and then a STRING with the piece's name -
/// MODEL_JUMBOTRON_INGAME, MODEL_PODIUM_MULTI, DUMMYNODE_CONTESTANT_1. Those
/// names are what makes the set placeable: without them the pieces are an
/// undifferentiated pile and the camera has nothing to aim at.
///
/// The same CHUNKGROUPSTART convention names the fonts, so
/// <see cref="RwFont"/> reads it the same way.
/// </summary>
public static class RwSet
{
    private const uint GroupStart = 0x29;
    private const uint StringId = 0x02;

    /// <summary>
    /// The named pieces of a stream, in file order. Empty when the stream does
    /// not name its clumps, which is how a costume is told from a set.
    /// </summary>
    public static List<RwSetPiece> Parse(byte[] d)
    {
        var pieces = new List<RwSetPiece>();
        string? pending = null;
        string render = "NORM_DEFAULT";
        int o = 0;

        while (o + RwStream.HeaderSize <= d.Length)
        {
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o));
            int size = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(o + 4));
            int data = o + RwStream.HeaderSize;
            if (size < 0 || data + size > d.Length) break;

            if (id == GroupStart)
            {
                // Each piece is preceded by two groups: its own name and then
                // a NORM_ render state. The render state is not a piece, and
                // letting it through overwrote every name with NORM_DEFAULT.
                var read = ReadName(d, data, size);
                if (read is null) { }
                else if (read.StartsWith("NORM_", StringComparison.Ordinal)) render = read;
                else { pending = read; render = "NORM_DEFAULT"; }
            }
            else if (id == RwId.Clump && pending is not null)
            {
                pieces.Add(Piece(d, pending, render, new RwNode(id, size, 0, data, 0)));
                pending = null;
            }
            else if (pending is not null && id != RwId.Clump)
            {
                // A group whose body is not a clump - an animation, or a bare
                // marker. Keep the name only when nothing will claim it later.
                if (pending.StartsWith("DUMMYNODE_", StringComparison.Ordinal))
                {
                    pieces.Add(new RwSetPiece { Name = pending });
                    pending = null;
                }
            }

            o = data + size;
        }

        return pieces;
    }

    private static RwSetPiece Piece(byte[] d, string name, string render, RwNode clump)
    {
        var tree = RwStream.ParseAt(d, clump.DataOffset, clump.Size);
        var frameList = RwStream.Flatten(tree).FirstOrDefault(n => n.Id == RwId.FrameList);
        var skeleton = frameList is null ? null : RwSkeleton.Parse(d, frameList);

        var root = skeleton is { Frames.Length: > 0 } ? skeleton.Frames[0] : null;
        return new RwSetPiece
        {
            Name = name,
            Render = render,
            Clump = clump,
            Matrix = root?.Matrix ?? [1, 0, 0, 0, 1, 0, 0, 0, 1],
            Translation = root?.Translation ?? [0, 0, 0],
        };
    }

    /// The payload is a count and then a STRING chunk holding the name.
    private static string? ReadName(byte[] d, int data, int size)
    {
        if (size < 16 || data + size > d.Length) return null;

        int s = data + 4;
        if (BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(s)) != StringId) return null;

        int len = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(s + 4));
        if (len <= 0 || s + 12 + len > d.Length) return null;

        var span = d.AsSpan(s + 12, len);
        int end = span.IndexOf((byte)0);
        return System.Text.Encoding.ASCII.GetString(end < 0 ? span : span[..end]);
    }
}
