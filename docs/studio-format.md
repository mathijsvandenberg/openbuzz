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

The hostess. There is no `DUMMYNODE` for her anywhere in the set, her model and
all eleven of her animations are authored at the origin, and nothing in the
scripts assigns her a position. She stands at `MODEL_SET_LECTERN` for now.
