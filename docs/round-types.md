# Round types

The rules are read out of the game's own scripts rather than remembered. Each
round has a `<Name>Round.luaasm` describing its loop and a
`<Name>RoundStart.luaasm` that sets it up, and `GenericData.luaasm` holds the
per-round parameter table.

## Point Builder - "Punten verdienen"

`PointsBuilderRound.luaasm`, in order:

```
PlayCommentContextQuestionTransition
AsyncRequestSongAudioIntoSlot1        load the clip
FadeInQuestionText / TeletypeInAnswers
PopUpAllViewports
StartAudioWithTickingAndFadeAtEnd     clip plays, ticking under it
StartVUMeter
AllowAllContestantsToAnswer           <- everyone answers, nobody buzzes in
ShowCountdownTimer  15
WaitForJudgementEndDisplayingAnsweredContestantViewports
HighlightCorrectAnswer
ShowAnimatedCrossesOnViewportsThatAnsweredIncorrectlyAndSlide
ShowAnimatedTicksOnViewportsThatAnsweredCorrectly
ShowAllContestantPointsAwarded / UpdateAllPodiumScores
```

Two things this settles, that guessing would not:

- **Nobody buzzes in.** `AllowAllContestantsToAnswer` means all four answer the
  same question at once, under a 15-second timer.
- **The award is flat.** `GenericData.luaasm` sets `PointsReduceWithTime` only
  for `SpeedTimeBuilderRoundID`, and `SinglePlayerRound` only for the two Time
  Builder rounds. Point Builder gets neither, so no speed bonus and everyone
  plays every question.

`RoundParameters[PointsBuilderRoundID].TopIconName` is `InputAnswers`, which is
the icon the viewport shows - answer buttons, not a buzzer - and agrees.

**Not recovered:** the size of the award. It is handed out by the engine rather
than by Lua, so `POINTS` in `Round.gd` is a stand-in.

The on-screen strings come from the text map: `RulesPointsBuilderTitle` is
"PUNTEN VERDIENEN" and `RulesPointsBuilderLine1` is the rule line, drawn in
`RoundInstructionsLarge` and `RoundInstructionsSmall` - the styles the A2D
bindings name for them.

## All ten

Every entry below is corroborated three ways: the input call in
`<Name>Round.luaasm`, the parameters in `GenericData.luaasm`, and the game's own
`Rules<Name>*` strings shown on the round intro.

| Round (game's own title) | Script | Input call | Parameters |
|---|---|---|---|
| PUNTEN VERDIENEN | `PointsBuilder` | AllowAllContestantsToAnswer | InputAnswers |
| WIE IS HET SNELST | `FastestFingerFirst` | AllowAllContestantsToAnswer | InputAnswers |
| FLITSRONDE | `LookBeforeYouLeap` | AllowSingleContestantToBuzzIn, then AllowActiveContestantToAnswer | InputBuzzer then InputAnswers |
| LEG HET VERBAND | `Snap` | AllowAllContestantsToAnswer | InputBuzzer |
| HAND AAN DE KNOP | `PointStealer` | AllowAllContestantsToAnswer | InputBuzzer |
| WAAR STOPT DE ZOEMER | `BuzzStop` | AllowActiveContestantToAnswer, GetContestantBuzzerPresses | InputAnswers |
| AFSCHUIVEN | `OffLoader` | AllowActiveContestantToAnswer | InputAnswers twice |
| WIE HEEFT DE BOM | `PassTheBomb` | AllowActiveContestantToAnswer | InputAnswers |
| TIJD VERDIENEN | `TimeBuilder` | AllowAllContestantsToAnswer | SinglePlayerRound |
| OP DE STOEL | `HotSeat` | AllowActiveContestantToAnswer | InputAnswers, InputBuzzer |

`SpeedTimeBuilder` is the one round that sets `PointsReduceWithTime`, so it is
the only one with a genuine speed curve in the parameters; `TimeBuilder` and
`SpeedTimeBuilder` are the two that set `SinglePlayerRound`.

Note the internal names and the shown titles disagree: `PointStealer` is
"HAND AAN DE KNOP" (Trigger Finger) and `LookBeforeYouLeap` is "FLITSRONDE"
(Quickfire). The `RoundNameText` parameter points at a `RoundName*` key for each,
though none of those keys resolve yet - the `Rules*Title` keys do, and those are
what the port shows.

## Where the port approximates

Structure and input model come from the game. These do not:

- **Point values.** Awarded by the engine, not by Lua, so a flat 1000 stands in
  for most rounds. Fastest Finger is the exception: its intro screen prints its
  own table - 1E GOEDE ANTWOORD +400 PTN down to 4E +100, and FOUTE ANTWOORDEN
  0 PTN - so those four tiers are read off the game, not invented, and the port
  draws the same table.
- **Snap and Trigger Finger content.** The game scrolls its own statements and
  answers on a timer; the port cycles the question's four options instead.
- **The bomb fuse.** Random between 20 and 40 seconds here.
- **The Flitsronde reveal rate.** The question and all four answers arrive a
  letter at a time, every line cut at the same character count, which is what
  the reference shots show. How fast is a guess: here the last letter lands
  about two fifths into the clock.
- **Hot Seat's clock.** Starts at 60 seconds unless a Time Builder round has
  banked some, because rounds do not yet chain into a session.

Each of these is named on screen in the round's own panel, so nothing passes as
recovered when it is not.

## A game

The Round tab's first three entries play a game rather than a single round:
short (3 rounds), medium (5) and long (7) - the three lengths the game's own
length menu offers. Scores and banked time carry across the whole game, and each
round runs four questions before handing on.

Two things about the order come from the game:

- **Hot Seat is the finale.** It is the only round with its own
  `HotSeatRoundEnd` script.
- **Time Builder feeds it.** Its own rule line says the time won is for
  "de laatste ronde" - the last round - so it is placed directly before.

The middle of the game is shuffled, because the order is not recoverable. The
length menu sets `NameOfGameToPlay` to `ShortMultiplayerGame` and friends, and no
script by those names is on the disc, so the sequence lives in the executable.
The rounds-per-game counts and the four questions per round are choices too.

## Playing them

`obz-viewer.exe`, Round tab; the list on the left switches type. `--round <id>`
picks one at startup and `--demo` plays it hands-free:

```bash
obz-viewer.exe -- --tab 2 --round buzz_stop --demo   # one round
obz-viewer.exe -- --tab 2 --game short --demo        # a whole game
```
