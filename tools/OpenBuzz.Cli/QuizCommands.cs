using System.Text;
using OpenBuzz.Quiz;

namespace OpenBuzz.Cli;

public static class QuizCommands
{
    public static int Stats(string extracted, string locale)
    {
        var bank = QuizBank.Load(extracted, locale);

        Console.WriteLine($"Locale {bank.Locale}: quid.str holds {bank.Strings.Count} strings");
        Console.WriteLine();
        Console.WriteLine($"{"pool",-14} {"questions",10} {"id range",14} {"songs",7} {"distinct Q text",16}");

        foreach (var (name, pool) in bank.Pools.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var q = pool.Questions;
            Console.WriteLine($"{name,-14} {q.Count,10} " +
                              $"{$"{q.Min(x => x.Id)}..{q.Max(x => x.Id)}",14} " +
                              $"{q.Select(x => x.SongId).Distinct().Count(),7} " +
                              $"{q.Select(x => x.QuestionTextId).Distinct().Count(),16}");
        }

        Console.WriteLine();
        var problems = bank.Validate();
        if (problems.Count == 0)
            Console.WriteLine("Validation: every string reference resolves, every pool is a subset of qall.");
        else
            foreach (var p in problems) Console.WriteLine($"  PROBLEM  {p}");

        return problems.Count == 0 ? 0 : 2;
    }

    /// <summary>
    /// Writes the resolved bank to a file rather than the console: it is game
    /// content, and it belongs next to the other extracted data, not in a log.
    /// </summary>
    public static int Dump(string extracted, string locale, string pool, string outPath)
    {
        var bank = QuizBank.Load(extracted, locale);
        if (!bank.Pools.ContainsKey(pool))
        {
            Console.Error.WriteLine($"No pool '{pool}'. Available: {string.Join(", ", bank.Pools.Keys.Order())}");
            return 1;
        }

        var sb = new StringBuilder();
        int n = 0;
        foreach (var q in bank.Resolve(pool))
        {
            sb.AppendLine($"#{q.Id}  song {q.SongId}");
            sb.AppendLine($"  Q: {q.Question}");
            for (int i = 0; i < q.Options.Count; i++)
                sb.AppendLine($"  {(i == 0 ? "*" : " ")} {q.Options[i]}");
            sb.AppendLine();
            n++;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
        Console.WriteLine($"Wrote {n} questions from '{pool}' to {outPath}  (* marks the correct answer)");
        return 0;
    }
}
