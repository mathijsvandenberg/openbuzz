extends Control

## Plays the game's 2D layer: A2D timelines drawn with its own atlas sprites
## and bitmap fonts.
##
## Reads the bundle `obz bundle` writes - PNG atlases plus JSON tables of sprite
## rectangles, glyph rectangles and resolved strings - so nothing here has to
## know about .uvs files, the PS2 texture swizzle or the 16-bit float in the
## font metrics. It is a reader of plain data.

const CANVAS := Vector2(640, 480)
const FPS := 25.0

@onready var _scenes: ItemList = %Scenes
@onready var _clips: ItemList = %Clips
@onready var _status: Label = %A2dStatus
@onready var _canvas: Control = %Canvas

var _dir := ""
var _sprites := {}
var _fonts := {}
var _text := {}
var _atlases := {}
var _glyph_sheets := {}

var _icon_bindings := {}
var _text_bindings := {}

var _scene_paths: Array[String] = []
var _animations: Array = []
var _current: Dictionary = {}
var _frame := 0.0
var _playing := true


func _ready() -> void:
	_dir = _find_bundle()
	if _dir.is_empty():
		_status.text = "Could not find extracted/godot2d. Run 'obz bundle' first."
		return

	_sprites = _read_json(_dir.path_join("sprites.json"))
	_fonts = _read_json(_dir.path_join("fonts.json"))
	_text = _read_json(_dir.path_join("text.json"))

	var scene_dir := _dir.path_join("scene")
	var handle := DirAccess.open(scene_dir)
	if handle == null:
		_status.text = "No scenes in %s" % scene_dir
		return

	var names := PackedStringArray()
	for f in handle.get_files():
		if f.get_extension().to_lower() == "json":
			names.append(f)
	names.sort()

	# Bindings are declared globally in Animation2dSetup, not per scene.
	for n in names:
		var data := _read_json(scene_dir.path_join(n))
		for actor in data.get("IconBindings", {}):
			_icon_bindings[actor] = data["IconBindings"][actor]
		for actor in data.get("TextBindings", {}):
			_text_bindings[actor] = data["TextBindings"][actor]

	for n in names:
		var data := _read_json(scene_dir.path_join(n))
		if data.get("Animations", []).is_empty():
			continue
		_scene_paths.append(scene_dir.path_join(n))
		_scenes.add_item(str(data.get("Name", n.get_basename())))

	if _scene_paths.is_empty():
		_status.text = "No animated scenes found."
		return

	_scenes.item_selected.connect(_on_scene_selected)
	_clips.item_selected.connect(_on_clip_selected)
	_canvas.draw.connect(_draw_canvas)
	_scenes.select(0)
	_on_scene_selected(0)


func _find_bundle() -> String:
	var base := OS.get_executable_path().get_base_dir()
	if OS.has_feature("editor"):
		base = ProjectSettings.globalize_path("res://")

	var d := base
	for i in range(6):
		var candidate := d.path_join("extracted/godot2d")
		if DirAccess.dir_exists_absolute(candidate):
			return candidate
		var up := d.get_base_dir()
		if up == d:
			break
		d = up
	return ""


func _read_json(path: String) -> Dictionary:
	var text := FileAccess.get_file_as_string(path)
	if text.is_empty():
		return {}
	var parsed = JSON.parse_string(text)
	return parsed if parsed is Dictionary else {}


func _atlas(name: String) -> Texture2D:
	if _atlases.has(name):
		return _atlases[name]
	var image := Image.load_from_file(_dir.path_join("atlas/%s.png" % name))
	var tex: Texture2D = ImageTexture.create_from_image(image) if image != null else null
	_atlases[name] = tex
	return tex


func _glyph_sheet(name: String) -> Texture2D:
	if _glyph_sheets.has(name):
		return _glyph_sheets[name]
	var image := Image.load_from_file(_dir.path_join("font/%s.png" % name))
	var tex: Texture2D = ImageTexture.create_from_image(image) if image != null else null
	_glyph_sheets[name] = tex
	return tex


func _on_scene_selected(index: int) -> void:
	var data := _read_json(_scene_paths[index])
	_animations = data.get("Animations", [])
	_clips.clear()
	for a in _animations:
		_clips.add_item("%s  (%d)" % [a.get("Name", "?"), int(a.get("FrameCount", 0))])
	if _clips.item_count > 0:
		_clips.select(0)
		_on_clip_selected(0)


func _on_clip_selected(index: int) -> void:
	_current = _animations[index]
	_frame = 0.0
	var objects: Array = _current.get("Objects", [])
	var bound := 0
	for o in objects:
		if _icon_bindings.has(o.get("Name", "")) or _text_bindings.has(o.get("Name", "")):
			bound += 1
	_status.text = "%d frames, %d objects, %d bound" % [
		int(_current.get("FrameCount", 0)), objects.size(), bound]


func _process(delta: float) -> void:
	if _current.is_empty() or not _playing:
		return
	var frames := float(_current.get("FrameCount", 1))
	_frame = fmod(_frame + delta * FPS, maxf(frames, 1.0))
	_canvas.queue_redraw()


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and event.keycode == KEY_SPACE:
		_playing = not _playing


