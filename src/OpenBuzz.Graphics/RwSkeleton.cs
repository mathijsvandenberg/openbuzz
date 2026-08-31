using System.Buffers.Binary;

namespace OpenBuzz.Graphics;

/// One frame of the hierarchy: a local transform and its parent.
public sealed class RwFrame
{
    /// Row-major 3x3 basis followed by the translation, as RenderWare stores it.
    public required float[] Matrix { get; init; }
    public required float[] Translation { get; init; }
    public required int Parent { get; init; }

    /// The HANIM node id on this frame, or null when it is not a bone.
    public int? NodeId { get; set; }
}

/// <summary>
/// The bone hierarchy of a clump: the FRAMELIST plus the HANIM plugin data.
///
/// Frames carry the transforms and the parent links; HANIM says which frames
/// are bones and in what order. One frame's HANIM lists the whole hierarchy,
/// and every bone frame carries its own HANIM giving just its node id, which is
/// what ties the two together.
/// </summary>
public sealed class RwSkeleton
{
    public required RwFrame[] Frames { get; init; }

    /// Frame index for each bone, in hierarchy order.
    public required int[] BoneFrames { get; init; }

    /// Parent bone for each bone, -1 for the root.
    public required int[] BoneParents { get; init; }

    public int BoneCount => BoneFrames.Length;

    /// <summary>
    /// Bind-pose world transform per bone, as a 3x3 basis followed by the
    /// translation, in the row-vector convention RenderWare uses.
    /// </summary>
    public float[][] WorldMatrices()
    {
        var result = new float[BoneCount][];

        for (int b = 0; b < BoneCount; b++)
        {
            var frame = Frames[BoneFrames[b]];
            float[] local = [.. frame.Matrix, .. frame.Translation];
            result[b] = BoneParents[b] < 0 ? local : Multiply(local, result[BoneParents[b]]);
        }

        return result;
    }

    /// Row-vector composition: the result applies a then b.
    public static float[] Multiply(float[] a, float[] b)
    {
        var m = new float[12];

        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                m[r * 3 + c] = a[r * 3] * b[c] + a[r * 3 + 1] * b[3 + c] + a[r * 3 + 2] * b[6 + c];

        for (int c = 0; c < 3; c++)
            m[9 + c] = a[9] * b[c] + a[10] * b[3 + c] + a[11] * b[6 + c] + b[9 + c];

        return m;
    }

    /// <summary>
    /// How many bones an inverse-bind set actually inverts, checked against the
    /// bind pose. A geometry that only uses one bone still ships a full-length
    /// array, and the entries for the bones it does not use are garbage - so
    /// the set has to be chosen by testing it, not by taking the first one.
    /// </summary>
    public int ScoreInverseBind(float[] inverseBind)
    {
        if (inverseBind.Length < BoneCount * 16) return 0;

        var world = WorldMatrices();
        int good = 0;

        for (int b = 0; b < BoneCount; b++)
        {
            float[] inverse =
            [
                inverseBind[b * 16], inverseBind[b * 16 + 1], inverseBind[b * 16 + 2],
                inverseBind[b * 16 + 4], inverseBind[b * 16 + 5], inverseBind[b * 16 + 6],
                inverseBind[b * 16 + 8], inverseBind[b * 16 + 9], inverseBind[b * 16 + 10],
                inverseBind[b * 16 + 12], inverseBind[b * 16 + 13], inverseBind[b * 16 + 14],
            ];

            var m = Multiply(inverse, world[b]);
            float error = 0;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    error += MathF.Abs(m[r * 3 + c] - (r == c ? 1f : 0f));
            for (int c = 0; c < 3; c++) error += MathF.Abs(m[9 + c]);

            if (error < 0.01f) good++;
        }

        return good;
    }

