using System.Drawing.Drawing2D;

namespace OpenBuzz.TexBrowser;

/// <summary>
/// Browses the decoded textures: the standalone `.tex` set and the 445 embedded
/// in the `.rp2` model streams. Single view or contact sheet, with the alpha
/// channel shown against a checkerboard so cut-outs are visible.
/// </summary>
public sealed class BrowserForm : Form
{
    private const int SheetCell = 132;
    private const int SheetPad = 10;

    private readonly List<TextureEntry> all;
    private List<TextureEntry> shown;

    private readonly TextBox filter = new() { Dock = DockStyle.Top, PlaceholderText = "filter..." };
    private readonly ListBox list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Canvas canvas = new() { Dock = DockStyle.Fill };
    private readonly StatusStrip status = new();
    private readonly ToolStripStatusLabel info = new();

    private readonly ToolStrip bar = new();
    private readonly ToolStripButton sheetButton = new("Contact sheet") { CheckOnClick = true };
    private readonly ToolStripButton alphaButton = new("Alpha") { CheckOnClick = true, Checked = true };
    private readonly ToolStripComboBox zoom = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };

    /// Where each thumbnail landed, so the sheet can be clicked.
    private readonly List<(Rectangle Cell, TextureEntry Entry)> sheetHits = [];

    public BrowserForm(List<TextureEntry> entries, string extractDir)
    {
        all = entries;
        shown = entries;

        Text = "OpenBuzz Texture Browser";
        ClientSize = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;

        zoom.Items.AddRange(["Fit", "1x", "2x", "4x"]);
        zoom.SelectedIndex = 0;

        bar.Items.AddRange([sheetButton, alphaButton, new ToolStripSeparator(), new ToolStripLabel("Zoom"), zoom]);
        bar.GripStyle = ToolStripGripStyle.Hidden;

        info.Text = $"{all.Count} textures from {extractDir}";
        status.Items.Add(info);

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(list);
        left.Controls.Add(filter);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 340, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(canvas);

        Controls.Add(split);
        Controls.Add(bar);
        Controls.Add(status);

        filter.TextChanged += (_, _) => ApplyFilter();
        list.SelectedIndexChanged += (_, _) => { canvas.AutoScrollPosition = Point.Empty; Refresh2(); };
        canvas.Paint += Paint2;
        canvas.MouseClick += SheetClick;
        sheetButton.CheckedChanged += (_, _) => Refresh2();
        alphaButton.CheckedChanged += (_, _) => canvas.Invalidate();
        zoom.SelectedIndexChanged += (_, _) => Refresh2();

        ApplyFilter();
    }

    private TextureEntry? Current => list.SelectedItem as TextureEntry;

    private void ApplyFilter()
    {
        var text = filter.Text.Trim();
        shown = text.Length == 0
            ? all
            : all.Where(e => e.Label.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();

        list.BeginUpdate();
        list.Items.Clear();
        list.Items.AddRange(shown.ToArray());
        if (shown.Count > 0) list.SelectedIndex = 0;
        list.EndUpdate();

        Refresh2();
    }

    /// Recomputes the scroll extent for the current mode, then repaints.
    private void Refresh2()
    {
        if (sheetButton.Checked)
        {
            int columns = Math.Max(1, (canvas.ClientSize.Width - SheetPad) / (SheetCell + SheetPad));
            int rows = (shown.Count + columns - 1) / columns;
            canvas.AutoScrollMinSize = new Size(0, rows * (SheetCell + SheetPad) + SheetPad);
        }
        else
        {
            canvas.AutoScrollMinSize = Size.Empty;
        }

        canvas.Invalidate();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (sheetButton.Checked)
        {
            info.Text = $"{shown.Count} of {all.Count} textures";
            return;
        }

        info.Text = Current is { } e
            ? $"{e.Source}  |  {e.Name}  |  {e.Image.Width} x {e.Image.Height}  |  {shown.Count} of {all.Count}"
            : $"{shown.Count} of {all.Count} textures";
    }

    private void Paint2(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TranslateTransform(canvas.AutoScrollPosition.X, canvas.AutoScrollPosition.Y);

        if (sheetButton.Checked) PaintSheet(g);
        else PaintSingle(g);
    }

    private void PaintSingle(System.Drawing.Graphics g)
    {
        if (Current is not { } entry) return;

        var image = entry.Image;
        float scale = zoom.SelectedIndex switch
        {
            1 => 1f,
            2 => 2f,
            3 => 4f,
            _ => Math.Min((canvas.ClientSize.Width - 40f) / image.Width,
                          (canvas.ClientSize.Height - 40f) / image.Height),
        };
        scale = Math.Max(scale, 0.05f);

        int w = (int)(image.Width * scale), h = (int)(image.Height * scale);
        var rect = new Rectangle((canvas.ClientSize.Width - w) / 2, (canvas.ClientSize.Height - h) / 2, w, h);

        DrawBacking(g, rect);
        g.DrawImage(image, rect);
        g.DrawRectangle(Pens.DimGray, rect);
    }

    private void PaintSheet(System.Drawing.Graphics g)
    {
        sheetHits.Clear();

        int columns = Math.Max(1, (canvas.ClientSize.Width - SheetPad) / (SheetCell + SheetPad));
        var clip = g.ClipBounds;

        for (int i = 0; i < shown.Count; i++)
        {
            int cx = SheetPad + i % columns * (SheetCell + SheetPad);
            int cy = SheetPad + i / columns * (SheetCell + SheetPad);
            var cell = new Rectangle(cx, cy, SheetCell, SheetCell);
            sheetHits.Add((cell, shown[i]));

            // Only decode what is actually on screen; 445 textures at once is
            // far more than the window ever shows.
            if (cy + SheetCell < clip.Top || cy > clip.Bottom) continue;

            var image = shown[i].Image;
            float scale = Math.Min((float)SheetCell / image.Width, (float)SheetCell / image.Height);
            int w = Math.Max(1, (int)(image.Width * scale)), h = Math.Max(1, (int)(image.Height * scale));
            var rect = new Rectangle(cx + (SheetCell - w) / 2, cy + (SheetCell - h) / 2, w, h);

            DrawBacking(g, rect);
            g.DrawImage(image, rect);

            if (ReferenceEquals(shown[i], Current))
                g.DrawRectangle(Pens.Gold, cell);
        }
    }

    /// A checkerboard where alpha is shown, flat grey where it is ignored.
    private void DrawBacking(System.Drawing.Graphics g, Rectangle rect)
    {
        if (!alphaButton.Checked)
        {
            g.FillRectangle(Brushes.Black, rect);
            return;
        }

        const int square = 8;
        var old = g.Clip;
        g.SetClip(rect, CombineMode.Intersect);

        g.FillRectangle(Brushes.Gainsboro, rect);
        using var dark = new SolidBrush(Color.FromArgb(170, 170, 170));
        for (int y = rect.Top; y < rect.Bottom; y += square)
            for (int x = rect.Left; x < rect.Right; x += square)
                if ((x - rect.Left) / square % 2 == (y - rect.Top) / square % 2)
                    g.FillRectangle(dark, x, y, square, square);

        g.Clip = old;
    }

    private void SheetClick(object? sender, MouseEventArgs e)
    {
        if (!sheetButton.Checked) return;

        var point = new Point(e.X - canvas.AutoScrollPosition.X, e.Y - canvas.AutoScrollPosition.Y);
        foreach (var (cell, entry) in sheetHits)
        {
            if (!cell.Contains(point)) continue;
            list.SelectedItem = entry;
            if (e.Clicks > 1) sheetButton.Checked = false;
            canvas.Invalidate();
            UpdateStatus();
            return;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (canvas.IsHandleCreated) Refresh2();
    }
}
