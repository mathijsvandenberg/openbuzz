class_name Studio
extends Node3D

## The television studio, as the game builds it.
##
## `StudioModels.rp2` is not a model: it is a set of forty-three named pieces
## and seven markers, each placed by its own root frame. The names are the whole
## point - MODEL_JUMBOTRON_INGAME is the screen the round is played on,
## DUMMYNODE_CONTESTANT_1..4 are where the players stand, MODEL_PODIUM_SINGLE
## and MODEL_PODIUM_MULTI are the two stagings. Nothing here is positioned by
## eye; every number comes out of the file.
##
## The one thing the file does not hold is the camera. QuizSupportCode_
## CameraAngles.luaasm names the angles - CAMERA_SCREEN, CAMERA_CONTESTANTS,
## CAMERA_HOST, CAMERA_STUDIO, CAMERA_SINGLEPLAYER - but their coordinates live
## in the executable. CAMERA_SCREEN is therefore derived rather than read: the
## jumbotron is a flat quad of known size and facing, so squaring the camera to
## it and backing off far enough to frame it is not a guess.

## The screen the round is played on: a flat quad, 580 x 436, upright, facing
## +Z, centred at (0, 309, -397). Its own geometry says so.
const SCREEN_PIECE := "MODEL_JUMBOTRON_INGAME"

## Pieces that belong to the prize room, which is a different scene.
## ANIMATEDMODEL_JUMBOTRON is deliberately not here: it is the cabinet the
## screen sits in - the bezel and lights in the reference shots - and hiding it
## left the picture floating with nothing round it.
const HIDDEN := [
	"MODEL_PRIZEROOM", "MODEL_PRIZEROOM_LIGHTS", "MODEL_PRIZEROOM_STARS",
	"MODEL_PRIZEROOM_LOGO",
]

var _pieces := {}
var _root: Node3D = null
var _render := {}


func load_set(models_dir: String) -> bool:
	var path := models_dir.path_join("StudioModels.glb")
	if not FileAccess.file_exists(path):
		return false

	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	if doc.append_from_file(path, state) != OK:
		return false

	_root = doc.generate_scene(state) as Node3D
	if _root == null:
		return false

	add_child(_root)
	_index(_root)

	_render = _read_render_states(path)
	_apply_render_states()

	for name in HIDDEN:
		hide_piece(name)
	return true


## The render state the exporter carried through, read straight out of the
## GLB rather than relying on how the importer treats extras.
static func _read_render_states(path: String) -> Dictionary:
	var out := {}
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return out

	f.seek(12)                        # past the GLB header
	var length := f.get_32()
	var kind := f.get_32()
	if kind != 0x4E4F534A:            # the JSON chunk comes first
		return out

	var parsed = JSON.parse_string(f.get_buffer(length).get_string_from_utf8())
	if not (parsed is Dictionary) or not parsed.has("nodes"):
		return out

	for node in parsed["nodes"]:
		var extras = node.get("extras", {})
		if extras is Dictionary and extras.has("render"):
			out[str(node.get("name", ""))] = str(extras["render"])
	return out


## How the set is drawn.
##
## Every piece is unshaded. A PS2 set carries its lighting in the texture, and
## relighting it here washed the dome out to white and flattened everything the
## artist painted in. The additive pieces - the flares, the ground light, the
## podium glows - additionally blend rather than cover, which is what the
## NORM_ADDITIVE_BLENDING state in the file means; drawn opaque they came out
## as black slabs standing across the studio.
func _apply_render_states() -> void:
	for part in _all_meshes(_root):
		var mesh := part as MeshInstance3D
		var state := str(_render.get(str(mesh.name), "NORM_DEFAULT"))

		var material := StandardMaterial3D.new()
		material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		material.albedo_texture = _texture_of(mesh)
		material.cull_mode = BaseMaterial3D.CULL_DISABLED

		if state.contains("ADDITIVE"):
			material.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
			material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		else:
			material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA_SCISSOR

		mesh.material_override = material


static func _all_meshes(node: Node) -> Array:
	var out := []
	if node is MeshInstance3D:
		out.append(node)
	for child in node.get_children():
		out.append_array(_all_meshes(child))
	return out


static func _texture_of(mesh: MeshInstance3D) -> Texture2D:
	if mesh.mesh == null or mesh.mesh.get_surface_count() == 0:
		return null
	var base := mesh.mesh.surface_get_material(0) as BaseMaterial3D
	return null if base == null else base.albedo_texture


## Pieces are exported one node per name, but a piece made of several meshes
## gets a _0, _1 suffix, so a name matches a group and not just a node.
func _index(node: Node) -> void:
	for child in node.get_children():
		var stem := _stem(str(child.name))
		if not _pieces.has(stem):
			_pieces[stem] = []
		_pieces[stem].append(child)
		_index(child)


