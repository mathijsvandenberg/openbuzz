using OpenBuzz.Audio;
using OpenBuzz.Quiz;

namespace OpenBuzz.Round;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var extracted = Opt(args, "--in") ?? FindExtracted();
        var locale = Opt(args, "--locale") ?? "NET";
        var pool = Opt(args, "--pool") ?? "qtitle";
        int rate = int.TryParse(Opt(args, "--rate"), out var r) ? r : VgpFile.DefaultSampleRate;

        try
        {
            var bank = QuizBank.Load(extracted, locale);
            if (!bank.Pools.ContainsKey(pool))
                throw new ArgumentException($"No pool '{pool}'. Available: {string.Join(", ", bank.Pools.Keys.Order())}");

            var songs = SongTable.Load(Path.Combine(extracted, "BM1", "Rounds", locale, "rri.dat"));
            var soundDir = Path.Combine(extracted, "Sound");
            if (!Directory.Exists(soundDir))
                throw new DirectoryNotFoundException($"{soundDir} - run 'obz extract' first.");

            Application.Run(new RoundForm(bank, songs, soundDir, rate, pool));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{ex.Message}\n\nExtracted data expected under:\n{extracted}",
                            "OpenBuzz", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string? Opt(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string FindExtracted()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "extracted");
            if (Directory.Exists(candidate)) return candidate;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "extracted");
    }
}
