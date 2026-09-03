namespace OpenBuzz.Cli;

using System.Text;
using OpenBuzz.Cli.Lua;

/// <summary>
/// Which screen-text keys the game asks for, and which of them still have no
/// string behind them.
///
/// The engine fetches localised text by name through a hash in `default.ndx`
/// that has not been identified, so `docs/text-key-map.txt` stands in for it,
/// one key to one line of `default.str`. That map is filled by hand, and this
/// is the worksheet for filling it: every key the scripts actually use, which
/// screen uses it, and - for the ones still unmapped - the lines of the string
/// table nothing has claimed yet.
///
/// The report carries game text, so it is written under `extracted/` and stays
/// out of the repository. The key map itself is identifiers and integers only,
/// which is why that one can be checked in.
/// </summary>
internal static class TextTodo
{
    /// Natives whose *first* argument names a string in the table.
    private static readonly HashSet<string> KeyFirst = new(StringComparer.Ordinal)
    {
        "GetTextFromNamedString", "GetNumberOfCharsInNamedString",
        "GetCharacterInNamedString", "AddMenuItem", "AddMenuTitleText",
        "AddMenuStatusText", "AddPossibleMenuItem", "AddIconMenuItem",
        "AddIconMenuTitleText", "GetPixelWidthOfString",
    };

    /// Natives that take the key as their *last* string argument, after the
    /// box and the font. `PlaceCustom*` are deliberately absent: custom means
    /// the caller passes the text itself rather than a name.
    private static readonly HashSet<string> KeyLast = new(StringComparer.Ordinal)
    {
        "PlaceTextAt", "PlaceTextAndIconAt", "PlacePreLocaleTextAt",
        "PlaceSizedTextAndIconAt", "PlaceTextAtAndIconAtWithBiggerGap",
    };

    /// Strings that are arguments to those natives but name something else -
    /// a font, a justification, a colour - rather than a line of text.
    private static readonly HashSet<string> NotAKey = new(StringComparer.Ordinal)
    {
        "Centre", "Center", "Left", "Right", "Top", "Bottom", "Middle",
        "White", "Black", "Red", "Green", "Blue", "Orange", "Yellow", "Grey",
        "Fade", "Move", "Pulse", "UP", "DOWN", "NA", "None", "TAKEN",
    };

    public static int Run(string scriptDir, string mapPath, string stringsPath,
        string outPath, string spritesPath)
    {
        var mapped = ReadMap(mapPath);
        var strings = ReadStrings(stringsPath);

        // The *AndIcon* natives take an icon name as well as a key, so the
        // atlas is what tells the two apart - BlueTriangleButton and fill are
        // pictures, not lines of text.
        var icons = ReadSprites(spritesPath);

        // key -> the places that ask for it
        var used = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var path in Directory.GetFiles(scriptDir, "*.clu", SearchOption.AllDirectories))
        {
            var script = Path.GetFileNameWithoutExtension(path);
            LuaProto proto;
            try { proto = LuaUndump.Load(File.ReadAllBytes(path), script); }
            catch { continue; }

            foreach (var call in LuaDataExtractor.Extract(proto))
            {
                string? key = null;
                if (KeyFirst.Contains(call.Function)) key = call.Text(0);
                else if (KeyLast.Contains(call.Function)) key = LastText(call);
                // The A2D scenes bind text by name too, which is where every
                // round's rules panel gets its lines.
                else if (call.Function.StartsWith("SetActorToTextMapping", StringComparison.Ordinal))
                    key = call.Text(1);
                if (!Plausible(key) || icons.Contains(key!)) continue;

                if (!used.TryGetValue(key!, out var where)) used[key!] = where = [];
                where.Add(script);
            }
        }

        var missing = used.Keys.Where(k => !mapped.ContainsKey(k)).ToList();
        var claimed = mapped.Values.ToHashSet();
        var free = Enumerable.Range(1, strings.Length)
                             .Where(id => !claimed.Contains(id)
                                       && !string.IsNullOrWhiteSpace(strings[id - 1]))
                             .ToList();

        // Keys the map covers that no call names. Some are held in tables the
        // scripts index rather than passed to a native - the 24 buzzer sounds
        // are a list, not a call - so the map is legitimately ahead of the scan.
        var unseen = mapped.Keys.Where(k => !used.ContainsKey(k)).ToList();

        Console.WriteLine($"{used.Count} keys are named by a call.");
        Console.WriteLine($"   mapped   {used.Count - missing.Count}");
        Console.WriteLine($"   to do    {missing.Count}");
        Console.WriteLine($"{mapped.Count} keys in the map; {unseen.Count} of them are held in tables"
            + " rather than named by a call.");
        Console.WriteLine($"{strings.Length} strings in the table, {free.Count} not yet claimed.");
        Console.WriteLine();

        foreach (var (screen, keys) in ByScreen(missing, used))
        {
            Console.WriteLine($"{screen}  ({keys.Count})");
            foreach (var k in keys) Console.WriteLine($"   {k}");
        }

