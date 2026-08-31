class_name Bundle
extends RefCounted

## The data `obz bundle` writes, and the drawing that goes with it.
##
## Atlas sprites and bitmap-font glyphs arrive as PNG plus JSON rectangles, so
## nothing here knows about .uvs files, the PS2 texture swizzle or the 16-bit
## float in the font metrics. Shared so the 2D player and the round draw text
## identically rather than drifting apart.

var dir := ""
var sprites := {}
var fonts := {}
var text := {}
var quiz := []

var _atlases := {}
var _sheets := {}


func load_from(start: String) -> bool:
	dir = _find(start)
	if dir.is_empty():
		return false

	sprites = _json(dir.path_join("sprites.json"))
	fonts = _json(dir.path_join("fonts.json"))
	text = _json(dir.path_join("text.json"))

	var parsed = JSON.parse_string(FileAccess.get_file_as_string(dir.path_join("quiz.json")))
	quiz = parsed if parsed is Array else []
	return true


## Walks up looking for the bundle, so a build can sit in dist/.
static func _find(start: String) -> String:
	var d := start
	for i in range(6):
		var candidate := d.path_join("extracted/godot2d")
		if DirAccess.dir_exists_absolute(candidate):
			return candidate
		var up := d.get_base_dir()
		if up == d:
			break
		d = up
	return ""


static func base_dir() -> String:
	if OS.has_feature("editor"):
		return ProjectSettings.globalize_path("res://")
	return OS.get_executable_path().get_base_dir()


func _json(path: String) -> Dictionary:
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(path))
	return parsed if parsed is Dictionary else {}


func atlas(name: String) -> Texture2D:
	if not _atlases.has(name):
		var image := Image.load_from_file(dir.path_join("atlas/%s.png" % name))
		_atlases[name] = ImageTexture.create_from_image(image) if image != null else null
	return _atlases[name]


func sheet(name: String) -> Texture2D:
	if not _sheets.has(name):
		var image := Image.load_from_file(dir.path_join("font/%s.png" % name))
		_sheets[name] = ImageTexture.create_from_image(image) if image != null else null
	return _sheets[name]


func draw_sprite(canvas: CanvasItem, sprite_name: String, dest: Rect2, tint: Color) -> void:
	if not sprites.has(sprite_name):
		return
	var s: Dictionary = sprites[sprite_name]
	var tex := atlas(str(s["atlas"]))
	if tex == null:
		return
	canvas.draw_texture_rect_region(
		tex, dest, Rect2(float(s["x"]), float(s["y"]), float(s["w"]), float(s["h"])), tint)


func font_or_default(style: String) -> String:
	if fonts.has(style):
		return style
	if fonts.has("GeneralLarge"):
		return "GeneralLarge"
	return fonts.keys()[0] if not fonts.is_empty() else ""


func measure(style: String, body: String, scale := 1.0) -> float:
	if not fonts.has(style):
		return 0.0
	var glyphs: Dictionary = fonts[style]["glyphs"]
	var w := 0.0
	for c in body:
		w += float(glyphs[c]["advance"]) if glyphs.has(c) else float(fonts[style]["lineStep"]) * 0.25
	return w * scale


func line_step(style: String) -> float:
	return float(fonts[style]["lineStep"]) if fonts.has(style) else 12.0


## Draws one line, returning the pen x after it.
func draw_line_of_text(canvas: CanvasItem, style: String, body: String,
		at: Vector2, scale: float, colour: Color) -> float:
	if not fonts.has(style):
		return at.x
	var font: Dictionary = fonts[style]
	var tex := sheet(str(font["texture"]))
	if tex == null:
		return at.x

	var glyphs: Dictionary = font["glyphs"]
	var x := at.x
	for c in body:
		if not glyphs.has(c):
			x += float(font["lineStep"]) * 0.25 * scale
			continue
		var g: Dictionary = glyphs[c]
		var src := Rect2(float(g["x"]), float(g["y"]), float(g["w"]), float(g["h"]))
		canvas.draw_texture_rect_region(tex, Rect2(Vector2(x, at.y), src.size * scale), src, colour)
		x += float(g["advance"]) * scale
	return x


## Word-wraps into a box and draws, vertically centred. Shrinks rather than
## overflowing when a translation runs long.
func draw_wrapped(canvas: CanvasItem, style: String, body: String, box: Rect2,
		scale: float, colour: Color, justify := "Centre") -> void:
	if not fonts.has(style) or box.size.x <= 0.0:
		return

	var lines := wrap_text(style, body, box.size.x / maxf(scale, 0.001))
	var widest := 0.0
	for line in lines:
		widest = maxf(widest, measure(style, line))
	if widest * scale > box.size.x and widest > 0.0:
		scale = box.size.x / widest
		lines = wrap_text(style, body, box.size.x / maxf(scale, 0.001))

	var step := line_step(style) * scale
	var y := box.position.y + maxf(0.0, (box.size.y - lines.size() * step) * 0.5)
	for line in lines:
		var w := measure(style, line, scale)
		var x := box.position.x
		if justify == "Centre":
			x += (box.size.x - w) * 0.5
		elif justify == "Right":
			x += box.size.x - w
		draw_line_of_text(canvas, style, line, Vector2(x, y), scale, colour)
		y += step


func wrap_text(style: String, body: String, width: float) -> PackedStringArray:
	var lines := PackedStringArray()
	var line := ""
	for word in body.split(" ", false):
		var candidate := word if line.is_empty() else line + " " + word
		if measure(style, candidate) <= width or line.is_empty():
			line = candidate
		else:
			lines.append(line)
			line = word
	if not line.is_empty():
		lines.append(line)
	return lines
