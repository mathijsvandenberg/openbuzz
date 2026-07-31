using System.Text.Json;
using System.Text.Json.Serialization;
using OpenBuzz.Cli.Lua;

namespace OpenBuzz.Cli;

/// A transform keyframe: position, scale and rotation at a frame.
public sealed record TfmKey(int Frame, float ScaleX, float ScaleY, float Rotation, float X, float Y);

/// A colour keyframe. RGB is white throughout the disc; alpha is what animates.
public sealed record ColKey(int Frame, float R, float G, float B, float A);

/// An axis-aligned box in the scene's y-up coordinates.
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
    public List<TfmKey> Transform { get; } = [];
    public List<ColKey> Colour { get; } = [];
}

public sealed class A2dAnimation
{
    public required string Name { get; init; }
    public required int FrameCount { get; init; }
    public List<A2dObject> Objects { get; } = [];
}

public sealed class A2dScene
{
    public required string Name { get; init; }
    public List<A2dAnimation> Animations { get; } = [];

    /// Actor-to-sprite and actor-to-text bindings declared alongside the timelines.
    public Dictionary<string, string> IconBindings { get; } = [];

    /// <summary>
    /// Folds a chunk's recovered call stream into a scene.
    ///
    /// `Anm` opens an animation; `Obj` declares an object slot within it;
    /// `Tfm`, `Col` and `Bbx` attach to the slot named by their second
    /// argument. Slots are reused between animations, so an `Obj` for a slot
    /// already in use starts a fresh object rather than appending to the old one.
    /// </summary>
    public static A2dScene Build(string fallbackName, IReadOnlyList<LuaCall> calls)
    {
        // The chunk opens with a zero-argument call naming itself.
        var first = calls.FirstOrDefault();
        var scene = new A2dScene
        {
            Name = first is { Args.Count: 0 } && first.Function.StartsWith("BZ_", StringComparison.Ordinal)
                ? first.Function
                : fallbackName,
        };

        A2dAnimation? current = null;
        var slots = new Dictionary<int, A2dObject>();

        foreach (var call in calls)
        {
            switch (call.Function)
            {
                case "Anm" when call.Args.Count >= 2:
                    current = new A2dAnimation
                    {
                        Name = call.Text(0) ?? "?",
                        FrameCount = (int)(call.Number(1) ?? 0),
                    };
                    scene.Animations.Add(current);
                    slots.Clear();
                    break;

                case "Obj" when current is not null && call.Args.Count >= 2:
                {
                    int slot = (int)(call.Number(0) ?? -1);
                    var obj = new A2dObject { Slot = slot, Name = call.Text(1) ?? "?" };
                    slots[slot] = obj;
                    current.Objects.Add(obj);
                    break;
                }

                case "Tfm" when call.Args.Count >= 7:
                    if (slots.TryGetValue((int)(call.Number(1) ?? -1), out var t))
                        t.Transform.Add(new TfmKey(
                            (int)(call.Number(0) ?? 0),
                            (float)(call.Number(2) ?? 1), (float)(call.Number(3) ?? 1),
                            (float)(call.Number(4) ?? 0),
                            (float)(call.Number(5) ?? 0), (float)(call.Number(6) ?? 0)));
                    break;

                case "Col" when call.Args.Count >= 6:
                    if (slots.TryGetValue((int)(call.Number(1) ?? -1), out var c))
                        c.Colour.Add(new ColKey(
                            (int)(call.Number(0) ?? 0),
                            (float)(call.Number(2) ?? 1), (float)(call.Number(3) ?? 1),
                            (float)(call.Number(4) ?? 1), (float)(call.Number(5) ?? 1)));
                    break;

                case "Bbx" when call.Args.Count >= 6:
                    if (slots.TryGetValue((int)(call.Number(1) ?? -1), out var b))
                        b.Box = new Bounds(
                            (float)(call.Number(2) ?? 0), (float)(call.Number(3) ?? 0),
                            (float)(call.Number(4) ?? 0), (float)(call.Number(5) ?? 0));
                    break;

                case "SetActorToIconMapping" when call.Args.Count >= 2:
                    if (call.Text(0) is { } actor && call.Text(1) is { } icon)
                        scene.IconBindings[actor] = icon;
                    break;
            }
        }

        return scene;
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}
