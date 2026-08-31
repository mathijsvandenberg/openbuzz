using System.Buffers.Binary;

namespace OpenBuzz.Graphics;

/// One pose for one bone at one time.
public readonly record struct RwKeyframe(float Time, float Qx, float Qy, float Qz, float Qw,
                                         float Tx, float Ty, float Tz);

/// <summary>
/// An `ANIMANIMATION` clip: compressed hierarchical animation.
///
/// The keyframe is 22 bytes - a float time, four 16-bit floats for the
/// quaternion, three more for the translation, and a 32-bit link to the
/// previous keyframe of the same bone. Sizes agree exactly for all 13 clips in
/// every animation stream.
///
/// The link is a byte offset in units of 24, the in-memory keyframe size,
/// rather than the 22 bytes the stream uses - RenderWare's interpolator info
/// carries both sizes. Dividing by 24 gives the keyframe index, which is what
/// makes it possible to group keyframes by bone at all.
/// </summary>
public sealed class RwAnimation
{
    public const int KeyframeSize = 22;
    public const int MemoryKeyframeSize = 24;

    /// The compressed type; the uncompressed one is 1 and does not appear here.
    public const int CompressedType = 2;

    public required float Duration { get; init; }
    public required RwKeyframe[] Keyframes { get; init; }

    /// Bone index for each keyframe, recovered by following the previous-links.
    public required int[] BoneOfKeyframe { get; init; }

    /// Bone count, which is the number of keyframes at time zero.
    public required int BoneCount { get; init; }

    /// <summary>
    /// RenderWare's 16-bit float: one sign bit, four exponent bits biased by 15,
    /// eleven mantissa bits.
    ///
    /// Not IEEE half, which splits 1-5-10. The split was settled against known
    /// values rather than assumed: the bind-pose bone offsets say which code
    /// must decode to which number, and only 1-4-11 reproduces them - it puts
    /// 0x7800 at exactly 1.0 and lands every translation on its known offset.
    /// Under it, all 107,232 quaternions across the animation streams come out
    /// unit to within 0.02%.
    /// </summary>
    public static float Float16(ushort bits)
    {
        int sign = (bits >> 15) & 1;
        int exponent = (bits >> 11) & 0xF;
        int mantissa = bits & 0x7FF;

        float value = exponent == 0
            ? mantissa / 2048f * MathF.Pow(2, -14)
            : (1f + mantissa / 2048f) * MathF.Pow(2, exponent - 15);

        return sign != 0 ? -value : value;
    }

    public static List<RwAnimation> LoadAll(byte[] data) =>
        [.. RwStream.Flatten(RwStream.Parse(data))
                    .Where(n => n.Id == RwId.AnimAnimation)
                    .Select(n => Parse(data, n))
                    .OfType<RwAnimation>()];

    public static RwAnimation? Parse(byte[] d, RwNode node)
    {
        int o = node.DataOffset;
        int type = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(o + 4));
        int count = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(o + 8));
        float duration = BitConverter.ToSingle(d, o + 16);

        if (type != CompressedType || count <= 0) return null;

        int frames = o + 20;
        int custom = frames + count * KeyframeSize;
        if (custom + 24 > d.Length) return null;

        // The trailing custom data is the translation bounding box: a centre
        // and a half-extent, which the 16-bit floats index between.
        var offset = new float[3];
        var scale = new float[3];
        for (int i = 0; i < 3; i++)
        {
            offset[i] = BitConverter.ToSingle(d, custom + i * 4);
            scale[i] = BitConverter.ToSingle(d, custom + 12 + i * 4);
        }

        var keyframes = new RwKeyframe[count];
        var previous = new int[count];

        for (int k = 0; k < count; k++)
        {
            int at = frames + k * KeyframeSize;
            float F(int byteOffset) => Float16(BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(at + byteOffset)));

            keyframes[k] = new RwKeyframe(
                BitConverter.ToSingle(d, at),
                F(4), F(6), F(8), F(10),
                offset[0] + F(12) * scale[0],
                offset[1] + F(14) * scale[1],
                offset[2] + F(16) * scale[2]);

            previous[k] = (int)(BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at + 18)) / MemoryKeyframeSize);
        }

        // The clip opens with one keyframe per bone at time zero; every later
        // keyframe belongs to whichever bone its predecessor belongs to.
        int boneCount = keyframes.Count(k => k.Time == 0f);
        var boneOf = new int[count];
        for (int k = 0; k < count; k++)
            boneOf[k] = k < boneCount ? k
                      : previous[k] >= 0 && previous[k] < count ? boneOf[previous[k]]
                      : 0;

        return new RwAnimation
        {
            Duration = duration,
            Keyframes = keyframes,
            BoneOfKeyframe = boneOf,
            BoneCount = boneCount,
        };
    }

    /// The keyframes belonging to each bone, in time order.
    public List<int>[] KeyframesByBone()
    {
        var result = new List<int>[BoneCount];
        for (int b = 0; b < BoneCount; b++) result[b] = [];

        for (int k = 0; k < Keyframes.Length; k++)
        {
            int b = BoneOfKeyframe[k];
            if (b >= 0 && b < BoneCount) result[b].Add(k);
        }

        foreach (var list in result) list.Sort((x, y) => Keyframes[x].Time.CompareTo(Keyframes[y].Time));
        return result;
    }
}
