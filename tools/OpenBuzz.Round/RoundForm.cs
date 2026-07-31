using System.Diagnostics;
using System.Drawing.Drawing2D;
using OpenBuzz.Input;
using OpenBuzz.Quiz;
using OpenBuzz.Ui;

namespace OpenBuzz.Round;

public sealed class RoundForm : Form
{
    private const int Gutter = 28;

    private readonly VirtualBuzzInput _input = new();
    private readonly HandsetLayout[] _handsets;
    private readonly ClipPlayer _player = new();
    private readonly RoundGame _game;
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

        g.DrawString("1-4 buzz    QWER / ASDF / ZXCV / UIOP answer    F5 restart",
                     small, Brushes.DimGray, Gutter, ClientSize.Height - 30);
    }

    private void DrawOption(Graphics g, int index, Font font)
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

    private static void DrawWrapped(Graphics g, string text, Font font, Brush brush, RectangleF bounds)
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
