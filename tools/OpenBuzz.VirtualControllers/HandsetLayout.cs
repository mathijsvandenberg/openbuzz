using System.Drawing.Drawing2D;
using OpenBuzz.Input;

namespace OpenBuzz.VirtualControllers;

/// Geometry and colours for one drawn handset, and hit-testing against it.
public sealed class HandsetLayout(Rectangle bounds)
{
    public const int Width = 168;
    public const int Height = 400;

    public Rectangle Bounds { get; } = bounds;

    public Rectangle Buzzer => new(Bounds.X + (Width - 118) / 2, Bounds.Y + 24, 118, 118);

    public Rectangle AnswerButton(int index) =>
        new(Bounds.X + (Width - 122) / 2, Bounds.Y + 178 + index * 52, 122, 40);

    public static Color ColourOf(BuzzButton b) => b switch
    {
        BuzzButton.Red => Color.FromArgb(214, 32, 28),
        BuzzButton.Blue => Color.FromArgb(38, 110, 224),
        BuzzButton.Orange => Color.FromArgb(240, 138, 30),
        BuzzButton.Green => Color.FromArgb(48, 178, 74),
        _ => Color.FromArgb(238, 206, 42),
    };

    /// Returns the button under the point, or null.
    public BuzzButton? HitTest(Point p)
    {
        var buzzer = Buzzer;
        // Ellipse test, so the corners of the buzzer's bounding box do not count.
        double dx = (p.X - (buzzer.X + buzzer.Width / 2.0)) / (buzzer.Width / 2.0);
        double dy = (p.Y - (buzzer.Y + buzzer.Height / 2.0)) / (buzzer.Height / 2.0);
        if (dx * dx + dy * dy <= 1.0) return BuzzButton.Red;

        for (int i = 0; i < BuzzButtonExtensions.AnswerButtons.Length; i++)
            if (AnswerButton(i).Contains(p))
                return BuzzButtonExtensions.AnswerButtons[i];

        return null;
    }

    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
