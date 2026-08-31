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

## Skinning and animation - SOLVED

```bash
obz model export         # rigged glTF with every clip embedded
```

84 models export, 52 of them rigged, carrying 816 clips between them.

### The skeleton

FRAMELIST gives every frame a local transform and a parent. HANIM says which
frames are bones: one frame carries the whole hierarchy as an ordered list of
node ids, and every bone frame carries its own HANIM giving just its id, which
is what ties the two together. A bone parent is the nearest ancestor frame that
is itself a bone.

### SKIN

```
u8    numBones, numUsedBones, numWeightsPerVertex, pad
u8    usedBones[numUsedBones]
u8    boneIndices[numVertices * 4]        plain geometry only
f32   weights[numVertices * 4]            plain geometry only
f32   inverseBind[numBones * 16]
u32   boneLimit, numMeshes, numRLE
```

Native geometry wraps this in a STRUCT carrying the PS2 platform id and omits
the per-vertex arrays, which come off the DMA chain instead - the bone index
rides in the low bits of each weight, truncated to a byte and *then* made
zero-based. Both forms were confirmed by size accounting, exact for every
geometry.

**Not every SKIN is usable.** A part bound to a single bone still ships a
full-length inverse-bind array, and its entries for the bones it does not use
are meaningless. Picking the first skin in the file therefore rigs the character
to garbage - the mesh looks right in the bind pose, because world x inverseBind
is the identity there whatever the matrices are, and only comes apart once the
clip plays. The full-body skin is the one with the largest `numUsedBones`.

### Clips

ANIMANIMATION, type 2, compressed. 22 bytes per keyframe:

```
f32   time
i16   q[4]        quaternion
i16   t[3]        translation
u32   prevFrame
```

followed by 24 bytes of custom data: a translation centre and half-extent.

The link is a byte offset in units of **24**, the in-memory keyframe size,
rather than the 22 the stream uses - RenderWare carries both sizes. Dividing by
24 gives a keyframe index, which is what makes it possible to group keyframes by
bone: the clip opens with one keyframe per bone at time zero, and every later
keyframe belongs to whichever bone its predecessor does.

### The 16-bit float

The components are **not IEEE half**. They are a 16-bit float split
**1 sign, 4 exponent (bias 15), 11 mantissa**.

This was settled against known values rather than guessed. The bind-pose bone
offsets say which code has to decode to which number, and the codes for a zero
offset are shared by every bone that has one - 30 bones for x, 20 for z. Only
the 1-4-11 split reproduces them. It also puts 0x7800 at exactly 1.0, which is
why the extreme codes land on the centre plus or minus the half-extent exactly.

Two independent checks confirm it:

- translations reproduce the bind pose to within 0.005 units, quantisation aside
- all **107,232** quaternions across the animation streams come out unit to
  within 0.02%

An IEEE half split gives neither.

### Export

Joints become glTF nodes, the skin carries the inverse binds, and each clip
becomes rotation and translation channels. glTF stores matrices column-major and
multiplies column vectors while RenderWare stores a row-major basis and
multiplies row vectors, so the RenderWare rows become the glTF columns and the
layout maps across directly.

Verified by reading the exported `.glb` back and playing it: the characters
stay intact through the clip and move as people.

## Not started

`.pss` / `.ipu` video, and the lights and cameras in the studio streams.

## Tools

```bash
obz rw summary                  # what every stream contains
obz rw tree --file X.rp2        # chunk tree of one stream
obz rw textures                 # extract embedded textures to PNG
```
