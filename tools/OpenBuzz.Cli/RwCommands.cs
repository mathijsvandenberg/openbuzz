using OpenBuzz.Graphics;

namespace OpenBuzz.Cli;

/// <summary>
/// Reads the `RWStream/*.rp2` files: RenderWare streams holding the characters,
/// costumes, animations, studio set and cameras.
/// </summary>
public static class RwCommands
{
    /// Prints the chunk tree of one stream.
    public static int Tree(string path, int maxDepth)
    {
        if (!File.Exists(path)) { Console.Error.WriteLine($"Missing {path}"); return 1; }

        var data = File.ReadAllBytes(path);
        var nodes = RwStream.Parse(data, maxDepth);

        Console.WriteLine($"{Path.GetFileName(path)}  {data.Length:N0} bytes");
        foreach (var node in RwStream.Flatten(nodes))
            Console.WriteLine($"{new string(' ', node.Depth * 2)}{node.Name,-18} {node.Size,10:N0} @ 0x{node.DataOffset:X}");

        return 0;
    }

    /// Summarises what every stream contains, so the shape of the set is visible.
    public static int Summary(string dir)
    {
        var files = Directory.GetFiles(dir, "*.rp2").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) { Console.Error.WriteLine($"No .rp2 files under {dir}."); return 1; }

        Console.WriteLine($"{"file",-30} {"size",10} {"tex",5} {"clump",6} {"geom",5} {"atomic",7} {"anim",5} {"light",6}");

        int totalTextures = 0, totalGeometry = 0;
        foreach (var path in files)
        {
            var data = File.ReadAllBytes(path);
            var flat = RwStream.Flatten(RwStream.Parse(data)).ToList();

            int Count(uint id) => flat.Count(n => n.Id == id);
            int tex = Count(RwId.TextureNative), geom = Count(RwId.Geometry);
            totalTextures += tex;
            totalGeometry += geom;

            Console.WriteLine($"{Path.GetFileNameWithoutExtension(path),-30} {data.Length,10:N0} " +
                              $"{tex,5} {Count(RwId.Clump),6} {geom,5} {Count(RwId.Atomic),7} " +
                              $"{Count(RwId.AnimAnimation),5} {Count(RwId.Light),6}");
        }

        Console.WriteLine();
        Console.WriteLine($"{files.Length} streams, {totalTextures} embedded textures, {totalGeometry} geometries");
        return 0;
    }

    /// <summary>
    /// Extracts the textures embedded in the streams. Their payload is laid out
    /// exactly like a standalone `.tex`, so the same decoder reads them.
    /// </summary>
    public static int Textures(string dir, string outDir)
    {
        var files = Directory.GetFiles(dir, "*.rp2").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) { Console.Error.WriteLine($"No .rp2 files under {dir}."); return 1; }

        Directory.CreateDirectory(outDir);
        int written = 0, failed = 0;

        foreach (var path in files)
        {
            var data = File.ReadAllBytes(path);
            var stream = Path.GetFileNameWithoutExtension(path);
            int index = 0;

            foreach (var node in RwStream.Flatten(RwStream.Parse(data)).Where(n => n.Id == RwId.TextureNative))
            {
                var payload = data.AsSpan(node.DataOffset, node.Size).ToArray();
                index++;

                try
                {
                    var tex = Ps2Texture.Parse(payload, $"{stream}_{index}");
                    var name = string.IsNullOrWhiteSpace(tex.Name) ? $"{stream}_{index}" : tex.Name;
                    PngWriter.Write(Path.Combine(outDir, $"{stream}__{name}.png"), tex.ToRgba(), tex.Width, tex.Height);
                    written++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  !! {stream} #{index}: {ex.Message}");
                    failed++;
                }
            }
        }

        Console.WriteLine($"Extracted {written} textures to {outDir}" + (failed > 0 ? $", {failed} failed" : ""));
        return 0;
    }
}
