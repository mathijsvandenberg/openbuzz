# OpenBuzz

A from-scratch PC (x64) reimplementation of *Buzz!: The Music Quiz* (PS2,
SCES-53305), targeting C# and Dutch as the first locale.

**You need your own disc.** This repository contains no game data and never
will. The engine reads assets from a copy of the original disc that you supply;
the music clips in particular are licensed recordings that cannot be
redistributed. `.gitignore` is deliberately aggressive about this.

That extends to `docs/disasm/` - disassembled Lua is the game's own code, so it
stays local and is regenerated from your disc with `obz lua`. What *is* checked
in are the derived summaries: the format notes and the API name/arity tables,
which describe the interface rather than reproduce the implementation.

## Status

Format work is done for the layers that matter most; nothing renders yet.

| Area | State |
|---|---|
| `.PAK` archives | **Solved** - plain ZIP, stored. 4953 files extract in ~9s. |
| Lua `.clu` bytecode | **Solved** - custom Lua 5.0 fully decoded, 149/149 chunks. See [lua-format.md](docs/lua-format.md). |
| Native API surface | **Mapped** - 688 host functions. See [host-api.md](docs/host-api.md). |
| Text / questions | **Solved** - 8374 Dutch questions across 13 pools, all references validate. See [quiz-format.md](docs/quiz-format.md). |
| `.vgp` audio | **Solved** - 2336-byte sectors; stereo music / mono speech auto-detected from the trailer, 44100 Hz. See [audio-format.md](docs/audio-format.md). |
| `.vag` audio | **Solved** - standard 48-byte header, declares 11025/22050/44100 Hz |
| Controllers | **Real hardware working** - wired Buzz buzzers read as one HID pad (`054C:1000`) in the Godot round; virtual panel still behind `IBuzzInputSource` in the C# tools |
| Playable round | **Working** - `obz-round` plays a clip, takes buzzes, scores. Song-clip link unverified. |
| Song table | `rri.dat` decoded - 1000 songs, release year + clip name, all clips present |
| `.tex` textures | **Solved** - 42/42 decode, verified against PCSX2 captures. See [texture-format.md](docs/texture-format.md). |
| A2D animations | **Solved** - 176 animations, 21030 keyframes exported to JSON. See [a2d-format.md](docs/a2d-format.md). |
| `.rp2` models | **Solved** - all 968 geometries, plus skeletons, skin weights and 816 animation clips; exported to rigged glTF. See [rw-format.md](docs/rw-format.md). |
| Fonts | **Solved** - six bitmap fonts per stream: glyph rects, advances, character map; rendering in the round. See [font-format.md](docs/font-format.md). |
| Godot build | `obz-viewer.exe` - three tabs: models with their clips, the 2D layer in the game fonts, and a round playable on the real USB buzzers. See [godot/OpenBuzzViewer](godot/OpenBuzzViewer/README.md). |
| `.pss` / `.ipu` video | Not started - MPEG-2 program stream / Sony IPU |
| Real Buzz HID | Not started - USB HID, Sony VID `0x054C` |

## What the disc turned out to be

The game is a thin native engine plus **149 Lua scripts that hold the actual
game design**. Round logic reads like prose:

```
GetGlobal 2 3    ; R2 := _G["IlluminateAllViewports"]
Call      2 1 1  ; R2()
GetGlobal 2 4    ; R2 := _G["WaitUntilNoOneIsSpeaking"]
Call      2 1 1  ; R2()
GetGlobal 2 7    ; R2 := _G["SetFollowOnScript"]
LoadK     3 8    ; R3 := "PrintRoundScores"
Call      2 2 1  ; R2(R3)
```

Each round runs as a coroutine that drives the presentation through blocking
calls (`WaitSeconds`, `WaitForJudgementEnd...`, `WaitUntilNoOneIsSpeaking`), so
the control flow is sequential and readable rather than an event-graph tangle.

`Scripts/A2d/*.clu` are a different animal: 46 chunks that use Lua as a *data*
format, emitting keyframes via `Col` / `Tfm` / `Bbx` / `Obj` / `Anm`
(~25k calls). They are the 2D animation timelines, not logic.

## Two viable ports

1. **Reimplement in C#** - use `docs/disasm/` as the specification. Clean, and
   you own the result.
2. **Embed Lua 5.0 and run the original bytecode** - implement the 688 native
   functions as a shim. Much higher fidelity; the VM needs patching for the two
   format deviations documented in `lua-format.md`.

Route 2 looks more attractive than it first did, because the native surface is
mostly small, obviously-named, single-purpose calls.

## Playing a round

```bash
dotnet build OpenBuzz.sln -c Release
tools/OpenBuzz.Round/bin/Release/net9.0-windows/obz-round.exe
```

Plays a song clip, arms the four buzzers, first buzz wins the right to answer,
correct answers score. `1-4` buzz in, `QWER`/`ASDF`/`ZXCV`/`UIOP` pick a colour,
`F5` restarts. Options are shuffled per question, mirroring what the engine does
with `GetRandomisedIndex`.

`--pool` selects a question pool (default `qtitle`), `--rate` the sample rate,
`--locale` the language.

**Caveat:** the round assumes a question's `SongId` indexes `rri.dat` directly.
That is structurally sound - both are 0..999, and all 1000 clip names resolve to
real files - but it is not verified. If the on-screen answers do not fit what
you hear, that assumption is where to look.

## Watching the animations

```bash
godot --path godot/OpenBuzzPlayer
```

Plays the 176 extracted A2D timelines as placeholder rectangles, so the
choreography can be checked before any artwork exists. The design space is
640x480 and Godot stretches it 4:3, so the window is already a 1080p render of
the original layout.

Arrow keys change animation and scene, space pauses, `,`/`.` step a frame, `Y`
flips the vertical axis, `F` switches 25/50fps, `R` restarts.

Requires `obz a2d export` to have been run; the player finds `extracted/a2d` by
walking up from the project folder.

## Tooling

The `obz` CLI lands in `tools/OpenBuzz.Cli/bin/Release/net9.0/`.

```bash
obz extract --disc D:\ --out extracted
```
Unpacks the disc. Skips the French/German/Italian locale packs (~615 MB) unless
`--all` is passed; the Dutch build is ~1.1 GB.

```bash
obz lua --in extracted/Scripts --out docs/disasm
```
Disassembles every `.clu` to annotated `.luaasm`, resolving constants inline.

```bash
obz api --out docs/host-api.md
```
Classifies every global into native functions, script-defined functions, host
constants and script state, with call arity recovered from `OP_CALL`.

```bash
obz rkprobe
```
Re-derives the RK constant/register split from the corpus.

## Next up

- Parse `BM1/Text/NET/*.str` + `.ndx` - get the Dutch question bank readable.
- Decode `.vgp`; check against vgmstream's PS2 ADPCM handling.
- Prototype the Buzz controller HID layer (HidSharp) - independent of everything
  else, and cheap.
- Decompile rather than disassemble the Lua, to firm up route 2.


