using OpenBuzz.Graphics;

namespace OpenBuzz.Cli;

public static class TextureCommands
{
    public static int Info(string dir)
    {
        var files = Directory.GetFiles(dir, "*.tex", SearchOption.AllDirectories)
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) { Console.Error.WriteLine($"No .tex files under {dir}."); return 1; }

        int ok = 0, failed = 0;
        long slack = 0;

        Console.WriteLine($"{"file",-36} {"size",8} {"WxH",12} {"bpp",4} {"psm",5} {"TW/TH",8} {"TBW",5} {"buf",6}");
        foreach (var path in files)
        {
            var bytes = File.ReadAllBytes(path);
            try
            {
                var t = Ps2Texture.Parse(bytes, Path.GetFileNameWithoutExtension(path));
                int payload = (t.Depth == 8 ? t.Width * t.Height : t.Width * t.Height / 2)
                            + (t.Depth == 8 ? 1024 : 64) + 2 * Ps2Texture.BlockHeader;
                int header = bytes.Length - payload;
                slack += header;
                ok++;
                Console.WriteLine($"{Path.GetFileName(path),-36} {bytes.Length,8} " +
                                  $"{$"{t.Width}x{t.Height}",12} {t.Depth,4} 0x{t.Psm:X2} {$"{1 << t.Tw}x{1 << t.Th}",8} {t.Tbw,5} {t.BufferWidth,6}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"{Path.GetFileName(path),-36} {bytes.Length,8}   !! {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"parsed {ok}/{files.Length}, failed {failed}");
        if (ok > 0) Console.WriteLine($"mean chunk overhead {slack / ok} bytes (payload located via declared pixelSize/paletteSize)");
        return failed == 0 ? 0 : 2;
    }

    public static int Decode(string dir, string outDir)
    {
        var files = Directory.GetFiles(dir, "*.tex", SearchOption.AllDirectories)
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) { Console.Error.WriteLine($"No .tex files under {dir}."); return 1; }

        Directory.CreateDirectory(outDir);
        int ok = 0;

        foreach (var path in files)
        {
            try
            {
                var t = Ps2Texture.Load(path);
                PngWriter.Write(Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".png"),
                                t.ToRgba(), t.Width, t.Height);
                ok++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  !! {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        Console.WriteLine($"Decoded {ok}/{files.Length} textures to {outDir}");
        return 0;
    }

    /// Lists atlas sub-rectangles, resolved to pixels against their texture.
    public static int Atlas(string dir)
    {
        var files = Directory.GetFiles(dir, "*.uvs", SearchOption.AllDirectories)
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) { Console.Error.WriteLine($"No .uvs files under {dir}."); return 1; }

        int total = 0;
        foreach (var path in files)
        {
            var uvs = UvsFile.Load(path);
            var texPath = Path.ChangeExtension(path, ".tex");
            int w = 0, h = 0;
            if (File.Exists(texPath))
            {
                try { var t = Ps2Texture.Load(texPath); w = t.Width; h = t.Height; } catch { }
            }

            Console.WriteLine($"{Path.GetFileName(path)}  tag='{uvs.Tag}' v{uvs.Version}  {uvs.Rects.Count}/{uvs.DeclaredCount} rects" +
                              (w > 0 ? $"  ({w}x{h})" : "  (texture unreadable)"));
            foreach (var r in uvs.Rects.Take(4))
            {
                var (x, y, rw, rh) = r.ToPixels(w, h);
                Console.WriteLine($"    {r.Name,-28} u {r.U0:F3}..{r.U1:F3}  v {r.V0:F3}..{r.V1:F3}" +
                                  (w > 0 ? $"   = {x},{y} {rw}x{rh}px" : ""));
            }
            if (uvs.Rects.Count > 4) Console.WriteLine($"    ... {uvs.Rects.Count - 4} more");
            total += uvs.Rects.Count;
        }

        Console.WriteLine();
        Console.WriteLine($"{files.Length} atlases, {total} sub-rectangles");
        return 0;
    }
}


