# A2D — the 2D animation system

`Scripts/A2d/*.clu` are not game logic. They are 46 compiled Lua chunks that use
Lua purely as a **data format**: load constants into registers, call a global,
repeat. Between them they hold **176 animations, 1374 objects and 21030
keyframes**.

Because the bytecode already decodes, no reverse-engineering was needed — only
static evaluation. `LuaDataExtractor` walks each chunk following constant loads
and register moves, and records the arguments at every call. It is not an
interpreter: anything computed rather than loaded is recorded as null, so a file
that needed real evaluation would show up as missing values instead of silently
wrong numbers. Nothing on the disc does.

## The calls

A chunk opens with a zero-argument call naming itself, then repeats this shape:

```
BZ_FE_PIP_STATES()                          scene
Anm("ACT_P01_UP", 22)                       animation: name, frame count
Obj(0, "OBJ_P01")                           object: slot, name
Tfm(0, 0, 1, 1, 0, 173.35, -85.75)          frame 0, slot 0, scale, rotation, position
Bbx(0, 0, -47, 62, 47, -62)                 bounds: left, top, right, bottom
Col(0, 0, 1, 1, 1, 1)                       colour: r, g, b, a
Tfm(1, 0, 1, 1, 0, 173.35, -68.25)          next frame
```

| Call | Count | Arguments |
|---|---:|---|
| `Anm` | 176 | name, frame count (2..240) |
| `Obj` | 1374 | slot (0..28), name — 188 distinct |
| `Tfm` | 9532 | frame, slot, scaleX, scaleY, rotation, x, y |
| `Col` | 11498 | frame, slot, r, g, b, a |
| `Bbx` | 1374 | frame (always 0), slot, left, top, right, bottom |

`Anm` opens an animation and `Obj` declares a slot within it. Slots are reused
between animations, so an `Obj` for a slot already in use begins a fresh object.
`Tfm`, `Col` and `Bbx` attach to the slot named by their **second** argument.

Coordinates are **y-up** and centred on the origin — `Bbx(0,0,-47,62,47,-62)` is
a 94x124 box straddling zero. `Bbx` carries frame 0 in every one of its 1374
calls, so bounds are static per object rather than animated.

`Col`'s RGB is 1,1,1 in every call on the disc; only alpha (0..1) varies. It is
a fade track, not a tint track.

## Binding actors to artwork

Alongside the timelines, two calls attach content to objects:

- `SetActorToIconMapping(actor, icon)` — 76 calls, binding an object to a named
  sprite. Those names are the sub-rectangles in the `.uvs` atlases, which is the
  join between animation and texture.
- `SetActorToTextMappingWithJustificationAndSizeMultiplier(...)` — 93 calls, and
  a 4-argument variant with 12, binding an object to a text string with
  justification and a size multiplier of 1.25..10.

## Cross-checks

The data validates against work done earlier:

- `ACT_P01_UP` moves y from -85.75 to 76.2 over 22 frames with a decelerating
  step, then settles back to 71.55 — an ease-out with a small overshoot, exactly
  a player viewport popping up.
- Its object's bounds are 94 units wide, matching the 94px `PortraitSurroundWhite`
  rect decoded from `BZ_PIP_portrait_frame.uvs`. Animation units are texture
  pixels.
- `BZ_FE_PIP_STATES` holds 16 animations: P01..P04 x UP / BUZZING / CLEAR / DOWN.

## Tools

```bash
obz a2d stats                    # per-call counts and argument ranges
obz a2d dump --chunk NAME        # the recovered call stream, in order
obz a2d export                   # -> extracted/a2d/*.json
```

`export` folds each chunk into a scene tree — animations, objects, transform and
colour tracks, bounds, icon bindings — and writes JSON, about 4 MB in total. That
is the form the runtime should consume; extraction stays a tooling concern so the
game never has to parse Lua bytecode.
