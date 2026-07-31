using Godot;

namespace OpenBuzz.Player;

/// <summary>
/// Plays the extracted A2D timelines with placeholder rectangles.
///
/// The point is to verify the choreography — positions, easing, timing, bounds
/// — before any artwork exists, so the layout can be confirmed independently of
/// the unsolved texture decode. Every coordinate is used exactly as exported;
/// Godot's canvas stretch does the scaling, so this is already a 1080p render
/// of the original 640x480 design space.
/// </summary>
public partial class A2dPlayer : Node2D
{
    private const float CanvasWidth = 640f;
    private const float CanvasHeight = 480f;

    private readonly List<A2dSceneData> _scenes = [];
    private int _sceneIndex;
    private int _animIndex;

    private double _elapsed;
    private int _frame;
    private bool _playing = true;

    /// A2D y direction is not stated anywhere in the data, so it is a toggle
    /// rather than an assumption — one of these is right and the eye decides.
    private bool _flipY = true;

    private float _fps = 25f;   // PAL; 50 is the other candidate
    private string _status = "";
    private Font _font = null!;

    private A2dSceneData? Scene => _scenes.Count > 0 ? _scenes[_sceneIndex] : null;

    private A2dAnimationData? Animation =>
        Scene is { Animations.Count: > 0 } s ? s.Animations[Mathf.Min(_animIndex, s.Animations.Count - 1)] : null;

    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;

        var projectDir = ProjectSettings.GlobalizePath("res://");
        var sceneDir = A2dLoader.FindSceneDirectory(projectDir);

        if (sceneDir is null)
        {
            _status = "No extracted/a2d found. Run: obz a2d export";
            GD.PrintErr(_status);
            return;
        }

