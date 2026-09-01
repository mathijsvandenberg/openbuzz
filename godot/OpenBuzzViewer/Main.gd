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
@onready var _view: Control = $Viewport

var _paths: Array[String] = []
var _current: Node3D = null
var _player: AnimationPlayer = null
var _centre := Vector3.ZERO
var _radius := 1.0
var _yaw := 0.0
var _pitch := -0.12
var _zoom := 1.0

## Where the camera is looking, offset from the model's centre by panning.
var _pan := Vector3.ZERO
var _orbiting := false
var _panning := false

func _ready() -> void:
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

	_pan = Vector3.ZERO

	if first:
		_centre = Vector3.ZERO
		_radius = 1.0
		return

	_centre = box.get_center()
	_radius = maxf(box.size.x, maxf(box.size.y, box.size.z)) * 0.5
	if _radius <= 0.0:
		_radius = 1.0


func _process(_delta: float) -> void:
	var distance := _radius * 2.6 * _zoom
	var direction := Vector3(
		sin(_yaw) * cos(_pitch), sin(-_pitch) + CAMERA_HEIGHT, cos(_yaw) * cos(_pitch)).normalized()

	var target := _centre + _pan
	_camera.position = target + direction * distance
	_camera.look_at(target, Vector3.UP)


## Camera input is taken in _input rather than _unhandled_input. Controls
## consume mouse motion for their own hover handling, so by the time an event
## is "unhandled" the drags are already gone - which is why buttons worked and
## dragging did nothing at all.
func _input(event: InputEvent) -> void:
	# _input fires whether or not this tab is showing, so a drag meant for the
	# round would otherwise turn a model nobody can see.
	if not is_visible_in_tree():
		return

	if event is InputEventMouseButton:
		var button := event as InputEventMouseButton

		# Only drags that start over the model turn it; a drag on the sidebar
		# is somebody using the lists.
		if button.pressed and not _view.get_global_rect().has_point(button.position):
			return

		match button.button_index:
			MOUSE_BUTTON_LEFT:
				_panning = button.pressed
			MOUSE_BUTTON_RIGHT:
				_orbiting = button.pressed
			MOUSE_BUTTON_WHEEL_UP:
				if button.pressed:
					_zoom = clampf(_zoom * 0.9, MIN_ZOOM, MAX_ZOOM)
			MOUSE_BUTTON_WHEEL_DOWN:
				if button.pressed:
					_zoom = clampf(_zoom * 1.1, MIN_ZOOM, MAX_ZOOM)
		return

	if event is not InputEventMouseMotion:
		return

	var motion := event as InputEventMouseMotion

	if _orbiting:
		_yaw -= motion.relative.x * 0.01
		_pitch = clampf(_pitch - motion.relative.y * 0.01, -1.2, 1.2)
		return

	if _panning:
		# Pan along the screen, and scale it with the distance so the model
		# tracks the pointer at any zoom.
		var basis := _camera.global_transform.basis
		var speed := _radius * 2.6 * _zoom * 0.0016
		_pan -= basis.x * motion.relative.x * speed
		_pan += basis.y * motion.relative.y * speed


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


## --selftest drives the camera with synthetic input and reports whether it
## moved, so the controls can be checked without a hand on the mouse. It exists
## because a container quietly swallowing drags looks exactly like no code at
## all.
func run_self_test() -> void:
	var centre := _view.get_global_rect().get_center()

	# parse_input_event queues; the event is delivered on a later frame, so
	# every step has to wait for one.
	var before_yaw := _yaw
	await _send(_button(MOUSE_BUTTON_RIGHT, centre, true))
	await _send(_motion(centre + Vector2(140, 30), Vector2(140, 30)))
	await _send(_button(MOUSE_BUTTON_RIGHT, centre, false))

	var before_pan := _pan
	await _send(_button(MOUSE_BUTTON_LEFT, centre, true))
	await _send(_motion(centre + Vector2(40, 0), Vector2(40, 0)))
	await _send(_button(MOUSE_BUTTON_LEFT, centre, false))

	var before_zoom := _zoom
	await _send(_button(MOUSE_BUTTON_WHEEL_UP, centre, true))

	print("SELFTEST rotate=%s pan=%s zoom=%s" % [
		"ok" if not is_equal_approx(_yaw, before_yaw) else "DEAD",
		"ok" if _pan.distance_to(before_pan) > 0.0001 else "DEAD",
		"ok" if not is_equal_approx(_zoom, before_zoom) else "DEAD"])


func _button(index: int, at: Vector2, down: bool) -> InputEvent:
	var event := InputEventMouseButton.new()
	event.button_index = index
	event.pressed = down
	event.position = at
	event.global_position = at
	return event


func _motion(at: Vector2, relative: Vector2) -> InputEvent:
	var event := InputEventMouseMotion.new()
	event.position = at
	event.global_position = at
	event.relative = relative
	return event


func _send(event: InputEvent) -> void:
	Input.parse_input_event(event)
	await get_tree().process_frame
	await get_tree().process_frame
