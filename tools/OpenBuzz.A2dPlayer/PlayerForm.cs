using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using OpenBuzz.Animation;

namespace OpenBuzz.A2dPlayer;

/// <summary>
/// Plays the extracted A2D timelines with the game''s own artwork: sprites
/// resolved through the .uvs atlases into decoded textures, and text through
/// the recovered key map.
///
/// Coordinates are used exactly as exported and the 640x480 design space is
/// scaled to the window by a transform, so this is a full-resolution render of
/// the original layout rather than an upscaled bitmap.
///
/// D hides the debug chrome for comparison against an emulator capture.
/// </summary>
public sealed class PlayerForm : Form
{
    private readonly List<A2dScene> _scenes;
    private readonly string _sourceDir;

    /// <summary>
    /// Text bindings are declared globally, not per scene: all 105 of them live
    /// in the shared `Animation2dSetup` chunk and apply to actors wherever they
    /// appear. Looking them up on the scene being played finds nothing.
    /// </summary>
    private readonly Dictionary<string, TextBinding> _textBindings = new(StringComparer.Ordinal);

    /// Resolves keys to real strings where the mapping is known; unmapped keys
    /// fall back to showing the key itself rather than inventing text.
    private readonly TextKeyMap _text = TextKeyMap.Discover(AppContext.BaseDirectory);

    /// Icon bindings are global too, same as text bindings.
    private readonly Dictionary<string, string> _iconBindings = new(StringComparer.Ordinal);
    private readonly SpriteLibrary _sprites = SpriteLibrary.Discover(AppContext.BaseDirectory);

    /// Textures decode scrambled until the swizzle is solved, so drawing them
    /// is opt-out rather than forced.
    private bool _showSprites = true;

    /// Hides the debug chrome - grid, bounding boxes, origin dots, labels - so a
    /// frame can be compared against an emulator capture directly.
    private bool _showDebug = true;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 8 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private int _sceneIndex;
    private int _animIndex;
    private int _frame;
    private bool _playing = true;
    private bool _showNames = true;

    /// The vertical axis direction is not stated anywhere in the data, so it is
    /// a toggle rather than an assumption.
    private bool _flipY = true;

    /// PAL, but 25 vs 50 is undetermined - also a toggle.
    private double _fps = 25;

    private A2dScene Scene => _scenes[_sceneIndex];

    private A2dAnimation Animation =>
        Scene.Animations[Math.Min(_animIndex, Scene.Animations.Count - 1)];

    public PlayerForm(List<A2dScene> scenes, string sourceDir)
    {
        // Bindings come from every loaded scene, including declaration-only ones;

        // navigation only visits scenes that actually have timelines.
        _sourceDir = sourceDir;
        _scenes = [.. scenes.Where(s => s.Animations.Count > 0)];

        foreach (var s in scenes)
            foreach (var (actor, binding) in s.TextBindings)
                _textBindings[actor] = binding;

        foreach (var s in scenes)
            foreach (var (actor, icon) in s.IconBindings)
                _iconBindings[actor] = icon;

        Text = "OpenBuzz - A2D Player";
        BackColor = Color.FromArgb(16, 18, 24);
        DoubleBuffered = true;
        KeyPreview = true;
        ClientSize = new Size(1440, 1080 + 92);
        StartPosition = FormStartPosition.CenterScreen;

        _timer.Tick += (_, _) =>
        {
            if (_playing)
            {
                double now = _clock.Elapsed.TotalSeconds;
                int total = Math.Max(Animation.FrameCount, 1);
                _frame = (int)(now * _fps) % total;
            }
            Invalidate();
        };
        _timer.Start();
    }

    /// Dumps what the sprite library found, for diagnosing an empty render.


    public void WriteDiagnostics(string path)


