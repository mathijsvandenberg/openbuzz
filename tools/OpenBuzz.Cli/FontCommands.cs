using OpenBuzz.Graphics;

namespace OpenBuzz.Cli;

/// Reads the bitmap fonts out of the `Font*.rp2` streams.
public static class FontCommands
{
    private static readonly string[] Streams = ["Font.rp2", "Font_EUR.rp2", "Font_RUS.rp2"];

    public static int List(string dir)
    {
        foreach (var file in Streams)
        {
            var path = Path.Combine(dir, file);
            if (!File.Exists(path)) continue;

            Console.WriteLine(file);
            foreach (var f in RwFont.Load(path))
                Console.WriteLine($"  {f.Name,-24} {f.TextureName,-14} lineHeight={f.LineHeight,5:0.#} " +
                                  $"glyphs={f.Glyphs.Length,4}  map={f.CharMap.Length,5} entries, bias={f.CharBias}");
        }
        return 0;
    }

    /// <summary>
    /// Renders a line of text with every font and writes it to a PNG, so the
    /// metrics can be checked against the real thing rather than assumed.
    /// </summary>
    public static int Sample(string dir, string outPath, string text, int scale)
    {
        var path = Path.Combine(dir, "Font.rp2");
        if (!File.Exists(path)) { Console.Error.WriteLine($"Missing {path}"); return 1; }

        var data = File.ReadAllBytes(path);
        var fonts = RwFont.ParseAll(data);
        var atlases = Atlases(data);

        int pad = 4 * scale;
        int width = 0, height = pad;
        foreach (var f in fonts)
        {
            width = Math.Max(width, (int)(f.Measure(text) * scale) + pad * 2);
            height += (int)(f.LineStep * scale) + pad;
        }

        var canvas = new uint[width * height];
        Array.Fill(canvas, 0xFF201F23u);

        int y = pad;
        foreach (var f in fonts)
        {
            if (atlases.TryGetValue(f.TextureName, out var atlas))
                DrawText(canvas, width, height, f, atlas, text, pad, y, scale);
            y += (int)(f.LineStep * scale) + pad;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        PngWriter.Write(outPath, canvas, width, height);
        Console.WriteLine($"Wrote {outPath} ({width}x{height}), {fonts.Count} fonts");
        return 0;
    }

    private static Dictionary<string, Ps2Texture> Atlases(byte[] data)
    {
        var result = new Dictionary<string, Ps2Texture>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in RwStream.Flatten(RwStream.Parse(data)).Where(n => n.Id == RwId.TextureNative))
        {
            var tex = Ps2Texture.Parse(data.AsSpan(node.DataOffset, node.Size).ToArray(), "atlas");
            result[tex.Name] = tex;
        }
        return result;
    }

    private static void DrawText(uint[] canvas, int cw, int ch, RwFont font, Ps2Texture atlas,
                                 string text, int x0, int y0, int scale)
    {
        var pixels = atlas.ToRgba();
        float penX = x0;

        foreach (char c in text)
        {
            if (!font.TryGetGlyph(c, out var g)) { penX += font.LineHeight * 0.25f * scale; continue; }

            // UVs address texel centres; the cell is one pixel wider and
            // taller than the difference between them.
            int sx = (int)MathF.Round(g.U0 * atlas.Width - 0.5f), sy = (int)MathF.Round(g.V0 * atlas.Height - 0.5f);
            int sw = (int)MathF.Round((g.U1 - g.U0) * atlas.Width) + 1, sh = (int)MathF.Round((g.V1 - g.V0) * atlas.Height) + 1;

            for (int dy = 0; dy < sh * scale; dy++)
                for (int dx = 0; dx < sw * scale; dx++)
                {
                    int px = (int)penX + dx, py = y0 + dy;
                    if (px < 0 || px >= cw || py < 0 || py >= ch) continue;

                    int tx = sx + dx / scale, ty = sy + dy / scale;
                    if (tx < 0 || tx >= atlas.Width || ty < 0 || ty >= atlas.Height) continue;

                    canvas[py * cw + px] = Blend(canvas[py * cw + px], pixels[ty * atlas.Width + tx]);
                }

            penX += font.AdvanceOf(g) * scale;
        }
    }

    /// Source-over, the atlas being white glyphs carried in the alpha channel.
    private static uint Blend(uint dst, uint src)
    {
        uint a = src >> 24;
        if (a == 0) return dst;
        if (a == 255) return src;

        uint Mix(int shift) =>
            (uint)(((src >> shift & 0xFF) * a + (dst >> shift & 0xFF) * (255 - a)) / 255) << shift;

        return 0xFF000000u | Mix(0) | Mix(8) | Mix(16);
    }
}