## A piece made of several meshes is exported with a _0, _1 suffix, so the
## group name is the node name with any trailing index taken off.
static func _stem(name: String) -> String:
	var cut := name.rfind("_")
	if cut > 0 and name.substr(cut + 1).is_valid_int():
		return name.substr(0, cut)
	return name


func parts(name: String) -> Array:
	return _pieces.get(name, [])


func hide_piece(name: String) -> void:
	set_piece_visible(name, false)


func set_piece_visible(name: String, on: bool) -> void:
	for part in parts(name):
		if part is Node3D:
			part.visible = on


## Where a marker sits, in world space. Markers draw nothing; they exist to be
## asked this question.
func marker(name: String) -> Vector3:
	var found := parts(name)
	if found.is_empty():
		return Vector3.ZERO
	return (found[0] as Node3D).global_position


## The staging: four podiums for a multiplayer game, one for a single player.
func stage_for(players: int) -> void:
	var multi := players > 1
	set_piece_visible("MODEL_STAGE_MULTI", multi)
	set_piece_visible("MODEL_PODIUM_MULTI", multi)
	set_piece_visible("MODEL_STAGE_SINGLE", not multi)
	set_piece_visible("MODEL_PODIUM_SINGLE", not multi)
	for i in range(1, 5):
		set_piece_visible("MODEL_PODIUMGLOW_%d" % i, multi or i == 1)


# ------------------------------------------------------------------ the screen

## The screen quad in world space: centre, half-width, half-height. Taken from
## the piece rather than typed in, so it survives a re-export.
func screen_rect() -> Dictionary:
	var found := parts(SCREEN_PIECE)
	if found.is_empty():
		return {}

	var mesh := found[0] as MeshInstance3D
	if mesh == null:
		return {}

	var box := mesh.get_aabb()
	var basis := mesh.global_transform.basis
	# The quad is flat in its own XZ, so its local Y is the way it faces.
	return {
		centre = mesh.global_transform * box.get_center(),
		right = basis.x * box.size.x * 0.5,
		up = -basis.z * box.size.z * 0.5,
		normal = basis.y.normalized(),
	}


## The whole jumbotron as it appears in shot: the screen and the two light
## bars. JUMBOFLARES_1 sits directly above the screen and _2 directly below it,
## which is what makes the lit strips along the top and bottom edge of the
## reference. Framing the screen alone cropped both of them off.
const FRAME_PIECES := [SCREEN_PIECE, "MODEL_JUMBOFLARES_1", "MODEL_JUMBOFLARES_2"]


## The studio floor, taken from the foot of the jumbotron rather than assumed
## to be zero.
func floor_level() -> float:
	var box := jumbotron_bounds()
	return box.position.y


func jumbotron_bounds() -> AABB:
	var box := AABB()
	var first := true
	for name in FRAME_PIECES:
		for part in parts(name):
			var mesh := part as MeshInstance3D
			if mesh == null:
				continue
			var world := mesh.global_transform * mesh.get_aabb()
			box = world if first else box.merge(world)
			first = false
	return box


## Squares the camera on the jumbotron and backs off until it frames it.
## `margin` is how much room to leave round the edge.
func aim_at_screen(camera: Camera3D, aspect: float, margin := 1.06) -> void:
	var rect := screen_rect()
	if rect.is_empty():
		return

	var box := jumbotron_bounds()
	var half_w := box.size.x * 0.5 * margin
	var half_h := box.size.y * 0.5 * margin

	# Godot's fov is vertical, and widens to the horizontal by the aspect, so
	# whichever of the two needs more distance is the one that decides it.
	var vertical := tan(deg_to_rad(camera.fov) * 0.5)
	var distance := maxf(half_h / vertical, half_w / (vertical * maxf(aspect, 0.001)))

	# Square on. The reference shots have the screen's edges parallel to the
	# frame, so there is no tilt: dropping the camera to eye level to get the
	# hostess in shot keystoned the screen badly and was the wrong trade.
	var centre := box.get_center()
	camera.global_position = centre + (rect.normal as Vector3) * distance
	camera.look_at(centre, (rect.up as Vector3).normalized())


## Hangs a live texture on the screen, so what the round draws is what the
## studio shows. The piece is a flat quad with its own UVs; an unshaded
## material is right because a television screen emits rather than reflects.
func show_on_screen(texture: Texture2D) -> void:
	for part in parts(SCREEN_PIECE):
		var mesh := part as MeshInstance3D
		if mesh == null:
			continue
		var material := StandardMaterial3D.new()
		material.albedo_texture = texture
		material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		material.texture_filter = BaseMaterial3D.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS
		material.cull_mode = BaseMaterial3D.CULL_DISABLED
		mesh.material_override = material
