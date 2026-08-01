using System.Text;

namespace OpenBuzz.Quiz;

/// <summary>
/// A `.str` file: UTF-8 text, one string per line. References from the round
/// data are **1-based**, with 0 meaning "no string" - getting this wrong shifts
/// every question onto its own first answer, which reads plausibly enough to
/// hide the bug.
/// </summary>
public sealed class StringTable
{
    private readonly string[] _lines;

    private StringTable(string[] lines) => _lines = lines;

    public int Count => _lines.Length;

    public static StringTable Load(string path)
    {
        var text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        // Trailing newline would otherwise add a phantom empty entry.
        var lines = text.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0) lines = lines[..^1];
        return new StringTable(lines);
    }

    /// <param name="id">1-based string id; 0 yields null.</param>
    public string? Get(int id) =>
        id <= 0 || id > _lines.Length ? null : Unescape(_lines[id - 1]);

    public string GetOrEmpty(int id) => Get(id) ?? "";

    /// Long UI strings embed literal backslash-n rather than real newlines.
    private static string Unescape(string s) =>
        s.Contains("\\n", StringComparison.Ordinal) ? s.Replace("\\n", "\n") : s;
}
