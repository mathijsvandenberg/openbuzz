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
## The cameras are data too, and not in the executable as I first thought:
## StudioCameras.rp2 holds all fifty-one of them as RenderWare CAMERA chunks,
## under the very names the Lua passes to SetCameraAngle. `obz camera export`
## writes them to cameras.json and `use_camera` puts one in place exactly -
## position, orientation, field of view and clip planes, nothing derived.

## The round is played on the studio video wall, which is a surface of the
## world and not a prop: the material WORLD_STUDIO gives it is
## BZ_Set03_Videowall01. CAMERA_SCREEN frames exactly this and nothing else,
## which is how it was found - it does not point at MODEL_JUMBOTRON_INGAME.
const VIDEOWALL_MATERIAL := "BZ_Set03_Videowall01"

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
var _cameras := {}
var _rigs := {}
var _rig_root: Node3D = null

## Whether the set is being lit. Off, every surface is unshaded and shows the
## texture as painted, which is how a PS2 set is meant to look on its own.
var _lit := false


## The game renders 4:3, and the cameras say so themselves: every one of them
## has a view window whose aspect is exactly 1.3333.
const ASPECT := 4.0 / 3.0


func load_cameras(bundle_dir: String) -> bool:
	var path := bundle_dir.path_join("cameras.json")
	if not FileAccess.file_exists(path):
		return false
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(path))
	if not (parsed is Dictionary):
		return false
	_cameras = parsed
	return true


func camera_names() -> Array:
	return _cameras.keys()


## Puts a camera exactly where the game puts it.
##
## RenderWare gives a position, a forward and an up; Godot aims a camera at a
## point, so the target is one unit along forward. The view window is the
## half-extent of the frustum at unit distance, so the vertical field of view
## is 2*atan(window.y) - which the exporter has already worked out.
func use_camera(name: String, camera: Camera3D) -> bool:
	if not _cameras.has(name):
		return false

	var c: Dictionary = _cameras[name]
	camera.global_position = _vec(c["position"])
	camera.look_at(_vec(c["target"]), _vec(c["up"]))

	# Keep the height and let the width follow, because the fov the file gives
	# is a 4:3 frustum and the viewport is rendered 4:3 to match.
	camera.keep_aspect = Camera3D.KEEP_HEIGHT
	camera.fov = float(c["fovVertical"])
	camera.near = maxf(float(c["near"]), 0.05)
	camera.far = float(c["far"])
	return true


static func _vec(a) -> Vector3:
	return Vector3(float(a[0]), float(a[1]), float(a[2]))


## The eight light rigs, one per studio mood, out of `Lights*.rp2`.
func load_lights(bundle_dir: String) -> bool:
	var path := bundle_dir.path_join("lights.json")
	if not FileAccess.file_exists(path):
		return false
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(path))
	if not (parsed is Dictionary):
		return false
	_rigs = parsed
	return true


## Where a named light sits, from whichever rig is loaded. The positions are
## identical across all eight moods; only the colours change.
func has_light(name: String) -> bool:
	for mood in _rigs:
		for entry in _rigs[mood]:
			if str(entry.get("name", "")) == name:
				return true
	return false


func light_position(name: String) -> Vector3:
	for mood in _rigs:
		for entry in _rigs[mood]:
			if str(entry.get("name", "")) == name:
				return _vec(entry["position"])
	return Vector3.ZERO


func moods() -> Array:
	return _rigs.keys()


## <summary>
## Puts one mood's lights in the studio.
##
## All seven are point lights, so each becomes an OmniLight3D at the position
## and radius the file gives. The colours run above 1 - a spot sits at 2.0 and
## the white-out pools at 5.0 - so the brightest channel becomes the energy and
## the colour is normalised against it, which is the closest a renderer that
## separates the two can come to a straight multiplier.
##
## Lighting the set also means shading it, and that is a real change: unlit,
## every surface shows the texture exactly as the artist painted it, which is
## how the set is built. So this is opt-in rather than the default.
## </summary>
## <summary>
## How much of a RenderWare light to give a Godot one.
##
## This is the one number in the rig that is not read, and it cannot be: the
## file gives a linear multiplier for a fixed-function renderer that adds it to
## an already-painted texture, and Godot wants an energy for a physical light
## that illuminates an albedo. Handed over one for one, seven lights at 2.0 and
## a white-out at 5.0 blow the whole set out.
##
## The set is the reason it is small. Its textures already carry their lighting,
## so the rig is a coloured wash over them, not the illumination itself.
## </summary>
const LIGHT_SCALE := 1.0

## Flat white ambient standing in for "the texture as painted". Slightly under
## 1 because the rig then adds on top, and the two together should land where
## the unlit set already sits rather than above it.
const AMBIENT_BASE := 0.85


