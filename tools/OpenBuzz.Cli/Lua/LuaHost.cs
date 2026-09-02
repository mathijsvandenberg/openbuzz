namespace OpenBuzz.Cli.Lua;

/// <summary>
/// The natives that have to answer rather than record.
///
/// Most of the 688 are things the engine *does* - show a viewport, play a
/// sample - and a stub that records the call and returns nil is enough to walk
/// a script. The ones here are things the engine is *asked*, where returning
/// nil stops the script dead: how many contestants there are, whether this is
/// a multiplayer game, what a contestant chose.
///
/// This is the list that grows as the port becomes real. Each entry is a
/// native moved out of the trace and into behaviour, and the trace is what
/// says which one to do next.
/// </summary>
public sealed class LuaHost
{
    /// How many handsets are in play. Party mode is two to four.
    public int Players { get; set; } = 4;

    public bool MultiPlayer => Players > 1;

    /// Which character each seat picked, by roster index, or -1 for undecided.
    public int[] Chosen { get; } = [-1, -1, -1, -1];

    /// Which seat each handset claimed on the "kies een plaats" screen. Not
    /// hardwired: a handset takes whichever seat its player presses.
    public int[] SeatOfHandset { get; } = [-1, -1, -1, -1];

    /// The screen-text table, keyed the way the scripts key it. Only 29 of the
    /// 81 keys resolve, so an unknown key comes back as the key itself rather
    /// than as an empty string - visible in a trace, and visible on screen.
    public Dictionary<string, string> Text { get; } = new(StringComparer.Ordinal);

    public void Install(LuaVm vm)
    {
        vm.Register("GetTextFromNamedString", a =>
        {
            var key = a.Length > 0 ? LuaValues.ToStringValue(a[0]) : "";
            return [Text.TryGetValue(key, out var v) ? v : key];
        });

        vm.Register("GetNumberOfActiveInputDevices", _ => [(double)Players]);

        // Nothing is remembered between runs yet, so every seat starts on the
        // first character rather than on whoever used it last.
        vm.Register("GetTheCharacterIndexLastUsedForThisSeat", _ => [(double)1]);

        // A seat's logical position is where its viewport sits on screen; the
        // physical one is which handset claimed it. They only differ once a
        // player has taken a seat out of order, which is the whole point of the
        // "kies een plaats" screen, so identity is the right starting state.
        vm.Register("GetLogicalSeatPosition", a => [Number(a, 0)]);
        vm.Register("GetPhysicalSeatPosition", a => [Number(a, 0)]);

        vm.Register("GetMaxNumberOfContestants", _ => [(double)4]);
        vm.Register("GetNumberOfContestants", _ => [(double)Players]);
        vm.Register("GameIsMultiPlayer", _ => [MultiPlayer]);
        vm.Register("GameIsSinglePlayer", _ => [!MultiPlayer]);

        // A contestant is "in" once their handset has claimed a seat.
        vm.Register("IsContestantPlaying", a =>
            [Seat(a) >= 0 && Seat(a) < Players]);

        vm.Register("GetContestantCharacter", a =>
        {
            int seat = Seat(a);
            return [seat >= 0 && seat < Chosen.Length ? (double)Chosen[seat] : -1.0];
        });

        // Nothing has been claimed or chosen yet when a trace starts, so the
        // select screen runs its "waiting for players" path, which is the one
        // worth seeing.
        vm.Register("HasContestantChosenCharacter", a =>
            [Seat(a) is var s && s >= 0 && s < Chosen.Length && Chosen[s] >= 0]);

        vm.Register("GetNumberOfCharacters", _ => [(double)Roster.Length]);
        vm.Register("GetCharacterName", a =>
        {
            int i = (int)Number(a, 0) - 1;
            return [i >= 0 && i < Roster.Length ? Roster[i] : ""];
        });
    }

    /// The sixteen contestants, in the order their costume models appear on the
    /// disc. Three costumes each, forty-eight models.
    public static readonly string[] Roster =
    [
        "Angie", "Ash", "Barley", "Bradley", "Cinnamon", "Gina", "Jean", "Keiko",
        "Mercy", "Pelvis", "Punk", "Razor", "Stevie", "Tina", "Walrus", "Winona",
    ];

    private static int Seat(object?[] a) => a.Length == 0 ? -1 : (int)Number(a, 0) - 1;

    private static double Number(object?[] a, int i) =>
        i < a.Length && a[i] is double d ? d : 0;
}
