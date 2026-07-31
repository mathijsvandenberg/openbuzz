namespace OpenBuzz.Quiz;

/// A question with its strings resolved.
public sealed record ResolvedQuestion(
    int Id,
    int SongId,
    string Question,
    string CorrectAnswer,
    IReadOnlyList<string> Options);

/// <summary>
/// The question bank for one locale.
///
/// `qall.rnd` is the master set of every question; the other pools are subsets
/// that reuse the same global question ids, so a port should treat qall as the
/// bank and the rest as round-type selections rather than loading each
/// independently.
/// </summary>
public sealed class QuizBank
{
    public required string Locale { get; init; }
    public required StringTable Strings { get; init; }
    public required IReadOnlyDictionary<string, QuestionPool> Pools { get; init; }

    public const string MasterPool = "qall";

    public static QuizBank Load(string extractedRoot, string locale = "NET")
    {
        var textDir = Path.Combine(extractedRoot, "BM1", "Text", locale);
        var roundDir = Path.Combine(extractedRoot, "BM1", "Rounds", locale);

        if (!Directory.Exists(textDir)) throw new DirectoryNotFoundException(textDir);
        if (!Directory.Exists(roundDir)) throw new DirectoryNotFoundException(roundDir);

        var strings = StringTable.Load(Path.Combine(textDir, "quid.str"));

        var pools = new Dictionary<string, QuestionPool>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(roundDir, "*.rnd"))
        {
            // qyearevent.rnd is present but empty on this disc.
            if (new FileInfo(path).Length == 0) continue;
            var pool = QuestionPool.Load(path);
            pools[pool.Name] = pool;
        }

        return new QuizBank { Locale = locale, Strings = strings, Pools = pools };
    }

    public ResolvedQuestion Resolve(QuestionRecord r) => new(
        r.Id,
        r.SongId,
        Strings.GetOrEmpty(r.QuestionTextId),
        Strings.GetOrEmpty(r.CorrectOption),
        [.. r.Options.Select(id => Strings.GetOrEmpty(id))]);

    public IEnumerable<ResolvedQuestion> Resolve(string poolName) =>
        Pools.TryGetValue(poolName, out var pool)
            ? pool.Questions.Select(Resolve)
            : [];

    /// <summary>
    /// Checks the bank is internally consistent: every string reference in
    /// range, and every pool's questions present in the master pool.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (!Pools.TryGetValue(MasterPool, out var master))
            return ["qall.rnd missing"];

        var masterIds = master.Questions.Select(q => q.Id).ToHashSet();

        foreach (var (name, pool) in Pools.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            int badRefs = 0, notInMaster = 0;
            foreach (var q in pool.Questions)
            {
                if (Strings.Get(q.QuestionTextId) is null) badRefs++;
                foreach (var o in q.Options)
                    if (Strings.Get(o) is null) badRefs++;
                if (name != MasterPool && !masterIds.Contains(q.Id)) notInMaster++;
            }
            if (badRefs > 0) problems.Add($"{name}: {badRefs} string references out of range");
            if (notInMaster > 0) problems.Add($"{name}: {notInMaster} questions absent from {MasterPool}");
        }

        return problems;
    }
}
