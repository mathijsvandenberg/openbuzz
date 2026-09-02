# The studio: set, cameras and screen layout

Everything the round screen is made of turned out to be data. None of it is in
the executable, which is where I first assumed the numbers lived.

## The set

`StudioModels.rp2` is not a model. It is fifty named pieces, each preceded by a
`CHUNKGROUPSTART` whose payload is a count and then a `STRING` with the piece's
name, and each placed by its own root frame:

    MODEL_SET_DOME  MODEL_AUDIENCE  MODEL_SET_LECTERN  MODEL_FLOOR
    MODEL_STAGE_MULTI  MODEL_PODIUM_MULTI  MODEL_PODIUMGLOW_1..4
    ANIMATEDMODEL_JUMBOTRON  MODEL_JUMBOTRON_INGAME  ANIMATEDMODEL_CLOCK
    DUMMYNODE_CONTESTANT_1..4  DUMMYNODE_CLOCK_CENTRE  DUMMYNODE_GAMEWIN

A `DUMMYNODE_` draws nothing; it marks a spot. Each piece is followed by a
second group naming its render state, `NORM_DEFAULT` or
`NORM_ADDITIVE_BLENDING`; eleven pieces are additive - the flares, the ground
light, the podium glows - and drawn opaque they are black slabs across the set.

The same convention names the fonts, so `RwFont` reads it the same way.

## The world

`StudioScene.rp2` is `WORLD_STUDIO`, an `RpWorld`: a BSP of plane sectors
ending in five atomic sectors. On PS2 each sector carries its geometry exactly
as a model geometry does - a `BINMESH` split per material and a `NATIVEDATA` of
VU1 DMA chains - so the existing PS2 reader handles them unchanged. The decode
comes to 3341 triangles against the 3341 the world header declares.

This is the set proper: the walls, the floor, and the screen the round is
played on. The props stand in it.

## The cameras

`StudioCameras.rp2` holds all fifty-one cameras as ordinary `CAMERA` chunks,
under exactly the names the Lua passes to `SetCameraAngle`. Each is a clump of
two frames: a placement, and a constant child with rows `(-1,0,0) (0,0,-1)
(0,-1,0)` which is the authoring tool's axis fix. Composing them:

    forward = -row1(frame0)    up = -row2(frame0)    right = -row0(frame0)

That is checked rather than assumed. `CAMERA_CONTESTANT_1..4` must look at
`DUMMYNODE_CONTESTANT_1..4`, whose positions the set gives independently, and
under this rule each aims at its own contestant to a cosine of 0.997. No other
axis or sign is close.

The `CAMERA` struct is 32 bytes: view window (2 floats), view offset (2), near,
far, fog, projection. The view window is the frustum half-extent at unit
distance, so the field of view is `2*atan(window)`. Every camera in the file has
an aspect of exactly 1.3333.

`CAMERA_SCREEN`, the round view:

    position     (-207.531, 275.325, 472.241)
    forward      (-0.7075, -0.0010, -0.7067)
    up           (-0.0007,  1.0000, -0.0007)
    fov          50.000 horizontal, 38.553 vertical
    near / far   86.379 / 12956.890

It does not point at `MODEL_JUMBOTRON_INGAME`. It points into the world, at the
surface the world's material list calls `BZ_Set03_Videowall01`. That is the
round screen, and the gold bezel with its light strips is the geometry around
it.

`obz camera list` and `obz camera export` read all of this.

## The lights

`Lights*.rp2` are the studio's lighting, one file per mood: neutral, intro, red
tension, round win, game win, two celebrations and a white-out. Each holds the
same seven point lights under the same names, and only the colours change
between them:

    ANIMATEDLIGHT_CONTSPOT   LIGHT_CONTESTANTPOOL
    ANIMATEDLIGHT_HOSTSPOT   LIGHT_HOSTPLATFORMPOOL
    ANIMATEDLIGHT_MONISPOT   LIGHT_MONITORPOOL
    LIGHT_DOME

The file is shaped exactly like `StudioCameras.rp2`, so it reads the same way.
The `LIGHT` struct is 24 bytes: radius, colour as three floats, minus the
cosine of the half-angle, then type and flags packed into one word. Every light
in every rig is type `0x80`, a point light, so the cone angle is unused.

The positions confirm the names against the set: `CONTSPOT` sits over the
contestant marks, `HOSTSPOT` over `MODEL_SET_LECTERN`, and `MONISPOT` out at
x = -538, on the negative-X side where `CAMERA_SCREEN` is pointed. That is the
monitor the round is played on, and it is a different screen from the jumbotron.

