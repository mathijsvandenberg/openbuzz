using OpenBuzz.Input;
using OpenBuzz.Quiz;

namespace OpenBuzz.Round;

public enum RoundPhase
{
    Idle,        // waiting to start the next question
    Listening,   // clip playing, all buzzers armed
    Answering,   // someone buzzed in; they must pick a colour
    Revealing,   // result on screen
    Finished,
}

/// One option as presented on screen, after shuffling.
public readonly record struct PresentedOption(string Text, bool IsCorrect);

/// <summary>
/// A Points Builder style round: play a clip, first buzz wins the right to
/// answer, correct answers score. Deliberately small - the point is to exercise
/// questions, audio, input and scoring together, not to reproduce the show.
/// </summary>
public sealed class RoundGame
{
    public const int PointsForCorrect = 1000;
    public const int QuestionsPerRound = 5;

    private static readonly TimeSpan AnswerLimit = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan RevealTime = TimeSpan.FromSeconds(3.5);

    private readonly QuizBank _bank;
    private readonly SongTable _songs;
    private readonly ClipPlayer _player;
    private readonly IBuzzInputSource _input;
    private readonly string _soundDir;
    private readonly int _sampleRate;
    private readonly Random _rng = new();
    private readonly List<QuestionRecord> _pool;

    private TimeSpan _phaseElapsed;

    public RoundGame(QuizBank bank, SongTable songs, ClipPlayer player, IBuzzInputSource input,
                     string soundDir, int sampleRate, string poolName)
    {
        _bank = bank;
        _songs = songs;
        _player = player;
        _input = input;
        _soundDir = soundDir;
        _sampleRate = sampleRate;

        // Only questions whose song has a clip we can actually find.
        _pool = [.. bank.Pools[poolName].Questions.Where(q => ClipPathFor(q) is not null)];
        Scores = new int[input.ControllerCount];
    }

    public RoundPhase Phase { get; private set; } = RoundPhase.Idle;
    public int[] Scores { get; }
    public int QuestionNumber { get; private set; }
    public string QuestionText { get; private set; } = "";
    public IReadOnlyList<PresentedOption> Options { get; private set; } = [];
    public int? BuzzedPlayer { get; private set; }
    public int? ChosenOption { get; private set; }
    public bool LastAnswerCorrect { get; private set; }
    public string Status { get; private set; } = "Press any buzzer to start";
    public string? CurrentClip => _player.CurrentClip;
    public int PoolSize => _pool.Count;

    private string? ClipPathFor(QuestionRecord q)
    {
        if (!_songs.TryGet(q.SongId, out var song)) return null;
        var path = Path.Combine(_soundDir, song.Clip + ".vgp");
        return File.Exists(path) ? path : null;
    }

    public void Update(TimeSpan elapsed)
    {
        _phaseElapsed += elapsed;

        switch (Phase)
        {
            case RoundPhase.Idle:
                if (AnyRedPressed() is not null) StartQuestion();
                break;

            case RoundPhase.Listening:
                if (AnyRedPressed() is { } player) BuzzIn(player);
                break;

            case RoundPhase.Answering:
                HandleAnswerChoice();
                if (Phase == RoundPhase.Answering && _phaseElapsed > AnswerLimit)
                    Reveal(correct: false, "Too slow!");
                break;

            case RoundPhase.Revealing:
                if (_phaseElapsed > RevealTime)
                {
                    if (QuestionNumber >= QuestionsPerRound) Finish();
                    else StartQuestion();
                }
                break;
        }
    }

    private int? AnyRedPressed()
    {
        for (int i = 0; i < _input.ControllerCount; i++)
            if (_input.WasPressed(i, BuzzButton.Red))
                return i;
        return null;
    }

    private void StartQuestion()
    {
        var record = _pool[_rng.Next(_pool.Count)];
        var resolved = _bank.Resolve(record);

        QuestionNumber++;
        QuestionText = resolved.Question;

        // Correct answer is stored first, so shuffle for display - the same
        // thing the engine does via GetRandomisedIndex.
        var options = resolved.Options
            .Select((text, i) => new PresentedOption(text, i == 0))
            .ToArray();
        _rng.Shuffle(options);
        Options = options;

        BuzzedPlayer = null;
        ChosenOption = null;

        if (ClipPathFor(record) is { } clip) _player.Play(clip, _sampleRate);

        Status = "Listen - hit your buzzer when you know it";
        for (int i = 0; i < _input.ControllerCount; i++) _input.Lamp(i).Flash(TimeSpan.FromMilliseconds(320));

        SetPhase(RoundPhase.Listening);
    }

    private void BuzzIn(int player)
    {
        _player.Stop();
        BuzzedPlayer = player;

        for (int i = 0; i < _input.ControllerCount; i++)
        {
            if (i == player) _input.Lamp(i).On();
            else _input.Lamp(i).Off();
        }

        Status = $"Player {player + 1} buzzed - pick a colour";
        SetPhase(RoundPhase.Answering);
    }

    private void HandleAnswerChoice()
    {
        if (BuzzedPlayer is not { } player) return;

        for (int i = 0; i < BuzzButtonExtensions.AnswerButtons.Length; i++)
        {
            if (!_input.WasPressed(player, BuzzButtonExtensions.AnswerButtons[i])) continue;
            if (i >= Options.Count) return;

            ChosenOption = i;
            bool correct = Options[i].IsCorrect;
            if (correct) Scores[player] += PointsForCorrect;
            Reveal(correct, correct ? $"Player {player + 1} is right! +{PointsForCorrect}" : $"Player {player + 1} is wrong");
            return;
        }
    }

    private void Reveal(bool correct, string message)
    {
        LastAnswerCorrect = correct;
        Status = message;
        _player.Stop();
        for (int i = 0; i < _input.ControllerCount; i++) _input.Lamp(i).Off();
        SetPhase(RoundPhase.Revealing);
    }

    private void Finish()
    {
        int best = Array.IndexOf(Scores, Scores.Max());
        Status = $"Round over - Player {best + 1} wins with {Scores[best]}";
        _player.Stop();
        SetPhase(RoundPhase.Finished);
    }

    public void Restart()
    {
        Array.Clear(Scores);
        QuestionNumber = 0;
        Status = "Press any buzzer to start";
        SetPhase(RoundPhase.Idle);
    }

    private void SetPhase(RoundPhase phase)
    {
        Phase = phase;
        _phaseElapsed = TimeSpan.Zero;
    }
}
