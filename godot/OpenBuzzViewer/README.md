# OpenBuzz Viewer

Two tabs, built with Godot 4.7.2 (.NET).

**Models** - pick a character, pick a clip, watch it play.

**2D layer** - the A2D timelines, drawn with the game's own atlas sprites and
bitmap fonts. Space pauses.

```bash
dist/obz-viewer.exe
```

Drag to orbit, wheel to zoom.

## Where the data comes from

Nothing is built into the executable. Both tabs read from `extracted/`, walking
up from wherever the exe sits:

```bash
obz model export     # -> extracted/models/*.glb    for the Models tab
obz bundle           # -> extracted/godot2d/        for the 2D layer tab
```

Models load at runtime through `GLTFDocument` rather than as imported
resources. The 2D layer reads the bundle: PNG atlases plus JSON tables of
sprite rectangles, glyph rectangles and resolved strings. So the engine side is
a reader of plain data - it knows nothing about `.uvs` files, the PS2 texture
swizzle, or the 16-bit float in the font metrics.

## Building it

Needs Godot 4.7.2 with the matching export templates installed.

```bash
godot --headless --path godot/OpenBuzzViewer --export-release "Windows Desktop"
```

The preset writes to `dist/obz-viewer.exe` with the pack embedded, so the result
is a single file like the other tools.

## Checking it without looking

`--tab <n> --shot <file>` renders one frame of a tab and exits, which is how
the build gets verified:

```bash
dist/obz-viewer.exe -- --tab 1 --shot check.png
```
