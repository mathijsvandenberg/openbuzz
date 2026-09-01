# OpenBuzz Viewer

Two tabs, built with Godot 4.7.2 (.NET).

**Models** - pick a character, pick a clip, watch it play.

**2D layer** - the A2D timelines, drawn with the game's own atlas sprites and
bitmap fonts. Space pauses.

**Round** - a playable round on the real buzzers. A clip plays, someone hits
their red button, that player picks a colour. Right answer +1, wrong -1.

```bash
dist/obz-viewer.exe
```

Drag to orbit, wheel to zoom.

## Where the data comes from

Nothing is built into the executable. Both tabs read from `extracted/`, walking
up from wherever the exe sits:

```bash
obz model export             # -> extracted/models/*.glb   for the Models tab
obz audio decode --limit 120 # -> extracted/wav/*.wav      the round needs clips
obz bundle                   # -> extracted/godot2d/       2D layer and questions
```

`obz bundle` only writes questions whose clip has been decoded, so decode first
or the round has nothing to play.

Models load at runtime through `GLTFDocument` rather than as imported
resources. The 2D layer reads the bundle: PNG atlases plus JSON tables of
sprite rectangles, glyph rectangles and resolved strings. So the engine side is
a reader of plain data - it knows nothing about `.uvs` files, the PS2 texture
swizzle, or the 16-bit float in the font metrics.

## Building it

Needs Godot 4.7.2 with the matching export templates installed.

```bash
godot --headless --path godot/OpenBuzzViewer --export-release "Windows Desktop"
```

The preset writes to `dist/obz-viewer.exe` with the pack embedded, so the result
is a single file like the other tools.

## Checking it without looking

`--tab <n> --shot <file>` renders one frame of a tab and exits, which is how
the build gets verified:

```bash
dist/obz-viewer.exe -- --tab 1 --shot check.png
```

## Controllers

The wired Buzz buzzers enumerate as a single HID game controller - "Namtai
Buzz", vendor `0x054C` product `0x1000` - carrying all four handsets in one
report. SDL has no gamepad mapping for it, so `Round.gd` reads the buttons raw,
five per handset in report order, and finds the pad by its vendor and product
id rather than by name.

Button order within a handset, measured on the hardware:

| raw | button |
|---|---|
| 0 | red buzzer |
| 1 | yellow (bottom) |
| 2 | green |
| 3 | orange |
| 4 | blue (top) |

That is bottom-to-top, while the answers are listed top-to-bottom on screen, so
`ANSWER_BUTTONS` maps them in reverse. The status bar names the handset, the
colour and the raw index of the last press, so a mismatch stays visible.

A keyboard stands in when no buzzers are attached: `1`-`4` buzz, then
`QWER` / `ASDF` / `ZXCV` / `UIOP` answer.

## Lamps

The red lamp on each buzzer is lit by a HID **output** report, which the engine
cannot send, so a helper does it. `dist/obz-lamps.exe` holds the device open and
takes one four-digit pattern per line:

```bash
obz-lamps --set 1010     # lamps 1 and 3 on
obz-lamps --probe        # what the device says about itself
obz-lamps --serve        # a pattern per line on stdin, which is what the round uses
```

`Lamps.gd` starts it with `--serve` and keeps it running, because a round
changes the lamps several times a second and a process per change would not
keep up. The status bar says `lamps on` when the helper started, or why it did
not.

In a round: all four lit invites a buzz, the buzzed player alone stays lit, a
right answer blinks it, a wrong one goes dark.

The report is the one the Linux `hid-sony` driver uses for these buzzers - a
leading zero then one byte per lamp, `0xFF` for lit - padded to the length the
device declares, which it reports as 8.
