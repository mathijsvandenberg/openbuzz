# Speech: which line, and when

Buzz and Rose speak 413 lines on the Dutch disc — 285 commentary (`C_<id>_<n>`)
and 138 fixed (`F_<id>_<n>`). `NETSpeechInfo` declares every one of them and the
clips agree exactly, with nothing declared that is missing and nothing present
that is undeclared.

That says *what exists*. It says nothing about *when it plays*.

## Where the mapping is not

Not in the executable. Searching `SLES_533.05` for all 413 ids as aligned
32-bit words turns up 34 scattered hits — 33 in `.text`, one in `.rodata` — and
no run of them anywhere. There is no id table to find.

## Where it is

In the scripts, which name the ids directly and hand them to the speech natives.
Every one of the twelve round introductions is the same call:

```
DoRoundIntroduction(speaker, round, round, a, b, c, announce, rules, 111000)
```

`RoundIntroduction`'s `main/6` then plays those three arguments on its own
clock:

| when | what |
|---|---|
| opened at instruction 100 | `OpenFixedSpeechIntoSpecificSlot("Host", announce, 1)` |
| timing marker 5 | `StartSpeechWhenLoaded` — the round announcement |
| timing marker 6 | light rings up |
| timing marker 8.4 | `OpenFixedSpeechIntoSpecificSlot("Host", 111000, 3)` — the shared line |
| timing marker 9.4 | buzzer lamps off, rings down |
| then | `DoAnimatedInstructions(speaker, …, rules, …)` |

and `DoAnimatedInstructions` walks the rules table in order — `R5[1]`, `R5[i]`,
`R5[table.getn(R5)]` — opening each into a slot and waiting for it to finish
before the next.

So a round's introduction is fully determined:

```
PassTheBomb   530000, 111000, 530100, 530110, 530120
BuzzStop      590000, 111000, 590100, 590110, 590120
SpeedTimeBuilder  600000, 111000, 600100, 600110, 600120
```

`111000` is shared by all twelve. The rest is one block per round.

## Computed ids

Per-contestant lines are not written out four times. The script does the
arithmetic:

```
[70] GetGlobal 7 30   ; R7 := CurrentlyPlayingContestantSeat
[71] Add       7 287 7 ; R7 := 530200 + R7
[72] Sub       7 7 288 ; R7 := R7 - 1
[74] Call      5 4 2   ; OpenFixedSpeechIntoSpecificSlot("Host", R7, 3)
```

The disc carries `F_530200` through `F_530203`, three variations each — four
lines, one per place on the stage, and the `3` is the variation count the call
asks for. Five such families are recovered:

| where | expression | resolves to |
|---|---|---|
| `PassTheBombRoundStart` | `530200 + CurrentlyPlayingContestantSeat - 1` | 530200–530203 |
| `PassTheBombRound` | `530300 + CurrentlyPlayingContestantSeat - 1` | 530300–530303 |
| `LookBeforeYouLeapRound` | `570200 + n - 1` | 570200–570203 |
| `PointStealerRound` | `540200 + n - 1` | 540200–540203 |
| `QuizSupportCode_RoundSharedCode` | `550400 + n - 1` | 550400–550403 |

Each resolves to exactly four, which is the number of places the stage has
whatever the headcount.

## Linking a cue sheet to a round

Two links, both drawn by the scripts themselves.

The round id its start script passes to `SetCurrentRoundID` settles nine of the
ten rounds. It cannot settle all ten, for two reasons found on the way:

- **Two cue sheets share an id.** `QuickfireQuiz` reuses `FastestFingerFirstID`
  and `QuizMaster` reuses `PointsBuilderRoundID`. The round logic name breaks
  the tie.
- **One round's id is not the one it plays under.** `GenericData` defines both
  `TimeBuilderRoundID` (16) and `SpeedTimeBuilderRoundID` (17). Nothing outside
  `GenericData` ever reads the first, yet the tuned parameters are filed under
  it, while `SpeedTimeBuilderRoundStart` sets the second and hands off to
  `TimeBuilderRound`. The follow-on script is what names the round being played.

With both, all ten rounds resolve, uniquely, each to its own block.

## What is still a bucket

The running commentary. The round scripts choose those through named comment
contexts opened by natives — `OpenCommentContext*` is defined by no script — so
the selection lives in the executable and has not been recovered. Until it is,
commentary is picked from the bucket and labelled as such, rather than pretending
to be the right line.

## Reproducing

```bash
dist/obz.exe speech cues
```

Reads the scripts and the decoded clips, prints the cue sheet, and writes
`extracted/godot2d/speech-cues.json`. Nothing is emitted for an id the disc
cannot actually speak.
