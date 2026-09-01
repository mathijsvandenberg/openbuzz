extends Node3D

## The studio behind the round screen.
##
## The reference shots stage it the same way every round: the big screen fills
## most of the frame with the hostess standing to the right of it, in front.
## So the 3D layer holds her, the 2D layer draws the screen over the left of it,
## and the strip on the right is left clear for her to stand in.

const IDLE_SWITCH := 9.0

@onready var _camera: Camera3D = $Camera

var _figure: Node3D = null
var _player: AnimationPlayer = null
var _clips: PackedStringArray = []
var _next_idle := IDLE_SWITCH


func _ready() -> void:
	var dir := _find_models()
	if dir.is_empty():
		return

	_figure = _load(dir.path_join("Hostess.glb"))
	if _figure == null:
		_figure = _load(dir.path_join("Host.glb"))
	if _figure == null:
		return

	add_child(_figure)
	_play_random_idle()

	# Framing has to wait for the skeleton to be posed: in _ready the bones are
	# still at their rest transforms and the measured bounds are empty, which
	# put the camera inside the model and rendered nothing at all.
	await get_tree().process_frame
	await get_tree().process_frame
	_frame_figure()


static func _find_models() -> String:
	var d := Bundle.base_dir()
	for i in range(6):
		var candidate := d.path_join("extracted/models")
		if DirAccess.dir_exists_absolute(candidate):
			return candidate
		var up := d.get_base_dir()
		if up == d:
			break
		d = up
	return ""


func _load(path: String) -> Node3D:
	if not FileAccess.file_exists(path):
		return null

	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	if doc.append_from_file(path, state) != OK:
		return null

	var scene := doc.generate_scene(state)
	if scene == null:
		return null

	_player = _find(scene, "AnimationPlayer") as AnimationPlayer
	if _player != null:
		_clips = _player.get_animation_list()
	return scene


## Stands her at the right of the frame, turned a little towards the screen.
## Framed exactly as the Models tab frames a character, which is known to work:
## the mesh AABB, a distance of 2.6 radii, and Godot's default field of view.
## Narrowing the fov while keeping that distance was what put the camera inside
## her dress.
func _frame_figure() -> void:
	# Turn her first. Her geometry is offset from her own origin, so rotating
	# after measuring swings the body out of the frame that was just computed -
	# which is exactly what kept putting her half out of shot.
	_figure.rotation_degrees = Vector3(0, 200, 0)
	_figure.force_update_transform()

	var meshes: Array[Node] = []
	_collect(_figure, "MeshInstance3D", meshes)

	var box := AABB()
	var first := true
	for m in meshes:
		var mi := m as MeshInstance3D
		var world := mi.global_transform * mi.get_aabb()
		box = world if first else box.merge(world)
		first = false
	if first:
		return

	var centre := box.get_center()
	var radius := maxf(maxf(box.size.x, box.size.y), box.size.z) * 0.5
	if radius <= 0.0:
		radius = 1.0

	# The AABB of a skinned mesh describes it unposed, and for these rigs it
	# sits well below the body that actually renders. The lift and the pull-back
	# are tuned against the render rather than derived, because the bounds
	# cannot be trusted to say where she is.
	var aim := centre + Vector3(0.0, radius * 0.50, 0.0)
	var direction := Vector3(0.0, 0.30, 1.0).normalized()
	_camera.position = aim + direction * radius * 2.75
	_camera.look_at(aim, Vector3.UP)

	if OS.get_cmdline_user_args().has("--stage-shot"):
		_capture()


func _process(delta: float) -> void:
	if _player == null or _clips.is_empty():
		return
	_next_idle -= delta
	if _next_idle <= 0.0:
		_play_random_idle()


## The clips are unnamed poses and gestures, so one is picked at random and
## held for a while rather than pretending to know which is an idle.
func _play_random_idle() -> void:
	_next_idle = IDLE_SWITCH
	if _player == null or _clips.is_empty():
		return

	var name := _clips[randi() % _clips.size()]
	_player.play(name)
	var anim := _player.get_animation(name)
	if anim != null:
		anim.loop_mode = Animation.LOOP_LINEAR


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


## Saves what the stage viewport alone renders, with nothing composited over
## it, so the framing can be judged without the 2D layer in the way.
func _capture() -> void:
	await get_tree().create_timer(1.2).timeout
	await RenderingServer.frame_post_draw
	var image := get_viewport().get_texture().get_image()
	var path := OS.get_user_data_dir().path_join("stage-only.png")
	image.save_png(path)
	print("STAGE-ONLY ", path, " ", image.get_size())
