using OpenBuzz.Animation;

namespace OpenBuzz.A2dPlayer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var dir = Opt(args, "--in") ?? A2dScene.FindExportDirectory(AppContext.BaseDirectory);
        if (dir is null || !Directory.Exists(dir))
        {
            MessageBox.Show(
                "Could not find extracted/a2d.\n\nRun 'obz a2d export' first, or pass --in <dir>.",
                "OpenBuzz A2D Player", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var scenes = A2dScene.LoadAll(dir);
        if (scenes.Count == 0)
        {
            MessageBox.Show($"No scenes loaded from {dir}.", "OpenBuzz A2D Player",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.Run(new PlayerForm(scenes, dir));
    }

    private static string? Opt(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
