# OpenBuzz Model Viewer

Browses the models and animations `obz model export` writes: pick a character,
pick a clip, watch it play. Built with Godot 4.7.2 (.NET).

```bash
dist/obz-viewer.exe
```

Drag to orbit, wheel to zoom.

## Where the models come from

The viewer loads `.glb` files at **runtime** through `GLTFDocument` rather than
importing them as resources, so **no game data is built into the executable** -
it reads whatever is in `extracted/models`, walking up from wherever the exe
sits. Run `obz model export` first.

## Building it

Needs Godot 4.7.2 with the matching export templates installed.

```bash
godot --headless --path godot/OpenBuzzViewer --export-release "Windows Desktop"
```

The preset writes to `dist/obz-viewer.exe` with the pack embedded, so the result
is a single file like the other tools.

## Checking it without looking

`--shot <file>` renders one frame and exits, which is how the build gets
verified:

```bash
dist/obz-viewer.exe -- --shot check.png
```
