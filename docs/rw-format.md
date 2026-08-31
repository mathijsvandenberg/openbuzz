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

## Geometry - SOLVED

Bit 24 of the geometry format word is a "native" flag, and it decides the whole
layout. The set is split 253 plain to 715 native.

```bash
obz model list                    # what every stream holds
obz model export                  # -> extracted/models/*.glb
```

### Plain geometry (native = 0)

The vertex data is in the GEOMETRY chunk's own STRUCT as plain float arrays:

```
u32   format, numTriangles, numVertices, numMorphTargets
u32   colour[numVertices]                 if PRELIT
f32   uv[numUVs][numVertices][2]          if TEXTURED or TEXTURED2
u16   v2, v1, material, v3                per triangle
f32   boundingSphere[4]
u32   hasVertices, hasNormals
f32   position[numVertices][3]
f32   normal[numVertices][3]              if hasNormals
```

Confirmed by size accounting: predicted and actual STRUCT sizes agree exactly
for all 253, which also settles that this build writes no ambient/specular/
diffuse trio between the header and the colours.

### Native geometry (native = 1)

The STRUCT is a 40-byte stub and the data sits in `EXTENSION -> NATIVEDATA` as
VU1 DMA chains, with `BINMESH` giving one split per material. Ported from
rwtools, `src/ps2native.cpp`.

Each split is a chain of 16-byte tags. Section A tags point at one block
covering the whole split; section B tags carry per-block vertex data inline,
with the type in the tag's last word selecting the format - float or int16
positions, float or int16 UVs, packed or padded int8 normals, day/night or plain
colours, skin weights.

Consecutive blocks overlap by two vertices so the triangle strip runs across
them, and the overlap is trimmed when a tag says the block was not the last.

**Strip restarts are encoded as repeated positions, not repeated indices** - the
float-position blocks hand out a fresh index for every entry, so an index-only
degeneracy test culls nothing. Comparing coordinates instead leaves exactly the
triangle count the geometry header declares: 214 and 851 for the two Angie
meshes, 435 and 1962 for the plain ones.

### Winding

RenderWare stores a triangle as `vertex2, vertex1, material, vertex3`. Taking
them in stored order makes the face normal disagree with the supplied vertex
normals on 1954 of 1962 triangles; reordering to `v1, v2, v3` agrees on 1954. So
the normals decide the winding rather than an assumption about handedness, and
no coordinate flip is needed.

### Export

`obz model export` writes glTF 2.0 binary with positions, normals, UVs and the
stream's own textures embedded, one primitive per material. Deliberately an
export rather than a renderer: it needs no engine, so the geometry can be
checked in Blender or any viewer before anything is decided about how to draw
it.

## Skinning and animation - not started

The frame lists carry `HANIM`, each native geometry carries a `SKIN` chunk, and
the `*Animations.rp2` streams hold 13 `ANIMANIMATION` clips each. The weights
and bone indices are read off the DMA chain already but are not yet exported.

## Tools

```bash
obz rw summary                  # what every stream contains
obz rw tree --file X.rp2        # chunk tree of one stream
obz rw textures                 # extract embedded textures to PNG
```
