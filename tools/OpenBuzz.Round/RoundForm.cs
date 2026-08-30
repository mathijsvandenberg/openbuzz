using System.Diagnostics;
using System.Drawing.Drawing2D;
using OpenBuzz.Input;
using OpenBuzz.Quiz;
using OpenBuzz.Animation;
using OpenBuzz.Ui;

namespace OpenBuzz.Round;

public sealed class RoundForm : Form
{
    private const int Gutter = 28;

    private readonly VirtualBuzzInput _input = new();
    private readonly HandsetLayout[] _handsets;
    private readonly ClipPlayer _player = new();
    private readonly RoundGame _game;

    /// The game's own artwork: A2D timelines, atlas sprites and the recovered
    /// string map, all shared with the standalone player through OpenBuzz.Ui.
    private readonly A2dRenderer? _art;
    private readonly A2dAnimation? _bumper;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastTick;

    private static readonly Dictionary<Keys, (int Controller, BuzzButton Button)> KeyMap = BuildKeyMap();

    public RoundForm(QuizBank bank, SongTable songs, string soundDir, int sampleRate, string pool)
    {
        Text = "OpenBuzz - Round";
        BackColor = Color.FromArgb(18, 20, 26);
        ForeColor = Color.Gainsboro;
        DoubleBuffered = true;
        KeyPreview = true;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(Gutter * 2 + HandsetLayout.Width * 4 + 3 * 16, 900);

        _handsets = new HandsetLayout[_input.ControllerCount];
        for (int i = 0; i < _handsets.Length; i++)
            _handsets[i] = new HandsetLayout(new Rectangle(
                Gutter + i * (HandsetLayout.Width + 16), 470, HandsetLayout.Width, HandsetLayout.Height));

        _game = new RoundGame(bank, songs, _player, _input, soundDir, sampleRate, pool);

        // Artwork is optional: without extracted/a2d and extracted/Textures the
        // round still plays, it just falls back to the plain presentation.
        if (A2dScene.FindExportDirectory(AppContext.BaseDirectory) is { } a2dDir)
        {
            var scenes = A2dScene.LoadAll(a2dDir);
            if (scenes.Count > 0)
            {
                _art = new A2dRenderer(scenes,
                                       SpriteLibrary.Discover(AppContext.BaseDirectory),
                                       TextKeyMap.Discover(AppContext.BaseDirectory));

                _bumper = scenes.FirstOrDefault(s => s.Name.Contains("BUMP_PointsBuilder", StringComparison.Ordinal))
                          ?.Animations.FirstOrDefault();
            }
        }

        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        var dt = now - _lastTick;
        _lastTick = now;

        _input.Update(dt);   // must run before the game so WasPressed is fresh
        _game.Update(dt);
        Invalidate();
    }

