using OpenBuzz.Animation;
using OpenBuzz.Cli.Lua;

namespace OpenBuzz.Cli;

/// <summary>
/// Folds a chunk's recovered call stream into an <see cref="A2dScene"/>.
///
/// `Anm` opens an animation; `Obj` declares an object slot within it; `Tfm`,
/// `Col` and `Bbx` attach to the slot named by their **second** argument. Slots
/// are reused between animations, so an `Obj` for a slot already in use starts a
/// fresh object rather than appending to the previous one.
/// </summary>
public static class A2dSceneBuilder
{
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

                // Two variants: with and without justification arguments.

                case "SetActorToTextMappingWithSizeMultiplier" when call.Args.Count >= 4:

                    if (call.Text(0) is { } ta && call.Text(1) is { } tk)

                        scene.TextBindings[ta] = new TextBinding(tk, call.Text(2) ?? "", "Left", "Centre",

                                                                 (float)(call.Number(3) ?? 1));

                    break;


                case "SetActorToTextMappingWithJustificationAndSizeMultiplier" when call.Args.Count >= 6:

                    if (call.Text(0) is { } ja && call.Text(1) is { } jk)

                        scene.TextBindings[ja] = new TextBinding(jk, call.Text(2) ?? "", call.Text(3) ?? "Left",

                                                                 call.Text(4) ?? "Centre", (float)(call.Number(5) ?? 1));

                    break;


                case "SetActorToIconMapping" when call.Args.Count >= 2:
                    if (call.Text(0) is { } actor && call.Text(1) is { } icon)
                        scene.IconBindings[actor] = icon;
                    break;
            }
        }

        return scene;
    }
}