## One frame, in the 640x480 design space scaled to fit the panel.
func _draw_canvas() -> void:
	var view := _canvas.size
	var scale := minf(view.x / CANVAS.x, view.y / CANVAS.y)
	var offset := (view - CANVAS * scale) * 0.5

	_canvas.draw_rect(Rect2(offset, CANVAS * scale), Color(0.06, 0.06, 0.08))
	if _current.is_empty():
		return

	var frame := int(_frame)
	for obj in _current.get("Objects", []):
		var t := _sample(obj.get("Transform", []), frame)
		if t.is_empty():
			continue

		var colour := _sample(obj.get("Colour", []), frame)
		var alpha := float(colour.get("A", 1.0)) if not colour.is_empty() else 1.0
		if alpha <= 0.004:
			continue

		var box: Dictionary = obj.get("Box", {})
		var left := float(box.get("Left", -8.0))
		var top := float(box.get("Top", 8.0))
		var width := float(box.get("Width", 16.0))
		var height := float(box.get("Height", 16.0))

		# The design space has y up; the panel has y down.
		var origin := Vector2(float(t.get("X", 0.0)), CANVAS.y - float(t.get("Y", 0.0)))
		var sx := float(t.get("ScaleX", 1.0))
		var sy := float(t.get("ScaleY", 1.0))
		var dest := Rect2(
			offset + (origin + Vector2(left * sx, -top * sy)) * scale,
			Vector2(width * sx, height * sy) * scale)

		var name := str(obj.get("Name", ""))
		var tint := Color(1, 1, 1, alpha)

		if _icon_bindings.has(name):
			_draw_sprite(str(_icon_bindings[name]), dest, tint)
		elif _text_bindings.has(name):
			_draw_text(_text_bindings[name], dest, tint, scale)


func _draw_sprite(sprite_name: String, dest: Rect2, tint: Color) -> void:
	if not _sprites.has(sprite_name):
		return
	var s: Dictionary = _sprites[sprite_name]
	var tex := _atlas(str(s["atlas"]))
	if tex == null:
		return
	var src := Rect2(float(s["x"]), float(s["y"]), float(s["w"]), float(s["h"]))
	_canvas.draw_texture_rect_region(tex, dest, src, tint)


func _draw_text(binding: Dictionary, dest: Rect2, tint: Color, scale: float) -> void:
	var key := str(binding.get("Key", ""))
	var body: String = _text.get(key, key)
	var resolved := _text.has(key)

	var style := str(binding.get("Style", "GeneralLarge"))
	if not _fonts.has(style):
		style = "GeneralLarge"
	if not _fonts.has(style):
		return

	var font: Dictionary = _fonts[style]
	var sheet := _glyph_sheet(str(font["texture"]))
	if sheet == null:
		return

	var glyphs: Dictionary = font["glyphs"]
	var line_step := float(font["lineStep"])
	var size := float(binding.get("SizeMultiplier", 1.0))
	var pixels := clampf(size, 0.2, 2.2) * scale

	# Shrink rather than overflow when a translation runs long.
	var lines := _wrap(body, glyphs, dest.size.x / maxf(pixels, 0.001))
	var widest := 0.0
	for line in lines:
		widest = maxf(widest, _measure(line, glyphs))
	if widest * pixels > dest.size.x and widest > 0.0:
		pixels *= dest.size.x / (widest * pixels)
		lines = _wrap(body, glyphs, dest.size.x / maxf(pixels, 0.001))

	# An unresolved key draws as the key itself, in grey, so a placeholder is
	# visibly a placeholder rather than quietly reading as content.
	var colour := tint if resolved else Color(0.62, 0.64, 0.7, tint.a)
	var justify := str(binding.get("HorizontalJustify", "Left"))
	var y := dest.position.y + maxf(0.0, (dest.size.y - lines.size() * line_step * pixels) * 0.5)

	for line in lines:
		var w := _measure(line, glyphs) * pixels
		var x := dest.position.x
		if justify == "Centre":
			x += (dest.size.x - w) * 0.5
		elif justify == "Right":
			x += dest.size.x - w

		for c in line:
			if not glyphs.has(c):
				x += line_step * 0.25 * pixels
				continue
			var g: Dictionary = glyphs[c]
			var src := Rect2(float(g["x"]), float(g["y"]), float(g["w"]), float(g["h"]))
			_canvas.draw_texture_rect_region(
				sheet, Rect2(Vector2(x, y), src.size * pixels), src, colour)
			x += float(g["advance"]) * pixels
		y += line_step * pixels


func _measure(text: String, glyphs: Dictionary) -> float:
	var w := 0.0
	for c in text:
		w += float(glyphs[c]["advance"]) if glyphs.has(c) else 8.0
	return w


func _wrap(text: String, glyphs: Dictionary, width: float) -> PackedStringArray:
	var lines := PackedStringArray()
	var line := ""
	for word in text.split(" ", false):
		var candidate := word if line.is_empty() else line + " " + word
		if _measure(candidate, glyphs) <= width or line.is_empty():
			line = candidate
		else:
			lines.append(line)
			line = word
	if not line.is_empty():
		lines.append(line)
	return lines


## Latest keyframe at or before the frame, as the game does it.
func _sample(keys: Array, frame: int) -> Dictionary:
	if keys.is_empty():
		return {}
	var best: Dictionary = {}
	for k in keys:
		if int(k.get("Frame", 0)) <= frame:
			best = k
		else:
			break
	return best if not best.is_empty() else keys[0]
