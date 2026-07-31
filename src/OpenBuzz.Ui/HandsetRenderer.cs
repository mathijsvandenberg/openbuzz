using System.Drawing.Drawing2D;
using OpenBuzz.Input;

namespace OpenBuzz.Ui;

/// Draws a handset. Shared so the standalone controller panel and the game
/// screen cannot drift apart.
public static class HandsetRenderer
{
    public static void Draw(Graphics g, HandsetLayout h, IBuzzInputSource input, int index,
                            string caption, Font captionFont, Font buzzerFont)
    {
        using (var body = HandsetLayout.RoundedRect(h.Bounds, 22))
        using (var fill = new SolidBrush(Color.FromArgb(38, 40, 48)))
        using (var edge = new Pen(Color.FromArgb(60, 64, 74), 1.5f))
        {
            g.FillPath(fill, body);
            g.DrawPath(edge, body);
        }

        DrawBuzzer(g, h, input, index, buzzerFont);

        for (int b = 0; b < BuzzButtonExtensions.AnswerButtons.Length; b++)
        {
            var button = BuzzButtonExtensions.AnswerButtons[b];
            var rect = h.AnswerButton(b);
            bool held = input.IsDown(index, button);
            if (held) rect.Inflate(-3, -3);

            var colour = HandsetLayout.ColourOf(button);
            using var path = HandsetLayout.RoundedRect(rect, 9);
            using (var fill = new SolidBrush(held ? ControlPaint.Light(colour, 0.4f) : colour))
                g.FillPath(fill, path);
            using (var edge = new Pen(Color.FromArgb(120, 0, 0, 0), 1.2f))
                g.DrawPath(edge, path);
        }

        using var label = new SolidBrush(Color.FromArgb(190, 205, 215, 230));
        using var sf = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(caption, captionFont, label,
                     new RectangleF(h.Bounds.X, h.Bounds.Bottom - 28, h.Bounds.Width, 22), sf);
    }

    private static void DrawBuzzer(Graphics g, HandsetLayout h, IBuzzInputSource input, int index, Font font)
    {
        var lamp = input.Lamp(index);
        var buzzer = h.Buzzer;
        if (input.IsDown(index, BuzzButton.Red)) buzzer.Inflate(-4, -4);

        if (lamp.IsLit)
        {
            using var glow = new SolidBrush(Color.FromArgb(60, 255, 80, 70));
            var halo = buzzer;
            halo.Inflate(14, 14);
            g.FillEllipse(glow, halo);
        }

        var top = lamp.IsLit ? Color.FromArgb(255, 120, 110) : Color.FromArgb(150, 26, 24);
        var bottom = lamp.IsLit ? Color.FromArgb(228, 40, 34) : Color.FromArgb(96, 18, 16);

        using (var grad = new LinearGradientBrush(buzzer, top, bottom, LinearGradientMode.Vertical))
            g.FillEllipse(grad, buzzer);
        using (var rim = new Pen(Color.FromArgb(lamp.IsLit ? 200 : 90, 255, 160, 150), 2f))
            g.DrawEllipse(rim, buzzer);

        using var text = new SolidBrush(Color.FromArgb(210, 255, 255, 255));
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("BUZZ", font, text, buzzer, sf);
    }
}
