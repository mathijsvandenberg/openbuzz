namespace OpenBuzz.TexBrowser;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var dir = Opt(args, "--in") ?? TextureEntry.FindExtractDirectory(AppContext.BaseDirectory);
        if (dir is null || !Directory.Exists(dir))
        {
            MessageBox.Show(
                "Could not find the 'extracted' folder.\n\nRun 'obz tex decode' and 'obz rw textures' first, or pass --in <dir>.",
                "OpenBuzz Texture Browser", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var entries = TextureEntry.Discover(dir);
        if (entries.Count == 0)
        {
            MessageBox.Show(
                $"No decoded textures under {dir}.\n\nRun 'obz tex decode' and 'obz rw textures' first.",
                "OpenBuzz Texture Browser", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.Run(new BrowserForm(entries, dir, Opt(args, "--filter"), args.Contains("--sheet")));
    }

    private static string? Opt(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
