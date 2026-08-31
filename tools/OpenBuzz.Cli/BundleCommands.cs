using System.Text.Json;
using OpenBuzz.Animation;
using OpenBuzz.Graphics;

namespace OpenBuzz.Cli;

/// <summary>
/// Writes the 2D layer in a form an engine can read without any of this code:
/// PNG atlases, and JSON tables of sprite rectangles, font glyphs and resolved
/// strings. The A2D timelines are already JSON, so they are copied as they are.
///
/// The point is that the engine side stays a reader of plain data rather than a
/// port of the decoders.
/// </summary>
public static class BundleCommands
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static int Run(string extracted, string outDir)
    {
        Directory.CreateDirectory(outDir);

        int atlases = Atlases(Path.Combine(extracted, "Textures"), outDir);
        int fonts = Fonts(Path.Combine(extracted, "RWStream", "Font.rp2"), outDir);
        int strings = Strings(extracted, outDir);
        int scenes = Scenes(Path.Combine(extracted, "a2d"), outDir);

        Console.WriteLine($"Bundled {atlases} atlases, {fonts} fonts, {strings} strings, {scenes} scenes to {outDir}");
        return 0;
    }

    /// Decoded atlas textures plus a name-to-rectangle index.
    private static int Atlases(string texDir, string outDir)
    {
        if (!Directory.Exists(texDir)) { Console.Error.WriteLine($"Missing {texDir}"); return 0; }

        var imageDir = Path.Combine(outDir, "atlas");
        Directory.CreateDirectory(imageDir);

        var sprites = new Dictionary<string, object>(StringComparer.Ordinal);
        int count = 0;

        foreach (var uvsPath in Directory.GetFiles(texDir, "*.uvs").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var texPath = Path.ChangeExtension(uvsPath, ".tex");
            if (!File.Exists(texPath)) continue;

            Ps2Texture tex;
            try { tex = Ps2Texture.Load(texPath); }
            catch (Exception ex) { Console.Error.WriteLine($"  !! {Path.GetFileName(texPath)}: {ex.Message}"); continue; }

            var atlas = Path.GetFileNameWithoutExtension(uvsPath);
            PngWriter.Write(Path.Combine(imageDir, atlas + ".png"), tex.ToRgba(), tex.Width, tex.Height);
            count++;

            foreach (var rect in UvsFile.Load(uvsPath).Rects)
            {
                var (x, y, w, h) = rect.ToPixels(tex.Width, tex.Height);
                if (w <= 0 || h <= 0) continue;

                // Rounding a normalised UV can land a pixel past the edge.
                x = Math.Clamp(x, 0, tex.Width - 1);
                y = Math.Clamp(y, 0, tex.Height - 1);
                sprites[rect.Name] = new
                {
                    atlas,
                    x, y,
                    w = Math.Min(w, tex.Width - x),
                    h = Math.Min(h, tex.Height - y),
                };
            }
        }

        File.WriteAllText(Path.Combine(outDir, "sprites.json"), JsonSerializer.Serialize(sprites, Json));
        return count;
    }

    /// <summary>
    /// Font atlases plus per-character pixel rectangles, so the engine never
    /// has to know about the 16-bit float or the biased character map.
    /// </summary>
    private static int Fonts(string fontPath, string outDir)
    {
        if (!File.Exists(fontPath)) { Console.Error.WriteLine($"Missing {fontPath}"); return 0; }

        var data = File.ReadAllBytes(fontPath);
        var imageDir = Path.Combine(outDir, "font");
        Directory.CreateDirectory(imageDir);

        var atlases = new Dictionary<string, Ps2Texture>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in RwStream.Flatten(RwStream.Parse(data)).Where(n => n.Id == RwId.TextureNative))
        {
            var tex = Ps2Texture.Parse(data.AsSpan(node.DataOffset, node.Size).ToArray(), "atlas");
            atlases[tex.Name] = tex;
        }

        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var font in RwFont.ParseAll(data))
        {
            if (!atlases.TryGetValue(font.TextureName, out var tex)) continue;

            var image = font.TextureName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? font.TextureName[..^4] : font.TextureName;

            if (written.Add(image))
                PngWriter.Write(Path.Combine(imageDir, image + ".png"), tex.ToRgba(), tex.Width, tex.Height);

            var glyphs = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int c = 32; c < 256; c++)
            {
                if (!font.TryGetGlyph((char)c, out var g)) continue;

                // The UVs address texel centres, so the cell spans one pixel
                // more than the difference between them.
                glyphs[((char)c).ToString()] = new
                {
                    x = (int)MathF.Round(g.U0 * tex.Width - 0.5f),
                    y = (int)MathF.Round(g.V0 * tex.Height - 0.5f),
                    w = (int)MathF.Round((g.U1 - g.U0) * tex.Width) + 1,
                    h = (int)MathF.Round((g.V1 - g.V0) * tex.Height) + 1,
                    advance = font.AdvanceOf(g),
                };
            }

            result[font.Name] = new { texture = image, lineHeight = font.LineHeight, lineStep = font.LineStep, glyphs };
        }

        File.WriteAllText(Path.Combine(outDir, "fonts.json"), JsonSerializer.Serialize(result, Json));
        return result.Count;
    }

    /// Every text key that resolves, so the engine can look one up by name.
    private static int Strings(string extracted, string outDir)
    {
        var map = TextKeyMap.Discover(extracted);
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);

        var keysPath = Path.Combine(extracted, "..", "docs", "text-key-map.txt");
        if (File.Exists(keysPath))
        {
            foreach (var line in File.ReadAllLines(keysPath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('[') || trimmed.StartsWith('#')) continue;

                var key = trimmed.Split('=')[0].Trim();
                if (key.Length == 0) continue;
                if (map.Resolve(key) is { } text) resolved[key] = text;
            }
        }

        File.WriteAllText(Path.Combine(outDir, "text.json"), JsonSerializer.Serialize(resolved, Json));
        return resolved.Count;
    }

    /// The A2D timelines, copied as they are.
    private static int Scenes(string a2dDir, string outDir)
    {
        if (!Directory.Exists(a2dDir)) { Console.Error.WriteLine($"Missing {a2dDir}"); return 0; }

        var sceneDir = Path.Combine(outDir, "scene");
        Directory.CreateDirectory(sceneDir);

        int count = 0;
        foreach (var path in Directory.GetFiles(a2dDir, "*.json"))
        {
            File.Copy(path, Path.Combine(sceneDir, Path.GetFileName(path)), overwrite: true);
            count++;
        }
        return count;
    }
}
