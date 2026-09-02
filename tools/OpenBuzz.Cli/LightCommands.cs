namespace OpenBuzz.Cli;

using System.Text.Json;
using OpenBuzz.Graphics;

/// Reads the eight `Lights*.rp2` rigs, one per studio mood.
internal static class LightCommands
{
    public static int List(string inDir, string? stream = null)
    {
        var rigs = Load(inDir, stream);
        if (rigs.Count == 0) { Console.Error.WriteLine($"no Lights*.rp2 under {inDir}"); return 1; }

        foreach (var (mood, lights) in rigs)
        {
            Console.WriteLine($"{mood}  ({lights.Count} lights)");
            foreach (var l in lights)
                Console.WriteLine(
                    $"  {l.Name,-24} rgb({l.Colour[0],5:0.00} {l.Colour[1],5:0.00} {l.Colour[2],5:0.00})  " +
                    $"radius {l.Radius,6:0}  pos({l.Position[0],8:0.#} {l.Position[1],7:0.#} {l.Position[2],8:0.#})  " +
                    $"{Kind(l.Type)}");
        }
        return 0;
    }

    public static int Export(string inDir, string outPath, string? stream = null)
    {
        var rigs = Load(inDir, stream);
        if (rigs.Count == 0) { Console.Error.WriteLine($"no Lights*.rp2 under {inDir}"); return 1; }

        var payload = rigs.ToDictionary(r => r.Mood, r => r.Lights.Select(l => new
        {
            name = l.Name,
            type = l.Type,
            kind = Kind(l.Type),
            position = l.Position,
            direction = l.Direction,
            colour = l.Colour,
            energy = Math.Round(l.Energy, 4),
            radius = l.Radius,
            cone = Math.Round(l.ConeDegrees, 3),
        }).ToList());

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"Wrote {rigs.Count} light rigs to {outPath}");
        return 0;
    }

    private static string Kind(int type) => type switch
    {
        1 => "directional",
        2 => "ambient",
        0x80 => "point",
        0x81 => "spot",
        0x82 => "soft spot",
        _ => $"type 0x{type:X2}",
    };

    private static List<(string Mood, List<RwLightSource> Lights)> Load(string inDir, string? stream = null)
    {
        var rigs = new List<(string, List<RwLightSource>)>();
        if (!Directory.Exists(inDir)) return rigs;

        // The studio's moods are Lights<Mood>.rp2; the green room keeps its one
        // rig under its own name, so a caller can ask for a single stream.
        var files = stream is null
            ? Directory.GetFiles(inDir, "Lights*.rp2").OrderBy(p => p, StringComparer.Ordinal)
            : Directory.GetFiles(inDir, stream).OrderBy(p => p, StringComparer.Ordinal);

        foreach (var path in files)
        {
            var lights = RwLightSet.Parse(File.ReadAllBytes(path));
            if (lights.Count == 0) continue;
            // "LightsRedTension" -> "RedTension", which is how a mood is named.
            var name = Path.GetFileNameWithoutExtension(path);
            var mood = name.StartsWith("Lights", StringComparison.Ordinal)
                ? name["Lights".Length..] : name;
            rigs.Add((mood, lights));
        }
        return rigs;
    }
}
