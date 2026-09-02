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
	_add_contestants(dir, players)
	_make_portraits(players)
	return true


## <summary>
## The contestant viewports.
##
## QuizSupportCode_ViewportDisplays calls these viewports, and they are not a
## metaphor: one holds a border, a name bar, corner icons and a model, and
## SetupViewportPortraitDisplayGraphicAndPosition puts the portrait in it. The
## player cards along the bottom of the round screen are these.
##
## Which is what CAMERA_CONTESTANT_1..4 are for. They were the four cameras
## whose convention got checked against the contestant marks, aiming at their
## own contestant to a cosine of 0.997 - and this is the shot they were aiming
## for. Each viewport shares the studio world and looks through its own one.
## </summary>
const PORTRAIT_SIZE := 192

var _portraits: Array[SubViewport] = []


func _make_portraits(players: int) -> void:
	for seat in range(players):
		var view := SubViewport.new()
		view.name = "Portrait%d" % (seat + 1)
		view.size = Vector2i(PORTRAIT_SIZE, PORTRAIT_SIZE)
		view.render_target_update_mode = SubViewport.UPDATE_ALWAYS
		view.transparent_bg = false
		# Shares the studio rather than building a second one, so the portrait
		# is the contestant on the real set under the real lights.
		view.world_3d = get_viewport().world_3d
		view.own_world_3d = false
		add_child(view)

		var camera := Camera3D.new()
		# Only this seat's contestant. The name the game uses for these is a
		# portrait display, and a portrait is one person.
		camera.cull_mask = 1 << (seat + 1)
		view.add_child(camera)

		var angle := "CAMERA_CONTESTANT_%d" % (seat + 1)
		if use_camera(angle, camera):
			Log.info("viewport", "seat %d portrait through %s" % [seat + 1, angle])
		else:
			Log.error("viewport", "no %s" % angle)

		_portraits.append(view)


static func _set_layers(node: Node, mask: int) -> void:
	if node is VisualInstance3D:
		(node as VisualInstance3D).layers = mask
	for child in node.get_children():
		_set_layers(child, mask)


## The live portrait for a seat, for the round screen to draw in its card.
func portrait(seat: int) -> Texture2D:
	return null if seat < 0 or seat >= _portraits.size() else _portraits[seat].get_texture()


## The sixteen contestants, in the order their costume models sit on the disc.
## Three costumes each, forty-eight models.
const ROSTER := [
	"Angie", "Ash", "Barley", "Bradley", "Cinnamon", "Gina", "Jean", "Keiko",
	"Mercy", "Pelvis", "Punk", "Razor", "Stevie", "Tina", "Walrus", "Winona",
]

var _contestants: Array[Node3D] = []
var _contestant_players: Array[AnimationPlayer] = []


## Stands a contestant on each mark.
##
## The marks are DUMMYNODE_CONTESTANT_1..4 and the model goes on one with no
## offset of its own, because that is what the engine does: 0x0013BCD0 attaches
## a model to a node by name and nothing adjusts it afterwards. The costumes
## have very different bind poses - Angie is 173 units with her origin at the
## ankle, Walrus 142 with his at the hip - and compensating for that here would
## be inventing a rule the game does not have.
func _add_contestants(dir: String, players: int) -> void:
	var picks := _picked_characters(players)

	for seat in range(players):
		var mark := "DUMMYNODE_CONTESTANT_%d" % (seat + 1)
		if parts(mark).is_empty():
			Log.warn("cast", "no %s in the set" % mark)
			continue

		var name := str(picks[seat])
		var figure := _load(dir.path_join("%sCostume01.glb" % name))
		if figure == null:
			Log.warn("cast", "no model for %s" % name)
			continue

		add_child(figure)
		# Position and facing both come from the mark. The marks are turned 45
		# degrees because the stage runs diagonally, so taking the rotation from
		# the mark is what makes a contestant face the right way; picking an
		# angle by hand had them all in profile.
		figure.global_transform = marker_transform(mark)

		# Each contestant also sits on a layer of their own, so a portrait
		# viewport can show that one person and nothing else. The set would
		# otherwise be in the way: CAMERA_CONTESTANT_1 looks straight through
		# podium 1 to reach contestant 1.
		_set_layers(figure, 1 | (1 << (seat + 1)))

		# Each figure's own player, found in its own scene. _load parks one in
		# a shared member, which is fine for the single hostess and quietly
		# wrong for four contestants: every one of them ended up driving
		# whichever model happened to load last.
		var own := _find(figure, "AnimationPlayer") as AnimationPlayer
		_contestants.append(figure)
		if own != null:
			_contestant_players.append(own)
			_play_idle_on(own)

		Log.info("cast", "seat %d: %s at %s facing %s" % [
			seat + 1, name, str(figure.global_position),
			str(figure.global_transform.basis.get_euler() * 180.0 / PI)])


## Who is in which seat. --characters names them; otherwise the first few of
## the roster, so a run is repeatable.
func _picked_characters(players: int) -> Array:
	var args := OS.get_cmdline_user_args()
	var at := args.find("--characters")
	if at >= 0 and at + 1 < args.size():
		var named := args[at + 1].split(",", false)
		if named.size() >= players:
			return named
	return ROSTER.slice(0, players)


func _play_idle_on(player: AnimationPlayer) -> void:
	var clips := player.get_animation_list()
	if clips.is_empty():
		return
	var pick := clips[randi() % clips.size()]
	player.play(pick)
	var anim := player.get_animation(pick)
	if anim != null:
		anim.loop_mode = Animation.LOOP_LINEAR


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

		# --only <piece,piece> hides everything else, for looking at one piece
		# of the set on its own.
		var only := args.find("--only")
		if only >= 0 and only + 1 < args.size():
			var keep := args[only + 1].split(",", false)
			var shown := show_only(keep)
			Log.info("studio", "--only %s: %d meshes visible" % [str(keep), shown])

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
