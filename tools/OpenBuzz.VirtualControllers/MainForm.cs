using System.Diagnostics;
using System.Drawing.Drawing2D;
using OpenBuzz.Input;
using OpenBuzz.Ui;

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

    /// Button currently held by the mouse, so a drag off the button releases it.
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

    /// Keyboard columns mirror the on-screen order: one group of keys per handset.
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

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var small = new Font("Segoe UI", 8.5f);
        using var mono = new Font("Consolas", 8.5f);
        using var buzzerFont = new Font("Segoe UI", 11f, FontStyle.Bold);

        g.DrawString($"Input source: {_input.Description}    " +
                     "keys 1-4 = buzzers, QWER / ASDF / ZXCV / UIOP = answers    " +
                     "F1-F4 cycle lamp, F5 flash all, F6 off",
                     small, Brushes.Gray, Gutter, 16);

        for (int i = 0; i < _handsets.Length; i++)
            HandsetRenderer.Draw(g, _handsets[i], _input, i,
                                 $"Player {i + 1}   ({_input.Lamp(i).Mode})", small, buzzerFont);

        int y = PanelTop + HandsetLayout.Height + 16;
        g.DrawString("Events", small, Brushes.Gray, Gutter, y);
        for (int i = 0; i < _log.Count; i++)
            g.DrawString(_log[i], mono, Brushes.DarkGray, Gutter, y + 18 + i * 15);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _input.Dispose(); }
        base.Dispose(disposing);
    }
}
