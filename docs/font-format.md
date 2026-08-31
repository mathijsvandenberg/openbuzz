# Bitmap fonts - SOLVED

The `Font.rp2`, `Font_EUR.rp2` and `Font_RUS.rp2` streams each hold six bitmap
fonts. Everything needed to lay out text - the atlas, the per-glyph rectangle,
the advance and the character map - is now parsed and rendering.

```bash
obz font list                       # every font in all three streams
obz font sample --scale 2           # render a line with each one
```

## Where it lives

The font data sits after the texture dictionary in the same chunk stream, using
a Buzz-specific chunk id, each font preceded by a group carrying its name:

```
TEXDICTIONARY            the six atlases
CHUNKGROUPSTART  { u32 count, STRING "GeneralLarge" }
FONT (0x199)     { header, character map, glyph table, atlas name }
CHUNKGROUPEND
CHUNKGROUPSTART  { u32 count, STRING "ExtraLarge" }
FONT (0x199)     ...
```

**The FONT chunk's declared size is wrong.** It reads 3111 for fonts whose data
is 19,513 bytes. The extent has to be computed from the header instead, which
lands exactly on the following CHUNKGROUPEND for all six fonts in all three
streams.

## The FONT chunk

```
+0x00  u32    0x01000001      magic
+0x04  u32    0
+0x08  f32    lineHeight      pixels; every glyph is exactly this tall
+0x0C  f32    5 / lineHeight  derived, unused
+0x10  u32    0
+0x14  u32    2
+0x18  u32    161             constant across every font
+0x1C  u32    glyphCount
+0x20  u32    charBias
+0x24  u16    [charBias + 128]    character map
       21 x   [glyphCount]        glyph table
       u32    1
       char   [32]                atlas name, e.g. "SynBol18.png"
```

### Character map

Indexed by `character + charBias`, giving a glyph index or `0xFFFF` for
"no such glyph". The map always spans -128 to charBias, hence its
`charBias + 128` entries.

The bias is why the high Latin-1 letters land *below* the ASCII block: the game
indexes with a signed character, so 0xC1 arrives as -63. The giveaway that this
reading is right is the single gap in the accented run at 0xF7 - the division
sign, exactly where Latin-1 puts it among the letters.

### Glyph table

21 bytes each: five floats, then a byte that is always zero.

```
f32 u0, v0, u1, v1     rectangle in the atlas, normalised
f32 advance            in units of lineHeight
```

**The UVs address texel centres, not edges.** Every one of the 592 coordinates
across all six fonts has a fractional part of exactly 0.5 when multiplied by the
atlas size, so the pixel cell is

```
x0 = u0 * width  - 0.5          w = (u1 - u0) * width  + 1
y0 = v0 * height - 0.5          h = (v1 - v0) * height + 1
```

and the pen moves `advance * lineHeight + 1` pixels, lines step
`lineHeight + 1`. Cells tile the atlas exactly at that stride, which is how the
convention was pinned down: consecutive glyphs on a row start exactly
`advance + 1` apart, with no gap and no overlap.

Getting this wrong is visible rather than subtle. Rounding `u0 * width`
directly shifts each glyph by a pixel, but not all of them the same way, so the
baseline wobbles; and advancing by `advance * lineHeight` is one pixel short per
character, which reads as letters packed too tightly.

There are no side bearings in the metrics - the blank margin is inside the cell,
typically one to three pixels each side.

## The fonts

| Name | Atlas | Line height | Glyphs |
|---|---|---|---|
| `Default` | SynBol15.png | 23 | 121 |
| `GeneralLarge` | SynBol18.png | 28 | 121 |
| `ExtraLarge` | SynBla26.png | 42 | 121 |
| `ClipboardSmall` | flood22.png | 34 | 121 |
| `RoundInstructionsSmall` | UnivB14.png | 24 | 111 |
| `RoundInstructionsLarge` | UnivB26.png | 39 | 62 |

All are uppercase-only; no font maps a lowercase letter.

`Font_RUS.rp2` uses the same layout with different counts and a different bias
(110 glyphs, bias 8314), which is a useful independent check that the model is
not overfitted to the Western build.

## Which font goes where

`GenericData.lua` binds the styles, and the names match exactly:

| Script global | Font | Scaling |
|---|---|---|
| `QuestionFontName` | GeneralLarge | 1 |
| `AnswerFontName` | GeneralLarge | 0.9 |
| `GeneralTitleFontName` | ExtraLarge | 1 |
| `ClipboardTitleFontName` | ClipboardSmall | 1.32 |
| `ClipboardTextFontName` | ClipboardSmall | 1 |
| `RoundCompleteFontName` | RoundInstructionsLarge | 1.5 |
| `PrizeFontName` | RoundInstructionsLarge | 1.1 |

Two names the scripts use - `GeneralSmall` and `Score` - are not in any of the
three streams, so they must fall back at runtime. Not yet chased down.

## Which font the A2D layer uses

Text bindings in the A2D data carry a `Style`, and it is simply the font name -
the animation data only ever uses `RoundInstructionsSmall` and
`RoundInstructionsLarge`. So a round title such as `RulesPointsBuilderTitle`
("Punten verdienen") draws in the face the original uses, with no mapping table
needed.

## Rendering

`FontLibrary` in `OpenBuzz.Ui` loads the stream and pairs each font with its
decoded atlas; `BitmapFont` draws, measures, word-wraps and tints.

The atlases are white glyphs carried in the alpha channel, so colour is a tint
applied at draw time - which is how the game shows one face in white, gold and
the four player colours.

Each glyph is cut into its own bitmap up front. Drawing a sub-rectangle of a
shared atlas lets GDI+ sample the neighbouring glyphs whenever the destination
is not pixel-aligned, and that shows up as fragments of other letters clinging
to the baseline.