Colours run above 1 - a spot sits at 2.0, and the white-out pools at 5.0 - so
they are multipliers, not 0..1 colours.

`obz light list` and `obz light export` read them.

### What the rig is, and is not

The rig is not the illumination. Its lights reach 350 to 700 units into a set
that spans nearly 6000, so lighting the studio from the rig alone leaves
everything outside those pools black, and raising its scale does almost nothing
- the set's textures already carry their lighting, the way a PS2 set does. The
rig is a coloured wash over the top.

So the port lights it as flat white ambient at 0.85, which for this albedo is
close to the set as painted, with the rig adding its pools on top. That mapping
is the one approximation here; the rig's own numbers are read exactly.

Two Godot-specific traps cost time and are worth writing down. The scene shipped
`ambient_light_source = 1`, which is DISABLED and not COLOR, so every change to
the ambient energy did nothing at all. And the placeholder directional lights
have to be switched off when a rig goes in, or they drown it - which looks
exactly like the rig being too bright.

## The screen layout

`GenericData.clu` keeps the 640x480 layout as plain constant assignment, with a
`RoundParameters` table holding a defaults entry and per-round overrides.
`obz layout show` and `obz layout export` recover it.

    QuestionTextPositionX      142     AnswerPositionXStart     142
    QuestionTextPositionY        6     AnswerPositionYStart      92
    QuestionTextWidth          430     AnswerPositionXInc         0
    QuestionTextHeight          80     AnswerPositionYInc        64
                                       AnswerPositionWidth      390
    StartX                     132     AnswerPositionHeight      55
    StartY                     360
    Width / Height           80/80     CONST_QuestionAnswerOffsetX   -44
    BlockGap                    25     CONST_QuestionAnswerOffsetY_OneLine 11

The three single-player rounds - Time Builder, Speed Time Builder, Hot Seat -
override `StartX` to 228, which is also `SinglePlayerViewportX`.

One trap: this build encodes RK operands against a threshold of 250, not the
256 released Lua 5.0 uses. `LuaOpcodes` already carried that finding; a fresh
extractor that assumed 256 read every constant six slots off and returned
`QuestionTextPositionX` as 67.

## Still not read

The countdown pie's position. `CountdownTimerIconX/Y` is (523, 17), the top
right, and the reference plainly shows the pie at the top left, so that is a
different element and the pie is still placed by eye.

The host and the hostess. This one is settled rather than open: their positions
are not on the disc at all. Six checks, each independent:

  * No `DUMMYNODE` for either in `StudioModels`, `StudioLights`,
    `StudioParticles` or any `GreenRoom` file.
  * The executable's own list of node names it looks up has
    `DUMMYNODE_CONTESTANT_`, `DUMMYNODE_CLOCK_CENTRE`, `DUMMYNODE_PRIZEROOM`,
    the four spot cones and the six light groups - and nothing for a presenter.
    It does hold `ANIMATEDMODEL_HOST` and `ANIMATEDMODEL_HOSTESS`, so it loads
    them by name and places them from code.
  * Their clump root frames sit at the origin. So do the contestants', which is
    the point: contestants get moved to a marker afterwards.
  * All 53 host clips and all 11 hostess clips keep their root at the origin.
  * Their geometry is not authored in place either - the mesh bounds centre on
    their own origin, not on any part of the set.
  * `ANIMATEDMODEL_GRHOST`, the green-room host, is a placed set piece, and even
    that sits at (0, 0, 0). Characters are never placed by the set in this
    engine.

The nearest anchor the data offers is the light rig. It has three POOL and SPOT
pairs for three locations - contestants, host platform, monitor - and CONTSPOT
is demonstrably the contestants', so HOSTSPOT is the host's key light and
MONISPOT the hostess's. Projected straight down onto `MODEL_WALKWAY_GLASS`,
whose top face is at y = 53.2, that puts the host at (-3.5, 53.2, 161.7) and the
hostess at (-538.1, 53.2, 143.4), which is what the port uses.

It is worth being clear that this does not reproduce the reference. Under
CAMERA_SCREEN the hostess comes out dead centre laterally - within one unit -
but 25.4 degrees below the axis, where the frame's half-height is 19.3, so she
falls just under the bottom edge. The reference has her at the right of frame
and about half its height. So the anchor is the best the disc supports and it
is still not where the game puts her.

What is left is disassembling MIPS around the `ANIMATEDMODEL_HOSTESS` reference
in the executable. That is a different kind of job from reading a format.
