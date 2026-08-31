using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using OpenBuzz.Animation;

namespace OpenBuzz.Ui;

/// <summary>
/// Draws one frame of an A2D animation with the game's own artwork: sprites
/// resolved through the `.uvs` atlases into decoded textures, and text through
/// the recovered key map.
///
/// Shared so the debug player and the game screen render identically rather
/// than drifting apart. Bindings are looked up in a merged table because they
/// are declared globally, in the `Animation2dSetup` chunk, not per scene.
/// </summary>
public sealed class A2dRenderer
{
    private readonly Dictionary<string, TextBinding> _text = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _icons = new(StringComparer.Ordinal);

    public SpriteLibrary Sprites { get; }
    public TextKeyMap Strings { get; }

    public int TextBindingCount => _text.Count;
    public int IconBindingCount => _icons.Count;

    public A2dRenderer(IEnumerable<A2dScene> scenes, SpriteLibrary sprites, TextKeyMap strings)
    {
        Sprites = sprites;
        Strings = strings;

        foreach (var scene in scenes)
        {
            foreach (var (actor, binding) in scene.TextBindings) _text[actor] = binding;
            foreach (var (actor, icon) in scene.IconBindings) _icons[actor] = icon;
        }
    }

    /// <summary>
    /// Renders one frame into a graphics context already transformed so that
    /// one unit equals one pixel of the 640x480 design space.
    /// </summary>
    public void Draw(System.Drawing.Graphics g, A2dAnimation animation, int frame, bool flipY = true)
    {
        foreach (var obj in animation.Objects)
        {
            if (!obj.IsLive(frame)) continue;
            if (obj.TransformAt(frame) is not { } t) continue;

            float alpha = obj.ColourAt(frame)?.A ?? 1f;
            if (alpha <= 0.004f) continue;

            var box = obj.Box ?? new Bounds(-8, 8, 8, -8);
            var origin = ToCanvas(t.X, t.Y, flipY);
            int a = (int)Math.Clamp(alpha * 255f, 0, 255);

            if (_icons.TryGetValue(obj.Name, out var icon) && Sprites.Find(icon) is { } sprite)
            {
                DrawSprite(g, sprite, origin, box, t, alpha, flipY);
                continue;
            }

            if (_text.TryGetValue(obj.Name, out var binding))
                DrawText(g, binding, origin, box, t, a, flipY);
        }
    }

    private static PointF ToCanvas(float x, float y, bool flipY) =>
        new(x, flipY ? A2dScene.CanvasHeight - y : y);

    private static void DrawSprite(System.Drawing.Graphics g, Sprite sprite, PointF origin,
                                   Bounds box, TfmKey t, float alpha, bool flipY)
    {
        var dest = new RectangleF(
            origin.X + box.Left * t.ScaleX,
            origin.Y - box.Top * t.ScaleY,
            box.Width * t.ScaleX,
            box.Height * t.ScaleY);

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });

        g.DrawImage(sprite.Texture, Rectangle.Round(dest),
                    sprite.Source.X, sprite.Source.Y, sprite.Source.Width, sprite.Source.Height,
                    GraphicsUnit.Pixel, attributes);
    }

    private void DrawText(System.Drawing.Graphics g, TextBinding binding, PointF origin,
                          Bounds box, TfmKey t, int alpha, bool flipY)
    {
        // An unresolved key renders as the key itself, in grey, so a placeholder
        // is visibly a placeholder rather than quietly reading as content.
        var resolved = Strings.Resolve(binding.Key);
        var colour = resolved is null
            ? Color.FromArgb(alpha, 150, 156, 170)
            : Color.FromArgb(Math.Min(255, alpha + 30), 250, 250, 235);

        var text = resolved ?? binding.Key;

        var layout = new RectangleF(
            origin.X + box.Left * t.ScaleX,
            origin.Y - box.Top * t.ScaleY,
            box.Width * t.ScaleX,
            box.Height * t.ScaleY);

        var align = binding.HorizontalJustify switch
        {
            "Centre" => TextAlign.Centre,
            "Right" => TextAlign.Right,
            _ => TextAlign.Left,
        };

        // The binding's Style is the game's own font name - the A2D data only
        // ever uses RoundInstructionsSmall and RoundInstructionsLarge - so the
        // text can be drawn in the face the original uses.
        if (FontLibrary.Shared?.Get(binding.Style) is { } bitmapFont)
        {
            float scale = binding.SizeMultiplier > 0 ? Math.Min(binding.SizeMultiplier, 2.2f) : 1f;

            // Shrink rather than overflow when a translation runs long.
            var longest = bitmapFont.Wrap(text, layout.Width, scale).DefaultIfEmpty("").Max(l => bitmapFont.Measure(l, scale));
            if (longest > layout.Width && longest > 0) scale *= layout.Width / longest;

            bitmapFont.DrawWrapped(g, text, layout, scale, colour, align);
            return;
        }

        float basePoints = binding.Style.Contains("Large", StringComparison.OrdinalIgnoreCase) ? 9f : 6.5f;
        float points = Math.Clamp(basePoints * Math.Min(binding.SizeMultiplier, 2.2f), 5f, 26f);

        using var font = new Font("Segoe UI", points, FontStyle.Bold);
        using var brush = new SolidBrush(colour);
        using var format = new StringFormat
        {
            Alignment = binding.HorizontalJustify switch
            {
                "Centre" => StringAlignment.Center,
                "Right" => StringAlignment.Far,
                _ => StringAlignment.Near,
            },
            LineAlignment = binding.VerticalJustify == "Top" ? StringAlignment.Near : StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
        };

        g.DrawString(text, font, brush, layout, format);
    }

    /// <summary>
    /// Sets up a graphics context so the 640x480 design space maps into
    /// <paramref name="view"/>, preserving 4:3 and centring the result.
    /// Returns the saved state so the caller can restore it.
    /// </summary>
    public static GraphicsState BeginCanvas(System.Drawing.Graphics g, Rectangle view)
    {
        float scale = Math.Min(view.Width / A2dScene.CanvasWidth, view.Height / A2dScene.CanvasHeight);
        float offsetX = view.X + (view.Width - A2dScene.CanvasWidth * scale) / 2f;
        float offsetY = view.Y + (view.Height - A2dScene.CanvasHeight * scale) / 2f;

        var state = g.Save();
        g.TranslateTransform(offsetX, offsetY);
        g.ScaleTransform(scale, scale);
        g.SetClip(new RectangleF(0, 0, A2dScene.CanvasWidth, A2dScene.CanvasHeight));
        return state;
    }
}