func use_mood(mood: String, scale := LIGHT_SCALE) -> int:
	if not _rigs.has(mood):
		return 0

	if _rig_root != null:
		_rig_root.queue_free()
	_rig_root = Node3D.new()
	_rig_root.name = "LightRig"
	add_child(_rig_root)

	var made := 0
	for entry in _rigs[mood]:
		var light := OmniLight3D.new()
		light.name = str(entry.get("name", "light"))
		light.position = _vec(entry["position"])
		light.omni_range = float(entry["radius"])

		var energy := maxf(float(entry.get("energy", 1.0)), 0.001)
		var c = entry["colour"]
		light.light_color = Color(float(c[0]) / energy, float(c[1]) / energy, float(c[2]) / energy)
		light.light_energy = energy * scale
		# A PS2 set of this vintage casts no shadow maps, and switching them on
		# here only makes the audience self-shadow into mud.
		light.shadow_enabled = false

		_rig_root.add_child(light)
		made += 1

	# Ambient goes to flat white at full strength, which for an unshaded-looking
	# albedo is the same picture as no lighting at all - the set as painted -
	# and the rig then adds its pools on top.
	#
	# The rig is not the illumination and cannot be. Its lights reach 350 to 700
	# units into a set that spans nearly 6000, so switching the ambient off left
	# everything outside those pools black, and raising the rig's own scale to
	# 2.5 barely touched it. The two placeholder directionals do go: they were
	# stand-ins for exactly this rig.
	_set_ambient(AMBIENT_BASE, Color(1, 1, 1))
	for node in _all_of_class(get_tree().current_scene, "DirectionalLight3D"):
		(node as DirectionalLight3D).visible = false
	var switched := _set_lit(true)
	if OS.get_cmdline_user_args().has("--lights-report"):
		print("LIGHTS mood=", mood, " lights=", made, " scale=", scale,
			" surfaces switched=", switched, " rigs=", _rigs.keys())
	return made


func _set_ambient(energy: float, colour := Color(1, 1, 1)) -> void:
	for node in _all_of_class(get_tree().current_scene, "WorldEnvironment"):
		var env := (node as WorldEnvironment).environment
		if env != null:
			# The scene ships this as DISABLED, which is why every change to the
			# ambient energy did nothing at all until it was switched to COLOR.
			env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
			env.ambient_light_energy = energy
			env.ambient_light_color = colour


static func _all_of_class(node: Node, cls: String) -> Array:
	var out := []
	if node == null:
		return out
	if node.get_class() == cls:
		out.append(node)
	for child in node.get_children():
		out.append_array(_all_of_class(child, cls))
	return out


## Switches every non-additive surface between showing its texture as painted
## and taking the rig's light. The additive pieces - flares, glows - stay
## unshaded either way, because they are light rather than lit.
func _set_lit(on: bool) -> int:
	_lit = on
	var switched := 0
	var mode := BaseMaterial3D.SHADING_MODE_PER_PIXEL if on else BaseMaterial3D.SHADING_MODE_UNSHADED

	for part in _all_meshes(self):
		var mesh := part as MeshInstance3D
		if mesh.mesh == null:
			continue

		# The additive pieces are light, not lit, so they stay unshaded either
		# way; and the video wall is a screen, which emits its own picture.
		var state := str(_render.get(str(mesh.name), "NORM_DEFAULT"))
		if state.contains("ADDITIVE"):
			continue

		for i in range(mesh.mesh.get_surface_count()):
			var material := mesh.get_surface_override_material(i) as StandardMaterial3D
			if material == null or material.resource_name == VIDEOWALL_MATERIAL:
				continue
			material.shading_mode = mode
			# A painted set has no specular of its own; leaving Godot's default
			# on put a sheen over the backdrop that read as blown-out.
			material.metallic = 0.0
			material.roughness = 1.0
			material.specular = 0.0
			switched += 1

	return switched


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

	# The set the props stand in. StudioModels holds only the props; the walls,
	# the floor and the round screen itself are WORLD_STUDIO in StudioScene,
	# which is what CAMERA_SCREEN is actually pointed at.
	_load_world(models_dir)

	_render = _read_render_states(path)
	_apply_render_states()

	for name in HIDDEN:
		hide_piece(name)
	return true


func _load_world(models_dir: String) -> void:
	var path := models_dir.path_join("StudioScene.glb")
	if not FileAccess.file_exists(path):
		return

	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	if doc.append_from_file(path, state) != OK:
		return

	var scene := doc.generate_scene(state)
	if scene == null:
		return

	add_child(scene)
	_index(scene)


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
	for part in _all_meshes(self):
		var mesh := part as MeshInstance3D
		var state := str(_render.get(str(mesh.name), "NORM_DEFAULT"))
		if mesh.mesh == null:
			continue

		# Per surface, not material_override: a world sector holds many
		# materials at once, and an override would flatten them all to one.
		for i in range(mesh.mesh.get_surface_count()):
			var base := mesh.mesh.surface_get_material(i) as BaseMaterial3D
			var material := StandardMaterial3D.new()
			material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
			material.cull_mode = BaseMaterial3D.CULL_DISABLED
			if base != null:
				material.albedo_texture = base.albedo_texture
				material.resource_name = base.resource_name

			if state.contains("ADDITIVE"):
				material.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
				material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
			else:
				material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA_SCISSOR

			mesh.set_surface_override_material(i, material)


static func _all_meshes(node: Node) -> Array:
	var out := []
	if node is MeshInstance3D:
		out.append(node)
	for child in node.get_children():
		out.append_array(_all_meshes(child))
	return out


## Hangs a live texture on the studio video wall, so what the round draws is
## what the studio shows. A screen emits rather than reflects, hence unshaded.
func show_on_screen(texture: Texture2D) -> int:
	var hung := 0
	for part in _all_meshes(self):
		var mesh := part as MeshInstance3D
		if mesh.mesh == null:
			continue
		for i in range(mesh.mesh.get_surface_count()):
			var base := mesh.mesh.surface_get_material(i) as BaseMaterial3D
			if base == null or base.resource_name != VIDEOWALL_MATERIAL:
				continue
			var material := StandardMaterial3D.new()
			material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
			material.albedo_texture = texture
			material.texture_filter = BaseMaterial3D.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS
			material.cull_mode = BaseMaterial3D.CULL_DISABLED
			mesh.set_surface_override_material(i, material)
			hung += 1
	return hung


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
