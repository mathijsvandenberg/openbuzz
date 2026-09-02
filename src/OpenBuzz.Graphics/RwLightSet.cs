namespace OpenBuzz.Graphics;

using System.Buffers.Binary;

/// One light, resolved into world space.
public sealed class RwLightSource
{
    public required string Name { get; init; }

    /// RenderWare light type: 0x80 point, 0x81 spot, 0x82 soft spot,
    /// 1 directional, 2 ambient. Everything in the studio rigs is a point.
    public required int Type { get; init; }
    public required int Flags { get; init; }

    public required float[] Position { get; init; }

    /// Which way it faces. Meaningless for a point or ambient light, but the
    /// frame carries one either way.
    public required float[] Direction { get; init; }

    /// Linear colour. The rigs run above 1.0 - a spot sits at 2.0 and the
    /// white-out pools at 5.0 - so this is a multiplier, not a 0..1 colour.
    public required float[] Colour { get; init; }

    public required float Radius { get; init; }

    /// Stored as minus the cosine of the half-angle. Only a spot uses it.
    public required float MinusCosAngle { get; init; }

    public bool IsPositioned => Type >= 0x80;
    public bool IsSpot => Type is 0x81 or 0x82;

    /// The brightest channel, which is the natural energy to hand a renderer
    /// that wants a colour and a separate intensity.
    public float Energy => Math.Max(Colour[0], Math.Max(Colour[1], Colour[2]));

    public double ConeDegrees =>
        IsSpot && Math.Abs(MinusCosAngle) <= 1 ? 2 * Math.Acos(-MinusCosAngle) * 180.0 / Math.PI : 0;
}

/// <summary>
/// `Lights*.rp2`: the studio's lighting, one file per mood.
///
/// Eight rigs - neutral, intro, red tension, round win, game win, two
/// celebrations and a white-out - each holding the same seven point lights
/// under the same names, with only the colours changing between them:
///
///     ANIMATEDLIGHT_CONTSPOT   LIGHT_CONTESTANTPOOL
///     ANIMATEDLIGHT_HOSTSPOT   LIGHT_HOSTPLATFORMPOOL
///     ANIMATEDLIGHT_MONISPOT   LIGHT_MONITORPOOL
///     LIGHT_DOME
///
/// The file is shaped exactly like `StudioCameras.rp2` - a named chunk group
/// per light, then a clump whose frame list places it and whose LIGHT chunk
/// describes it - so this reads the same way <see cref="RwCameraSet"/> does.
///
/// The positions confirm the naming: CONTSPOT sits over the contestant marks
/// the studio set gives, HOSTSPOT over MODEL_SET_LECTERN, and MONISPOT out on
/// the negative-X side where CAMERA_SCREEN is pointed - which is the monitor
/// the round is played on, and a different screen from the jumbotron.
/// </summary>
public static class RwLightSet
{
    private const uint GroupStart = 0x29;
    private const uint StringId = 0x02;

    public static List<RwLightSource> Parse(byte[] d)
    {
        var lights = new List<RwLightSource>();
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
                var light = Read(d, pending, data, size);
                if (light is not null) lights.Add(light);
                pending = null;
            }

            o = data + size;
        }

        return lights;
    }

    private static RwLightSource? Read(byte[] d, string name, int data, int size)
    {
        var tree = RwStream.ParseAt(d, data, size);

        var frameList = RwStream.Flatten(tree).FirstOrDefault(n => n.Id == RwId.FrameList);
        var lightNode = RwStream.Flatten(tree).FirstOrDefault(n => n.Id == RwId.Light);
        if (frameList is null || lightNode is null) return null;

        var skeleton = RwSkeleton.Parse(d, frameList);
        if (skeleton.Frames.Length == 0) return null;

        var placement = skeleton.Frames[0];
        var m = placement.Matrix;

        var body = RwStream.Flatten([lightNode]).FirstOrDefault(n => n.Id == RwId.Struct);
        if (body is null || body.Size < 24) return null;

        int s = body.DataOffset;
        int typeAndFlags = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(s + 20));

        return new RwLightSource
        {
            Name = name,
            Type = (typeAndFlags >> 16) & 0xFFFF,
            Flags = typeAndFlags & 0xFFFF,
            Position = [placement.Translation[0], placement.Translation[1], placement.Translation[2]],
            // Same composed basis as a camera: the placement frame's second row,
            // negated, is the way it faces.
            Direction = Unit(-m[3], -m[4], -m[5]),
            Radius = Single(d, s),
            Colour = [Single(d, s + 4), Single(d, s + 8), Single(d, s + 12)],
            MinusCosAngle = Single(d, s + 16),
        };
    }

    private static float Single(byte[] d, int at) =>
        BinaryPrimitives.ReadSingleLittleEndian(d.AsSpan(at));

    private static float[] Unit(float x, float y, float z)
    {
        double n = Math.Sqrt(x * x + y * y + z * z);
        if (n < 1e-6) return [0, -1, 0];
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
