namespace OpenBuzz.Cli;

using System.Text.Json;
using OpenBuzz.Cli.Lua;

/// <summary>
/// The host and hostess speech, indexed against the scripts that declare it.
///
/// The disc carries Buzz and Rose in Dutch as mono clips in `NETSPEAK.PAK`:
/// `C_&lt;id&gt;_&lt;variation&gt;` for commentary and `F_&lt;id&gt;_&lt;variation&gt;` for fixed
/// speech. Which lines exist is not guesswork - the scripts say so directly,
/// calling `SetCommentaryVariationCount(id, n)` and
/// `SetFixedSpeechVariationCount(id, n)` for every line they can play.
///
/// So this reads those calls, checks them against the clips actually on the
/// disc, and writes an index the engine can play from. Anything declared but
/// missing, or present but never declared, is reported rather than hidden -
/// a line the scripts ask for and the disc does not have would otherwise be
/// silence with no explanation.
/// </summary>
internal static class SpeechCommands
{
    private const string Commentary = "SetCommentaryVariationCount";
    private const string Fixed = "SetFixedSpeechVariationCount";

    public static int Run(string scriptDir, string wavDir, string? outPath, string locale)
    {
        // Only this locale's table. Every language ships its own SpeechInfo -
        // DEN, ESP, FIN, FRA, GER, ITA, NOR, POR and NET - and scanning all of
        // them declares lines this disc has no clips for, which reads as
        // missing audio when it is really another language's list.
        var info = Path.Combine(scriptDir, locale + "SpeechInfo.clu");
        var only = File.Exists(info) ? new[] { info } : null;

        var declared = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal)
        {
            ["commentary"] = [],
            ["fixed"] = [],
        };

        int scanned = 0;
        foreach (var path in only ?? Directory.GetFiles(scriptDir, "*.clu", SearchOption.AllDirectories))
        {
            LuaProto proto;
            try { proto = LuaUndump.Load(File.ReadAllBytes(path), Path.GetFileName(path)); }
            catch { continue; }
            scanned++;

            foreach (var call in LuaDataExtractor.Extract(proto))
            {
                var bucket = call.Function == Commentary ? "commentary"
                           : call.Function == Fixed ? "fixed" : null;
                if (bucket is null) continue;

                var id = call.Number(0);
                var count = call.Number(1);
                if (id is null || count is null) continue;

                // The same line is declared from several scripts; the largest
                // count wins, since that is how many the engine may reach for.
                int key = (int)id.Value;
                int have = declared[bucket].GetValueOrDefault(key);
                declared[bucket][key] = Math.Max(have, (int)count.Value);
            }
        }

        var onDisc = new Dictionary<string, Dictionary<int, List<int>>>(StringComparer.Ordinal)
        {
            ["commentary"] = Clips(wavDir, "C_"),
            ["fixed"] = Clips(wavDir, "F_"),
        };

        Console.WriteLine(only is null
            ? $"Scanned {scanned} scripts; no {locale}SpeechInfo found."
            : $"Read {locale}SpeechInfo.");
        Console.WriteLine();

        var index = new Dictionary<string, Dictionary<string, int[]>>(StringComparer.Ordinal);

