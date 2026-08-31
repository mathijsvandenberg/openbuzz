using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using OpenBuzz.Graphics;

namespace OpenBuzz.Ui;

/// Where a drawn string sits relative to the x it is given.
public enum TextAlign { Left, Centre, Right }

/// <summary>
/// One of the game's own bitmap fonts, ready to draw: the metrics from
/// `Font.rp2` paired with its decoded atlas.
///
/// The atlases are white glyphs carried in the alpha channel, so colour comes
/// from a tint applied at draw time - which is how the game gets the same face
/// in white, gold and the player colours.
/// </summary>
public sealed class BitmapFont : IDisposable
{
    private readonly RwFont _font;

    /// Each glyph cut out of the atlas into its own bitmap. Drawing a
    /// sub-rectangle of a shared atlas lets GDI+ sample the neighbouring
    /// glyphs whenever the destination is not pixel-aligned, which shows up as
    /// fragments of other letters clinging to the baseline.
    private readonly Bitmap?[] _glyphs;

    internal BitmapFont(RwFont font, Bitmap atlas)
    {
        _font = font;
        _glyphs = new Bitmap?[font.Glyphs.Length];

        for (int i = 0; i < font.Glyphs.Length; i++)
        {
            var glyph = font.Glyphs[i];
            int sx = (int)MathF.Round(glyph.U0 * atlas.Width);
            int sy = (int)MathF.Round(glyph.V0 * atlas.Height);
            int sw = (int)MathF.Round(glyph.U1 * atlas.Width) - sx;
            int sh = (int)MathF.Round(glyph.V1 * atlas.Height) - sy;

            if (sw <= 0 || sh <= 0 || sx < 0 || sy < 0 ||
                sx + sw > atlas.Width || sy + sh > atlas.Height) continue;

            _glyphs[i] = atlas.Clone(new Rectangle(sx, sy, sw, sh), atlas.PixelFormat);
        }
    }

    public string Name => _font.Name;

    /// Line height in pixels at scale 1. Every glyph is exactly this tall.
    public float LineHeight => _font.LineHeight;

    public float Measure(string text, float scale = 1f) => _font.Measure(text) * scale;

    /// <summary>
    /// Draws <paramref name="text"/> with its top-left at (x, y), scaled and
    /// tinted. Returns the pen x after the last glyph.
    /// </summary>
    public float Draw(System.Drawing.Graphics g, string text, float x, float y,
                      float scale, Color tint, TextAlign align = TextAlign.Left)
    {
        if (string.IsNullOrEmpty(text)) return x;

        float penX = align switch
        {
            TextAlign.Centre => x - Measure(text, scale) / 2f,
            TextAlign.Right => x - Measure(text, scale),
            _ => x,
        };

        using var attributes = Tint(tint);
        var saved = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;

        foreach (char c in text)
        {
            if (!_font.TryGetIndex(c, out int i))
            {
                penX += LineHeight * 0.25f * scale;
                continue;
            }

            if (_glyphs[i] is { } image)
            {
                var dest = new RectangleF(penX, y, image.Width * scale, image.Height * scale);
                g.DrawImage(image, Round(dest), 0, 0, image.Width, image.Height,
                            GraphicsUnit.Pixel, attributes);
            }

            penX += _font.Glyphs[i].Advance * LineHeight * scale;
        }

        g.InterpolationMode = saved;
        return penX;
    }

    /// <summary>
    /// Word-wraps into <paramref name="box"/> and draws, vertically centred.
    /// Returns the height used. Falls back to breaking a single over-long word
    /// rather than letting it run past the edge.
    /// </summary>
    public float DrawWrapped(System.Drawing.Graphics g, string text, RectangleF box,
                             float scale, Color tint, TextAlign align = TextAlign.Centre)
    {
        var lines = Wrap(text, box.Width, scale);
        float step = LineHeight * scale * 1.15f;
        float y = box.Y + Math.Max(0, (box.Height - lines.Count * step) / 2f);

        float x = align switch
        {
            TextAlign.Centre => box.X + box.Width / 2f,
            TextAlign.Right => box.Right,
            _ => box.X,
        };

        foreach (var line in lines)
        {
            Draw(g, line, x, y, scale, tint, align);
            y += step;
        }

        return lines.Count * step;
    }

    public List<string> Wrap(string text, float width, float scale = 1f)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return lines;

        var line = new System.Text.StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (Measure(candidate, scale) <= width || line.Length == 0)
            {
                line.Clear();
                line.Append(candidate);
                continue;
            }

            lines.Add(line.ToString());
            line.Clear();
            line.Append(word);
        }

        if (line.Length > 0) lines.Add(line.ToString());
        return lines;
    }

    /// The largest scale at or below <paramref name="max"/> that fits the width.
    public float FitScale(string text, float width, float max = 1f)
    {
        float natural = Measure(text);
        return natural <= 0 ? max : Math.Min(max, width / natural);
    }

    private static Rectangle Round(RectangleF r) =>
        Rectangle.FromLTRB((int)MathF.Round(r.Left), (int)MathF.Round(r.Top),
                           (int)MathF.Round(r.Right), (int)MathF.Round(r.Bottom));

    /// Multiplies the glyph's white through the tint, leaving alpha alone.
    private static ImageAttributes Tint(Color c)
    {
        var matrix = new ColorMatrix(
        [
            [c.R / 255f, 0, 0, 0, 0],
            [0, c.G / 255f, 0, 0, 0],
            [0, 0, c.B / 255f, 0, 0],
            [0, 0, 0, c.A / 255f, 0],
            [0, 0, 0, 0, 1],
        ]);

        var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix);
        attributes.SetWrapMode(WrapMode.TileFlipXY);   // no edge bleed when scaling
        return attributes;
    }

    public void Dispose()
    {
        foreach (var g in _glyphs) g?.Dispose();
    }
}
