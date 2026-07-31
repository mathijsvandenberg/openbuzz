using System.Diagnostics;
using System.Drawing.Drawing2D;
using OpenBuzz.Input;

namespace OpenBuzz.VirtualControllers;

/// <summary>
/// An on-screen stand-in for four Buzz handsets. Everything it does goes through
/// <see cref="VirtualBuzzInput"/>, so swapping in real USB hardware later is a
/// change of one line and nothing downstream notices.
/// </summary>
public sealed class MainForm : Form
{
    private const int Gutter = 24;
    private const int PanelTop = 56;

    private readonly VirtualBuzzInput _input = new();
    private readonly HandsetLayout[] _handsets;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastTick;

    /// Buttons currently held by the mouse, so a drag off the button releases it.
    private (int Controller, BuzzButton Button)? _mouseHeld;

    private readonly List<string> _log = [];

    public MainForm()
    {
        Text = "OpenBuzz - Virtual Buzz Controllers";
        BackColor = Color.FromArgb(24, 26, 32);
        ForeColor = Color.Gainsboro;
        DoubleBuffered = true;
        KeyPreview = true;
        ClientSize = new Size(Gutter * 2 + HandsetLayout.Width * 4 + 3 * 16,
                              PanelTop + HandsetLayout.Height + 130);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        _handsets = new HandsetLayout[_input.ControllerCount];
        for (int i = 0; i < _handsets.Length; i++)
            _handsets[i] = new HandsetLayout(new Rectangle(
                Gutter + i * (HandsetLayout.Width + 16), PanelTop,
                HandsetLayout.Width, HandsetLayout.Height));

        _input.ButtonPressed += (_, e) => Log($"P{e.Controller + 1}  {e.Button} pressed");
        _input.ButtonReleased += (_, e) => Log($"P{e.Controller + 1}  {e.Button} released");

        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void Log(string line)
    {
        _log.Add(line);
        if (_log.Count > 6) _log.RemoveAt(0);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        _input.Update(now - _lastTick);
        _lastTick = now;
        Invalidate();
    }

    // ---- input -----------------------------------------------------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        for (int i = 0; i < _handsets.Length; i++)
        {
            if (_handsets[i].HitTest(e.Location) is not { } button) continue;
            _mouseHeld = (i, button);
            _input.SetButton(i, button, true);
            return;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_mouseHeld is { } held)
        {
            _input.SetButton(held.Controller, held.Button, false);
            _mouseHeld = null;
        }
        base.OnMouseUp(e);
    }

    /// Keyboard rows mirror the on-screen order: one column of keys per handset.
    private static readonly Dictionary<Keys, (int Controller, BuzzButton Button)> KeyMap = BuildKeyMap();

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
        if (KeyMap.TryGetValue(e.KeyCode, out var hit))
        {
            _input.SetButton(hit.Controller, hit.Button, true);
            e.Handled = true;
            return;
        }

        // F1..F4 cycle a handset's lamp: off -> on -> flashing.
        if (e.KeyCode is >= Keys.F1 and <= Keys.F4)
        {
            var lamp = _input.Lamp(e.KeyCode - Keys.F1);
            switch (lamp.Mode)
            {
                case LampMode.Off: lamp.On(); break;
                case LampMode.On: lamp.Flash(); break;
                default: lamp.Off(); break;
            }
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.F5)
        {
            for (int i = 0; i < _input.ControllerCount; i++) _input.Lamp(i).Flash();
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.F6)
        {
            for (int i = 0; i < _input.ControllerCount; i++) _input.Lamp(i).Off();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (KeyMap.TryGetValue(e.KeyCode, out var hit))
        {
            _input.SetButton(hit.Controller, hit.Button, false);
            e.Handled = true;
            return;
        }
        base.OnKeyUp(e);
    }

    // ---- painting --------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var title = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var small = new Font("Segoe UI", 8.5f);
        using var mono = new Font("Consolas", 8.5f);

        g.DrawString($"Input source: {_input.Description}    " +
                     "keys 1-4 = buzzers, QWER / ASDF / ZXCV / UIOP = answers    " +
                     "F1-F4 cycle lamp, F5 flash all, F6 off",
                     small, Brushes.Gray, Gutter, 16);

        for (int i = 0; i < _handsets.Length; i++)
            DrawHandset(g, _handsets[i], i, title, small);

        int y = PanelTop + HandsetLayout.Height + 16;
        g.DrawString("Events", small, Brushes.Gray, Gutter, y);
        for (int i = 0; i < _log.Count; i++)
            g.DrawString(_log[i], mono, Brushes.DarkGray, Gutter, y + 18 + i * 15);
    }

    private void DrawHandset(Graphics g, HandsetLayout h, int index, Font title, Font small)
    {
        using (var body = HandsetLayout.RoundedRect(h.Bounds, 22))
        using (var fill = new SolidBrush(Color.FromArgb(38, 40, 48)))
        using (var edge = new Pen(Color.FromArgb(60, 64, 74), 1.5f))
        {
            g.FillPath(fill, body);
            g.DrawPath(edge, body);
        }

        // Red buzzer, lit according to the lamp.
        var lamp = _input.Lamp(index);
        bool down = _input.IsDown(index, BuzzButton.Red);
        var buzzer = h.Buzzer;
        if (down) buzzer.Inflate(-4, -4);

        var baseRed = HandsetLayout.ColourOf(BuzzButton.Red);
        var top = lamp.IsLit ? Color.FromArgb(255, 120, 110) : Color.FromArgb(150, 26, 24);
        var bottom = lamp.IsLit ? Color.FromArgb(228, 40, 34) : Color.FromArgb(96, 18, 16);

        if (lamp.IsLit)
        {
            using var glow = new SolidBrush(Color.FromArgb(60, 255, 80, 70));
            var halo = buzzer; halo.Inflate(14, 14);
            g.FillEllipse(glow, halo);
        }

        using (var grad = new LinearGradientBrush(buzzer, top, bottom, LinearGradientMode.Vertical))
            g.FillEllipse(grad, buzzer);
        using (var rim = new Pen(Color.FromArgb(lamp.IsLit ? 200 : 90, 255, 160, 150), 2f))
            g.DrawEllipse(rim, buzzer);

        using (var brush = new SolidBrush(Color.FromArgb(210, 255, 255, 255)))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString("BUZZ", title, brush, buzzer, sf);

        // Four answer buttons.
        for (int b = 0; b < BuzzButtonExtensions.AnswerButtons.Length; b++)
        {
            var button = BuzzButtonExtensions.AnswerButtons[b];
            var rect = h.AnswerButton(b);
            bool held = _input.IsDown(index, button);
            if (held) rect.Inflate(-3, -3);

            var colour = HandsetLayout.ColourOf(button);
            using var path = HandsetLayout.RoundedRect(rect, 9);
            using (var fill = new SolidBrush(held ? ControlPaint.Light(colour, 0.4f) : colour))
                g.FillPath(fill, path);
            using (var edge = new Pen(Color.FromArgb(120, 0, 0, 0), 1.2f))
                g.DrawPath(edge, path);
        }

        using var label = new SolidBrush(Color.FromArgb(180, 200, 210, 225));
        using var sfc = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString($"Player {index + 1}   ({lamp.Mode})", small, label,
                     new RectangleF(h.Bounds.X, h.Bounds.Bottom - 26, h.Bounds.Width, 20), sfc);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _input.Dispose(); }
        base.Dispose(disposing);
    }
}