        Write(outPath, used, mapped, missing, strings, free);
        Console.WriteLine();
        Console.WriteLine($"Worksheet written to {outPath}");
        Console.WriteLine("It carries game text, so it stays out of the repository.");
        return 0;
    }

    /// The last string argument of a call, which is where Place*TextAt puts the
    /// key - after the box, the scaling and the font name.
    private static string? LastText(LuaCall call)
    {
        for (int i = call.Args.Count - 1; i >= 0; i--)
            if (call.Args[i] is string s) return s;
        return null;
    }

    /// Keys look like identifiers. Fonts, colours and justifications are passed
    /// to the same calls and are not lines of text.
    private static bool Plausible(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length < 3 || key.Length > 64) return false;
        if (NotAKey.Contains(key)) return false;
        if (key.Contains(' ') || key.Contains('%') || key.Contains('/')) return false;
        if (!char.IsLetter(key[0])) return false;
        if (key.EndsWith("FontName", StringComparison.Ordinal)) return false;
        return key.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    /// Groups the outstanding keys by the script that asks for them, so a whole
    /// screen can be worked through at once.
    private static List<(string Screen, List<string> Keys)> ByScreen(
        List<string> missing, SortedDictionary<string, SortedSet<string>> used)
    {
        var byScreen = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var key in missing)
        {
            var screen = used[key].First();
            if (!byScreen.TryGetValue(screen, out var list)) byScreen[screen] = list = [];
            list.Add(key);
        }
        return byScreen.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    private static void Write(string outPath, SortedDictionary<string, SortedSet<string>> used,
        Dictionary<string, int> mapped, List<string> missing, string[] strings, List<int> free)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Screen text still to identify");
        sb.AppendLine();
        sb.AppendLine($"{used.Count} keys are asked for by the scripts. {used.Count - missing.Count}");
        sb.AppendLine($"are mapped in `docs/text-key-map.txt`; {missing.Count} are not.");
        sb.AppendLine();
        sb.AppendLine("The engine looks these up by a hash of the name, and that hash has not been");
        sb.AppendLine("identified, so each line below has to be matched to a string by meaning. Add a");
        sb.AppendLine("`Key = id` line to the key map for each one you settle.");
        sb.AppendLine();
        sb.AppendLine("**This file is game text and is not in the repository.**");
        sb.AppendLine();

        sb.AppendLine("## Keys with no string yet");
        sb.AppendLine();
        sb.AppendLine("Each screen lists its outstanding keys, then the unclaimed strings nearest the");
        sb.AppendLine("ids that screen already uses. The table is authored screen by screen, so a key's");
        sb.AppendLine("answer is usually a few lines from its neighbours - which turns 570 candidates");
        sb.AppendLine("into a shortlist. A screen with nothing anchored yet has no neighbourhood and");
        sb.AppendLine("falls back to the full list at the end.");
        sb.AppendLine();

        foreach (var (screen, keys) in ByScreen(missing, used))
        {
            sb.AppendLine($"### {screen}");
            sb.AppendLine();
            foreach (var k in keys)
            {
                var also = used[k].Skip(1).ToList();
                sb.AppendLine(also.Count == 0
                    ? $"- `{k}` = "
                    : $"- `{k}` =   <!-- also {string.Join(", ", also.Take(4))} -->");
            }
            sb.AppendLine();

            var near = Neighbourhood(screen, used, mapped, free);
            if (near.Count > 0)
            {
                sb.AppendLine($"Nearby unclaimed ({near.Count}):");
                sb.AppendLine();
                sb.AppendLine("| id | string |");
                sb.AppendLine("|---:|---|");
                foreach (var id in near) sb.AppendLine($"| {id} | {Cell(strings[id - 1])} |");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Strings nothing has claimed");
        sb.AppendLine();
        sb.AppendLine("Line numbers in `BM1/Text/NET/default.str`. A key's neighbours are usually its");
        sb.AppendLine("neighbours here too: the table is authored screen by screen.");
        sb.AppendLine();
        sb.AppendLine("| id | string |");
        sb.AppendLine("|---:|---|");
        foreach (var id in free) sb.AppendLine($"| {id} | {Cell(strings[id - 1])} |");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, sb.ToString());
    }

    /// How far either side of a screen's known ids to look. Wide enough to
    /// cross a short block, tight enough to stay a shortlist.
    private const int Reach = 24;

    /// <summary>
    /// The unclaimed strings sitting near the ids a screen already uses.
    ///
    /// The string table is authored a screen at a time - the round rules run in
    /// one block, the menu titles in another - so once one key on a screen is
    /// anchored, the rest are usually within a few lines of it.
    /// </summary>
    private static List<int> Neighbourhood(string screen,
        SortedDictionary<string, SortedSet<string>> used,
        Dictionary<string, int> mapped, List<int> free)
    {
        var anchors = used.Where(kv => kv.Value.Contains(screen) && mapped.ContainsKey(kv.Key))
                          .Select(kv => mapped[kv.Key]).ToList();
        if (anchors.Count == 0) return [];

        return free.Where(id => anchors.Any(a => Math.Abs(a - id) <= Reach)).ToList();
    }

    /// One table cell: pipes escaped and the literal line break the long UI
    /// strings carry flattened, so a row stays a row.
    private static string Cell(string text)
    {
        var s = text.Replace("|", "\\|").Replace("\\n", " ").Trim();
        return s.Length > 96 ? s[..96] + "…" : s;
    }

    private static HashSet<string> ReadSprites(string path)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return names;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            foreach (var p in doc.RootElement.EnumerateObject()) names.Add(p.Name);
        }
        catch { }
        return names;
    }

    private static Dictionary<string, int> ReadMap(string path)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!File.Exists(path)) return map;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line[0] == '[') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            if (int.TryParse(line[(eq + 1)..].Trim(), out int id))
                map[line[..eq].Trim()] = id;
        }
        return map;
    }

    private static string[] ReadStrings(string path)
    {
        if (!File.Exists(path)) return [];
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        var lines = text.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0) lines = lines[..^1];
        return lines;
    }
}
