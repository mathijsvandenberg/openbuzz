# OpenBuzz

A from-scratch PC (x64) reimplementation of *Buzz!: The Music Quiz* (PS2,
SCES-53305), targeting C# and Dutch as the first locale.

**You need your own disc.** This repository contains no game data and never
will. The engine reads assets from a copy of the original disc that you supply;
the music clips in particular are licensed recordings that cannot be
redistributed. `.gitignore` is deliberately aggressive about this.

That extends to `docs/disasm/` — disassembled Lua is the game's own code, so it
stays local and is regenerated from your disc with `obz lua`. What *is* checked
in are the derived summaries: the format notes and the API name/arity tables,
which describe the interface rather than reproduce the implementation.

## Status

Format work is done for the layers that matter most; nothing renders yet.

| Area | State |
|---|---|
| `.PAK` archives | **Solved** — plain ZIP, stored. 4953 files extract in ~9s. |
| Lua `.clu` bytecode | **Solved** — custom Lua 5.0 fully decoded, 149/149 chunks. See [lua-format.md](docs/lua-format.md). |
| Native API surface | **Mapped** — 688 host functions. See [host-api.md](docs/host-api.md). |
| Text / questions | **Solved** — 8374 Dutch questions across 13 pools, all references validate. See [quiz-format.md](docs/quiz-format.md). |
| `.vgp` audio | **Solved** — 2336-byte sectors; stereo music / mono speech auto-detected from the trailer, 44100 Hz. See [audio-format.md](docs/audio-format.md). |
| `.vag` audio | **Solved** — standard 48-byte header, declares 11025/22050/44100 Hz |
| Controllers | **Virtual panel working** — 4 handsets, lamps, behind `IBuzzInputSource` |
| `.tex` textures | Not started — PS2 swizzled/palettised |
| `.rp2` models | Not started — RenderWare PS2 streams |
| `.pss` / `.ipu` video | Not started — MPEG-2 program stream / Sony IPU |
| Real Buzz HID | Not started — USB HID, Sony VID `0x054C` |

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

1. **Reimplement in C#** — use `docs/disasm/` as the specification. Clean, and
   you own the result.
2. **Embed Lua 5.0 and run the original bytecode** — implement the 688 native
   functions as a shim. Much higher fidelity; the VM needs patching for the two
   format deviations documented in `lua-format.md`.

Route 2 looks more attractive than it first did, because the native surface is
mostly small, obviously-named, single-purpose calls.

## Tooling

```bash
dotnet build OpenBuzz.sln -c Release
```

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

- Parse `BM1/Text/NET/*.str` + `.ndx` — get the Dutch question bank readable.
- Decode `.vgp`; check against vgmstream's PS2 ADPCM handling.
- Prototype the Buzz controller HID layer (HidSharp) — independent of everything
  else, and cheap.
- Decompile rather than disassemble the Lua, to firm up route 2.