    private static Dictionary<Keys, (int, BuzzButton)> BuildKeyMap()
    {
        Keys[] red = [Keys.D1, Keys.D2, Keys.D3, Keys.D4];
        Keys[][] answers =
        [
            [Keys.Q, Keys.W, Keys.E, Keys.R],
            [Keys.A, Keys.S, Keys.D, Keys.F],
            [Keys.Z, Keys.X, Keys.C, Keys.V],
            [Keys.U, Keys.I, Keys.O, Keys.P],
        ];

        var map = new Dictionary<Keys, (int, BuzzButton)>();
        for (int c = 0; c < 4; c++)
        {
            map[red[c]] = (c, BuzzButton.Red);
            for (int b = 0; b < 4; b++)
                map[answers[c][b]] = (c, BuzzButtonExtensions.AnswerButtons[b]);
        }
        return map;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (KeyMap.TryGetValue(e.KeyCode, out var hit)) { _input.SetButton(hit.Controller, hit.Button, true); e.Handled = true; return; }
        if (e.KeyCode == Keys.F5) { _game.Restart(); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (KeyMap.TryGetValue(e.KeyCode, out var hit)) { _input.SetButton(hit.Controller, hit.Button, false); e.Handled = true; return; }
        base.OnKeyUp(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        for (int i = 0; i < _handsets.Length; i++)
            if (_handsets[i].HitTest(e.Location) is { } button) { _input.SetButton(i, button, true); return; }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        for (int i = 0; i < _handsets.Length; i++)
            foreach (var b in BuzzButtonExtensions.AllButtons)
                _input.SetButton(i, b, false);
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var head = new Font("Segoe UI", 20f, FontStyle.Bold);
        using var body = new Font("Segoe UI", 13f, FontStyle.Bold);
        using var small = new Font("Segoe UI", 9f);
        using var buzzerFont = new Font("Segoe UI", 11f, FontStyle.Bold);

        // The round opens on its A2D bumper, played with the game's own artwork.
        if (_game.Phase == RoundPhase.Bumper && _art is not null && _bumper is not null)
        {
            DrawBumper(g, small, buzzerFont);
            return;
        }

        g.DrawString($"Question {Math.Max(_game.QuestionNumber, 1)} of {RoundGame.QuestionsPerRound}" +
                     $"     pool: {_game.PoolSize} questions" +
                     (_game.CurrentClip is { } c ? $"     clip: {c}" : ""),
                     small, Brushes.Gray, Gutter, 20);

        DrawWrapped(g, _game.QuestionText, head, Brushes.White,
                    new RectangleF(Gutter, 48, ClientSize.Width - Gutter * 2, 90));

        for (int i = 0; i < _game.Options.Count; i++) DrawOption(g, i, body);

        using (var statusBrush = new SolidBrush(_game.Phase == RoundPhase.Revealing
                   ? (_game.LastAnswerCorrect ? Color.FromArgb(120, 230, 140) : Color.FromArgb(240, 120, 110))
                   : Color.Gainsboro))
            g.DrawString(_game.Status, body, statusBrush, Gutter, 420);

        for (int i = 0; i < _handsets.Length; i++)
            HandsetRenderer.Draw(g, _handsets[i], _input, i, $"Player {i + 1}   {_game.Scores[i]}", small, buzzerFont);

        // At the end, award each player their place medal, as the game does.
        if (_game.Phase == RoundPhase.Finished)
        {
            var ranking = Enumerable.Range(0, _game.Scores.Length)
                                    .OrderByDescending(i => _game.Scores[i])
                                    .ToArray();
            for (int place = 0; place < ranking.Length; place++)
            {
                var h = _handsets[ranking[place]].Bounds;
                DrawMedal(g, place, new Rectangle(h.X + (h.Width - 72) / 2, h.Y - 78, 72, 72));
            }
        }

        g.DrawString("1-4 buzz    QWER / ASDF / ZXCV / UIOP answer    F5 restart",
                     small, Brushes.DimGray, Gutter, ClientSize.Height - 30);
    }

    /// <summary>
    /// Plays the round's A2D bumper over the whole window, in the original
    /// 640x480 design space scaled up. The handsets stay visible underneath so a
    /// player can see their buzzer is live during the intro.
    /// </summary>
    private void DrawBumper(System.Drawing.Graphics g, Font small, Font buzzerFont)
    {
        using var background = new SolidBrush(Color.Black);
        var stage = new Rectangle(0, 0, ClientSize.Width, 460);
        g.FillRectangle(background, stage);

        var state = A2dRenderer.BeginCanvas(g, stage);
        _art!.Draw(g, _bumper!, _game.BumperFrame);
        g.Restore(state);

        for (int i = 0; i < _handsets.Length; i++)
            HandsetRenderer.Draw(g, _handsets[i], _input, i, $"Player {i + 1}", small, buzzerFont);

        g.DrawString($"{_game.Status}    frame {_game.BumperFrame}/{RoundGame.BumperFrames}",
                     small, Brushes.DimGray, Gutter, ClientSize.Height - 30);
    }

    /// <summary>
    /// Draws the place medal for a finishing position, using the real
    /// `PIP_1st`..`PIP_4th` sprites when the artwork is available.
    /// </summary>
    private void DrawMedal(System.Drawing.Graphics g, int place, Rectangle bounds)
    {
        string[] names = ["PIP_1st", "PIP_2nd", "PIP_3rd", "PIP_4th"];
        if (_art?.Sprites.Find(names[Math.Clamp(place, 0, 3)]) is not { } sprite) return;

        g.DrawImage(sprite.Texture, bounds,
                    sprite.Source.X, sprite.Source.Y, sprite.Source.Width, sprite.Source.Height,
                    GraphicsUnit.Pixel);
    }

    private void DrawOption(System.Drawing.Graphics g, int index, Font font)
    {
        var button = BuzzButtonExtensions.AnswerButtons[index];
        var colour = HandsetLayout.ColourOf(button);
        var rect = new Rectangle(Gutter, 160 + index * 60, ClientSize.Width - Gutter * 2, 50);

        bool reveal = _game.Phase is RoundPhase.Revealing or RoundPhase.Finished;
        var option = _game.Options[index];

        // During the reveal, mark the right answer and the player's mistake.
        Color fill = colour;
        if (reveal && option.IsCorrect) fill = ControlPaint.Light(colour, 0.9f);
        else if (reveal) fill = ControlPaint.Dark(colour, 0.45f);

        using (var path = HandsetLayout.RoundedRect(rect, 12))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
            if (reveal && _game.ChosenOption == index)
                using (var pen = new Pen(Color.White, 3f)) g.DrawPath(pen, path);
        }

        var text = new Rectangle(rect.X + 18, rect.Y, rect.Width - 36, rect.Height);
        using var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(option.Text, font, Brushes.Black, text, sf);
    }

    private static void DrawWrapped(System.Drawing.Graphics g, string text, Font font, Brush brush, RectangleF bounds)
    {
        using var sf = new StringFormat { Trimming = StringTrimming.EllipsisWord };
        g.DrawString(text, font, brush, bounds, sf);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _player.Dispose(); _input.Dispose(); }
        base.Dispose(disposing);
    }
}
