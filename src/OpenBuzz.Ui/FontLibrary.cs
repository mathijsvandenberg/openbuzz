using System.Drawing.Imaging;
using OpenBuzz.Graphics;

namespace OpenBuzz.Ui;

/// <summary>
/// The game's fonts, loaded from `extracted/RWStream/Font.rp2`.
///
/// Names are the ones the scripts use - `GenericData.lua` binds QuestionFontName
/// to "GeneralLarge", ClipboardTitleFontName to "ClipboardSmall", and so on - so
/// call sites can ask for the same font the original asks for.
/// </summary>
public sealed class FontLibrary : IDisposable
{
    private readonly Dictionary<string, BitmapFont> _fonts = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Diagnostics { get; } = [];
    public int Count => _fonts.Count;
    public IEnumerable<string> Names => _fonts.Keys;

    /// <summary>
    /// The fonts found next to the running executable, loaded once. Shared
    /// because they are immutable and every renderer wants the same six.
    /// </summary>
    public static FontLibrary? Shared => _shared ??= Discover(AppContext.BaseDirectory);
    private static FontLibrary? _shared;

    public BitmapFont? Get(string name) => _fonts.GetValueOrDefault(name);

    /// Falls back through the styles that exist in every build.
    public BitmapFont? GetOrDefault(string name) =>
        Get(name) ?? Get("GeneralLarge") ?? Get("Default") ?? _fonts.Values.FirstOrDefault();

    /// Walks up from <paramref name="startDirectory"/> looking for the stream.
    public static FontLibrary? Discover(string startDirectory)
    {
        for (var d = new DirectoryInfo(startDirectory); d is not null; d = d.Parent)
        {
            var path = Path.Combine(d.FullName, "extracted", "RWStream", "Font.rp2");
            if (File.Exists(path)) return Load(path);
        }
        return null;
    }

    public static FontLibrary Load(string path)
    {
        var library = new FontLibrary();
        var data = File.ReadAllBytes(path);

        // Decode each atlas once; several fonts share one.
        var atlases = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in RwStream.Flatten(RwStream.Parse(data)).Where(n => n.Id == RwId.TextureNative))
        {
            try
            {
                var tex = Ps2Texture.Parse(data.AsSpan(node.DataOffset, node.Size).ToArray(), "atlas");
                atlases[tex.Name] = ToBitmap(tex);
            }
            catch (Exception ex)
            {
                library.Diagnostics.Add($"atlas: {ex.Message}");
            }
        }

        foreach (var font in RwFont.ParseAll(data))
        {
            if (!atlases.TryGetValue(font.TextureName, out var atlas))
            {
                library.Diagnostics.Add($"{font.Name}: no atlas named {font.TextureName}");
                continue;
            }

            // Each font gets its own clone so disposal stays simple.
            library._fonts[font.Name] = new BitmapFont(font, (Bitmap)atlas.Clone());
            library.Diagnostics.Add($"{font.Name} <- {font.TextureName}, {font.Glyphs.Length} glyphs, line {font.LineHeight}");
        }

        foreach (var atlas in atlases.Values) atlas.Dispose();
        return library;
    }

    private static Bitmap ToBitmap(Ps2Texture tex)
    {
        var bmp = new Bitmap(tex.Width, tex.Height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, tex.Width, tex.Height);
        var locked = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        var rgba = tex.ToRgba();
        var bgra = new int[rgba.Length];
        for (int i = 0; i < rgba.Length; i++)
        {
            uint p = rgba[i];
            // ToRgba packs R,G,B,A low-to-high; GDI+ wants B,G,R,A.
            bgra[i] = (int)((p & 0xFF00FF00) | ((p & 0xFF) << 16) | ((p >> 16) & 0xFF));
        }

        System.Runtime.InteropServices.Marshal.Copy(bgra, 0, locked.Scan0, bgra.Length);
        bmp.UnlockBits(locked);
        return bmp;
    }

    public void Dispose()
    {
        foreach (var f in _fonts.Values) f.Dispose();
        _fonts.Clear();
    }
}
