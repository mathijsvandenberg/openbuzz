# Textures, atlases and fonts

> **Status: pixel layout UNSOLVED.** Headers, dimensions, palettes and atlases
> are all correct and verified. The index de-interleave is not: decoded images
> come out visually scrambled. Everything below about swizzling is a description
> of what was *tried*, not a working answer. `.uvs` and the palette work stand.

## `.tex` — RenderWare PS2 native textures

42 files. Each is a RenderWare chunk stream that begins mid-tree, with no
enclosing texture dictionary: `STRUCT("PS2")`, `STRING(name)`,
`STRING(maskName)`, then a struct whose payload opens with **another** chunk
header — the raster info is one level down, at that nested chunk's data offset:

| Offset | Field |
|---|---|
| +0 | width (uint32) |
| +4 | height (uint32) |
| +8 | depth — 8 or 4 |
| +12 | raster format — `0x00022504` for 8-bit, `0x00024504` for 4-bit |

Reading the outer struct directly yields `1 x height @ 0x1C020020`, because
`0x1C020020` is the RenderWare library id sitting where the depth appears to be.
That is the failure mode to recognise.

### Pixel payload

The indices and CLUT are wrapped in GS transfer packets. Rather than walking
those, both are taken from the **end of the file**: the palette is the final
1024 bytes (256 RGBA entries; 64 bytes / 16 entries at 4bpp), with the indices
immediately before it. Every file on the disc matches
`width x height + palette + ~332 bytes of header` exactly, which is what makes
that safe.

### Palette — solved

- **Palette order.** A 256-entry CLUT is in CSM1 layout: within every block of
  32 entries the second and third groups of 8 are swapped. 16-entry palettes are
  linear and need no fixing.
- **Alpha range.** PS2 alpha runs 0..128, where 128 is opaque. Scale by 255/128.
- **Channel order** in the file is R, G, B, A.

Confirmed against `BZ_Language_flags`, whose CLUT holds `006300` green,
`FE0001` red, `FCFFFF` white and `0808A7` blue contiguously at indices 64..71 —
exactly the Italian and Dutch flag colours. Colours come out right; only their
placement is wrong.

### Index de-interleave — UNSOLVED

Decoded images are scrambled. `obz tex probe` scores candidate layouts on two
metrics, both computed from palette indices so they are independent of the CLUT:

- *coherence* — fraction of neighbouring pixels sharing an index.
- *flat rows* — fraction of rows that are >=80% one index. Flag artwork is solid
  bands, so a correct decode must score high here. Nothing tried exceeds **6.6%**.

Tried and rejected: linear (no de-interleave); the standard PSMT8 block/column
unswizzle; its inverse; a 36-point sweep of the block width, block height, swap
offset and column-scale terms; and treating the buffer as two image rows packed
per buffer row, both as contiguous halves and byte-interleaved.

Beware coherence alone: it reaches 85% on layouts that are plainly wrong,
because scattered pixels still frequently match. The flat-row metric is what
exposes them, and the two rank candidates in *opposite* orders.

**The strongest open lead.** Read as a plain image of width 512, vertical
coherence is 4.4% while horizontal is 71% — the row stride is wrong, not merely
the scatter. Sweeping the stride puts a clear peak at **1024 bytes** (78.3%,
against 74% at 1020 and 1028), i.e. twice the width, so a 512x256 texture
occupies a 1024x128 buffer. The obvious readings of that pairing both fail the
flat-row test, so the relationship is more involved.

The next thing to try is the GS register block, which this code currently reads
past and ignores: it sits at +16 into the raster struct and begins
`00 80 30 99 05 00 00 20 1C ...`. `TEX0` encodes `TBW`, the texture buffer width
in units of 64 texels, which would state the real stride outright instead of
leaving it to be inferred. That is where to go next.

Sizes range from 32x32 to 512x512. All are palettised; nothing on the disc is
true colour.

## `.uvs` — atlas indices

13 files, 151 sub-rectangles. Header is a name field holding the tag `1C04`,
then a version byte (always 3) and an entry count. Each entry is a name field
followed by four floats: `u0, v0, u1, v1`.

A **name field** is a length byte, that many bytes of NUL-terminated text, then
padding so the text occupies a multiple of four bytes — measured from the start
of the text, not the file offset, so the floats are often not 4-byte aligned
within the file.

The padding is always **at least one byte**: a name whose length is already
divisible by four still advances a whole extra word. Getting this wrong parses
most files correctly and truncates the rest at the first such name, which is why
the tool reports parsed-vs-declared counts rather than trusting either alone.

Rects are normalised UVs; multiply by the texture size for pixels. Verified
semantically — `hor_line` resolves to 46x2px, `vert_line` to 2x46px, and
`1stPlace`..`4thPlace` to a row of 64px tiles.

## Fonts

Fonts are ordinary `.tex` atlases; there is no separate font format.

| Texture | Size | Depth |
|---|---|---|
| `SynLTS15` / `SynLTS15old` | 256x128 | 8 |
| `SynLTS18`, `SynLTS20` | 256x256 | 8 |
| `SynLTS26`, `SynLTS35` | 512x256 | 8 |
| `SyntaxLTStd-Bold15`, `-Bold18` | 256x512 | 4 |
| `SyntaxLTStd-Black26` | 512x512 | 4 |
| `BZ_fonts_AardvarkBold` | 512x512 | 8 |
| `BZ_fonts_digitalstrip` | 256x256 | 8 |

The Syntax faces are the UI text at several point sizes; Aardvark and Digital
Strip are display faces.

None of them has a `.uvs`, so glyph rectangles come from elsewhere. The
`characterMap.txt` / `NamedCharacterMap.txt` / `UnnamedCharacterMap.txt` files in
each locale's text directory list the glyph repertoire in order — `characterMap`
is UTF-16LE, the other two are plain text — which is enough to map a glyph index
to a character, but the per-glyph cell rectangles and advance widths have not
been located yet. That is the open question for text rendering.
