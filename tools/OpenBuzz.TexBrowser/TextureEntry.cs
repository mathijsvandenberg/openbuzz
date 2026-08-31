namespace OpenBuzz.TexBrowser;

/// One decoded texture on disk, plus where it came from.
public sealed class TextureEntry
{
    public required string Path { get; init; }

    /// The stream the texture was embedded in, or "Textures" for standalone ones.
    public required string Source { get; init; }

    /// The name the game gives it.
    public required string Name { get; init; }

    private Image? image;

    public Image Image => image ??= LoadDetached();

    public string Label => $"{Source}  -  {Name}";

    public override string ToString() => Label;

    /// <summary>
    /// Loads through a copy so the file is not kept locked - the extractor may
    /// be re-run while the browser is open.
    /// </summary>
    private Image LoadDetached()
    {
        using var stream = File.OpenRead(Path);
        using var loaded = Image.FromStream(stream);
        var copy = new Bitmap(loaded.Width, loaded.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(copy);
        g.DrawImageUnscaled(loaded, 0, 0);
        return copy;
    }

    /// <summary>
    /// Collects every extracted PNG. Model textures are named
    /// `Stream__TextureName.png`; standalone ones are just the texture name.
    /// </summary>
    public static List<TextureEntry> Discover(string root)
    {
        var entries = new List<TextureEntry>();

        Add(System.IO.Path.Combine(root, "rwpng"), split: true);
        Add(System.IO.Path.Combine(root, "png"), split: false);

        return entries.OrderBy(e => e.Source, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                      .ToList();

        void Add(string dir, bool split)
        {
            if (!Directory.Exists(dir)) return;

            foreach (var file in Directory.GetFiles(dir, "*.png"))
            {
                var stem = System.IO.Path.GetFileNameWithoutExtension(file);
                var cut = split ? stem.IndexOf("__", StringComparison.Ordinal) : -1;

                entries.Add(new TextureEntry
                {
                    Path = file,
                    Source = cut > 0 ? stem[..cut] : "Textures",
                    Name = cut > 0 ? stem[(cut + 2)..] : stem,
                });
            }
        }
    }

    /// <summary>
    /// Walks up from <paramref name="startDir"/> looking for the extraction
    /// output, so the exe runs from `dist/` or from a build directory alike.
    /// </summary>
    public static string? FindExtractDirectory(string startDir)
    {
        for (var d = new DirectoryInfo(startDir); d is not null; d = d.Parent)
        {
            var candidate = System.IO.Path.Combine(d.FullName, "extracted");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
}
