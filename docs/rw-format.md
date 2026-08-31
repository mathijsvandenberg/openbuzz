# `.rp2` - RenderWare streams

143 files under `RWStream/`, holding the characters, costumes, animations,
prizes, studio set, cameras and lights. They are ordinary RenderWare streams,
so the chunk ids come from librw's `src/rwbase.h` rather than guesswork.

## Shape of the set

```
143 streams, 445 embedded textures, 968 geometries
```

| Kind | Contents |
|---|---|
| `<Name>Costume01..03` | 1 texture, 3 clumps, 6-9 geometries - a character in one outfit |
| `<Name>Animations` | 13 `ANIMANIMATION` chunks, no geometry |
| `<Name>WinAnimation` | 3 `ANIMANIMATION` chunks |
| `Prize01..29` | props |
| `Studio*`, `GreenRoom*` | the set, its models, lights and cameras |
| `Lights*`, `*Cameras` | small streams of lights and camera paths |
| `Font`, `Font_EUR`, `Font_RUS` | 3D text |

## Structure

A costume stream looks like this:

```
TEXDICTIONARY
  STRUCT                        numTextures, deviceId
  TEXTURENATIVE                 the character's skin
  EXTENSION
CHUNKGROUPSTART / CHUNKGROUPEND
CLUMP
  STRUCT
  FRAMELIST
    STRUCT                      frame matrices and parents
    EXTENSION -> USERDATA, HANIM    bone hierarchy
  GEOMETRYLIST -> GEOMETRY ...
  ATOMIC ...
```

`HANIM` on the frames means the characters are **skinned with a bone
hierarchy**, and the separate `*Animations.rp2` files supply the clips.

## Textures - done

The `TEXTURENATIVE` payloads are laid out exactly like the standalone `.tex`
files, so the same decoder reads them with no changes. All 445 extract
correctly:

```bash
obz rw textures            # -> extracted/rwpng
```

They come out under their in-game names, e.g.
`AngieCostume01__BZ_Texture_AngieStarlet.png`.

`obz-tex.exe` browses the result - both this set and the standalone `.tex`
one - as a list or a contact sheet, with alpha shown against a checkerboard.

## Geometry - not started

`GEOMETRY` chunks on PS2 do not hold plain vertex arrays. The real data sits in
an `EXTENSION -> NATIVEDATA` chunk as **VU1 DMA chains**: packets of vertex,
normal, UV and colour data interleaved with VIF tags, meant to be fed straight
to the vector unit. Reading them means walking the DMA chain and decoding the
VIF unpack commands, with the vertex format varying per mesh.

librw implements this in `src/ps2/ps2raster.cpp`'s geometry side and in
`rwps2.cpp`, and rwtools has an independent reader in `src/ps2native.cpp`
(`Geometry::readPs2NativeData`) that walks exactly these section-A/section-B
chains. Both are worth diffing before writing anything - the texture work made
the cost of not doing that very clear.

## Tools

```bash
obz rw summary                  # what every stream contains
obz rw tree --file X.rp2        # chunk tree of one stream
obz rw textures                 # extract embedded textures to PNG
```
