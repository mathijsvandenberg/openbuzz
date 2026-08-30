# Textures, atlases and fonts

**Solved.** All 42 textures decode correctly, verified against captures of the
game running in PCSX2.

## `.tex` — RenderWare PS2 native textures

Each file is a RenderWare chunk stream beginning mid-tree, with no enclosing
texture dictionary: `STRUCT("PS2")`, `STRING(name)`, `STRING(maskName)`, then a
struct whose payload opens with **another** chunk header — the raster info is
one level down, at that nested chunk's data offset.

Reading the outer struct directly yields `1 x height @ 0x1C020020`, because
`0x1C020020` is the RenderWare library id sitting where the depth appears to be.

### The raster header

librw's `StreamRasterExt`, 0x40 bytes. It declares its own payload sizes, and
using them is what makes decoding reliable:

| Offset | Field |
|---|---|
| +0x00 | width (int32) |
| +0x04 | height (int32) |
| +0x08 | depth (int32) — 8 or 4 |
| +0x0C | rasterFormat (**uint16**) |
| +0x0E | version (**int16**) |
| +0x10 | tex0 (uint64) |
| +0x18 | paletteOffset, tex1low |
| +0x20 | miptbp1, miptbp2 (uint64 each) |
| +0x30 | **pixelSize** (uint32) |
| +0x34 | **paletteSize** (uint32) |
| +0x38 | totalSize, mipmapVal |

### The payload, and the bug that cost a dozen rounds

Pixels and CLUT sit in two blocks at the end of the file, sized by `pixelSize`
and `paletteSize`. **Each block opens with an 0x50-byte GIF/DMA header** before
the real data — visible in librw as `read8(raster->palette - 0x50, paletteSize)`.

Every texture on the disc has `pixelSize == width*height + 80` and
`paletteSize == 1024 + 80`.

This code originally *inferred* the payload location: CLUT as the last 1024
bytes, indices as the `width*height` bytes before it. The arithmetic seemed to
confirm it — every file is `W*H + 1024 + ~332` — but those 332 bytes are partly
the two block headers, so the indices were read **exactly 80 bytes early in
every file**.

No swizzle model can correct a shifted input. That single unchecked assumption
produced a long series of increasingly elaborate de-interleave theories, each of
which moved the corruption around without removing it. The transform was never
the problem; the bytes fed into it were.

### De-swizzling

Ported from librw, `src/ps2/ps2raster.cpp`:

```c
static uint32
swizzle(uint32 x, uint32 y, uint32 logw)
{
    x ^= (Y(1)^Y(2))<<2;
    nx = (x&7) | ((x>>1)&~7);
    ny = (y&1) | ((y>>1)&~1);
    n  = Y(1) | X(3)<<1;
    return n | nx<<2 | ny<<(logw-1+2);
}
```

`unswizzleRaster` applies it four rows at a time, copying each 4-row group to a
scratch buffer and scattering from it. Note `X(3)` reads the value of `x`
*after* the XOR.

Bits of `y` are XORed into `x`, so the axes are coupled. That is why no
within-row permutation, no separable row-order × column-order, and no fixed
16×16 block permutation could express it — the mapping is not periodic on a
16-wide block at all, it is periodic on **four rows** with a width-dependent
`logw` term.

**Swizzling is conditional.** `version == 0` means the raster is not swizzled;
`version == 1` swizzles 8-bit; `version == 2` is "new style". All textures on
this disc are version 2.

### Palette

- **CSM1 order.** A 256-entry CLUT has, within every block of 32 entries, its
  second and third groups of 8 swapped. 16-entry palettes are linear.
- **Alpha** runs 0..128, where 128 is opaque; scale by 255/128. Do not force it
  opaque — the font atlases and icon sheets rely on genuine transparency.
- **Channel order** in the file is R, G, B, A.

## `.uvs` — atlas indices

13 files, 151 sub-rectangles. Header is a name field holding the tag `1C04`,
then a version byte (always 3) and an entry count. Each entry is a name field
followed by four floats: `u0, v0, u1, v1`.

A **name field** is a length byte, that many bytes of NUL-terminated text, then
padding so the text occupies a multiple of four bytes — measured from the start
of the text, not the file offset, so the floats are often not 4-byte aligned
within the file. The padding is always **at least one byte**: a name whose
length is already divisible by four still advances a whole extra word.

## Fonts

Fonts are ordinary `.tex` atlases and now decode with everything else:
`SynLTS15/18/20/26/35` and `SyntaxLTStd-*` are the UI faces at several sizes,
plus `BZ_fonts_AardvarkBold` and `BZ_fonts_digitalstrip` as display faces.

None has a `.uvs`, so per-glyph cell rectangles and advance widths still have to
be located. The `characterMap.txt` / `NamedCharacterMap.txt` /
`UnnamedCharacterMap.txt` files give the glyph repertoire in order, which maps a
glyph index to a character but not its position. That is the remaining open
question for text rendering.

## Method note

Empirical derivation was the right tool for the `.vgp` audio container and the
A2D schema, where no reference implementation exists. It was the wrong tool
here: librw is open source and documents this format completely. Several rounds
of metrics — luminance smoothness, run length, transition counting, seriation,
simulated annealing — each ranked a plausible-looking wrong answer above the
truth, because they optimise for smoothness rather than correctness. Check for
an existing implementation before inferring one.
