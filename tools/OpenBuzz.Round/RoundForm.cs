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

        // The question screen is drawn in the game's own 640x480 design space and
        // scaled up, so proportions and layout match the original rather than
        // being re-invented for whatever the window happens to be.
        var stage = new Rectangle(0, 0, ClientSize.Width, 460);
        var state = A2dRenderer.BeginCanvas(g, stage);
        DrawQuestionScreen(g);
        g.Restore(state);

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
    /// Draws the question screen the way the game does: the question centred and
    /// numbered at the top, then four rows of a small coloured button followed by
    /// left-aligned white text, and the player viewports along the bottom.
    ///
    /// All coordinates are in the 640x480 design space, matching the A2D data, so
    /// this sits in the same coordinate system as the bumper and the animations.
    /// </summary>
    private void DrawQuestionScreen(System.Drawing.Graphics g)
    {
        const float W = 640, H = 480;

        using (var panel = new LinearGradientBrush(new RectangleF(0, 0, W, H),
                   Color.FromArgb(58, 72, 88), Color.FromArgb(30, 38, 50), LinearGradientMode.Vertical))
            g.FillRectangle(panel, 0, 0, W, H);

        // Faint horizontal banding, as on the game's back-projected screen.
        using (var band = new SolidBrush(Color.FromArgb(10, 255, 255, 255)))
            for (int y = 0; y < H; y += 90)
                g.FillRectangle(band, 0, y, W, 45);

        using var title = new Font("Segoe UI", 15f, FontStyle.Bold);
        using var answerFont = new Font("Segoe UI", 12f, FontStyle.Bold);
        using var small = new Font("Segoe UI", 7.5f);

        using (var centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString($"{Math.Max(_game.QuestionNumber, 1)}: {_game.QuestionText}",
                         title, Brushes.White, new RectangleF(40, 26, W - 80, 76), centre);

        bool reveal = _game.Phase is RoundPhase.Revealing or RoundPhase.Finished;

        for (int i = 0; i < _game.Options.Count; i++)
        {
            var option = _game.Options[i];
            var colour = HandsetLayout.ColourOf(BuzzButtonExtensions.AnswerButtons[i]);
            float y = 142 + i * 52;

            // The button is a dark square with a bright coloured border, not a
            // filled bar - that is what makes the screen read as the game's.
            var button = new RectangleF(72, y, 34, 34);
            using (var face = new SolidBrush(Color.FromArgb(230, 18, 22, 30)))
            using (var edge = new Pen(colour, 3f))
            using (var path = HandsetLayout.RoundedRect(Rectangle.Round(button), 7))
            {
                g.FillPath(face, path);
                g.DrawPath(edge, path);
            }

            var textColour = reveal
                ? (option.IsCorrect ? Color.FromArgb(150, 255, 170) : Color.FromArgb(150, 158, 170))
                : Color.White;

            using var brush = new SolidBrush(textColour);
            using var format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
            g.DrawString(option.Text.ToUpperInvariant(), answerFont, brush,
                         new RectangleF(122, y - 4, W - 150, 42), format);

            if (reveal && _game.ChosenOption == i)
                using (var marker = new Pen(Color.White, 2f))
                    g.DrawEllipse(marker, 54, y + 12, 10, 10);
        }

        DrawViewports(g, small);

        using (var status = new SolidBrush(reveal
                   ? (_game.LastAnswerCorrect ? Color.FromArgb(140, 235, 160) : Color.FromArgb(240, 130, 120))
                   : Color.FromArgb(200, 210, 225)))
        using (var centre = new StringFormat { Alignment = StringAlignment.Center })
            g.DrawString(_game.Status, answerFont, status, new RectangleF(0, 352, W, 24), centre);
    }

    /// Player viewports along the bottom, using the game's own frame sprites.
    private void DrawViewports(System.Drawing.Graphics g, Font font)
    {
        var surround = _art?.Sprites.Find("PortraitSurroundWhite");
        var bar = _art?.Sprites.Find("ViewportBarWhite");

        for (int i = 0; i < _input.ControllerCount; i++)
        {
            float x = 92 + i * 118;
            var frame = new RectangleF(x, 386, 94, 60);

            if (surround is { } s)
                g.DrawImage(s.Texture, Rectangle.Round(frame),
                            s.Source.X, s.Source.Y, s.Source.Width, s.Source.Height, GraphicsUnit.Pixel);
            else
                using (var pen = new Pen(Color.FromArgb(120, 200, 230), 2f))
                    g.DrawRectangle(pen, frame.X, frame.Y, frame.Width, frame.Height);

            // A buzzed-in player's viewport lights up, as in the game.
            if (_game.BuzzedPlayer == i)
                using (var glow = new Pen(Color.FromArgb(255, 240, 120), 3f))
                    g.DrawRectangle(glow, frame.X - 2, frame.Y - 2, frame.Width + 4, frame.Height + 4);

            var label = new RectangleF(x, 446, 94, 22);
            if (bar is { } b)
                g.DrawImage(b.Texture, Rectangle.Round(label),
                            b.Source.X, b.Source.Y, b.Source.Width, b.Source.Height, GraphicsUnit.Pixel);

            using var text = new SolidBrush(Color.White);
            using var centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString($"SPELER {i + 1}", font, text, label, centre);

            using var scoreBrush = new SolidBrush(Color.FromArgb(190, 210, 235));
            g.DrawString($"{_game.Scores[i]}", font, scoreBrush,
                         new RectangleF(x, 404, 94, 20), centre);
        }
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _player.Dispose(); _input.Dispose(); }
        base.Dispose(disposing);
    }
}