        _scenes.AddRange(A2dLoader.LoadAll(sceneDir));
        _status = $"{_scenes.Count} scenes from {sceneDir}";
        GD.Print(_status);
    }

    public override void _Process(double delta)
    {
        var anim = Animation;
        if (anim is null) return;

        if (_playing)
        {
            _elapsed += delta;
            int total = Mathf.Max(anim.FrameCount, 1);
            _frame = (int)(_elapsed * _fps) % total;
        }

        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        switch (key.Keycode)
        {
            case Key.Right: StepAnimation(1); break;
            case Key.Left: StepAnimation(-1); break;
            case Key.Down: StepScene(1); break;
            case Key.Up: StepScene(-1); break;
            case Key.Space: _playing = !_playing; break;
            case Key.R: _elapsed = 0; _frame = 0; break;
            case Key.Y: _flipY = !_flipY; break;
            case Key.F: _fps = Mathf.IsEqualApprox(_fps, 25f) ? 50f : 25f; break;
            case Key.Period: _playing = false; _frame++; break;
            case Key.Comma: _playing = false; _frame = Mathf.Max(0, _frame - 1); break;
            case Key.Escape: GetTree().Quit(); break;
            default: return;
        }

        QueueRedraw();
    }

    private void StepScene(int delta)
    {
        if (_scenes.Count == 0) return;
        _sceneIndex = (_sceneIndex + delta + _scenes.Count) % _scenes.Count;
        _animIndex = 0;
        _elapsed = 0;
        _frame = 0;
    }

    private void StepAnimation(int delta)
    {
        if (Scene is not { Animations.Count: > 0 } s) return;
        _animIndex = (_animIndex + delta + s.Animations.Count) % s.Animations.Count;
        _elapsed = 0;
        _frame = 0;
    }

    /// Maps an A2D point into screen space, honouring the y-direction toggle.
    private Vector2 ToScreen(float x, float y) => new(x, _flipY ? CanvasHeight - y : y);

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, CanvasWidth, CanvasHeight), new Color(0.09f, 0.10f, 0.13f));
        DrawGuides();

        var anim = Animation;
        if (anim is null)
        {
            DrawString(_font, new Vector2(16, 40), _status, HorizontalAlignment.Left, -1, 14, Colors.OrangeRed);
            return;
        }

        foreach (var obj in anim.Objects) DrawObject(obj);

        DrawHud(anim);
    }

    private void DrawGuides()
    {
        var grid = new Color(1, 1, 1, 0.06f);
        for (int x = 0; x <= (int)CanvasWidth; x += 80) DrawLine(new Vector2(x, 0), new Vector2(x, CanvasHeight), grid);
        for (int y = 0; y <= (int)CanvasHeight; y += 80) DrawLine(new Vector2(0, y), new Vector2(CanvasWidth, y), grid);

        var axis = new Color(1, 1, 1, 0.18f);
        DrawLine(new Vector2(CanvasWidth / 2, 0), new Vector2(CanvasWidth / 2, CanvasHeight), axis);
        DrawLine(new Vector2(0, CanvasHeight / 2), new Vector2(CanvasWidth, CanvasHeight / 2), axis);
    }

    private void DrawObject(A2dObjectData obj)
    {
        if (!obj.IsLive(_frame)) return;
        if (obj.TransformAt(_frame) is not { } t) return;

        var box = obj.Box ?? new BoundsData { Left = -8, Top = 8, Right = 8, Bottom = -8 };
        var colour = obj.ColourAt(_frame);
        float alpha = colour?.A ?? 1f;
        if (alpha <= 0.004f) return;

        // Slot drives the hue so objects stay visually distinct while the art
        // is still placeholder; the exported RGB is white throughout.
        var tint = Color.FromHsv((obj.Slot * 0.137f) % 1f, 0.55f, 1f);
        tint.A = alpha;

        var origin = ToScreen(t.X, t.Y);
        float rotation = Mathf.DegToRad(t.Rotation) * (_flipY ? -1f : 1f);

        DrawSetTransform(origin, rotation, new Vector2(t.ScaleX, _flipY ? -t.ScaleY : t.ScaleY));

        var local = new Rect2(box.Left, box.Bottom, box.Width, box.Height);
        DrawRect(local, tint with { A = alpha * 0.28f });
        DrawRect(local, tint, filled: false, width: 1.5f);

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        DrawCircle(origin, 1.6f, tint);
        if (box.Width >= 40 && box.Height >= 18)
            DrawString(_font, origin + new Vector2(-box.Width / 2 + 4, -2), obj.Name,
                       HorizontalAlignment.Left, box.Width, 8, new Color(1, 1, 1, alpha * 0.85f));
    }

    private void DrawHud(A2dAnimationData anim)
    {
        var panel = new Rect2(0, CanvasHeight - 56, CanvasWidth, 56);
        DrawRect(panel, new Color(0, 0, 0, 0.62f));

        var scene = Scene!;
        DrawString(_font, new Vector2(8, CanvasHeight - 40),
                   $"{scene.Name}   [{_sceneIndex + 1}/{_scenes.Count}]",
                   HorizontalAlignment.Left, -1, 11, Colors.White);

        DrawString(_font, new Vector2(8, CanvasHeight - 27),
                   $"{anim.Name}   [{_animIndex + 1}/{scene.Animations.Count}]   " +
                   $"frame {_frame}/{anim.FrameCount}   {anim.Objects.Count} objects   {_fps:0}fps",
                   HorizontalAlignment.Left, -1, 10, new Color(0.75f, 0.85f, 1f));

        DrawString(_font, new Vector2(8, CanvasHeight - 13),
                   $"arrows: anim / scene   space: {(_playing ? "pause" : "play")}   , . step   " +
                   $"R restart   Y flip:{(_flipY ? "up" : "down")}   F fps   Esc quit",
                   HorizontalAlignment.Left, -1, 9, new Color(0.6f, 0.6f, 0.65f));

        // Progress bar for the current clip.
        float progress = anim.FrameCount > 0 ? (float)_frame / anim.FrameCount : 0;
        DrawRect(new Rect2(0, CanvasHeight - 3, CanvasWidth * progress, 3), new Color(0.3f, 0.8f, 1f, 0.9f));
    }
}
