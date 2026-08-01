using System.Text;

namespace OpenBuzz.Animation;

/// <summary>
/// Resolves A2D text keys to actual strings, standing in for the `default.ndx`
/// hash function that has not been identified.
///
/// The map is locale-independent: every locale on the disc ships a
/// byte-identical `default.ndx`, so a given id denotes the same key in Dutch,
/// French, German and Italian. Pointing <see cref="Load"/> at a different
/// locale's `default.str` yields that language with no other change.
///
/// The map is partial by design — keys whose pairing is uncertain are left out
/// rather than guessed, so an unresolved key returns null and the caller can
/// show the key itself instead of plausible-looking wrong text.
/// </summary>
public sealed class TextKeyMap
{
    private readonly Dictionary<string, int> _ids;
    private readonly string[] _strings;

    private TextKeyMap(Dictionary<string, int> ids, string[] strings)
    {
        _ids = ids;
        _strings = strings;
    }

    public int MappedKeys => _ids.Count;
    public int StringCount => _strings.Length;

    /// <summary>Resolves a key to its string, or null when unmapped.</summary>
    public string? Resolve(string key)
    {
        if (!_ids.TryGetValue(key, out int id)) return null;
        if (id <= 0 || id > _strings.Length) return null;

        var s = _strings[id - 1];
        // Long UI strings embed a literal backslash-n rather than a real newline.
        return s.Contains("\\n", StringComparison.Ordinal) ? s.Replace("\\n", "\n") : s;
    }

    public static TextKeyMap Empty() => new([], []);

    /// <param name="mapPath">docs/text-key-map.txt</param>
    /// <param name="stringsPath">BM1/Text/&lt;LANG&gt;/default.str</param>
    public static TextKeyMap Load(string mapPath, string stringsPath)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);

        if (File.Exists(mapPath))
        {
            foreach (var raw in File.ReadAllLines(mapPath))
            {
                var line = raw.Trim();
                // Skip comments, blanks, section headers, and the unresolved
                // list, which has names but no '=' and must not be mapped.
                if (line.Length == 0 || line[0] == '#' || line[0] == '[') continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line[..eq].Trim();
                if (int.TryParse(line[(eq + 1)..].Trim(), out int id) && key.Length > 0)
                    ids[key] = id;
            }
        }

        string[] strings = [];
        if (File.Exists(stringsPath))
        {
            var text = Encoding.UTF8.GetString(File.ReadAllBytes(stringsPath));
            strings = text.Split('\n');
            if (strings.Length > 0 && strings[^1].Length == 0) strings = strings[..^1];
        }

        return new TextKeyMap(ids, strings);
    }

    /// <summary>
    /// Locates the map and a locale's strings by walking up from a starting
    /// directory, matching how the other tools find their data.
    /// </summary>
    public static TextKeyMap Discover(string startDirectory, string locale = "NET")
    {
        for (var d = new DirectoryInfo(startDirectory); d is not null; d = d.Parent)
        {
            var map = Path.Combine(d.FullName, "docs", "text-key-map.txt");
            var str = Path.Combine(d.FullName, "extracted", "BM1", "Text", locale, "default.str");
            if (File.Exists(map) && File.Exists(str)) return Load(map, str);
        }
        return Empty();
    }
}
