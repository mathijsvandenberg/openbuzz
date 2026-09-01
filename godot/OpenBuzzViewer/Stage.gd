extends Studio

## The studio as the round sees it: the set, the hostess, and the camera.
##
## Everything placed here comes out of the game's own data. The set pieces and
## the contestant marks are in StudioModels.rp2; the camera is squared on the
## jumbotron, whose size and facing that file also gives. What the round draws
## goes onto the jumbotron itself rather than over the top of it, because in the
## reference shots the question is on the studio screen and the studio is what
## you are looking at.

const IDLE_SWITCH := 9.0

## Where the hostess stands. There is no DUMMYNODE for her - the markers name
## the four contestants, the clock, the prize room and the game win - but the
## set does have MODEL_SET_LECTERN, which is the presenter's spot, and she is
## put beside it. How far to the side is the one number here still set against
## the reference rather than read out of the file.
const LECTERN := "MODEL_SET_LECTERN"
const HOSTESS_SIDE := 300.0     ## out to the right of the lectern
const HOSTESS_TURN := 200.0

@onready var _camera: Camera3D = $Camera

var _figure: Node3D = null
var _player: AnimationPlayer = null
var _clips: PackedStringArray = []
var _next_idle := IDLE_SWITCH
var _framed := false


func build(screen: Texture2D, players: int) -> bool:
	var dir := _find_models()
	if dir.is_empty():
		return false

	if not load_set(dir):
		return false

	stage_for(players)
	if screen != null:
		show_on_screen(screen)

	_add_hostess(dir)
	return true


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


func _add_hostess(dir: String) -> void:
	_figure = _load(dir.path_join("Hostess.glb"))
	if _figure == null:
		_figure = _load(dir.path_join("Host.glb"))
	if _figure == null:
		return

	add_child(_figure)
	_stand_hostess()
	_play_random_idle()


## Puts her on the studio floor beside the screen, at the screen's own scale.
## Her model and the set are in the same units, so no scaling is needed - which
## is worth saying, because guessing a scale was what made her look like a
## figurine in front of a wall before.
func _stand_hostess() -> void:
	var rect := screen_rect()
	if rect.is_empty():
		return

	var stand := Vector3.ZERO
	var lectern := parts(LECTERN)
	if not lectern.is_empty():
		stand = (lectern[0] as Node3D).global_position

	# Beside the lectern, on the same floor, turned back towards the screen.
	_figure.global_position = stand + (rect.right as Vector3).normalized() * HOSTESS_SIDE
	_figure.rotation_degrees = Vector3(0, HOSTESS_TURN, 0)

	if OS.get_cmdline_user_args().has("--stage-report"):
		var box := AABB()
		var first := true
		for m in _meshes_of(_figure):
			var world: AABB = m.global_transform * m.get_aabb()
			box = world if first else box.merge(world)
			first = false
		print("STAGE lectern=", stand, " screen=", rect.centre)
		print("STAGE hostess at=", _figure.global_position, " bounds=", box)


static func _meshes_of(node: Node) -> Array:
	var out := []
	if node is MeshInstance3D:
		out.append(node)
	for child in node.get_children():
		out.append_array(_meshes_of(child))
	return out


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


## The camera has to wait for the viewport to have a size, because the distance
## that frames the screen depends on the aspect.
func _process(delta: float) -> void:
	var view := get_viewport().get_visible_rect().size
	if not _framed and view.x > 1.0 and view.y > 1.0:
		aim_at_screen(_camera, view.x / view.y)
		_framed = true

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
