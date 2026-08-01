using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenBuzz.Animation;

/// A transform keyframe: position, scale and rotation at a frame.
public sealed record TfmKey(int Frame, float ScaleX, float ScaleY, float Rotation, float X, float Y);

/// A colour keyframe. RGB is white throughout the disc; alpha is what animates.
public sealed record ColKey(int Frame, float R, float G, float B, float A);

/// An axis-aligned box in the object's own space, centred on its origin.
public sealed record Bounds(float Left, float Top, float Right, float Bottom)
{
    public float Width => Right - Left;
    public float Height => Top - Bottom;
}

public sealed class A2dObject
{
    public required int Slot { get; init; }
    public required string Name { get; init; }
    public Bounds? Box { get; set; }
    public List<TfmKey> Transform { get; init; } = [];
    public List<ColKey> Colour { get; init; } = [];

    /// <summary>
    /// The transform in effect at a frame. Keys are dense but an object is not
    /// necessarily keyed on every frame, so this returns the last key at or
    /// before the frame rather than requiring an exact hit.
    /// </summary>
    public TfmKey? TransformAt(int frame)
    {
        TfmKey? best = null;
        foreach (var k in Transform)
        {
            if (k.Frame > frame) break;
            best = k;
        }
        return best;
    }

    public ColKey? ColourAt(int frame)
    {
        ColKey? best = null;
        foreach (var k in Colour)
        {
            if (k.Frame > frame) break;
            best = k;
        }
        return best ?? (Colour.Count > 0 ? Colour[0] : null);
    }

    /// An object should not be drawn before its first keyframe.
    public bool IsLive(int frame) => Transform.Count > 0 && Transform[0].Frame <= frame;
}

/// <summary>
/// Binds an object to a piece of text.
///
/// <paramref name="Key"/> is a named lookup into `default.str` via the hashes in
/// `default.ndx`. That hash function is **not yet identified** - 29 standard
/// candidates were tested against the 81 known keys and none produced a single
/// hit - so the key cannot currently be resolved to its Dutch string. Everything
/// else here (style, justification, size) is exact and usable now.
/// </summary>
public sealed record TextBinding(
    string Key,
    string Style,
    string HorizontalJustify,
    string VerticalJustify,
    float SizeMultiplier);

public sealed class A2dAnimation
{
    public required string Name { get; init; }
    public required int FrameCount { get; init; }
    public List<A2dObject> Objects { get; init; } = [];
}

/// <summary>
/// One A2D chunk: a set of animation clips over a shared cast of objects.
///
/// Coordinates are the original 640x480 design space. Object <see cref="Bounds"/>
/// are local and centred on the object's own origin, while a
/// <see cref="TfmKey"/>'s X/Y is its position on that canvas - two different
/// spaces that are easy to conflate.
/// </summary>
public sealed class A2dScene
{
    /// The design canvas the coordinates are expressed in.
    public const float CanvasWidth = 640f;
    public const float CanvasHeight = 480f;

    public required string Name { get; init; }
    public List<A2dAnimation> Animations { get; init; } = [];

    /// Actor-to-sprite bindings declared alongside the timelines.
    public Dictionary<string, string> IconBindings { get; init; } = [];

    /// Actor name -> text binding, from the SetActorToTextMapping* calls.
    public Dictionary<string, TextBinding> TextBindings { get; init; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static A2dScene? FromJson(string json) => JsonSerializer.Deserialize<A2dScene>(json, Options);

    /// <summary>
    /// Loads every exported scene in a directory, skipping any that fail to
    /// parse rather than aborting the whole load.
    /// </summary>
    public static List<A2dScene> LoadAll(string directory)
    {
        var scenes = new List<A2dScene>();
        foreach (var path in Directory.GetFiles(directory, "*.json")
                                      .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (FromJson(File.ReadAllText(path)) is { Animations.Count: > 0 } scene)
                    scenes.Add(scene);
            }
            catch (JsonException)
            {
            }
        }
        return scenes;
    }

    /// <summary>
    /// Locates the exported scenes by walking up from a starting directory,
    /// so tools work from a source checkout with no path configuration.
    /// </summary>
    public static string? FindExportDirectory(string startDirectory)
    {
        for (var d = new DirectoryInfo(startDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "extracted", "a2d");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
}
