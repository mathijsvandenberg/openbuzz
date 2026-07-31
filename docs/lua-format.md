# The `.clu` bytecode format

The game's logic ships as 149 precompiled Lua chunks (`Scripts/*.clu`, plus
`Scripts/A2d/*.clu`). They are Lua **5.0**, but from a build that predates the
5.0 release, so stock Lua tooling mis-decodes them. Both deviations below were
established empirically against all 149 chunks, not assumed.

## Chunk header

Signature `1B 4C 75 61` + version `0x50`, then:

| Field | Value on this disc |
|---|---|
| endianness | `1` (little) |
| `sizeof(int)` | 4 |
| `sizeof(size_t)` | 4 |
| `sizeof(Instruction)` | 4 |
| `SIZE_OP` | 6 |
| `SIZE_A` | 8 |
| `SIZE_B` | 9 |
| `SIZE_C` | 9 |
| `sizeof(lua_Number)` | **4 — `float`, not `double`** |
| `TEST_NUMBER` | `3B AF EF 4B` = 31415926.0f |

Two things to note. The header emits four separate `SIZE_*` bytes, which neither
released Lua 5.0 nor 5.1 does — this is a customised `luaU_header`. And
`lua_Number` is a 32-bit float, as you would expect on PS2 hardware; a reader
that assumes `double` desynchronises on the first numeric constant.

Debug info is stripped: line tables and local-variable names are empty in every
chunk. Function and global *names* survive, because they live in the constant
table rather than the debug section.

## Instruction layout

Lua 5.0 packs `iABC` opcode-first from the low bit:

```
 31       24 23       15 14        6 5    0
+-----------+-----------+-----------+------+
|     A     |     B     |     C     |  OP  |
+-----------+-----------+-----------+------+
             \_________ Bx (18 bits) ______/   (Bx overlays B and C)
```

`sBx = Bx - 131071`.

Lua **5.1** reordered this to `OP A C B`, which is the layout most references
and tools describe. Decoding these chunks that way yields plausible-looking
opcodes with nonsense operands — the opcode field is in the same place in both,
so nothing obviously fails.

## RK operands

Released Lua 5.0 flags "this operand is a constant, not a register" with
`BITRK = 1 << (SIZE_B - 1)` = 256. This build instead uses the older
`MAXSTACK`-relative encoding: **operands `>= 250` are constants**, index
`operand - 250`.

Verified with `obz rkprobe` across the whole corpus:

```
RK operands examined : 10313
  value < 250        : 6177   (max seen 35)
  value 250..255     : 1752      <-- only possible under MAXSTACK=250
  value >= 256       : 2384
largest maxstacksize : 44

impossible operands, threshold 250 : 0
impossible operands, threshold 256 : 1752
```

An operand counts as impossible if it names a register the function never
allocates or a constant beyond the end of its constant table. The 250 hypothesis
produces none; the 256 hypothesis produces 1752, and register operands never
exceed 35 against a maximum stack of 44 — so the 250..255 band cannot be
registers. The split is unambiguous.

`obz lua --rk 256` re-runs the decode under the other assumption if this ever
needs revisiting.

## What this means for the port

Because the format is now fully decoded, the original game logic is readable.
Two routes stay open:

- **Reimplement in C#** using the disassembly in `docs/disasm/` as the spec.
- **Embed Lua and run the original bytecode**, implementing the 688 native
  functions in `host-api.md`. This needs a Lua 5.0 VM patched for the two
  deviations above, or a recompile of the sources to stock 5.0 bytecode after
  decompiling.