    {


        var lines = new List<string>


        {


            $"baseDir      : {AppContext.BaseDirectory}",


            $"scenes       : {_scenes.Count}",


            $"textBindings : {_textBindings.Count}",


            $"iconBindings : {_iconBindings.Count}",


            $"textKeys     : {_text.MappedKeys}",


            $"sprites      : {_sprites.SpriteCount} in {_sprites.AtlasCount} atlases",


        };


        lines.AddRange(_sprites.Diagnostics);


        try { File.WriteAllLines(path, lines); } catch { }


    }



    // ---- input -----------------------------------------------------------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Right: StepAnimation(1); break;
            case Keys.Left: StepAnimation(-1); break;
            case Keys.Down: StepScene(1); break;
            case Keys.Up: StepScene(-1); break;
            case Keys.Space: _playing = !_playing; break;
            case Keys.R: Restart(); break;
            case Keys.Y: _flipY = !_flipY; break;
            case Keys.F: _fps = _fps == 25 ? 50 : 25; Restart(); break;
            case Keys.N: _showNames = !_showNames; break;
            case Keys.T: _showSprites = !_showSprites; break;
            case Keys.D: _showDebug = !_showDebug; break;
            case Keys.OemPeriod: _playing = false; _frame = Math.Min(_frame + 1, Animation.FrameCount); break;
            case Keys.Oemcomma: _playing = false; _frame = Math.Max(0, _frame - 1); break;
            case Keys.Escape: Close(); break;
            default: base.OnKeyDown(e); return;
        }

        e.Handled = true;
        Invalidate();
    }

    private void Restart()
    {
        _clock.Restart();
        _frame = 0;
    }

    private void StepScene(int delta)
    {
        _sceneIndex = (_sceneIndex + delta + _scenes.Count) % _scenes.Count;
        _animIndex = 0;
        Restart();
    }

    private void StepAnimation(int delta)
    {
        int n = Scene.Animations.Count;
        _animIndex = (_animIndex + delta + n) % n;
        Restart();
    }

    // ---- painting --------------------------------------------------------

    /// Maps an A2D point into canvas space, honouring the y-direction toggle.
    private PointF ToCanvas(float x, float y) =>
        new(x, _flipY ? A2dScene.CanvasHeight - y : y);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        int hud = 92;
        var view = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height - hud);

        // Fit the 640x480 design space into the window, 4:3, letterboxed. The
        // scale is applied as a transform, so rectangles stay vector-crisp at
        // any resolution rather than being upscaled bitmaps.
        float scale = Math.Min(view.Width / A2dScene.CanvasWidth, view.Height / A2dScene.CanvasHeight);
        float offsetX = (view.Width - A2dScene.CanvasWidth * scale) / 2f;
        float offsetY = (view.Height - A2dScene.CanvasHeight * scale) / 2f;

        var state = g.Save();
        g.TranslateTransform(offsetX, offsetY);
        g.ScaleTransform(scale, scale);
        g.SetClip(new RectangleF(0, 0, A2dScene.CanvasWidth, A2dScene.CanvasHeight));

        if (_showDebug) DrawCanvas(g); else ClearCanvas(g);
        foreach (var obj in Animation.Objects) DrawObject(g, obj, scale);

        g.Restore(state);
        DrawHud(g, hud);
    }

    private static void ClearCanvas(System.Drawing.Graphics g)
    {
        using var bg = new SolidBrush(Color.Black);
        g.FillRectangle(bg, 0, 0, A2dScene.CanvasWidth, A2dScene.CanvasHeight);
    }

    private static void DrawCanvas(System.Drawing.Graphics g)
    {
        using var bg = new SolidBrush(Color.FromArgb(24, 27, 34));
        g.FillRectangle(bg, 0, 0, A2dScene.CanvasWidth, A2dScene.CanvasHeight);

        using var grid = new Pen(Color.FromArgb(18, 255, 255, 255), 0.6f);
        for (int x = 0; x <= A2dScene.CanvasWidth; x += 80)
            g.DrawLine(grid, x, 0, x, A2dScene.CanvasHeight);
        for (int y = 0; y <= A2dScene.CanvasHeight; y += 80)
            g.DrawLine(grid, 0, y, A2dScene.CanvasWidth, y);

        using var axis = new Pen(Color.FromArgb(46, 255, 255, 255), 0.8f);
        g.DrawLine(axis, A2dScene.CanvasWidth / 2, 0, A2dScene.CanvasWidth / 2, A2dScene.CanvasHeight);
        g.DrawLine(axis, 0, A2dScene.CanvasHeight / 2, A2dScene.CanvasWidth, A2dScene.CanvasHeight / 2);
    }

    private void DrawObject(System.Drawing.Graphics g, A2dObject obj, float scale)
    {
        if (!obj.IsLive(_frame)) return;
        if (obj.TransformAt(_frame) is not { } t) return;

        var box = obj.Box ?? new Bounds(-8, 8, 8, -8);
        float alpha = obj.ColourAt(_frame)?.A ?? 1f;
        if (alpha <= 0.004f) return;

        // Slot drives the hue so objects stay distinguishable while the art is
        // still placeholder; the exported RGB is white throughout.
        var tint = FromHsv(obj.Slot * 49.3f % 360f, 0.55f, 1f);
        int a = (int)Math.Clamp(alpha * 255f, 0, 255);

        var origin = ToCanvas(t.X, t.Y);
        var state = g.Save();

        g.TranslateTransform(origin.X, origin.Y);
        g.RotateTransform(t.Rotation * (_flipY ? -1f : 1f));
        g.ScaleTransform(t.ScaleX, _flipY ? -t.ScaleY : t.ScaleY);

        if (_showDebug)
        {
            var local = new RectangleF(box.Left, box.Bottom, box.Width, box.Height);
            using var fill = new SolidBrush(Color.FromArgb(a * 28 / 100, tint));
            g.FillRectangle(fill, local);
            using var edge = new Pen(Color.FromArgb(a, tint), 1.4f);
            g.DrawRectangle(edge, local.X, local.Y, local.Width, local.Height);
        }

        g.Restore(state);

        if (_showDebug)
        {
            using var dot = new SolidBrush(Color.FromArgb(a, tint));
            g.FillEllipse(dot, origin.X - 1.6f, origin.Y - 1.6f, 3.2f, 3.2f);
        }

        // A text-bound object renders its string rather than its object name,
        // laid out with the justification and relative size the scripts specify.
        if (_textBindings.TryGetValue(obj.Name, out var binding))
        {
            DrawBoundText(g, binding, origin, box, t, a);
            return;
        }

        if (_showSprites && _iconBindings.TryGetValue(obj.Name, out var iconName)


            && _sprites.Find(iconName) is { } sprite)


        {


            var dest = new RectangleF(origin.X + box.Left * t.ScaleX,


                                      origin.Y - box.Top * t.ScaleY,


                                      box.Width * t.ScaleX, box.Height * t.ScaleY);


            var attr = new ImageAttributes();


            var m = new ColorMatrix { Matrix33 = alpha };


            attr.SetColorMatrix(m);


            g.DrawImage(sprite.Texture, Rectangle.Round(dest),


                        sprite.Source.X, sprite.Source.Y, sprite.Source.Width, sprite.Source.Height,


                        GraphicsUnit.Pixel, attr);


            attr.Dispose();


            return;


        }



        if (_showDebug && _showNames && box.Width * t.ScaleX >= 44 && box.Height * t.ScaleY >= 16)
        {
            using var font = new Font("Segoe UI", 6.5f);
            using var text = new SolidBrush(Color.FromArgb(Math.Min(255, a + 40), Color.White));
            g.DrawString(obj.Name, font, text, origin.X - box.Width / 2 + 3, origin.Y - 5);
        }
    }

    /// <summary>
    /// Draws a text-bound object. The string shown is the lookup *key*, not the
    /// Dutch text: resolving a key needs the `default.ndx` hash function, which
    /// is not yet identified. Position, box, justification and relative size are
    /// exact, so the layout is real even though the wording is a placeholder.
    /// </summary>
    private void DrawBoundText(System.Drawing.Graphics g, TextBinding binding, PointF origin, Bounds box, TfmKey t, int alpha)
    {
        // The style names a base size the multiplier scales; the mapping from
        // style to points is a guess, so this is approximate typography.
        float basePoints = binding.Style.Contains("Large", StringComparison.OrdinalIgnoreCase) ? 9f : 6.5f;
        float points = Math.Clamp(basePoints * Math.Min(binding.SizeMultiplier, 2.2f), 5f, 26f);

        using var font = new Font("Segoe UI", points, FontStyle.Bold);
        using var brush = new SolidBrush(Color.FromArgb(Math.Min(255, alpha + 30), 250, 250, 235));

        var layout = new RectangleF(
            origin.X + box.Left * t.ScaleX,
            origin.Y - (box.Top * t.ScaleY),
            box.Width * t.ScaleX,
            box.Height * t.ScaleY);

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

        var resolved = _text.Resolve(binding.Key);
        using var placeholder = new SolidBrush(Color.FromArgb(Math.Min(255, alpha), 150, 156, 170));
        g.DrawString(resolved ?? binding.Key, font, resolved is null ? placeholder : brush, layout, format);
    }

    private void DrawHud(System.Drawing.Graphics g, int height)
    {
        int top = ClientSize.Height - height;
        using var panel = new SolidBrush(Color.FromArgb(10, 12, 16));
        g.FillRectangle(panel, 0, top, ClientSize.Width, height);

        var anim = Animation;
        using var big = new Font("Segoe UI", 12f, FontStyle.Bold);
        using var mid = new Font("Segoe UI", 10f);
        using var small = new Font("Segoe UI", 9f);

        g.DrawString($"{Scene.Name}   [{_sceneIndex + 1}/{_scenes.Count}]",
                     big, Brushes.White, 16, top + 10);

        g.DrawString($"{anim.Name}   [{_animIndex + 1}/{Scene.Animations.Count}]    " +
                     $"frame {_frame}/{anim.FrameCount}    {anim.Objects.Count} objects    " +
                     $"{_fps:0} fps    y-axis: {(_flipY ? "up" : "down")}    text: {_text.MappedKeys} keys    sprites: {_sprites.SpriteCount} in {_sprites.AtlasCount} atlases",
                     mid, new SolidBrush(Color.FromArgb(150, 190, 240)), 16, top + 34);

        g.DrawString("arrows: animation / scene    space: play-pause    , . step frame    " +
                     "R restart    Y flip axis    F fps    N names  T sprites  D clean  Esc quit",
                     small, new SolidBrush(Color.FromArgb(120, 124, 134)), 16, top + 58);

        // Progress bar for the current clip.
        float progress = anim.FrameCount > 0 ? Math.Min(1f, (float)_frame / anim.FrameCount) : 0;
        using var bar = new SolidBrush(Color.FromArgb(70, 190, 245));
        g.FillRectangle(bar, 0, ClientSize.Height - 3, ClientSize.Width * progress, 3);
    }

    private static Color FromHsv(float hue, float saturation, float value)
    {
        int hi = (int)(hue / 60f) % 6;
        float f = hue / 60f - (int)(hue / 60f);
        float v = value * 255f, p = v * (1 - saturation);
        float q = v * (1 - f * saturation), t = v * (1 - (1 - f) * saturation);

        return hi switch
        {
            0 => Color.FromArgb((int)v, (int)t, (int)p),
            1 => Color.FromArgb((int)q, (int)v, (int)p),
            2 => Color.FromArgb((int)p, (int)v, (int)t),
            3 => Color.FromArgb((int)p, (int)q, (int)v),
            4 => Color.FromArgb((int)t, (int)p, (int)v),
            _ => Color.FromArgb((int)v, (int)p, (int)q),
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
