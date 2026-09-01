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

## The rest

Not started. Each has its script pair to read the same way:

| Round | Script |
|---|---|
| Fastest Finger | `FastestFingerRound` |
| Point Stealer | `PointStealerRound` |
| Time Builder | `TimeBuilderRound` |
| Speed Time Builder | `SpeedTimeBuilderRound` |
| Pass the Bomb | `PassTheBomb*` |
| Look Before You Leap | round id in `GenericData` |

`SpeedTimeBuilder` sets `PointsReduceWithTime`, so that one does have a speed
curve; the two Time Builder rounds set `SinglePlayerRound`.
