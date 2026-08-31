# glTF import smoke test

Checks that the models `obz model export` produces load into Godot with their
skeletons, skins and clips intact. Verified against **Godot 4.7.2 (.NET)**.

Copy a model in first - the `.gitignore` keeps game data out of the repo:

```bash
cp ../../extracted/models/AshCostume01.glb .
```

Then report what Godot sees:

```bash
godot --headless --path . --editor --quit-after 200
godot --headless --path . --script test.gd
```

Expected for a costume: one `Skeleton3D` with the character bone count, two
skinned `MeshInstance3D`, and an `AnimationPlayer` holding one animation per
clip - 16 for a character with an Animations and a WinAnimation stream.

`main.tscn` renders four frames of the first clip to `user://shots`, which is
where the pictures in the write-up came from. It needs a real window, so drop
`--headless`:

```bash
godot --path . --quit-after 400
```
