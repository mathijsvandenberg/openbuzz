namespace OpenBuzz.TexBrowser;

/// A flicker-free scrollable drawing surface. The form does the painting.
public sealed class Canvas : Panel
{
    public Canvas()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        AutoScroll = true;
        BackColor = Color.FromArgb(32, 32, 36);
    }
}
