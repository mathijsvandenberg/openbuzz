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

## <summary>
## Where the host and hostess stand.
##
## Neither has a mark, and that is settled rather than unknown. There is no
## DUMMYNODE for either of them in StudioModels, StudioLights, StudioParticles
## or any GreenRoom file; the executable's own list of node names it looks up
## has DUMMYNODE_CONTESTANT_, the clock, the prize room, the spot cones and the
## light groups, and nothing for a presenter; their clumps sit at the origin,
## as the contestants' do; all 53 host clips and 11 hostess clips keep their
## root at the origin; and their geometry is not authored in place. The engine
## positions them from code, under no name that can be searched for.
##
## What the data does give is a light named for each of them. The rig has three
## POOL and SPOT pairs for three locations - contestants, host platform,
## monitor - and CONTSPOT is demonstrably the contestants'. So HOSTSPOT is the
## host's key light and MONISPOT the hostess's, and both project down onto
## MODEL_WALKWAY_GLASS, whose top face is the floor they stand on.
##
## That is an anchor, not a stored coordinate, and it is the strongest one the
## disc offers.
## </summary>
const HOST_SPOT := "ANIMATEDLIGHT_HOSTSPOT"
const HOSTESS_SPOT := "ANIMATEDLIGHT_MONISPOT"
const WALKWAY := "MODEL_WALKWAY_GLASS"
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

	load_cameras(_bundle_dir())
	load_lights(_bundle_dir())

	Log.info("studio", "set loaded: %d pieces indexed, %d cameras, %d light rigs" % [
		_pieces.size(), _cameras.size(), _rigs.size()])
	stage_for(players)
	if screen != null and show_on_screen(screen) == 0:
		push_warning("no video wall surface found; the round has nowhere to draw")

	_add_hostess(dir)
	return true


static func _bundle_dir() -> String:
	var d := Bundle.base_dir()
	for i in range(6):
		var candidate := d.path_join("extracted/godot2d")
		if DirAccess.dir_exists_absolute(candidate):
			return candidate
		var up := d.get_base_dir()
		if up == d:
			break
		d = up
	return ""


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
	if not has_light(HOSTESS_SPOT):
		return

	# Straight down from her spot onto the walkway.
	var at := light_position(HOSTESS_SPOT)
	at.y = walkway_top()
	_figure.global_position = at
	_figure.rotation_degrees = Vector3(0, HOSTESS_TURN, 0)

	if OS.get_cmdline_user_args().has("--stage-report"):
		print("STAGE hostess at=", at, " walkway_top=", walkway_top(),
			" figure=", _figure != null, " visible=", _figure.visible)


## The top face of the studio walkway, which is the floor the presenters stand
## on. Read off the piece rather than assumed to be zero: it sits at 53.
func walkway_top() -> float:
	for part in parts(WALKWAY):
		var mesh := part as MeshInstance3D
		if mesh != null:
			return (mesh.global_transform * mesh.get_aabb()).end.y
	return 0.0


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


## Which of the game's cameras to look through. --camera names one; the round
## screen is CAMERA_SCREEN.
var _angle := "CAMERA_SCREEN"


func _process(delta: float) -> void:
	if not _framed:
		var args := OS.get_cmdline_user_args()
		var at := args.find("--camera")
		if at >= 0 and at + 1 < args.size():
			_angle = args[at + 1]
		if use_camera(_angle, _camera):
			Log.info("camera", "%s pos=%s fov=%.3f near=%.1f far=%.1f" % [
				_angle, str(_camera.global_position), _camera.fov, _camera.near, _camera.far])
		else:
			Log.error("camera", "no camera named %s in cameras.json" % _angle)

		# --lights <mood> lights the set from one of the game's eight rigs.
		# Without it the set stays unshaded, showing its textures as painted.
		var mood := args.find("--lights")
		if mood >= 0 and mood + 1 < args.size():
			var scale := Studio.LIGHT_SCALE
			var scale_at := args.find("--light-scale")
			if scale_at >= 0 and scale_at + 1 < args.size():
				scale = float(args[scale_at + 1])
			var lit := use_mood(args[mood + 1], scale)
			if lit == 0:
				Log.error("lights", "no light rig named %s" % args[mood + 1])
			else:
				Log.info("lights", "rig %s: %d lights at scale %.2f" % [args[mood + 1], lit, scale])

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
