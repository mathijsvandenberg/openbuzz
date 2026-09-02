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

## The screen layout, out of GenericData.clu. `obz layout export` writes it.
var layout := {}
var charselect := {}

var _atlases := {}
var _sheets := {}


func load_from(start: String) -> bool:
	dir = _find(start)
	if dir.is_empty():
		return false

	layout = _json(dir.path_join("layout.json"))

	# The character select keeps its own positioning in CharacterSelectSupport,
	# derived from the 640x480 screen rather than written down: CONST_PanelWidth
	# is 640/5, CONST_TitleWidth is four panels, CONST_ControlIndent is half a
	# panel less 28.
	charselect = _json(dir.path_join("charselect.json"))
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


## One layout number, by name.
##
## The game keeps these in RoundParameters, with a Defaults table and per-round
## overrides, so a round asks for its own value and falls back to the default.
## `fallback` is only reached when the extraction could not resolve a field,
## which it reports as missing rather than guessing.
## One of CharacterSelectSupport's CONST_ values.
func cs_of(key: String, fallback: float) -> float:
	var globals: Dictionary = charselect.get("globals", {})
	return float(globals[key]) if globals.has(key) and globals[key] is float else fallback


## Where panel n starts, as GetXForPanel computes it: the panel start less 5,
## plus a panel per seat less 2, plus 7 - which lands on a whole panel each time.
func panel_x(seat: int) -> float:
	return (cs_of("CONST_PanelStart", 128.0) - 5.0) 		+ (cs_of("CONST_PanelInc", 128.0) * float(seat - 1) - 2.0) + 7.0


func layout_of(key: String, fallback: float, round_id := "") -> float:
	var rounds: Dictionary = layout.get("rounds", {})
	if round_id != "" and rounds.has(round_id):
		var own: Dictionary = rounds[round_id]
		if own.has(key) and own[key] is float:
			return float(own[key])

	var defaults: Dictionary = rounds.get("DefaultsID", {})
	if defaults.has(key) and defaults[key] is float:
		return float(defaults[key])

	var globals: Dictionary = layout.get("globals", {})
	if globals.has(key) and globals[key] is float:
		return float(globals[key])

	return fallback


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


## As draw_sprite, turned half a turn. PlaceAndRenderGenericGraphics places the
## charselect gradient twice and calls SetIconRotationDegrees(180) on the second,
## so the glow runs from the other corner.
func draw_sprite_flipped(canvas: CanvasItem, sprite_name: String, dest: Rect2, tint: Color) -> void:
	canvas.draw_set_transform(dest.position + dest.size * 0.5, PI, Vector2.ONE)
	draw_sprite(canvas, sprite_name, Rect2(-dest.size * 0.5, dest.size), tint)
	canvas.draw_set_transform(Vector2.ZERO, 0.0, Vector2.ONE)


func font_or_default(style: String) -> String:
	if fonts.has(style):
		return style
	if fonts.has("GeneralLarge"):
		return "GeneralLarge"
	return fonts.keys()[0] if not fonts.is_empty() else ""


## The game's bitmap fonts are 65 glyphs - capitals, digits and punctuation -
## with no accents in any of them, so five characters the quiz data does use
## have nothing to draw: E I O A with a diaeresis, and E acute. Left alone they
## come out as a hole in the middle of a word (BJ RN ULVAEUS), so they fold to
## the plain letter instead.
const FOLD := {
	"Ä": "A", "É": "E", "Ë": "E", "Ï": "I", "Ö": "O",
	"Ü": "U", "À": "A", "Á": "A", "Â": "A", "È": "E",
	"Ê": "E", "Ì": "I", "Í": "I", "Î": "I", "Ò": "O",
	"Ó": "O", "Ô": "O", "Ù": "U", "Ú": "U", "Û": "U",
	"Ç": "C", "Ñ": "N",
}


## The glyph to draw for a character, folding an accent away when the sheet has
## no glyph for it. Returns "" when there is nothing to draw at all.
func _glyph_key(glyphs: Dictionary, c: String) -> String:
	if glyphs.has(c):
		return c
	var folded: String = FOLD.get(c, "")
	return folded if folded != "" and glyphs.has(folded) else ""


func measure(style: String, body: String, scale := 1.0) -> float:
	if not fonts.has(style):
		return 0.0
	var glyphs: Dictionary = fonts[style]["glyphs"]
	var w := 0.0
	for c in body:
		var key := _glyph_key(glyphs, c)
		w += float(glyphs[key]["advance"]) if key != "" else float(fonts[style]["lineStep"]) * 0.25
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
		var key := _glyph_key(glyphs, c)
		if key == "":
			x += float(font["lineStep"]) * 0.25 * scale
			continue
		var g: Dictionary = glyphs[key]
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


## Draws on exactly one line, shrinking to fit rather than wrapping.
##
## The answers never wrap in the game, and they must not here either: the long
## ones are the Snap and Trigger Finger statements, which are whole sentences
## and are the round. Shrinking keeps all of one, where wrapping crowds the
## line under it and cutting would leave it unanswerable. `floor` is how far
## the type may shrink, as a share of the size asked for; past that the line is
## cut with an ellipsis, which in this data only the very longest statement
## reaches.
func draw_one_line(canvas: CanvasItem, style: String, body: String, box: Rect2,
		scale: float, colour: Color, justify := "Left", floor_share := 0.48) -> void:
	if not fonts.has(style) or box.size.x <= 0.0 or body.is_empty():
		return

	var width := measure(style, body, scale)
	if width > box.size.x:
		var wanted := box.size.x / maxf(width, 0.001)
		scale *= maxf(wanted, floor_share)

		# Still over even at the floor: cut, and say so with an ellipsis.
		if wanted < floor_share:
			var room := box.size.x - measure(style, "...", scale)
			while body.length() > 1 and measure(style, body, scale) > room:
				body = body.substr(0, body.length() - 1)
			body = body.strip_edges() + "..."

	var x := box.position.x
	var drawn := measure(style, body, scale)
	if justify == "Centre":
		x += (box.size.x - drawn) * 0.5
	elif justify == "Right":
		x += box.size.x - drawn

	var y := box.position.y + maxf(0.0, (box.size.y - line_step(style) * scale) * 0.5)
	draw_line_of_text(canvas, style, body, Vector2(x, y), scale, colour)


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
