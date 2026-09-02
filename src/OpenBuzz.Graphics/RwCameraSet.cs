namespace OpenBuzz.Graphics;

using System.Buffers.Binary;

/// One camera, resolved into world space.
public sealed class RwCameraView
{
    public required string Name { get; init; }

    /// Where the camera is, in studio coordinates.
    public required float[] Position { get; init; }

    /// Which way it looks, and which way is up. Both unit length.
    public required float[] Forward { get; init; }
    public required float[] Up { get; init; }
    public required float[] Right { get; init; }

    /// The view window, which is the half-extent of the frustum at unit
    /// distance. Horizontal and vertical field of view follow from it.
    public required float[] ViewWindow { get; init; }
    public required float[] ViewOffset { get; init; }

    public required float Near { get; init; }
    public required float Far { get; init; }
    public required float Fog { get; init; }

    /// 1 perspective, 2 parallel.
    public required int Projection { get; init; }

    public double FovVerticalDegrees => 2.0 * Math.Atan(ViewWindow[1]) * 180.0 / Math.PI;
    public double FovHorizontalDegrees => 2.0 * Math.Atan(ViewWindow[0]) * 180.0 / Math.PI;
    public double Aspect => ViewWindow[1] == 0 ? 0 : ViewWindow[0] / ViewWindow[1];

    /// Where the camera is looking, one unit along its forward axis. Godot
    /// aims a camera at a point rather than along a vector.
    public float[] Target => [
        Position[0] + Forward[0], Position[1] + Forward[1], Position[2] + Forward[2],
    ];
}

/// <summary>
/// `StudioCameras.rp2`: every camera in the show, by name.
///
/// The Lua only ever names a camera - SetCameraAngle("CAMERA_SCREEN") - so for
/// a long time the coordinates looked like they had to be in the executable.
/// They are not. This file holds them, as ordinary RenderWare CAMERA chunks:
/// CAMERA_SCREEN, CAMERA_HOST, CAMERA_STUDIO, CAMERA_CONTESTANTS,
/// CAMERA_CONTESTANT_1..4, CAMERA_SINGLEPLAYER, and the animated ones for the
/// intro, the round wins and the game win.
///
/// Each camera is a clump of two frames. The first places it. The second is a
/// constant child - rows (-1,0,0), (0,0,-1), (0,-1,0) - which is the authoring
/// tool's axis fix, and composing the two gives the camera its real basis:
///
///     forward = -row1(frame0)     up = -row2(frame0)     right = -row0(frame0)
///
/// That is not assumed. CAMERA_CONTESTANT_1..4 must look at
/// DUMMYNODE_CONTESTANT_1..4, whose positions the studio set gives
/// independently, and under this rule each one aims at its own contestant to
/// within a cosine of 0.997. No other axis or sign comes close.
/// </summary>
public static class RwCameraSet
{
    private const uint GroupStart = 0x29;
    private const uint StringId = 0x02;

    public static List<RwCameraView> Parse(byte[] d)
    {
        var cameras = new List<RwCameraView>();
        string? pending = null;
        int o = 0;

        while (o + RwStream.HeaderSize <= d.Length)
        {
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o));
            int size = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(o + 4));
            int data = o + RwStream.HeaderSize;
            if (size < 0 || data + size > d.Length) break;

            if (id == GroupStart)
            {
                pending = ReadName(d, data, size) ?? pending;
            }
            else if (id == RwId.Clump && pending is not null)
            {
                var view = Read(d, pending, data, size);
                if (view is not null) cameras.Add(view);
                pending = null;
            }

            o = data + size;
        }

        return cameras;
    }

    private static RwCameraView? Read(byte[] d, string name, int data, int size)
    {
        var tree = RwStream.ParseAt(d, data, size);

        var frameList = RwStream.Flatten(tree).FirstOrDefault(n => n.Id == RwId.FrameList);
        var camera = RwStream.Flatten(tree).FirstOrDefault(n => n.Id == RwId.Camera);
        if (frameList is null || camera is null) return null;

        var skeleton = RwSkeleton.Parse(d, frameList);
        if (skeleton.Frames.Length == 0) return null;

        var placement = skeleton.Frames[0];
        var m = placement.Matrix;

        // The composed basis. See the note on the class for why these signs.
        float[] forward = Unit(-m[3], -m[4], -m[5]);
        float[] up = Unit(-m[6], -m[7], -m[8]);
        float[] right = Unit(-m[0], -m[1], -m[2]);

        var body = RwStream.Flatten([camera]).FirstOrDefault(n => n.Id == RwId.Struct);
        if (body is null || body.Size < 32) return null;

        int s = body.DataOffset;
        return new RwCameraView
        {
            Name = name,
            Position = [placement.Translation[0], placement.Translation[1], placement.Translation[2]],
            Forward = forward,
            Up = up,
            Right = right,
            ViewWindow = [Single(d, s), Single(d, s + 4)],
            ViewOffset = [Single(d, s + 8), Single(d, s + 12)],
            Near = Single(d, s + 16),
            Far = Single(d, s + 20),
            Fog = Single(d, s + 24),
            Projection = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(s + 28)),
        };
    }

    private static float Single(byte[] d, int at) =>
        BinaryPrimitives.ReadSingleLittleEndian(d.AsSpan(at));

    private static float[] Unit(float x, float y, float z)
    {
        double n = Math.Sqrt(x * x + y * y + z * z);
        if (n < 1e-6) return [0, 0, 1];
        return [(float)(x / n), (float)(y / n), (float)(z / n)];
    }

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
