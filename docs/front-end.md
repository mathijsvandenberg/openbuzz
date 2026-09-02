# The front end

The game does not start at a round. It starts at a menu, and the whole path is
on the disc as scripts.

## The flow

```
MainMenu                     HOOFDMENU
  MenuPlayMultiplayer          SPEL VOOR MEERDERE SPELERS
  MenuPlaySingleplayer         SPEL VOOR EEN SPELER
  MenuOptions                  OPTIEMENU
  MenuExtras                   EXTRA'S
      |  ChoosePositions, ClearAndRestartMappingDevices
      v
MultiPlayerGameLengthMenu    SPELDUUR
  MultiplayerGameLengthShort   KORT SPEL        -> NameOfGameToPlay = ShortMultiplayerGame
  MultiplayerGameLengthMedium  MIDDELLANG SPEL  -> MediumMultiplayerGame
  MultiplayerGameLengthLong    LANG SPEL        -> LongMultiplayerGame
      v
GameTypeMenu                 MUZIEKGENRE
  MusicTypeOptionAll           ALLE MUZIEK      -> SetRoundHistoricalBiasNone
  MusicTypeOptionOlder         OUDE MUZIEK      -> SetRoundHistoricalBiasEarly
  MusicTypeOptionNewer         MODERNE MUZIEK   -> SetRoundHistoricalBiasLate
      v
CharacterSelectMultiBeta     the stage enums, in declaration order
  ENUM_BUZZTOSTART_STAGE       DRUK OP DE ZOEMER OM TE SPELEN
  ENUM_CHARTYPE_STAGE          KIES EEN PERSONAGE
  ENUM_MODEL_STAGE             KIES KLEDING
  ENUM_BUZZER_STAGE            KIES EEN ZOEMER
  ENUM_NAMEENTRY_STAGE         VOER JE NAAM IN
  ENUM_END_STAGE               -> PrepareForAndStartGame
```

The order is the scripts' own: each menu's last act is
`SetFollowOnScript` naming the next one.

## Buttons

From `CharacterSelectMultiBeta.ProcessButtonPushes`:

| button | what it does |
|---|---|
| `BlueTriangleButton` | `CycleIndexToPreviousElementInTable` |
| `YellowSquareButton` | `CycleIndexToNextElementInTable` |
| `Buzzer` | `AttemptToAcceptElement` |
| `GreenCrossButton` | `AttemptToReturnElement` |

A character or a buzzer somebody already holds cannot be taken: the script
checks `CanWeTakeThisFieldIndex` and greys the entry out.

## The music genre

`SetRoundHistoricalBiasNone`, `Early` and `Late` are natives. Their handlers at
`0x0018DCB0`, `0x0018DC18` and `0x0018DC60` each store one value into the game
settings object at offset `0x14`:

| native | stores |
|---|---|
| Early | 0 |
| Late | 1 |
| None | 2 |

**Where the engine puts the line between early and late is not recovered.** The
field is read from a shared settings object in about a hundred places and the
question selector has not been traced. The port splits at 1980 and says so; the
year buckets it leans on *are* the game's, from the decade classifier at
`0x001C8BE0` — before 1960, 1970, 1980, 1990, 1999, which is JAREN '50 through
'00. The split itself is this port's choice, and it is the only invented number
in the front end.

## The clipboard

The menus are not a list with a cursor. `DoSimpleMenu` has a one-button-per-
option mode: each item carries a `ButtonIndex`, `GetIconNameForNthButton` draws
its icon beside it, and `WhichButtonWasPressed` is matched back through
`GetButtonIndexForIconName`. That function is a plain ladder:

| index | button |
|---|---|
| 0 | BuzzButton |
| 1 | BlueTriangleButton |
| 2 | OrangeCircleButton |
| 3 | GreenCrossButton |
| 4 | YellowSquareButton |

So item one is the blue button and there is no cursor at all. The handset
reports those colours as slots 4, 3, 2 and 1.

The layout comes from `GenericData` and from the upvalues of SimpleMenu's own
closures, which are loaded as constants in its `main`:

| what | where | value |
|---|---|---|
| `clipboard_logo` | DoSimpleMenu upvalues 0, 1 | (280, 10) |
| `lines`, resized | upvalues 2..5 | (100, 170, 445, 505) |
| title box | `MenuTitleTextX/Y`, `MenuTitleWidth` | (114, 43, 400) |
| title shift when the logo shows | AddMenuTitleText upvalue 0 | +77 |
| title colour | AddMenuTitleText upvalue 1 | Black |
| items | `OneButtonPerOptionSimpleMenuStartX/StartY/YInc` | (105, 173, 40) |
| button icon offset | `CONST_ClipboardIconOffsetX/Y` | (-44, 3) |
| fonts | `ClipboardTitleFontName/Scaling`, `ClipboardTextFontName/Scaling` | ClipboardSmall, 1.32 and 1.0 |

**The sheet itself is still missing.** In the game the menus happen in the green
room and the clipboard is a prop: `GreenRoomModels.glb` carries
`BZ_texture_clipboard` along with the floor manager holding it, the green-room
host, and the two goons on the lift doors, and `MainMenu` finishes with
`DoEndOfGreenRoomAndUnload`. Until that scene is staged the port writes on a
plain pale rectangle, marked in the source as the one invented thing on the
screen. Staging the green room is the next step, and every model it needs is
already extracted.

## The text

Only 29 of the 659 strings were resolved before this, because the name-to-id
hash in `default.ndx` is still unidentified — a sweep of the named hash
families (djb2, sdbm, FNV-1/1a, Jenkins, ELF, CRC32 and case variants) matches
none of the 29 known pairs, not even partially, and the deltas between names
differing by one character vary with the prefix, so there is an avalanche step
and no simple polynomial will fall out of a sweep.

The front-end block was pinned without it. `CharacterSelectShared` names 24
buzzer sounds — Awooga, AirHorn, Alarm, Belch, CarHorn, Cat, Chicken, Chipmunk,
DogBark, Duck, EvilLaugh, Frog, GirlLaugh, Goose, Horn, Horse, Monkey, Sheep,
Siren, Space, Stadium, Train, Turkey, Whistle — and `default.str` carries
exactly 24 consecutive strings at 216..239 that are those noises in Dutch, in
the same order. Twenty-four of twenty-four in order is not coincidence, and it
anchors the block around it.

The four stage titles are the same kind of evidence: the enums run CHARTYPE,
MODEL, BUZZER, NAMEENTRY and 317..320 are KIES EEN PERSONAGE / KIES KLEDING /
KIES EEN ZOEMER / VOER JE NAAM IN, four consecutive strings in that order.

That took the table from 29 keys to 81. The rest still needs the hash.

## Skipping it

`--game <short|medium|long>` and `--round <id>` go straight to a round, which is
what testing wants. Without either, the game starts where the game starts.
