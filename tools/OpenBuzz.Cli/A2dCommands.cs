using System.Globalization;
using System.Text;
using OpenBuzz.Animation;
using OpenBuzz.Cli.Lua;

namespace OpenBuzz.Cli;

/// <summary>
/// The `Scripts/A2d` chunks are not logic: they use Lua as a data format,
/// emitting 2D animation timelines through a handful of global calls. These
/// commands recover the call streams and summarise their shape so the schema
/// can be inferred from the data rather than assumed.
/// </summary>
public static class A2dCommands
{
    public static int Stats(string dir)
    {
        var files = Directory.GetFiles(dir, "*.clu", SearchOption.AllDirectories)
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) { Console.Error.WriteLine($"No .clu files under {dir}."); return 1; }

        var byFunction = new Dictionary<string, List<LuaCall>>(StringComparer.Ordinal);
        int total = 0;

        foreach (var path in files)
        {
            var root = LuaUndump.Load(File.ReadAllBytes(path), Path.GetFileName(path));
            foreach (var call in LuaDataExtractor.Extract(root))
            {
                if (!byFunction.TryGetValue(call.Function, out var list))
                    byFunction[call.Function] = list = [];
                list.Add(call);
                total++;
            }
        }

        Console.WriteLine($"{files.Length} chunks, {total} calls recovered");
        Console.WriteLine();
        Console.WriteLine($"{"function",-24} {"calls",8} {"arity",6} {"argument ranges",40}");

        foreach (var (name, calls) in byFunction.OrderByDescending(kv => kv.Value.Count))
        {
            var arities = calls.Select(c => c.Args.Count).Distinct().Order().ToArray();
            Console.WriteLine($"{name,-24} {calls.Count,8} {string.Join("/", arities),6}   {Describe(calls)}");
        }

        return 0;
    }

    /// Per-argument summary: string, constant, or numeric range.
    private static string Describe(List<LuaCall> calls)
    {
        int argc = calls.Max(c => c.Args.Count);
        var parts = new List<string>();

        for (int i = 0; i < argc && i < 8; i++)
        {
            var values = calls.Where(c => i < c.Args.Count).Select(c => c.Args[i]).ToArray();
            if (values.Length == 0) { parts.Add("-"); continue; }

            if (values.All(v => v is string))
            {
                int distinct = values.Cast<string>().Distinct().Count();
                parts.Add($"str({distinct})");
            }
            else if (values.All(v => v is double))
            {
                var nums = values.Cast<double>().ToArray();
                double lo = nums.Min(), hi = nums.Max();
                int distinct = nums.Distinct().Count();
                parts.Add(distinct == 1
                    ? Num(lo)
                    : $"{Num(lo)}..{Num(hi)}");
            }
            else if (values.All(v => v is null)) parts.Add("nil");
            else parts.Add("mixed");
        }

        return string.Join("  ", parts);
    }

    private static string Num(double d) =>
        d == Math.Floor(d) && Math.Abs(d) < 1e9
            ? ((long)d).ToString(CultureInfo.InvariantCulture)
            : d.ToString("0.###", CultureInfo.InvariantCulture);

    /// Folds every chunk into a scene and writes it out as JSON for the runtime.
    public static int Export(string dir, string outDir)
    {
        var files = Directory.GetFiles(dir, "*.clu", SearchOption.AllDirectories)
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) { Console.Error.WriteLine($"No .clu files under {dir}."); return 1; }

        Directory.CreateDirectory(outDir);
        int scenes = 0, anims = 0, objects = 0, keys = 0;

        foreach (var path in files)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var root = LuaUndump.Load(File.ReadAllBytes(path), Path.GetFileName(path));
            var scene = A2dSceneBuilder.Build(name, LuaDataExtractor.Extract(root));

            File.WriteAllText(Path.Combine(outDir, name + ".json"), scene.ToJson());

            scenes++;
            anims += scene.Animations.Count;
            foreach (var a in scene.Animations)
            {
                objects += a.Objects.Count;
                keys += a.Objects.Sum(o => o.Transform.Count + o.Colour.Count);
            }
        }

        Console.WriteLine($"Exported {scenes} scenes, {anims} animations, {objects} objects, {keys} keyframes to {outDir}");
        return 0;
    }

    /// Writes the recovered call stream for one chunk, in order.
    public static int Dump(string dir, string chunk, string outPath)
    {
        var path = Directory.GetFiles(dir, "*.clu", SearchOption.AllDirectories)
                            .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p)
                                .Equals(chunk, StringComparison.OrdinalIgnoreCase));
        if (path is null)
        {
            Console.Error.WriteLine($"No chunk named '{chunk}' under {dir}.");
            return 1;
        }

        var root = LuaUndump.Load(File.ReadAllBytes(path), Path.GetFileName(path));
        var calls = LuaDataExtractor.Extract(root);

        var sb = new StringBuilder();
        sb.AppendLine($"; {Path.GetFileName(path)} - {calls.Count} calls");
        foreach (var call in calls) sb.AppendLine(call.ToString());

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"Wrote {calls.Count} calls to {outPath}");
        return 0;
    }
}

