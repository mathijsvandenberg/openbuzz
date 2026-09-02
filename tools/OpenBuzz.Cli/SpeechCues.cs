namespace OpenBuzz.Cli;

using System.Text.Json;
using OpenBuzz.Cli.Lua;

/// <summary>
/// Which line Buzz or Rose says, and when.
///
/// The variation counts in `NETSpeechInfo` say what exists; they say nothing
/// about when it plays. That is not in the executable either - searching it for
/// the 413 known ids as aligned words turns up 34 scattered hits and no table -
/// because the scripts carry the ids themselves and pass them to the speech
/// natives directly.
///
/// So the cue sheet is read back out of the scripts. Every round opens with
///
///     DoRoundIntroduction(speaker, round, round, a, b, c, intro, rules, 111000)
///
/// - identical in all eleven round scripts - and `RoundIntroduction` then plays
/// those three arguments at fixed points on its own clock: the intro line at
/// timing marker 5, the shared line 111000 opened at 8.4, and the rules table
/// handed to `DoAnimatedInstructions`, which walks it in order and waits for
/// each line to finish before the next.
///
/// Per-contestant lines are computed rather than written out, as in
/// `530200 + seat - 1`, so those arrive here as expressions and are matched
/// against the clips actually on the disc.
/// </summary>
internal static class SpeechCues
{
    private const string Intro = "DoRoundIntroduction";

    /// The natives that speak, and what the id argument means in each.
    private static readonly Dictionary<string, int> IdArg = new(StringComparer.Ordinal)
    {
        ["OpenFixedSpeechIntoSpecificSlot"] = 1,
        ["OpenFixedSpeechIntoLastUsedSlot"] = 1,
        ["OpenFixedSpeechIntoSpecificSmallerSlot"] = 1,
        ["PlayDynamicCommentarySpeech"] = 0,
        ["OpenCommentaryIntoSpecificSlot"] = 1,
        ["OpenCommentaryIntoLastUsedSlot"] = 1,
    };

    public static int Run(string scriptDir, string wavDir, string? outPath)
    {
        var have = OnDisc(wavDir);
        if (have.Count == 0)
        {
            Console.Error.WriteLine($"no decoded clips under {wavDir}; run `obz audio decode` first");
            return 1;
        }

        var rounds = new SortedDictionary<string, object>(StringComparer.Ordinal);
        var direct = new SortedDictionary<string, SortedDictionary<string, List<string>>>(StringComparer.Ordinal);
        var families = new SortedDictionary<string, object>(StringComparer.Ordinal);

        foreach (var path in Directory.GetFiles(scriptDir, "*.clu", SearchOption.AllDirectories))
        {
            var script = Path.GetFileNameWithoutExtension(path);
            if (script.EndsWith("SpeechInfo", StringComparison.Ordinal)) continue;

            LuaProto proto;
            try { proto = LuaUndump.Load(File.ReadAllBytes(path), script); }
            catch { continue; }

            var calls = LuaDataExtractor.Extract(proto);

            // The same script says which round it is, a few instructions
            // earlier: SetCurrentRoundID(PassTheBombRoundID). Recording it
            // beside the cue avoids matching rounds by trimming their names,
            // which does not survive SpeedTimeBuilder / TimeBuilderRoundID.
            var roundId = calls.FirstOrDefault(c => c.Function == "SetCurrentRoundID")?.Global(0);

            // And which round logic it hands off to, its first instruction:
            // SetFollowOnScript("TimeBuilderRound"). This is the only link that
            // holds for every round - SpeedTimeBuilderRoundStart runs
            // TimeBuilderRound, and the ids alone would never say so.
            var follows = calls.FirstOrDefault(c => c.Function == "SetFollowOnScript")?.Text(0);

            foreach (var call in calls)
            {
                if (call.Function == Intro && call.Args.Count >= 9)
                {
                    var round = call.Text(1) ?? script;
                    rounds[round] = new
                    {
                        script,
                        roundId,
                        follows,
                        speaker = call.Text(0),
                        // Timing markers are RoundIntroduction's own, not ours.
                        announce = Line(call.Number(6), have),
                        shared = Line(call.Number(8), have),
                        rules = (call.List(7)?.Items ?? [])
                            .Select(v => Line(v as double?, have))
                            .Where(v => v is not null).ToList(),
                    };
                    continue;
                }

                if (!IdArg.TryGetValue(call.Function, out int slot)) continue;

                if (call.Number(slot) is { } n && have.ContainsKey((int)n))
                {
                    var key = ((int)n).ToString();
                    if (!direct.TryGetValue(script, out var byFn)) direct[script] = byFn = [];
                    if (!byFn.TryGetValue(call.Function, out var ids)) byFn[call.Function] = ids = [];
                    if (!ids.Contains(key)) ids.Add(key);
                }
                else if (call.Expr(slot) is { } e)
                {
                    // A computed id. The constant is the family root; the run
                    // of clips actually present says how many members it has,
                    // rather than an assumed four.
                    int root = (int)e.Constant;
                    var members = new List<string>();
                    for (int i = 1; i <= 8 && have.ContainsKey(root + i); i++)
                        members.Add((root + i).ToString());
                    if (members.Count == 0 && have.ContainsKey(root)) members.Add(root.ToString());
                    if (members.Count == 0) continue;

                    families[$"{script}.{call.Function}"] = new
                    {
                        expression = e.Text,
                        variations = call.Number(slot + 1),
                        ids = members,
                    };
                }
            }
        }

        Console.WriteLine($"{rounds.Count} round introductions, "
            + $"{direct.Sum(s => s.Value.Sum(f => f.Value.Count))} direct cues, "
            + $"{families.Count} computed families.");
        Console.WriteLine();

        foreach (var (round, cue) in rounds)
        {
            dynamic c = cue;
            Console.WriteLine($"{round}  ({c.speaker} introduces)");
            Console.WriteLine($"   marker 5.0   announce   {c.announce}");
            Console.WriteLine($"   marker 8.4   shared     {c.shared}");
            foreach (var r in (List<string?>)c.rules)
                Console.WriteLine($"   then         rules      {r}");
        }

        if (families.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Computed per-seat lines:");
            foreach (var (where, fam) in families)
            {
                dynamic f = fam;
                Console.WriteLine($"   {where}");
                Console.WriteLine($"      {f.expression}  ->  {string.Join(", ", (List<string>)f.ids)}");
            }
        }

        if (outPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            File.WriteAllText(outPath, JsonSerializer.Serialize(
                new { rounds, direct, families },
                new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine();
            Console.WriteLine($"Wrote the cue sheet to {outPath}");
        }
        return 0;
    }

    /// An id only counts as a cue if the disc can actually speak it.
    private static string? Line(double? id, Dictionary<int, int> have) =>
        id is { } n && have.ContainsKey((int)n) ? ((int)n).ToString() : null;

    /// Every line the disc has, with how many variations, fixed and commentary.
    private static Dictionary<int, int> OnDisc(string wavDir)
    {
        var found = new Dictionary<int, int>();
        if (!Directory.Exists(wavDir)) return found;

        foreach (var path in Directory.GetFiles(wavDir, "*.wav"))
        {
            var parts = Path.GetFileNameWithoutExtension(path).Split('_');
            if (parts.Length < 3) continue;
            if (parts[0] is not ("C" or "F")) continue;
            if (!int.TryParse(parts[1], out int id)) continue;
            found[id] = found.GetValueOrDefault(id) + 1;
        }
        return found;
    }
}
