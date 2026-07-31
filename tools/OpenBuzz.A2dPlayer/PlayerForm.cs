using System.Diagnostics;
using System.Drawing.Drawing2D;
using OpenBuzz.Animation;

namespace OpenBuzz.A2dPlayer;

/// <summary>
/// Plays the extracted A2D timelines as placeholder rectangles.
///
/// The point is to confirm the choreography â€” positions, easing, timing, bounds
/// â€” before any artwork exists, so the layout can be verified independently of
/// the unsolved texture decode. Coordinates are used exactly as exported and the
/// 640x480 design space is scaled to the window, so this is already a
/// full-resolution render of the original layout: nothing is resampled.
/// </summary>
public sealed class PlayerForm : Form
{
    private readonly List<A2dScene> _scenes;
    private readonly string _sourceDir;
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

    /// PAL, but 25 vs 50 is undetermined â€” also a toggle.
    private double _fps = 25;

    private A2dScene Scene => _scenes[_sceneIndex];

    private A2dAnimation Animation =>
        Scene.Animations[Math.Min(_animIndex, Scene.Animations.Count - 1)];

    public PlayerForm(List<A2dScene> scenes, string sourceDir)
    {
        _scenes = scenes;
        _sourceDir = sourceDir;

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

        DrawCanvas(g);
        foreach (var obj in Animation.Objects) DrawObject(g, obj, scale);

        g.Restore(state);
        DrawHud(g, hud);
    }

    private static void DrawCanvas(Graphics g)
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

    private void DrawObject(Graphics g, A2dObject obj, float scale)
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

        var local = new RectangleF(box.Left, box.Bottom, box.Width, box.Height);
        using (var fill = new SolidBrush(Color.FromArgb(a * 28 / 100, tint)))
            g.FillRectangle(fill, local);
        using (var edge = new Pen(Color.FromArgb(a, tint), 1.4f))
            g.DrawRectangle(edge, local.X, local.Y, local.Width, local.Height);

        g.Restore(state);

        using (var dot = new SolidBrush(Color.FromArgb(a, tint)))
            g.FillEllipse(dot, origin.X - 1.6f, origin.Y - 1.6f, 3.2f, 3.2f);

        if (_showNames && box.Width * t.ScaleX >= 44 && box.Height * t.ScaleY >= 16)
        {
            using var font = new Font("Segoe UI", 6.5f);
            using var text = new SolidBrush(Color.FromArgb(Math.Min(255, a + 40), Color.White));
            g.DrawString(obj.Name, font, text, origin.X - box.Width / 2 + 3, origin.Y - 5);
        }
    }

    private void DrawHud(Graphics g, int height)
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
                     $"{_fps:0} fps    y-axis: {(_flipY ? "up" : "down")}",
                     mid, new SolidBrush(Color.FromArgb(150, 190, 240)), 16, top + 34);

        g.DrawString("arrows: animation / scene    space: play-pause    , . step frame    " +
                     "R restart    Y flip axis    F fps    N names    Esc quit",
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
