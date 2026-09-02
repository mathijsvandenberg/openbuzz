# Running the disc's own scripts

The port embeds a Lua 5.0 interpreter and runs the game's compiled chunks
rather than reimplementing what they say. The scripts are the design: round
order, point tiers, wait timings and reveal rates are all stated there exactly,
and every time this port hand-wrote a round, one of them had to be guessed.

    obz run --chunk PointsBuilderRound --trace trace.txt

## What makes it a 5.0 VM

Two deviations, both already settled in `LuaOpcodes` and both fatal if missed:
the iABC field order is `OP C B A`, and an RK operand counts as a constant
above **250**, not the 256 released Lua 5.0 uses. A stock 5.1 VM decodes this
bytecode into nonsense, and a 5.0 VM using 256 reads every constant six slots
off.

Anything unimplemented throws rather than being skipped. A quiz that runs while
silently dropping an opcode is worse than one that stops and names it.

## Coroutines are not optional

Every round is one. `startScript` builds the round body with
`coroutine.create` and stores it as `questionScript`; the round start script
drives it, and every wait inside a round is a yield. Without coroutines a round
cannot get past its first line.

A tree-walking interpreter cannot suspend a C# call stack, so each coroutine
gets a thread and the two hand control back through a pair of semaphores. Only
one ever runs at a time, which is Lua's own rule, so the threads buy suspension
without buying concurrency.

## Stubs, and the trace

Natives are registered on first use as stubs that record the call and return
nil, so a round runs without the engine existing yet. What comes back is the
implementation order:

    IlluminateAllViewports()
    HideAllViewports(0)
    WaitUntilNoOneIsSpeaking()
    OpenIteration()
    AsyncRequestSongAudioIntoSlot1()
    WaitForAudioLoad(nil)
    ActivateHostessQuestionRevealBehaviour()
    FadeInQuestionText()
    TeletypeInAnswers()
    SetScreenCameraAngle()
    PopUpAllViewports(0.1)
    StartAudioWithTickingAndFadeAtEnd(1, nil)
    AllowAllContestantsToAnswer(15)
    ShowCountdownTimer()
    WaitForJudgementEndDisplayingAnsweredContestantViewports()
    AddAnswerIconsAtBottomRightForAllContestants(3, 3)
    HighlightCorrectAnswer()
    WaitSeconds(1.45)
    ShowAnimatedCrossesOnViewportsThatAnsweredIncorrectlyAndSlide()
    WaitSeconds(0.25)
    ShowAnimatedTicksOnViewportsThatAnsweredCorrectly()

Points Builder reaches 74 distinct natives. Fastest Finger reaches the same 74,
Snap 40, Buzz Stop 18, Pass The Bomb 15 - so one implementation pass covers
most of the game.

`--preload GenericData` runs the data script first, which is why the numbers
above are real: `StartAudioWithTickingAndFadeAtEnd(1, ...)` is
`NaturalFadeAudioTimeSeconds`, and `AddAnswerIconsAtBottomRightForAllContestants
(3, 3)` is `ViewportAnswerIconRightOffset` and `Bottom`. Without it every value
global becomes a function stub the moment a script reads it.

Three natives are real rather than stubbed, because the data scripts use their
results to build the tables everything else reads: `AllowGlobalVariables`,
`DisallowGlobalVariables` and `TableCopy`.

## What the trace already corrected

`TeletypeInAnswers` is called by **Points Builder**, not only by Flitsronde.
The letter-at-a-time reveal is how every round puts its answers up; Flitsronde's
distinction is elsewhere. The port currently does the reveal for Flitsronde
alone, which is wrong.

## Where this lives

`tools/OpenBuzz.Cli/Lua/` for now, so the CLI can drive it. It moves to a
library when the engine embeds it, which is the point of building it.

## Turning stubs into behaviour

`LuaHost` holds the natives that have to *answer* rather than record. Most of
the 688 are things the engine does - show a viewport, play a sample - and a
recording stub is enough to walk a script. The ones that get asked a question
stop the script dead if they return nil, so those move into `LuaHost` first,
and the trace says which one is next: run, see where it stops, implement, run
again.

That loop took the multiplayer character select from four calls to the whole
setup:

    SetupQuadrantViewportForCharacterStage(1..3)
    ForceCharacterCarouselJumpToIndex(seat, 1)
    ShowCharacterCarousel(seat)
    SetButtonAutoRepeatOn(seat, "BlueTriangleButton")
    SetButtonAutoRepeatOn(seat, "YellowSquareButton")
    SetCharacterSelectCameraAngle()
    SetCurrentScene2d("SCENE2D_MAIN")
    SetCurrentRoundID(2)
    ShowCharacterSelectViewports()
    GetPixelWidthOfFirstCharacterInString("A", "GeneralSmall", 1.2)
    TurnOnLogicalDeviceLightSupport()

Several things fall out of that which were guesses before. The carousel is
scrolled with the blue triangle and yellow square buttons. `SetCurrentRoundID(2)`
matches `CharacterSelectID = 2` in the layout table. The name entry measures
each letter through `GetPixelWidthOfFirstCharacterInString`. And the buzzer
lamps are turned on by the script, not by the engine.