        foreach (var bucket in new[] { "commentary", "fixed" })
        {
            var says = declared[bucket];
            var has = onDisc[bucket];

            var missing = says.Keys.Where(id => !has.ContainsKey(id)).OrderBy(id => id).ToList();
            var undeclared = has.Keys.Where(id => !says.ContainsKey(id)).OrderBy(id => id).ToList();
            int clips = has.Values.Sum(v => v.Count);

            Console.WriteLine($"== {bucket}");
            Console.WriteLine($"   declared by scripts : {says.Count} lines");
            Console.WriteLine($"   on the disc         : {has.Count} lines, {clips} clips");
            Console.WriteLine($"   declared but absent : {missing.Count}"
                + (missing.Count == 0 ? "" : "  " + string.Join(", ", missing.Take(8))));
            Console.WriteLine($"   present, undeclared : {undeclared.Count}"
                + (undeclared.Count == 0 ? "" : "  " + string.Join(", ", undeclared.Take(8))));

            var shortfall = says.Where(kv => has.TryGetValue(kv.Key, out var v) && v.Count < kv.Value)
                                .OrderBy(kv => kv.Key).ToList();
            Console.WriteLine($"   fewer clips than declared : {shortfall.Count}"
                + (shortfall.Count == 0 ? "" : "  " + string.Join(", ",
                    shortfall.Take(5).Select(kv => $"{kv.Key} wants {kv.Value} has {has[kv.Key].Count}"))));
            Console.WriteLine();

            index[bucket] = has.ToDictionary(
                kv => kv.Key.ToString(),
                kv => kv.Value.OrderBy(v => v).ToArray());
        }

        if (outPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            File.WriteAllText(outPath,
                JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Wrote the speech index to {outPath}");
        }
        return 0;
    }

    /// <summary>
    /// Where each line is used.
    ///
    /// The ids are not a table in the executable - a search of it turns up 34
    /// scattered words and no run of them - because the scripts name them
    /// directly. Every round start script names its own block, so the usage is
    /// recoverable by finding calls whose arguments are known line ids.
    /// </summary>
    public static int Usage(string scriptDir, string wavDir, string locale)
    {
        var known = new HashSet<int>();
        foreach (var kind in new[] { "C_", "F_" })
            foreach (var id in Clips(wavDir, kind).Keys) known.Add(id);

        if (known.Count == 0)
        {
            Console.Error.WriteLine($"no decoded clips under {wavDir}");
            return 1;
        }

        // script -> function -> ids
        var use = new Dictionary<string, Dictionary<string, SortedSet<int>>>(StringComparer.Ordinal);

        foreach (var path in Directory.GetFiles(scriptDir, "*.clu", SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            // The SpeechInfo tables only declare counts; they are not usage.
            if (name.EndsWith("SpeechInfo", StringComparison.Ordinal)) continue;

            LuaProto proto;
            try { proto = LuaUndump.Load(File.ReadAllBytes(path), name); }
            catch { continue; }

            foreach (var call in LuaDataExtractor.Extract(proto))
                for (int i = 0; i < call.Args.Count; i++)
                {
                    var n = call.Number(i);
                    if (n is null) continue;
                    int id = (int)n.Value;
                    if (!known.Contains(id)) continue;

                    if (!use.TryGetValue(name, out var byFn)) use[name] = byFn = [];
                    if (!byFn.TryGetValue(call.Function, out var set)) byFn[call.Function] = set = [];
                    set.Add(id);
                }
        }

        Console.WriteLine($"{known.Count} lines on the disc; {use.Count} scripts name one.");
        Console.WriteLine();
        foreach (var (script, byFn) in use.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            Console.WriteLine(script);
            foreach (var (fn, set) in byFn.OrderBy(k => k.Key, StringComparer.Ordinal))
                Console.WriteLine($"   {fn,-44} {string.Join(", ", set)}");
        }
        return 0;
    }

    /// The variations actually present, by line id.
    private static Dictionary<int, List<int>> Clips(string wavDir, string prefix)
    {
        var found = new Dictionary<int, List<int>>();
        if (!Directory.Exists(wavDir)) return found;

        foreach (var path in Directory.GetFiles(wavDir, prefix + "*.wav"))
        {
            var parts = Path.GetFileNameWithoutExtension(path).Split('_');
            if (parts.Length < 3) continue;
            if (!int.TryParse(parts[1], out int id)) continue;
            if (!int.TryParse(parts[2], out int variation)) continue;

            if (!found.TryGetValue(id, out var list)) found[id] = list = [];
            list.Add(variation);
        }
        return found;
    }
}
