namespace OpenBuzz.Cli;

using System.Text.Json;
using OpenBuzz.Cli.Lua;

/// <summary>
/// The on-screen layout, out of `GenericData.clu`.
///
/// The game keeps its 640x480 screen layout in that script as plain constant
/// assignment - QuestionTextPositionX, AnswerPositionYInc, the contestant
/// block StartX/StartY/Width/Height - plus a RoundParameters table with the
/// per-round overrides. Reading it means the port draws where the game draws
/// instead of where a screenshot suggested.
/// </summary>
internal static class LayoutCommands
{
    private const string Script = "GenericData.clu";

    public static int Export(string inDir, string outPath)
    {
        var path = FindScript(inDir);
        if (path is null)
        {
            Console.Error.WriteLine($"{Script} not found under {inDir}");
            return 1;
        }

        var proto = LuaUndump.Load(File.ReadAllBytes(path), Path.GetFileName(path));
        var data = LuaTableExtractor.Extract(proto);

        var payload = new
        {
            source = Path.GetFileName(path),
            globals = data.Globals.Where(kv => kv.Value is not null)
                          .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                          .ToDictionary(kv => kv.Key, kv => kv.Value),
            rounds = data.Tables.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                         .ToDictionary(kv => kv.Key,
                             kv => kv.Value.Where(f => f.Value is not null)
                                     .OrderBy(f => f.Key, StringComparer.Ordinal)
                                     .ToDictionary(f => f.Key, f => f.Value)),
        };

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"Wrote {payload.globals.Count} globals and " +
                          $"{payload.rounds.Count} round tables to {outPath}");
        return 0;
    }

    public static int Show(string inDir, string? filter)
    {
        var path = FindScript(inDir);
        if (path is null) { Console.Error.WriteLine($"{Script} not found"); return 1; }

        var data = LuaTableExtractor.Extract(LuaUndump.Load(File.ReadAllBytes(path), Path.GetFileName(path)));

        Console.WriteLine("globals:");
        foreach (var (name, value) in data.Globals.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (value is null) continue;
            if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine($"  {name,-42} = {Format(value)}");
        }

        foreach (var (table, fields) in data.Tables.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var shown = fields.Where(f => f.Value is not null &&
                            (filter is null || f.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                        .OrderBy(f => f.Key, StringComparer.Ordinal).ToList();
            if (shown.Count == 0) continue;
            Console.WriteLine($"\n{table}:");
            foreach (var (field, value) in shown)
                Console.WriteLine($"  {field,-42} = {Format(value)}");
        }
        return 0;
    }

    private static string Format(object? v) => v switch
    {
        double d when d == Math.Floor(d) => ((long)d).ToString(),
        double d => d.ToString("0.####"),
        bool b => b ? "true" : "false",
        _ => v?.ToString() ?? "nil",
    };

    private static string? FindScript(string inDir)
    {
        var direct = Path.Combine(inDir, Script);
        if (File.Exists(direct)) return direct;
        return Directory.EnumerateFiles(inDir, Script, SearchOption.AllDirectories).FirstOrDefault();
    }
}
