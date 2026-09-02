namespace OpenBuzz.Cli;

using OpenBuzz.Cli.Lua;

/// <summary>
/// What the shipped game carries but never uses.
///
/// The scripts are this game's design document, and they kept things the
/// release did not: round types that sit in the parameter table but are not
/// among the ten you can play, globals assigned and never read, debug
/// switches, and engine functions no script ever calls.
///
/// It also guards a mistake this game makes easy. A constant can look
/// authoritative - a sensible name, a plausible value, sitting in GenericData
/// beside constants that are real - and still be dead. CountdownTimerIconX is
/// exactly that, and drawing at it looked sourced while being a guess.
/// </summary>
internal static class DeadCommands
{
    private static readonly string NL = Environment.NewLine;

    /// The ten rounds the finished game offers, by their parameter table id.
    private static readonly string[] Shipped =
    [
        "PointsBuilderRoundID", "FastestFingerFirstID", "LookBeforeYouLeapRoundID",
        "SnapRoundID", "PointStealerRoundID", "BuzzStopRoundID", "OffLoaderRoundID",
        "PassTheBombRoundID", "TimeBuilderRoundID", "HotSeatRoundID",
    ];

    /// <summary>
    /// How far a round got.
    ///
    /// A parameter table on its own is a name and a few layout fields. A round
    /// script, a start script and both sets of art mean it was built. Saying
    /// which is the point: "not among the shipped ten" covers both an idea
    /// parked in a table and a finished round that did not make it.
    /// </summary>
    private static string Completeness(string stem, string scriptDir, string artDir)
    {
        bool Script(string suffix) =>
            File.Exists(Path.Combine(scriptDir, stem + suffix + ".clu"));

        bool Art(string prefix) =>
            Directory.Exists(artDir) && Directory.EnumerateFiles(artDir, "*.json")
                .Any(f => string.Equals(Path.GetFileNameWithoutExtension(f),
                                        prefix + stem, StringComparison.OrdinalIgnoreCase));

        var have = new List<string>();
        if (Script("Round")) have.Add("round script");
        if (Script("RoundStart") || Script("QuizRoundStart")) have.Add("start script");
        if (Art("BZ_FE_RS_")) have.Add("round-start art");
        if (Art("BZ_FE_BUMP_")) have.Add("bumper art");

        return have.Count == 0 ? "parameters only - never built" : string.Join(", ", have);
    }

    public static int Run(string scriptDir, string? apiPath)
    {
        var files = Directory.GetFiles(scriptDir, "*.clu", SearchOption.AllDirectories)
                             .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (files.Length == 0)
        {
            Console.Error.WriteLine($"no .clu under {scriptDir}");
            return 1;
        }

        var setBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var read = new HashSet<string>(StringComparer.Ordinal);
        var called = new HashSet<string>(StringComparer.Ordinal);
        var tables = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);

        foreach (var path in files)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            LuaProto proto;
            try { proto = LuaUndump.Load(File.ReadAllBytes(path), name); }
            catch { continue; }

            var usage = LuaUsage.Scan(proto);
            foreach (var g in usage.Set)
            {
                if (!setBy.TryGetValue(g, out var who)) setBy[g] = who = [];
                who.Add(name);
            }
            foreach (var g in usage.Read) read.Add(g);
            foreach (var g in usage.Called) called.Add(g);

            foreach (var (t, fields) in LuaTableExtractor.Extract(proto).Tables)
                if (!tables.ContainsKey(t)) tables[t] = fields;
        }

        Console.WriteLine($"Scanned {files.Length} scripts.");

        var cut = tables.Keys.Where(t => t.EndsWith("ID", StringComparison.Ordinal))
                             .Where(t => !Shipped.Contains(t))
                             .Where(t => tables[t].ContainsKey("RoundNameText"))
                             .OrderBy(t => t, StringComparer.Ordinal).ToList();

        var artDir = Path.Combine(
            Path.GetDirectoryName(scriptDir.TrimEnd(Path.DirectorySeparatorChar)) ?? ".", "a2d");

        Console.WriteLine($"{NL}== Round types on the disc but not among the shipped ten ({cut.Count})");
        foreach (var t in cut)
        {
            tables[t].TryGetValue("RoundNameText", out var label);
            // RoundNameCallMyBluff -> CallMyBluff, which is what names its files.
            var stem = (label?.ToString() ?? t).Replace("RoundName", "");
            Console.WriteLine($"   {t,-30} {stem,-24} {Completeness(stem, scriptDir, artDir)}");
            foreach (var (k, v) in tables[t].OrderBy(f => f.Key, StringComparer.Ordinal))
                if (k != "RoundNameText" && v is not null)
                    Console.WriteLine($"        {k,-30} = {v}");
        }

        Console.WriteLine($"{NL}   A round the shipped game does not offer may have been cut from it");
        Console.WriteLine("   or parked for a later title; the disc alone cannot tell those apart.");

        var unread = setBy.Keys.Where(g => !read.Contains(g) && !called.Contains(g))
                               .OrderBy(g => g, StringComparer.Ordinal).ToList();
        Console.WriteLine($"\n== Globals assigned and never read ({unread.Count})");
        foreach (var g in unread.Take(60))
            Console.WriteLine($"   {g,-46} set by {string.Join(", ", setBy[g].Distinct().Take(2))}");
        if (unread.Count > 60) Console.WriteLine($"   ... and {unread.Count - 60} more");

        var debug = setBy.Keys.Concat(read).Concat(called).Distinct()
                         .Where(g => g.Contains("DEBUG", StringComparison.Ordinal)
                                  || g.Contains("Debug", StringComparison.Ordinal))
                         .OrderBy(g => g, StringComparer.Ordinal).ToList();
        Console.WriteLine($"\n== Debug switches and hooks ({debug.Count})");
        foreach (var g in debug) Console.WriteLine($"   {g}");

        // Rounds that were built and then left out of the parameter table -
        // the opposite orphan to the ones above, and the reason QuizMaster does
        // not show up there despite having a round script, a start script and
        // both sets of art.
        var built = Directory.EnumerateFiles(scriptDir, "*Round.clu")
            .Select(f => Path.GetFileNameWithoutExtension(f)[..^"Round".Length])
            .Where(stem => !tables.Keys.Any(t =>
                t.StartsWith(stem, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s, StringComparer.Ordinal).ToList();

        Console.WriteLine($"{NL}== Rounds with a script but no parameter table ({built.Count})");
        foreach (var stem in built)
            Console.WriteLine($"   {stem,-30} {Completeness(stem, scriptDir, artDir)}");

        // Deliberately no "engine functions never called" section. host-api.md
        // derives its list from the calls themselves, so asking which of them
        // are never called answers itself. Doing it properly means enumerating
        // what the executable registers - the binding loop at 0x0017F1E4 hands
        // each name to 0x00116EA0 - and comparing that against these calls.

        return 0;
    }
}
