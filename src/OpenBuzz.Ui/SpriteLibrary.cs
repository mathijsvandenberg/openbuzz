using System.Drawing.Imaging;
using OpenBuzz.Graphics;

namespace OpenBuzz.Ui;

/// A named sprite: a sub-rectangle of a decoded texture atlas.
public sealed record Sprite(Bitmap Texture, Rectangle Source, string Atlas);

/// <summary>
/// Resolves the sprite names in A2D icon bindings to actual pixels, by pairing
/// each `.uvs` atlas index with its decoded `.tex`.
/// </summary>
public sealed class SpriteLibrary : IDisposable
{
    private readonly Dictionary<string, Sprite> _sprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Bitmap> _textures = [];

    public int SpriteCount => _sprites.Count;

    /// Why loading produced what it did - swallowing texture failures silently
    /// made an empty library indistinguishable from a missing directory.
    public List<string> Diagnostics { get; } = [];
    public int AtlasCount => _textures.Count;

    public Sprite? Find(string name) => _sprites.GetValueOrDefault(name);

    public static SpriteLibrary Discover(string startDirectory)
    {
        var library = new SpriteLibrary();

        for (var d = new DirectoryInfo(startDirectory); d is not null; d = d.Parent)
        {
            var dir = Path.Combine(d.FullName, "extracted", "Textures");

            library.Diagnostics.Add($"probe: {dir} exists={Directory.Exists(dir)}");
            if (!Directory.Exists(dir)) continue;
            library.LoadFrom(dir);
            break;
        }

        return library;
    }

    private void LoadFrom(string dir)
    {
        foreach (var uvsPath in Directory.GetFiles(dir, "*.uvs"))
        {
            var texPath = Path.ChangeExtension(uvsPath, ".tex");
            if (!File.Exists(texPath)) continue;

            Bitmap bitmap;
            Ps2Texture tex;
            try
            {
                tex = Ps2Texture.Load(texPath);
                bitmap = ToBitmap(tex);
            }
            catch (Exception ex)
            {
                Diagnostics.Add($"FAIL {Path.GetFileName(texPath)}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            _textures.Add(bitmap);
            var atlas = Path.GetFileNameWithoutExtension(uvsPath);

            foreach (var rect in UvsFile.Load(uvsPath).Rects)
            {
                var (x, y, w, h) = rect.ToPixels(tex.Width, tex.Height);
                if (w <= 0 || h <= 0) continue;

                // Clamp: rounding a normalised UV can land a pixel past the edge.
                x = Math.Clamp(x, 0, tex.Width - 1);
                y = Math.Clamp(y, 0, tex.Height - 1);
                w = Math.Min(w, tex.Width - x);
                h = Math.Min(h, tex.Height - y);

                _sprites[rect.Name] = new Sprite(bitmap, new Rectangle(x, y, w, h), atlas);
            }
        }
    }

    private static Bitmap ToBitmap(Ps2Texture tex)
    {
        var pixels = tex.ToRgba();
        var bitmap = new Bitmap(tex.Width, tex.Height, PixelFormat.Format32bppArgb);

        var locked = bitmap.LockBits(new Rectangle(0, 0, tex.Width, tex.Height),
                                     ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            // ToRgba packs R,G,B,A low-to-high; GDI+ 32bppArgb wants B,G,R,A.
            var bgra = new int[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                uint p = pixels[i];
                bgra[i] = (int)((p & 0xFF00FF00u) | ((p & 0xFFu) << 16) | ((p >> 16) & 0xFFu));
            }
            System.Runtime.InteropServices.Marshal.Copy(bgra, 0, locked.Scan0, bgra.Length);
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }

        return bitmap;
    }

    public void Dispose()
    {
        foreach (var b in _textures) b.Dispose();
        _textures.Clear();
        _sprites.Clear();
    }
}