    public static RwSkeleton? Parse(byte[] d, RwNode frameList)
    {
        int p = frameList.DataOffset + RwStream.HeaderSize;
        int count = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p));
        p += 4;

        var frames = new RwFrame[count];
        for (int i = 0; i < count; i++)
        {
            int o = p + i * 56;
            var matrix = new float[9];
            for (int k = 0; k < 9; k++) matrix[k] = BitConverter.ToSingle(d, o + k * 4);

            frames[i] = new RwFrame
            {
                Matrix = matrix,
                Translation = [BitConverter.ToSingle(d, o + 36), BitConverter.ToSingle(d, o + 40), BitConverter.ToSingle(d, o + 44)],
                Parent = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(o + 48)),
            };
        }

        // One EXTENSION per frame, in the same order, after the struct.
        var extensions = frameList.Children.Where(c => c.Id == RwId.Extension).ToList();
        int[]? order = null;

        for (int i = 0; i < extensions.Count && i < count; i++)
        {
            foreach (var hanim in extensions[i].Children.Where(c => c.Id == RwId.HAnim))
            {
                frames[i].NodeId = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(hanim.DataOffset + 4));

                int nodes = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(hanim.DataOffset + 8));
                if (nodes <= 0) continue;

                // This frame holds the hierarchy: the bone order, as node ids.
                order = new int[nodes];
                for (int n = 0; n < nodes; n++)
                    order[n] = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(hanim.DataOffset + 20 + n * 12));
            }
        }

        if (order is null) return null;

        var frameOfNode = new Dictionary<int, int>();
        for (int i = 0; i < count; i++)
            if (frames[i].NodeId is { } id) frameOfNode.TryAdd(id, i);

        var boneFrames = new List<int>();
        foreach (int id in order)
            if (frameOfNode.TryGetValue(id, out int frame)) boneFrames.Add(frame);

        var boneOfFrame = new Dictionary<int, int>();
        for (int b = 0; b < boneFrames.Count; b++) boneOfFrame[boneFrames[b]] = b;

        // A bone's parent is the nearest ancestor frame that is also a bone.
        var parents = new int[boneFrames.Count];
        for (int b = 0; b < boneFrames.Count; b++)
        {
            int f = frames[boneFrames[b]].Parent;
            while (f >= 0 && !boneOfFrame.ContainsKey(f)) f = frames[f].Parent;
            parents[b] = f >= 0 && boneOfFrame.TryGetValue(f, out int pb) ? pb : -1;
        }

        return new RwSkeleton { Frames = frames, BoneFrames = [.. boneFrames], BoneParents = parents };
    }
}

/// <summary>
/// A geometry's SKIN plugin: per-vertex bone indices and weights, and the
/// inverse bind matrix per bone.
///
/// Native geometry wraps the same payload in a STRUCT carrying the platform id
/// and leaves out the per-vertex arrays, which come off the DMA chain instead.
/// Both forms were confirmed by size accounting, and both give byte-identical
/// inverse bind matrices for the same character.
/// </summary>
public sealed class RwSkin
{
    public required int BoneCount { get; init; }

    /// How many bones this geometry actually binds to. A part that hangs off a
    /// single bone still ships a full-length inverse-bind array whose other
    /// entries are meaningless, so this is what says which skin is the real one.
    public required int UsedBones { get; init; }

    public required byte[] BoneIndices { get; init; }
    public required float[] Weights { get; init; }

    /// 16 floats per bone, RenderWare's row-major layout with padding.
    public required float[] InverseBind { get; init; }

    public static RwSkin? Parse(byte[] d, RwNode skin, int vertexCount, bool native)
    {
        int p = skin.DataOffset;

        if (native)
        {
            // STRUCT header, then the platform id.
            p += RwStream.HeaderSize + 4;
            vertexCount = 0;
        }

        if (p + 4 > d.Length) return null;

        int bones = d[p];
        int usedBones = d[p + 1];
        p += 4 + usedBones;

        var indices = new byte[vertexCount * 4];
        var weights = new float[vertexCount * 4];

        if (vertexCount > 0)
        {
            if (p + vertexCount * 20 + bones * 64 > d.Length) return null;

            Array.Copy(d, p, indices, 0, indices.Length);
            p += vertexCount * 4;

            for (int i = 0; i < weights.Length; i++) weights[i] = BitConverter.ToSingle(d, p + i * 4);
            p += vertexCount * 16;
        }

        if (p + bones * 64 > d.Length) return null;

        var inverse = new float[bones * 16];
        for (int i = 0; i < inverse.Length; i++) inverse[i] = BitConverter.ToSingle(d, p + i * 4);

        return new RwSkin
        {
            BoneCount = bones, UsedBones = usedBones,
            BoneIndices = indices, Weights = weights, InverseBind = inverse,
        };
    }
}
