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

## The green room

`MainMenuAnimationSetup` is the foyer, and `ShowStaticClipboard` is the state
the main menu sits in. In order it calls `HideStudio`, `ShowGreenRoom`,
`StartAnimation(GetCameraNameGreenRoom(5), GetAnimTypeNameIdle())`,
`SetCameraAngleForGreenRoom(GetCameraNameGreenRoom(5))`,
`ShowGreenRoomModels(5)`, a RoundWin animation on the floor manager, and Intro
animations on green room doors 5 and 6. So the menu is not on the studio's
video wall - the studio is hidden - it is written on a clipboard a character is
holding, in another room, under another camera.

Five streams make the room:

| stream | what |
|---|---|
| `GreenRoomScene` | the world shell, one sector, 1011 triangles against the 1011 the header declares |
| `GreenRoomProps` | 26 pieces: walls, floor, rug, spots, pictures, plasma screens, plants, the ON AIR sign, cupboard, chairs and six doors |
| `GreenRoomModels` | the people - `FMANAGE`, `GRHOST`, `GOON01`, `GOON02` - and `BZ_texture_clipboard` |
| `GreenRoomCameras` | five, `ANIMATEDCAMERA_GREENROOM01..05` |
| `GreenRoomLights` | one rig, thirteen lights, all named FOYER |

The port loads all five, and hangs the menu on the clipboard the way the round
screen is hung on the video wall: find the surface whose material is
`BZ_texture_clipboard` and give it the viewport texture. That finds three
surfaces.

**The shot is not right yet, and the reason is in the name.** These are
`ANIMATEDCAMERA_`s, and `ShowStaticClipboard` calls `StartAnimation` on the
camera as well as pointing at it. `GreenRoomCameras.rp2` carries an
`ANIMANIMATION` group per camera - the first is nearly nineteen kilobytes of
keyframes - so the framing the game uses is a keyframe, not the rest pose the
stream header gives, which is all that is read so far. Until those keyframes
are parsed the room loads correctly but the clipboard is not in front of the
lens.

So it is behind `--greenroom` rather than on by default: a menu nobody can find
would be worse than the stand-in. Reading the RenderWare keyframes is the next
step.

## The stand-in sheet

**The sheet itself is still missing.** In the game the menus happen in the green
room and the clipboard is a prop: `GreenRoomModels.glb` carries
`BZ_texture_clipboard` along with the floor manager holding it, the green-room
host, and the two goons on the lift doors, and `MainMenu` finishes with
`DoEndOfGreenRoomAndUnload`. Until that scene is staged the port writes on a
plain pale rectangle, marked in the source as the one invented thing on the
screen. Staging the green room is the next step, and every model it needs is
already extracted.

## The character select screen

Its numbers are not in GenericData. `CharacterSelectSupport`'s
`DeclareConstantPositioningVars` derives most of them from the 640x480 screen it
is drawing on, so they had to be folded rather than read:

| constant | how the script gets it | value |
|---|---|---|
| `CONST_LeftMargin`, `CONST_PanelWidth`, `CONST_PanelStart`, `CONST_PanelInc` | 640 / 5 | 128 |
| `CONST_ControlIndent` | PanelInc / 2 - 28 | 36 |
| `CONST_MarginBannerX` | 10 + 5 | 15 |
| `CONST_MarginBannerTopY` | 10 + 10 | 20 |
| `CONST_MarginBannerBottomY` | 480 - 140 | 340 |
| `CONST_TitleTextX` | LeftMargin | 128 |
| `CONST_TitleWidth` | PanelInc * 4 | 512 |
| `CONST_PortraitElementY` | written | 178 |
| `CONST_YPlacementOfNameBar` | written | 420 |
| `CONST_NameTextIncrement` | written | 7 |
| `CONST_WheelStartVertical`, `CONST_GapBetweenWheelElements` | written | 115, 36 |

`GetXForPanel(n)` is `(PanelStart - 5) + (PanelInc * (n - 1) - 2) + 7`, which
lands on 128, 256, 384 and 512 - four 128-wide panels tiling from the banner
column to the right edge.

`PlaceAndRenderGenericGraphics` is the background: `charselect_gradient` at
`CONST_GradientTL`, the same sprite again at `CONST_GradientBR` turned 180
degrees by `SetIconRotationDegrees`, and `sideframetop` and `sideframebottom`
stacked at `CONST_MarginBannerX`. Per panel, `PlaceNameBarIcon` puts a
`nameplate` at `CONST_YPlacementOfNameBar` and the portrait border is
`portrait_select` once a place is claimed and `portraitframe` before.
`GetStageTitle` names a title for the character, costume, buzzer and name
stages and none for buzz-to-start, so that screen carries none.

Two screens are easy to confuse. `ChoosePositions`, which MainMenu calls before
the length menu, is the KIES EEN PLAATS screen. The buzz-to-start stage above is
`CharacterSelectMultiBeta`'s, and its prompt is `BuzzToJoinPrompt`.

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
