class_name GreenRoom
extends Node3D

## The foyer the menus happen in, and the clipboard they are written on.
##
## `MainMenuAnimationSetup` does this, in order: HideStudio, ShowGreenRoom,
## SetCameraAngleForGreenRoom(GetCameraNameGreenRoom(5)), ShowGreenRoomModels(5),
## then animations on the floor manager and the two doors, and
## ShowStaticClipboard. So the menu is not drawn on the studio's video wall at
## all - it is written on a prop the floor manager is holding, in a different
## room, under a different camera.
##
## Three streams make it up. `GreenRoomScene` is the world shell,
## `GreenRoomProps` the walls, floor, doors, chairs, plasma screens and the ON
## AIR sign, and `GreenRoomModels` the people: the floor manager, the green-room
## host and the two goons on the lift doors. `GreenRoomCameras` and
## `GreenRoomLights` are its own; the lights are named FOYER, which is what the
## room is.

## The material the clipboard's paper uses. Finding it is how the menu gets on
## the prop, exactly as the round screen gets on the video wall.
const CLIPBOARD_MATERIAL := "BZ_texture_clipboard"

## The camera the main menu sits on: GetCameraNameGreenRoom(5).
const MENU_CAMERA := "ANIMATEDCAMERA_GREENROOM05"

const STREAMS := ["GreenRoomScene", "GreenRoomProps", "GreenRoomModels"]

var _cameras := {}
var _pieces := {}


func build(sheet: Texture2D, models_dir: String, bundle_dir: String) -> bool:
	var loaded := 0
	for stream in STREAMS:
		if _load(models_dir.path_join(stream + ".glb")):
			loaded += 1

	if loaded == 0:
		Log.warn("greenroom", "no green room models under %s" % models_dir)
		return false

	_load_cameras(bundle_dir)
	_load_lights(bundle_dir)

	var hung := show_on_clipboard(sheet)
	Log.info("greenroom", "%d streams, %d pieces, %d cameras, clipboard on %d surfaces" % [
		loaded, _pieces.size(), _cameras.size(), hung])
	return true


func _load(path: String) -> bool:
	if not FileAccess.file_exists(path):
		return false

	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	if doc.append_from_file(path, state) != OK:
		Log.warn("greenroom", "could not read %s" % path.get_file())
		return false

	var scene := doc.generate_scene(state)
	if scene == null:
		return false
	add_child(scene)
	for child in scene.get_children():
		_pieces[str(child.name)] = child
	return true


## The green room's own cameras, kept apart from the studio's fifty-one.
func _load_cameras(bundle_dir: String) -> void:
	var path := bundle_dir.path_join("greenroom-cameras.json")
	if not FileAccess.file_exists(path):
		return
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(path))
	if parsed is Dictionary:
		_cameras = parsed


## Points a camera at what the menu should see. The convention is the studio's,
## checked there against the contestant marks: forward and up are the negated
## second and third rows of the view matrix.
func aim(camera: Camera3D, name := MENU_CAMERA) -> bool:
	if not _cameras.has(name):
		Log.warn("greenroom", "no camera named %s" % name)
		return false

	var c: Dictionary = _cameras[name]
	var pos: Array = c.get("position", [0, 0, 0])
	var fwd: Array = c.get("forward", [0, 0, -1])
	var up: Array = c.get("up", [0, 1, 0])

	var eye := Vector3(pos[0], pos[1], pos[2])
	camera.global_position = eye
	camera.look_at_from_position(eye, eye + Vector3(fwd[0], fwd[1], fwd[2]),
		Vector3(up[0], up[1], up[2]))
	camera.fov = float(c.get("fovVertical", 34.52))
	camera.near = maxf(float(c.get("near", 25.0)), 0.05)
	camera.far = float(c.get("far", 1700.0))
	Log.info("greenroom", "%s pos=%s fov=%.3f" % [name, str(eye), camera.fov])
	return true


## The foyer rig: four directionals and nine omnis, all named FOYER.
func _load_lights(bundle_dir: String) -> void:
	var path := bundle_dir.path_join("greenroom-lights.json")
	if not FileAccess.file_exists(path):
		return
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(path))
	if not (parsed is Dictionary):
		return

	var made := 0
	for rig in parsed.values():
		for light in rig:
			var l: Dictionary = light
			var colour: Array = l.get("colour", [1, 1, 1])
			var pos: Array = l.get("position", [0, 0, 0])
			var tint := Color(colour[0], colour[1], colour[2])

			if str(l.get("kind", "point")) == "directional":
				var sun := DirectionalLight3D.new()
				sun.light_color = tint
				sun.light_energy = 0.35
				# A directional light has no place, only a heading, and the
				# stream stores that heading as a position off the origin.
				sun.look_at_from_position(Vector3.ZERO,
					-Vector3(pos[0], pos[1], pos[2]), Vector3.UP)
				add_child(sun)
			else:
				var lamp := OmniLight3D.new()
				lamp.light_color = tint
				lamp.light_energy = 1.4
				lamp.omni_range = maxf(float(l.get("radius", 300.0)), 1.0)
				lamp.position = Vector3(pos[0], pos[1], pos[2])
				add_child(lamp)
			made += 1
	Log.info("greenroom", "foyer rig: %d lights" % made)


## Hangs the menu on the clipboard, the same swap the video wall gets.
func show_on_clipboard(sheet: Texture2D) -> int:
	if sheet == null:
		return 0

	var hung := 0
	for mesh in _all_meshes(self):
		if mesh.mesh == null:
			continue
		for i in range(mesh.mesh.get_surface_count()):
			var base := mesh.mesh.surface_get_material(i) as BaseMaterial3D
			if base == null or base.resource_name != CLIPBOARD_MATERIAL:
				continue
			var paper := StandardMaterial3D.new()
			paper.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
			paper.albedo_texture = sheet
			paper.texture_filter = BaseMaterial3D.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS
			paper.cull_mode = BaseMaterial3D.CULL_DISABLED
			mesh.set_surface_override_material(i, paper)
			hung += 1
	return hung


func _all_meshes(node: Node) -> Array[MeshInstance3D]:
	var found: Array[MeshInstance3D] = []
	for child in node.get_children():
		if child is MeshInstance3D:
			found.append(child)
		found.append_array(_all_meshes(child))
	return found
