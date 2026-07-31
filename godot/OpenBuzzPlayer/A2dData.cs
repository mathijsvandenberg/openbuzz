using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenBuzz.Player;

/// Mirrors the JSON written by `obz a2d export`.
public sealed class A2dSceneData
{
    public string Name { get; set; } = "";
    public List<A2dAnimationData> Animations { get; set; } = [];
    public Dictionary<string, string> IconBindings { get; set; } = [];
}

public sealed class A2dAnimationData
{
    public string Name { get; set; } = "";
    public int FrameCount { get; set; }
    public List<A2dObjectData> Objects { get; set; } = [];
}

public sealed class A2dObjectData
{
    public int Slot { get; set; }
    public string Name { get; set; } = "";
    public BoundsData? Box { get; set; }
    public List<TfmKeyData> Transform { get; set; } = [];
    public List<ColKeyData> Colour { get; set; } = [];

    /// <summary>
    /// The transform in effect at a frame. Keys are emitted per frame, but not
    /// every object is keyed on every frame, so this holds the last key at or
    /// before the frame rather than assuming an exact hit.
    /// </summary>
    public TfmKeyData? TransformAt(int frame)
    {
        TfmKeyData? best = null;
        foreach (var k in Transform)
        {
            if (k.Frame > frame) break;
            best = k;
        }
        return best ?? (Transform.Count > 0 ? Transform[0] : null);
    }

    public ColKeyData? ColourAt(int frame)
    {
        ColKeyData? best = null;
        foreach (var k in Colour)
        {
            if (k.Frame > frame) break;
            best = k;
        }
        return best ?? (Colour.Count > 0 ? Colour[0] : null);
    }

    /// True once the object has any key at or before the frame — before its
    /// first key it should not be drawn at all.
    public bool IsLive(int frame) => Transform.Count > 0 && Transform[0].Frame <= frame;
}

public sealed class BoundsData
{
    public float Left { get; set; }
    public float Top { get; set; }
    public float Right { get; set; }
    public float Bottom { get; set; }

    public float Width => Right - Left;
    public float Height => Top - Bottom;
}

public sealed class TfmKeyData
{
    public int Frame { get; set; }
    public float ScaleX { get; set; } = 1;
    public float ScaleY { get; set; } = 1;
    public float Rotation { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
}

public sealed class ColKeyData
{
    public int Frame { get; set; }
    public float R { get; set; } = 1;
    public float G { get; set; } = 1;
    public float B { get; set; } = 1;
    public float A { get; set; } = 1;
}

public static class A2dLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// Finds the exported scenes by walking up from the Godot project folder
    /// looking for `extracted/a2d`, so the player works from a source checkout
    /// without any path configuration.
    /// </summary>
    public static string? FindSceneDirectory(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "extracted", "a2d");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    public static List<A2dSceneData> LoadAll(string directory)
    {
        var scenes = new List<A2dSceneData>();
        foreach (var path in Directory.GetFiles(directory, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var scene = JsonSerializer.Deserialize<A2dSceneData>(File.ReadAllText(path), Options);
                if (scene is { Animations.Count: > 0 }) scenes.Add(scene);
            }
            catch (JsonException)
            {
                // A malformed export should not take the whole player down.
            }
        }
        return scenes;
    }
}
