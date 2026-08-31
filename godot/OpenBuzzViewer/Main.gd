extends Control

## Model and animation viewer.
##
## Loads the .glb files that `obz model export` writes, at runtime through
## GLTFDocument rather than as imported resources, so no game data is baked
## into the build. The models stay where they were extracted.

const CAMERA_HEIGHT := 0.55
const MIN_ZOOM := 0.6
const MAX_ZOOM := 4.0

@onready var _models: ItemList = %Models
@onready var _clips: ItemList = %Clips
@onready var _status: Label = %Status
@onready var _title: Label = %Title
@onready var _world: Node3D = %World
@onready var _camera: Camera3D = %Camera

var _paths: Array[String] = []
var _current: Node3D = null
var _player: AnimationPlayer = null
var _centre := Vector3.ZERO
var _radius := 1.0
var _yaw := 0.0
var _pitch := -0.12
var _zoom := 1.0
var _dragging := false

## Set by --shot <file>, which captures the view and exits. Kept so the build
## can be checked without a person having to look at a window.
var _shot_path := ""
var _shot_delay := 0


func _ready() -> void:
	var args := OS.get_cmdline_user_args()
	var at := args.find("--shot")
	if at >= 0 and at + 1 < args.size():
		_shot_path = args[at + 1]
		_shot_delay = 30

	var dir := _find_models_dir()
	if dir.is_empty():
		_status.text = "Could not find extracted/models. Run 'obz model export' first."
		return

	_title.text = dir
	for name in _list_models(dir):
		_paths.append(dir.path_join(name))
		_models.add_item(name.get_basename())

	if _paths.is_empty():
		_status.text = "No .glb files in %s" % dir
		return

	_models.item_selected.connect(_on_model_selected)
	_clips.item_selected.connect(_on_clip_selected)
	_models.select(0)
	_on_model_selected(0)


## Walks up from the executable looking for the export directory, so the build
## can sit in dist/ next to the other tools.
func _find_models_dir() -> String:
	var base := OS.get_executable_path().get_base_dir()
	if OS.has_feature("editor"):
		base = ProjectSettings.globalize_path("res://")

	var d := base
	for i in range(6):
		var candidate := d.path_join("extracted/models")
		if DirAccess.dir_exists_absolute(candidate):
			return candidate
		var up := d.get_base_dir()
		if up == d:
			break
		d = up
	return ""


func _list_models(dir: String) -> PackedStringArray:
	var names := PackedStringArray()
	var handle := DirAccess.open(dir)
	if handle == null:
		return names
	for f in handle.get_files():
		if f.get_extension().to_lower() == "glb":
			names.append(f)
	names.sort()
	return names


func _on_model_selected(index: int) -> void:
	if _current != null:
		_current.queue_free()
		_current = null
		_player = null
	_clips.clear()

	var path: String = _paths[index]
	var doc := GLTFDocument.new()
	var state := GLTFState.new()

	var err := doc.append_from_file(path, state)
	if err != OK:
		_status.text = "Failed to read %s (error %d)" % [path.get_file(), err]
		return

	var scene := doc.generate_scene(state)
	if scene == null:
		_status.text = "Nothing to show in %s" % path.get_file()
		return

	_current = scene
	_world.add_child(_current)

	_player = _find(_current, "AnimationPlayer") as AnimationPlayer
	var bones := 0
	var skeleton := _find(_current, "Skeleton3D")
	if skeleton != null:
		bones = (skeleton as Skeleton3D).get_bone_count()

	var meshes: Array[Node] = []
	_collect(_current, "MeshInstance3D", meshes)
	var vertices := 0
	for m in meshes:
		var mesh: Mesh = (m as MeshInstance3D).mesh
		if mesh != null:
			for s in mesh.get_surface_count():
				vertices += mesh.surface_get_arrays(s)[Mesh.ARRAY_VERTEX].size()

	_frame_model(meshes)

	if _player != null:
		for name in _player.get_animation_list():
			_clips.add_item(name)
		if _clips.item_count > 0:
			_clips.select(0)
			_on_clip_selected(0)

	_status.text = "%d meshes, %s vertices, %d bones, %d clips" % [
		meshes.size(), _thousands(vertices), bones,
		_player.get_animation_list().size() if _player != null else 0]


func _on_clip_selected(index: int) -> void:
	if _player == null:
		return
	var name := _clips.get_item_text(index)
	_player.play(name)
	var anim := _player.get_animation(name)
	if anim != null:
		anim.loop_mode = Animation.LOOP_LINEAR


## Points the camera at whatever was just loaded.
func _frame_model(meshes: Array[Node]) -> void:
	var box := AABB()
	var first := true
	for m in meshes:
		var mi := m as MeshInstance3D
		var world := mi.global_transform * mi.get_aabb()
		box = world if first else box.merge(world)
		first = false

	if first:
		_centre = Vector3.ZERO
		_radius = 1.0
		return

	_centre = box.get_center()
	_radius = maxf(box.size.x, maxf(box.size.y, box.size.z)) * 0.5
	if _radius <= 0.0:
		_radius = 1.0


func _process(_delta: float) -> void:
	if not _shot_path.is_empty():
		_shot_delay -= 1
		if _shot_delay == 0:
			await RenderingServer.frame_post_draw
			var image := get_viewport().get_texture().get_image()
			image.save_png(_shot_path)
			print("wrote ", _shot_path)
			get_tree().quit()

	var distance := _radius * 2.6 * _zoom
	var direction := Vector3(
		sin(_yaw) * cos(_pitch), sin(-_pitch) + CAMERA_HEIGHT, cos(_yaw) * cos(_pitch)).normalized()
	_camera.position = _centre + direction * distance
	_camera.look_at(_centre, Vector3.UP)


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		var button := event as InputEventMouseButton
		if button.button_index == MOUSE_BUTTON_LEFT:
			_dragging = button.pressed
		elif button.pressed and button.button_index == MOUSE_BUTTON_WHEEL_UP:
			_zoom = clampf(_zoom * 0.9, MIN_ZOOM, MAX_ZOOM)
		elif button.pressed and button.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			_zoom = clampf(_zoom * 1.1, MIN_ZOOM, MAX_ZOOM)
	elif event is InputEventMouseMotion and _dragging:
		var motion := event as InputEventMouseMotion
		_yaw -= motion.relative.x * 0.01
		_pitch = clampf(_pitch + motion.relative.y * 0.01, -1.2, 1.2)


func _find(node: Node, cls: String) -> Node:
	if node.get_class() == cls:
		return node
	for child in node.get_children():
		var found := _find(child, cls)
		if found != null:
			return found
	return null


func _collect(node: Node, cls: String, into: Array[Node]) -> void:
	if node.get_class() == cls:
		into.append(node)
	for child in node.get_children():
		_collect(child, cls, into)


func _thousands(value: int) -> String:
	var text := str(value)
	var out := ""
	var count := 0
	for i in range(text.length() - 1, -1, -1):
		out = text[i] + out
		count += 1
		if count % 3 == 0 and i > 0:
			out = "," + out
	return out
