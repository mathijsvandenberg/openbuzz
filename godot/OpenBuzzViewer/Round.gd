extends Control

## Plays any of the ten round types on the real buzzers.
##
## What each round is comes from RoundRules, which reads it out of the game:
## the input model from <Name>Round.luaasm, the parameters from
## GenericData.luaasm, and the on-screen title and rules from the game's own
## text table. This file is the machine that runs them, and draws them with
## the game's own art out of the sprite atlases.
##
## Handset report order, measured on the hardware:
##     0  red buzzer   1  yellow   2  green   3  orange   4  blue
## Bottom to top, while answers list top to bottom, so they map in reverse.

const CANVAS := Vector2(640, 480)
const PLAYERS := 4
const BUTTONS_PER_HANDSET := 5

const BUZZ := 0
const ANSWER_BUTTONS := [4, 3, 2, 1]
const BUTTON_NAMES := ["red", "yellow", "green", "orange", "blue"]

const COLOURS := [
	Color(0.29, 0.53, 0.91),   # blue
	Color(0.95, 0.60, 0.16),   # orange
	Color(0.30, 0.76, 0.35),   # green
	Color(0.96, 0.85, 0.20),   # yellow
]

## The answer markers are the rounded colour squares from the round-start
## sheet. PIP_answer_* are the round buzzer discs and belong on a player card,
## which is what they were wrongly doing here.
const ANSWER_SPRITES := ["RS_dot_blue", "RS_dot_orange", "RS_dot_green", "RS_dot_yellow"]
const BUZZER_SPRITES := ["PIP_answer_B", "PIP_answer_O", "PIP_answer_G", "PIP_answer_Y"]
const PLACE_SPRITES := ["PIP_1st", "PIP_2nd", "PIP_3rd", "PIP_4th"]

## The icon the round intro puts beside its title, one per round.
const ROUND_ICONS := {
	points_builder = "RS_point_builder", fastest_finger = "RS_fastest_finger",
	quickfire = "RS_LB4UL", snap = "RS_snap", trigger_finger = "RS_point_stealer",
	buzz_stop = "RS_buzz_stop", off_loader = "RS_off_loader",
	pass_the_bomb = "RS_pass_bomb", time_builder = "RS_time_builder",
	hot_seat = "RS_HotSeat",
}

## Written out rather than resolved: the round ordinals are among the text keys
## whose hash the port has not cracked, so the table has no entry for them.
const ORDINALS := ["EERSTE", "TWEEDE", "DERDE", "VIERDE", "VIJFDE", "ZESDE", "ZEVENDE"]

## The canvas is the studio jumbotron now, so it is the whole screen: no strip
## is kept clear, because the hostess stands beside it in the set rather than
## being painted over it.
const PANEL := CANVAS

## Layout. Every number here is read out of GenericData.clu at load, not
## measured: the game keeps its 640 x 480 screen layout there as plain constant
## assignment. The fallbacks are what the extraction would have to fail for.
##
## The one exception is the countdown pie, and it is marked as such below.
var L := {}


func _read_layout() -> void:
	var id: String = str(_round.get("params", ""))
	L = {
		question = Rect2(
			_bundle.layout_of("QuestionTextPositionX", 142, id),
			_bundle.layout_of("QuestionTextPositionY", 6, id),
			_bundle.layout_of("QuestionTextWidth", 430, id),
			_bundle.layout_of("QuestionTextHeight", 80, id)),
		answer = Rect2(
			_bundle.layout_of("AnswerPositionXStart", 142, id),
			_bundle.layout_of("AnswerPositionYStart", 92, id),
			_bundle.layout_of("AnswerPositionWidth", 390, id),
			_bundle.layout_of("AnswerPositionHeight", 55, id)),
		answer_x_inc = _bundle.layout_of("AnswerPositionXInc", 0, id),
		answer_y_inc = _bundle.layout_of("AnswerPositionYInc", 64, id),

		# The colour square sits left of the answer text by this much, and a
		# one-line answer drops it by that much.
		icon_dx = _bundle.layout_of("CONST_QuestionAnswerOffsetX", -44, id),
		icon_dy = _bundle.layout_of("CONST_QuestionAnswerOffsetY_OneLine", 11, id),
		icon_size = _bundle.layout_of("ContestantAnswerIconWidth", 32, id),

		# The contestant blocks along the bottom.
		block = Rect2(
			_bundle.layout_of("StartX", 132, id),
			_bundle.layout_of("StartY", 360, id),
			_bundle.layout_of("Width", 80, id),
			_bundle.layout_of("Height", 80, id)),
		block_gap = _bundle.layout_of("BlockGap", 25, id),
		block_icon = Vector2(
			_bundle.layout_of("ContestantAnswerIconXOffset", 60, id),
			_bundle.layout_of("ContestantAnswerIconYOffset", 59, id)),
		rank_icon = Vector2(
			_bundle.layout_of("ContestantRankIconXOffset", 50, id),
			_bundle.layout_of("ContestantRankIconYOffset", -30, id)),

		title = Rect2(
			_bundle.layout_of("InstructionsTitlePositionX", 64, id),
			_bundle.layout_of("InstructionsTitlePositionY", 5, id),
			_bundle.layout_of("InstructionsTitleWidth", 500, id),
			_bundle.layout_of("InstructionsTitleHeight", 130, id)),

		question_scale = _bundle.layout_of("QuestionFontScaling", 1.0, id),
		answer_scale = _bundle.layout_of("AnswerFontScaling", 0.9, id),

		clock = CLOCK_PLACEHOLDER,

		# Seconds per character for the letter-at-a-time reveal. The game calls
		# it a teletype rate, which is what Flitsronde does.
		teletype = _bundle.layout_of("GeneralTextTeletypeRate", 0.04, id),

		# Where the answer icon sits in a contestant viewport.
		answer_icon_right = _bundle.layout_of("ViewportAnswerIconRightOffset", 3, id),
		answer_icon_bottom = _bundle.layout_of("ViewportAnswerIconBottomOffset", 3, id),
	}


## <summary>
## The countdown pie, and the one element on this screen whose position is not
## recovered.
##
## GenericData does hold CountdownTimerIconX/Y (523, 17) and Width/Height 64,
## and that looks like the answer, but it is dead data: GenericData sets those
## four globals and no script in the game ever reads them. Drawing at them would
## look sourced while being no better than a guess.
##
## The timer the round actually uses is native. QuizSupportCode_CountdownTimer
## drives it entirely through CounterDisplayShow, CounterDisplayHide,
## CounterDisplaySetHighValue, CounterDisplaySetDisplayValue and
## CounterDisplayStopTicking, and CounterDisplayShow takes no arguments at all -
## its binding at 0x0017F4F0 lazily constructs a 308-byte widget and shows it.
## The geometry is inside that widget, in code.
##
## It is not in the A2D data either. None of the 46 scenes uses the 93x92
## countdown sprite, and the in-round question screen is not an A2D scene at
## all - the round-start panels, bumpers and PIP overlays are.
##
## So the size below is the game's (the sprite is 93x92) and the corner is
## where the reference shows it, held as an admitted placeholder until the
## widget is traced. It is the only number on this screen in that state.
## </summary>
const CLOCK_PLACEHOLDER := Rect2(12, 10, 76, 76)

const CHASE_STEP := 0.16
const CUE_STEP := 1.0

enum Phase { SEATING, INTRO, PLAYING, PICKING, REVEAL, DONE }

## <summary>
## Which handset took which seat, and the other way round.
##
## Not hardwired, because the game is not: ChoosePositions maps devices to
## seats and can clear, compact and restart those mappings, so handset 4 is
## only in seat 4 if that is the colour its player pressed. A seat is claimed
## by pressing its colour - blue, orange, green, yellow for seats one to four,
## which is the order the choose-a-place screen shows them in.
## </summary>
var _seat_of_handset := [-1, -1, -1, -1]
var _handset_of_seat := [-1, -1, -1, -1]

## Whether the seats have been settled this session. Claiming happens once,
## not before every round.
var _seated := false

## Seats a bot is playing. Testing alone still needs two players.
var _bot_seats := {}
var _bot_clock := 0.0

## How long the claim screen waits before a bot fills the empty seats.
const BOT_FILL_AFTER := 6.0

## And how long it then reads the round intro before starting.
const BOT_START_AFTER := 3.0

@onready var _canvas: Control = %RoundCanvas
@onready var _screen: SubViewport = %ScreenContent
@onready var _stage: Studio = %Stage
@onready var _status: Label = %RoundStatus
@onready var _audio: AudioStreamPlayer = %Audio
@onready var _speech_out: AudioStreamPlayer = %Speech
@onready var _effects: Array[AudioStreamPlayer] = [%Effects, %Effects2]
@onready var _list: ItemList = %Rounds
@onready var _blurb: Label = %Blurb

var _virtual_pad: Node = null
var _bundle := Bundle.new()
var _speech := Speech.new()

## <summary>
## The buzzer noises.
##
## Pressing a buzzer makes a noise, and these are them: the named clips on the
## disc that are not tied to a character or a moment. The scripts call them
## exactly that - NEWGetAllGenericBuzzerSounds, alongside
## SetMakeBuzzNoiseForAllContestantButtons in the round itself.
##
## The character-prefixed sets - pb_, pg_, rb_, rg_ and tt_ across fifteen
## suffixes - are deliberately not in here. They are clearly per-character, but
## what each prefix means is not settled, and guessing would put the wrong
## sound on the wrong moment.
## </summary>
const BUZZER_NOISES := [
	"Ahooga", "Air_Horn", "Alarm", "Belch", "Car", "Cat", "Chicken", "Chipmunk",
	"Dog", "Duck", "Evil", "Frog", "Girl", "Goose", "Horn", "Horn_2", "Horse",
	"Monkey", "Sheep", "Siren", "Space", "Stadium", "Train", "Turkey", "Whistle",
]
var _lamps := Lamps.new()
var _pad := -1
var _held := {}
var _demo := false

## How many seats are in play. Four unless --players says otherwise; the
## reference shots are a two-player game, and one player is a valid game too.
var _players := PLAYERS

var _round: Dictionary = {}
var _questions: Array = []
var _index := 0
var _phase: int = Phase.INTRO

var _answers := [-1, -1, -1, -1]
var _times := [0.0, 0.0, 0.0, 0.0]
var _scores := [0, 0, 0, 0]
var _awarded := [0, 0, 0, 0]
var _banked := [0.0, 0.0, 0.0, 0.0]
var _places := [-1, -1, -1, -1]   ## finishing order, for the speed round rosettes

## Display order of the four options, and where the correct one landed.
## The disc stores the correct answer first in every record, so shown in
## file order the answer would always be the top button.
var _order := [0, 1, 2, 3]
var _correct := 0

var _active := 0          ## whose turn, for ACTIVE and CHASE rounds
var _winner := -1         ## who won a buzz race
var _cue := -1            ## which option BUZZ_ON_CUE is showing
var _cue_clock := 0.0
var _chase_clock := 0.0
var _fuse := 0.0
var _clock := 0.0
var _wav_dir := ""
var _last_button := ""
var _note := ""

## How much of the question and answers Flitsronde has typed out so far,
## counted in characters, and how fast they arrive.
var _reveal := 0.0
var _reveal_rate := 0.0
var _burst := 0.0         ## seconds left on the bomb going off

## A game is a queue of round ids. Empty means a single round, picked from the
## list for testing; scores and banked time carry across the whole queue.
var _queue: Array[String] = []
var _leg := 0
var _asked := 0


func _ready() -> void:
	var seats := OS.get_cmdline_user_args().find("--players")
	if seats >= 0 and seats + 1 < OS.get_cmdline_user_args().size():
		_players = clampi(int(OS.get_cmdline_user_args()[seats + 1]), 1, PLAYERS)

	if not _bundle.load_from(Bundle.base_dir()):
		_status.text = "Could not find extracted/godot2d. Run 'obz bundle' first."
		return

	_questions = _bundle.quiz
	if _questions.is_empty():
		_status.text = "No questions. Run 'obz audio decode' then 'obz bundle'."
		return

	# One line at a time. The script waits for each to finish before opening
	# the next, so the queue advances on the same signal.
	_speech_out.finished.connect(_next_line)

	if _speech.load_from(_bundle.dir, _wav_dir_of(_bundle)):
		Log.info("speech", "%d lines indexed (%d commentary, %d fixed), %d round cues" % [
			_speech.line_count(), _speech.commentary.size(), _speech.fixed.size(),
			_speech.rounds.size()])
	else:
		Log.warn("speech", "no speech.json - run 'obz speech'")

	_questions.shuffle()
	_wav_dir = _bundle.dir.get_base_dir().path_join("wav")
	_pull_to_front(OS.get_cmdline_user_args())
	_canvas.draw.connect(_draw_round)

	for key in ["short", "medium", "long"]:
		_list.add_item("GAME - %s (%d rounds)" % [key.to_upper(), RoundRules.LENGTHS[key]])
	for r in RoundRules.all():
		_list.add_item(RoundRules.title(r, _bundle.text))
	_list.item_selected.connect(_pick)

	# The round is drawn into its own viewport and that viewport is hung on
	# the studio jumbotron, so the screen in shot is the set's own screen.
	_stage.build(_screen.get_texture(), _players)

	var pad := get_tree().current_scene.find_child("Pad", true, false)
	if pad != null and pad.has_signal("pressed"):
		pad.pressed.connect(_on_handset)
		_virtual_pad = pad

	_find_pad()
	_lamps.start(Bundle.base_dir())
	var args := OS.get_cmdline_user_args()
	_demo = args.has("--demo")

	var start := 3
	var at := args.find("--round")
	if at >= 0 and at + 1 < args.size():
		for i in range(RoundRules.all().size()):
			if RoundRules.all()[i].id == args[at + 1]:
				start = 3 + i
	at = args.find("--game")
	if at >= 0 and at + 1 < args.size():
		start = ["short", "medium", "long"].find(args[at + 1])
		if start < 0:
			start = 0

	_list.select(start)
	_pick(start)


## `--question <text>` brings the first question mentioning that text to the
## front of the shuffle. For checking a layout against a known-awkward record -
## the longest statements are what the answer rows have to survive - rather
## than waiting for one to come round.
func _pull_to_front(args: PackedStringArray) -> void:
	var at := args.find("--question")
	if at < 0 or at + 1 >= args.size():
		return

	var want := args[at + 1].to_upper()
	for i in range(_questions.size()):
		var hay := str(_questions[i]["question"])
		for option in _questions[i]["options"]:
			hay += " " + str(option)
		if hay.to_upper().contains(want):
			_questions.push_front(_questions.pop_at(i))
			return


static func _wav_dir_of(bundle: Bundle) -> String:
	return bundle.dir.get_base_dir().path_join("wav")


## Plays a named effect from the disc. Two players, so a buzz landing on top of
## a stinger does not cut it off.
func _effect(name: String, volume := 0.0) -> void:
	var path := _wav_dir.path_join("%s.wav" % name)
	if not FileAccess.file_exists(path):
		Log.warn("effect", "no %s.wav - run 'obz audio decode'" % name)
		return
	Log.trace("effect", name)
	var stream := AudioStreamWAV.load_from_file(path)
	if stream == null:
		return

	for player in _effects:
		if not player.playing:
			player.stream = stream
			player.volume_db = volume
			player.play()
			return
	_effects[0].stream = stream
	_effects[0].play()


## Buzz and Rose. A known line is spoken by id; where no cue has been recovered
## the bucket still stands in, and says so, rather than staying silent.
func _say(kind: String) -> void:
	_play_clip(_speech.any_clip(kind), kind)


## Lines queued to play one after another. RoundIntroduction does the same,
## waiting for each to finish before opening the next, so they are not spoken
## over one another.
var _to_say: Array = []


func _play_clip(path: String, what: String) -> bool:
	if path == "" or not FileAccess.file_exists(path):
		return false
	var stream := AudioStreamWAV.load_from_file(path)
	if stream == null:
		return false
	_speech_out.stream = stream
	_speech_out.play()
	Log.info("speech", "%s %s" % [what, path.get_file()])
	return true


## The round introduction, in the order the script plays it: the announcement,
## then the shared line 111000, then each rule.
func _introduce_round() -> void:
	_to_say.clear()
	var cue := _speech.cue_round(str(_round.params), str(_round.rules))
	if cue == "":
		Log.warn("speech", "no introduction cue for %s" % _round.params)
		_say("fixed")
		return

	_to_say = _speech.intro_lines(cue)
	Log.info("speech", "%s introduces %s: %s" % [
		_speech.intro_speaker(cue), cue, str(_to_say)])
	_next_line()


## The next line in the queue. A line whose clip will not load takes the queue
## with it if we simply stop - nothing would fire `finished` - so keep going
## until one actually starts or the queue runs out.
func _next_line() -> void:
	while not _to_say.is_empty():
		var id := str(_to_say.pop_front())
		if _play_clip(_speech.clip_for("fixed", id), "fixed " + id):
			return
		Log.warn("speech", "no clip for line %s" % id)


func _find_pad() -> void:
	_pad = -1
	for id in Input.get_connected_joypads():
		var info := Input.get_joy_info(id)
		# Sony 0x054C, Buzz buzzers 0x1000.
		if str(info.get("vendor_id", "")) == "1356" and str(info.get("product_id", "")) == "4096":
			_pad = id
			return
	var pads := Input.get_connected_joypads()
	if not pads.is_empty():
		_pad = pads[0]


## The first three entries start a game; the rest are single rounds for testing.
func _pick(index: int) -> void:
	_scores = [0, 0, 0, 0]
	_banked = [0.0, 0.0, 0.0, 0.0]

	_index = 0
	if index < 3:
		var length: String = ["short", "medium", "long"][index]
		_queue = RoundRules.session(length)
		_leg = 0
		_begin_leg()
		return

	_queue = []
	_select_round(index - 3)


## Starts the round the queue is currently on.
func _begin_leg() -> void:
	_select_round_dict(RoundRules.by_id(_queue[_leg]))


func _select_round(index: int) -> void:
	_select_round_dict(RoundRules.all()[index])


func _select_round_dict(round: Dictionary) -> void:
	_round = round
	_blurb.text = "  " + str(_round.blurb) + (
		"\n\n  Approximated: " + str(_round.approximates) if _round.approximates != "" else "")
	# Scores and banked time deliberately survive this: a game carries them
	# across its rounds, and _pick clears them when a new game starts.
	_read_layout()
	Log.info("round", "%s (%s) input=%d score=%d %.0fs  layout question=%s answer=%s block=%s" % [
		str(_round.get("id","?")), str(_round.get("params","-")),
		int(_round.input), int(_round.score), float(_round.seconds),
		str(L.question), str(L.answer), str(L.block)])
	if _round.get("approximates", "") != "":
		Log.warn("round", "approximates: %s" % str(_round.approximates))
	_index = 0
	_active = 0
	# Each round counts its own share of questions. Without this reset the
	# count carried over and every round after the first handed on after one.
	_asked = 0
	_note = ""
	_phase = Phase.SEATING if _players > 1 and not _seated else Phase.INTRO
	_bot_clock = BOT_FILL_AFTER
	_audio.stop()
	_lamps.all(true)
	_introduce_round()
	if _demo:
		_start_question()


func _start_question() -> void:
	_phase = Phase.PLAYING
	_answers = [-1, -1, -1, -1]
	_times = [0.0, 0.0, 0.0, 0.0]
	_awarded = [0, 0, 0, 0]
	_winner = -1
	_cue = -1
	_cue_clock = 0.0
	_chase_clock = 0.0
	_clock = float(_round.seconds)
	_places = [-1, -1, -1, -1]
	_burst = 0.0
	_note = ""

	match int(_round.input):
		RoundRules.Mode.ALL, RoundRules.Mode.BUZZ_THEN_ANSWER:
			_lamps.all(true)
		RoundRules.Mode.BUZZ_ON_CUE:
			_lamps.all(true)
		RoundRules.Mode.ACTIVE:
			_lamps.only(_active)
		RoundRules.Mode.CHASE:
			_lamps.only(_active)

	if int(_round.score) == RoundRules.Score.BOMB and _fuse <= 0.0:
		_fuse = randf_range(20.0, 40.0)

	var q: Dictionary = _questions[_index]

	_order = [0, 1, 2, 3]
	_order.shuffle()
	_correct = _order.find(int(q["correct"]))

	# Flitsronde types the question and all four answers out together, a letter
	# at a time, every line cut at the same character count. The rate is the
	# game's own GeneralTextTeletypeRate, seconds per character, not a share of
	# the clock - which is what it used to be, fitted to a screenshot.
	_reveal = 0.0
	_reveal_rate = 0.0
	if bool(_round.get("reveals", false)):
		_reveal_rate = 1.0 / maxf(float(L.teletype), 0.001)

	Log.info("question", "#%d '%s' correct=%s (button %d) order=%s clip=%s%s" % [
		_asked + 1, str(q["question"]), str(q["options"][int(q["correct"])]),
		_correct, str(_order), str(q["clip"]),
		"" if _reveal_rate <= 0.0 else "  teletype=%.1f chars/s" % _reveal_rate])

	var path := _wav_dir.path_join("%s.wav" % str(q["clip"]))
	if FileAccess.file_exists(path):
		var stream := AudioStreamWAV.load_from_file(path)
		if stream != null:
			_audio.stream = stream
			_audio.play()


func _process(delta: float) -> void:
	_poll_buttons()

	if _phase == Phase.SEATING:
		_bot_clock -= delta
		if _claimed_count() >= _players or _bot_clock <= 0.0:
			_fill_with_bots()
			_seated = true
			_phase = Phase.INTRO
			_bot_clock = BOT_START_AFTER

	elif _phase == Phase.INTRO and not _bot_seats.is_empty():
		# A round with nobody human in it would otherwise sit on the intro
		# waiting for a press that is never coming.
		_bot_clock -= delta
		if _bot_clock <= 0.0:
			Log.trace("bot", "starting the round")
			_start_question()

	if _phase == Phase.PLAYING:
		_clock -= delta
		_bot_play(delta)
		_advance(delta)
	elif _phase == Phase.PICKING:
		# A pick has to be able to time out, or a round where nobody presses
		# waits for ever.
		_clock -= delta
		if _demo:
			_picked(_active if int(_round.input) == RoundRules.Mode.CHASE else _winner,
				ANSWER_BUTTONS[(_active + 1) % 4])
		if _clock <= 0.0:
			_give_up_pick()
	elif _phase == Phase.REVEAL:
		_clock -= delta
		_burst = maxf(_burst - delta, 0.0)
		if _clock <= 0.0:
			_next_question()

	_canvas.queue_redraw()
	if _phase != _logged_phase:
		Log.trace("phase", "%s -> %s" % [_phase_name_of(_logged_phase), _phase_name()])
		_logged_phase = _phase
	var leg := "" if _queue.is_empty() else "game %d/%d - " % [_leg + 1, _queue.size()]
	_status.text = "%s%s   |   %s   |   pad %s   |   lamps %s   |   %s" % [
		leg, str(_round.get("id", "-")), _phase_name(),
		"none" if _pad < 0 else str(_pad),
		"on" if _lamps.available else _lamps.reason, _last_button]


## Everything that ticks while a question is live, per input model.
func _advance(delta: float) -> void:
	# The reveal stops the moment somebody buzzes: what is on screen at that
	# point is what they committed to.
	if _reveal_rate > 0.0 and _winner < 0:
		_reveal += _reveal_rate * delta

	match int(_round.input):
		RoundRules.Mode.ALL:
			if _demo:
				for p in range(_players):
					if _answers[p] == -1 and _clock < float(_round.seconds) - 0.4 - p * 0.3:
						_answer(p, p % 4)
			if _all_in() or _clock <= 0.0:
				_finish()

		RoundRules.Mode.BUZZ_THEN_ANSWER:
			# The demo waits for the reveal to get somewhere before buzzing.
			# Buzzing in half a second freezes Flitsronde on two letters, which
			# is not what a player would do on a round built round a reveal.
			var think := 4.0 if _reveal_rate > 0.0 else 0.5
			if _demo and _winner < 0 and _clock < float(_round.seconds) - think:
				_press(1, BUZZ)
			elif _demo and _winner >= 0 and _clock < float(_round.seconds) - think - 0.7:
				_answer(_winner, 0)
			if _clock <= 0.0:
				_finish()

		RoundRules.Mode.BUZZ_ON_CUE:
			_cue_clock -= delta
			if _cue_clock <= 0.0:
				_cue = (_cue + 1) % 4
				_cue_clock = CUE_STEP
			if _demo and _clock < float(_round.seconds) - 2.0:
				_press(2, BUZZ)
			if _clock <= 0.0:
				_finish()

		RoundRules.Mode.ACTIVE:
			if int(_round.score) == RoundRules.Score.BOMB:
				_fuse -= delta
				if _fuse <= 0.0:
					_explode()
					return
			if _demo and _clock < float(_round.seconds) - 0.6:
				_answer(_active, 0)
			if _clock <= 0.0:
				_finish()

		RoundRules.Mode.CHASE:
			# The lamp travels while the clip plays; the music stopping fixes it.
			_chase_clock -= delta
			if _chase_clock <= 0.0:
				_active = (_active + 1) % _players
				_lamps.only(_active)
				_chase_clock = CHASE_STEP
			if not _audio.playing or _clock <= 0.0:
				_phase = Phase.PICKING
				_clock = 15.0
				_lamps.only(_active)
				_note = "SPELER %d" % (_active + 1)


## Whether every seat in play has answered.
func _all_in() -> bool:
	for p in range(_players):
		if _answers[p] == -1:
			return false
	return true


## Nobody picked in time: the question just ends, unscored.
func _claimed_count() -> int:
	var n := 0
	for seat in range(_players):
		if _handset_of_seat[seat] >= 0:
			n += 1
	return n


## The bot answers for the seats nobody took. It waits a beat, the way a person
## does, so the reveal does not fire the instant a question appears.
func _bot_play(_delta: float) -> void:
	for seat in _bot_seats:
		if seat >= _players or _answers[seat] != -1:
			continue
		var think: float = 1.0 + float(seat) * 0.8
		if float(_round.seconds) - _clock < think:
			continue
		# Right about half the time, so the scores move without being perfect.
		var pick: int = _correct if randf() < 0.5 else randi() % 4
		# A bot has no handset, but its seat still makes the noise a pressed
		# button makes - otherwise a table of bots plays in silence.
		_effect(BUZZER_NOISES[randi() % BUZZER_NOISES.size()], -4.0)
		_answer(seat, pick)
		Log.trace("bot", "seat %d answered %d" % [seat + 1, pick])


func _give_up_pick() -> void:
	_phase = Phase.REVEAL
	_clock = 3.0
	_note = "GEEN KEUZE"
	_lamps.all(false)


## Advances past any question whose clip was just played: only 47 clips are
## decoded, so plain shuffling repeats a song within a couple of questions.
func _advance_index() -> void:
	var previous := "" if _index >= _questions.size() else str(_questions[_index]["clip"])
	for _try in range(12):
		_index += 1
		if _index >= _questions.size():
			return
		if str(_questions[_index]["clip"]) != previous:
			return


func _next_question() -> void:
	_advance_index()
	_asked += 1

	# In a game each round runs its share of questions, then hands on.
	if not _queue.is_empty() and _asked >= RoundRules.QUESTIONS_PER_ROUND:
		_leg += 1
		if _leg >= _queue.size():
			_phase = Phase.DONE
			_lamps.all(false)
			return
		_begin_leg()
		return

	if _index >= _questions.size():
		_phase = Phase.DONE
		_lamps.all(false)
		return

	# Whose turn it is next, for the rounds that rotate.
	if int(_round.input) == RoundRules.Mode.ACTIVE:
		_active = (_active + 1) % _players

	# Hot Seat stays with one player until the time they banked runs out.
	if int(_round.score) == RoundRules.Score.STAKE:
		_active = 0
		if _banked[0] <= 0.0:
			if _queue.is_empty():
				_phase = Phase.DONE
				_lamps.all(false)
				return
			_leg += 1
			if _leg >= _queue.size():
				_phase = Phase.DONE
				_lamps.all(false)
				return
			_begin_leg()
			return

	_start_question()


func _finish() -> void:
	_phase = Phase.REVEAL
	_clock = 3.0
	_audio.stop()
	_lamps.all(false)
	Log.info("reveal", "answers=%s correct=%d" % [str(_answers.slice(0, _players)), _correct])

	var anyone_right := false
	for p in range(_players):
		if _answers[p] == _correct:
			anyone_right = true
	_effect("correct1" if anyone_right else "wrong1")
	_say("commentary")

	match int(_round.score):
		RoundRules.Score.FLAT:
			for p in range(_players):
				if _answers[p] == _correct:
					_awarded[p] = RoundRules.POINTS
					_scores[p] += RoundRules.POINTS

		RoundRules.Score.SPEED:
			# Ranked by how quickly the correct answer came in.
			var right := []
			for p in range(_players):
				if _answers[p] == _correct:
					right.append({p = p, t = _times[p]})
			right.sort_custom(func(a, b): return a.t < b.t)
			for rank in range(right.size()):
				var pts: int = RoundRules.SPEED_POINTS[mini(rank, RoundRules.SPEED_POINTS.size() - 1)]
				_awarded[right[rank].p] = pts
				_scores[right[rank].p] += pts
				_places[right[rank].p] = rank

		RoundRules.Score.STEAL:
			if _winner >= 0 and _answers[_winner] == _correct:
				_phase = Phase.PICKING
				_clock = 10.0
				_note = "PAK PUNTEN AF"
				_lamps.only(_winner)
				return

		RoundRules.Score.TIME:
			# Time left on the clock becomes time banked for Hot Seat.
			if _answers[_active] == _correct:
				_banked[_active] += maxf(_clock, 0.0)
				_awarded[_active] = int(maxf(_clock, 0.0))

		RoundRules.Score.STAKE:
			if _answers[_active] == _correct:
				_awarded[_active] = RoundRules.POINTS
				_scores[_active] += RoundRules.POINTS
			_banked[_active] = maxf(_banked[_active] - (float(_round.seconds) - _clock), 0.0)

		RoundRules.Score.BOMB:
			if _answers[_active] == _correct:
				_note = "DOORGEVEN"

	_log_scores()


func _log_scores() -> void:
	var parts := PackedStringArray()
	for p in range(_players):
		parts.append("P%d=%d%s" % [p + 1, _scores[p],
			"" if _awarded[p] == 0 else " (%+d)" % _awarded[p]])
	Log.info("score", " ".join(parts))


func _explode() -> void:
	_phase = Phase.REVEAL
	_clock = 3.0
	_audio.stop()
	_lamps.all(false)
	_scores[_active] -= RoundRules.POINTS
	_awarded[_active] = -RoundRules.POINTS
	_note = "BOEM"
	_burst = 3.0
	_fuse = randf_range(20.0, 40.0)


func _poll_buttons() -> void:
	if _pad < 0:
		return
	for player in range(_players):
		for slot in range(BUTTONS_PER_HANDSET):
			var button := player * BUTTONS_PER_HANDSET + slot
			var down := Input.is_joy_button_pressed(_pad, button)
			var was: bool = _held.get(button, false)
			_held[button] = down
			if down and not was:
				# Real hardware goes through the same mapping as the on-screen
				# pad, so a claimed seat means the same thing either way.
				_on_handset(player, slot)


## A press from a handset, real or on screen. Everything routes through here
## so the virtual pad and the hardware cannot drift apart.
func _on_handset(handset: int, slot: int) -> void:
	_last_button = "pad %d, %s" % [handset + 1, BUTTON_NAMES[slot]]

	if _phase == Phase.SEATING:
		_claim(handset, slot)
		return

	var seat: int = _seat_of_handset[handset]
	if seat < 0:
		# Unclaimed handsets still work outside a claim screen, so a quick test
		# does not have to sit through seating first.
		seat = handset
	_press(seat, slot)


## Claims a seat for a handset, by the colour pressed.
func _claim(handset: int, slot: int) -> void:
	var seat := ANSWER_BUTTONS.find(slot)
	if seat < 0 or seat >= _players:
		return
	if _handset_of_seat[seat] >= 0 and _handset_of_seat[seat] != handset:
		Log.info("seat", "seat %d already taken by pad %d" % [
			seat + 1, _handset_of_seat[seat] + 1])
		return

	# A handset only ever holds one seat, so taking a new one frees the old.
	var had: int = _seat_of_handset[handset]
	if had >= 0:
		_handset_of_seat[had] = -1

	_seat_of_handset[handset] = seat
	_handset_of_seat[seat] = handset
	_bot_seats.erase(seat)

	Log.info("seat", "pad %d took seat %d (%s)" % [handset + 1, seat + 1, BUTTON_NAMES[slot]])
	_refresh_pad()
	_set_lamps([_handset_of_seat[0] >= 0, _handset_of_seat[1] >= 0,
		_handset_of_seat[2] >= 0, _handset_of_seat[3] >= 0])


## Fills the seats nobody took, so one person can still play a party round.
func _fill_with_bots() -> void:
	for seat in range(_players):
		if _handset_of_seat[seat] < 0:
			_bot_seats[seat] = true
	if not _bot_seats.is_empty():
		Log.info("seat", "bot playing seats %s" % str(_bot_seats.keys()))
	_refresh_pad()


## Lamps go to the hardware and to the on-screen pad together, so what is lit
## on a real buzzer is lit on screen.
func _set_lamps(state: Array) -> void:
	_lamps.set_lamps(state)
	if _virtual_pad != null:
		_virtual_pad.set_lamps(state)


func _refresh_pad() -> void:
	if _virtual_pad != null:
		_virtual_pad.set_seats(_seat_of_handset)


func _press(player: int, slot: int) -> void:
	if _phase == Phase.INTRO:
		_start_question()
		return

	if _phase == Phase.PICKING:
		_picked(player, slot)
		return

	if _phase != Phase.PLAYING:
		return

	# Every press makes a noise, which is what the round asks for when it calls
	# SetMakeBuzzNoiseForAllContestantButtons.
	_effect(BUZZER_NOISES[randi() % BUZZER_NOISES.size()], -4.0)

	var choice := ANSWER_BUTTONS.find(slot)

	match int(_round.input):
		RoundRules.Mode.ALL:
			if choice >= 0:
				_answer(player, choice)

		RoundRules.Mode.BUZZ_THEN_ANSWER:
			if slot == BUZZ and _winner < 0:
				_winner = player
				_audio.stop()
				_lamps.only(player)
			elif choice >= 0 and player == _winner:
				_answer(player, choice)
				_finish()

		RoundRules.Mode.BUZZ_ON_CUE:
			# Buzzing claims whichever option is showing at that moment.
			if slot == BUZZ and _winner < 0 and _cue >= 0:
				_winner = player
				_answer(player, _cue)
				_finish()

		RoundRules.Mode.ACTIVE:
			if choice >= 0 and player == _active:
				_answer(player, choice)
				_finish()

		RoundRules.Mode.CHASE:
			pass   # nothing is live until the music stops


## The second press some rounds need: Off Loader picks a victim, Trigger Finger
## picks who to take points from, and Chase is the caught player answering.
func _picked(player: int, slot: int) -> void:
	var choice := ANSWER_BUTTONS.find(slot)
	if choice < 0:
		return

	if int(_round.score) == RoundRules.Score.STEAL:
		if player != _winner:
			return
		var victim := choice
		if victim == _winner:
			return
		var taken: int = mini(_scores[victim], RoundRules.POINTS)
		_scores[victim] -= taken
		_scores[_winner] += taken
		_awarded[_winner] = taken
		_awarded[victim] = -taken
		_phase = Phase.REVEAL
		_clock = 3.0
		_note = "AFGEPAKT VAN SPELER %d" % (victim + 1)
		return

	# Chase: the player the lamp stopped on answers.
	if int(_round.input) == RoundRules.Mode.CHASE:
		if player != _active:
			return
		_answer(player, choice)
		_finish()


func _answer(player: int, choice: int) -> void:
	if _answers[player] != -1:
		return
	_answers[player] = choice
	_times[player] = float(_round.seconds) - _clock
	Log.info("answer", "player %d chose %d (%s) at %.2fs" % [
		player + 1, choice, "correct" if choice == _correct else "wrong", _times[player]])

	if int(_round.input) == RoundRules.Mode.ALL:
		_set_lamps([_answers[0] == -1, _answers[1] == -1, _answers[2] == -1, _answers[3] == -1])


## How much of a string Flitsronde has typed out. Every line on screen is cut
## at the same character count, so they fill in together.
func _typed(body: String) -> String:
	if _reveal_rate <= 0.0 or _phase != Phase.PLAYING:
		return body
	return body.substr(0, mini(int(_reveal), body.length()))


func _unhandled_input(event: InputEvent) -> void:
	if not (event is InputEventKey and event.pressed and not event.echo):
		return
	var key: int = event.keycode

	const BUZZ_KEYS := [KEY_1, KEY_2, KEY_3, KEY_4]
	var at := BUZZ_KEYS.find(key)
	if at >= 0:
		_press(at, BUZZ)
		return

	const ANSWER_KEYS := [
		[KEY_Q, KEY_W, KEY_E, KEY_R],
		[KEY_A, KEY_S, KEY_D, KEY_F],
		[KEY_Z, KEY_X, KEY_C, KEY_V],
		[KEY_U, KEY_I, KEY_O, KEY_P],
	]
	for player in range(PLAYERS):
		var slot: int = ANSWER_KEYS[player].find(key)
		if slot >= 0:
			_press(player, ANSWER_BUTTONS[slot])
			return

var _logged_phase := -1


func _phase_name_of(p: int) -> String:
	var was := _phase
	_phase = p
	var out := _phase_name()
	_phase = was
	return out


func _phase_name() -> String:
	match _phase:
		Phase.SEATING: return "choose a place - %0.0fs" % maxf(_bot_clock, 0.0)
		Phase.INTRO: return "press a button to start"
		Phase.PLAYING: return "playing - %0.1fs" % maxf(_clock, 0.0)
		Phase.PICKING: return "waiting for a pick"
		Phase.REVEAL: return "revealing"
		_: return "finished"


# ---------------------------------------------------------------- drawing

func _draw_round() -> void:
	# The canvas is its own 640x480 viewport now - the size the game drew at -
	# so there is no letterboxing left to do and the panel is the whole frame.
	var scale := _canvas.size.x / CANVAS.x
	var offset := Vector2.ZERO

	_draw_screen_surface(offset, scale)

	if _questions.is_empty() or _round.is_empty():
		return

	match _phase:
		Phase.SEATING:
			_draw_seating(offset, scale)
		Phase.INTRO:
			_draw_intro(offset, scale)
		Phase.DONE:
			_draw_scores(offset, scale)
		_:
			_draw_question(offset, scale)
			_draw_cards(offset, scale)


## Places a canvas-space rectangle on screen.
func _at(offset: Vector2, scale: float, x: float, y: float, w: float, h: float) -> Rect2:
	return Rect2(offset + Vector2(x, y) * scale, Vector2(w, h) * scale)


func _sprite(offset: Vector2, scale: float, sprite_name: String,
		x: float, y: float, w: float, h: float, tint := Color.WHITE) -> void:
	_bundle.draw_sprite(_canvas, sprite_name, _at(offset, scale, x, y, w, h), tint)


func _text(offset: Vector2, scale: float, style: String, body: String,
		x: float, y: float, w: float, h: float,
		size: float, colour: Color, justify := "Left") -> void:
	_bundle.draw_wrapped(_canvas, style, body,
		_at(offset, scale, x, y, w, h), scale * size, colour, justify)


## As _text, but never wrapping: it shrinks to hold the line instead.
func _line(offset: Vector2, scale: float, style: String, body: String,
		x: float, y: float, w: float, h: float,
		size: float, colour: Color, justify := "Left") -> void:
	_bundle.draw_one_line(_canvas, style, body,
		_at(offset, scale, x, y, w, h), scale * size, colour, justify)


# ---------------------------------------------------------------- intro

## The jumbotron face: a blue-grey wash, brighter at the top, ruled into
## panels. This is the one part of the screen with no sprite behind it - the
## reference shows a back-projected wall, and the set carries it as texture on
## geometry the port draws over.
func _draw_screen_surface(offset: Vector2, scale: float) -> void:
	var screen := Rect2(offset, PANEL * scale)
	var bands := 24
	for i in range(bands):
		var t := float(i) / float(bands - 1)
		var strip := Rect2(screen.position + Vector2(0, screen.size.y * t / 1.0),
			Vector2(screen.size.x, screen.size.y / bands + 1.0))
		_canvas.draw_rect(strip, Color(0.13, 0.17, 0.24).lerp(Color(0.07, 0.09, 0.14), t))

	# The panel seams: four across and four down, as on the studio wall.
	for column in range(1, 4):
		var x := screen.position.x + screen.size.x * column / 4.0
		_canvas.draw_line(Vector2(x, screen.position.y), Vector2(x, screen.end.y),
			Color(1, 1, 1, 0.05), maxf(scale, 1.0))
	for band in range(1, 4):
		var y := screen.position.y + screen.size.y * band / 4.0
		_canvas.draw_line(Vector2(screen.position.x, y), Vector2(screen.end.x, y),
			Color(1, 1, 1, 0.05), maxf(scale, 1.0))


## The choose-a-place screen. A handset claims a seat by pressing that seat's
## colour, which is why a pad is never tied to a position.
func _draw_seating(offset: Vector2, scale: float) -> void:
	_text(offset, scale, "RoundInstructionsLarge", "KIES EEN PLAATS",
		L.title.position.x, L.title.position.y + 40, L.title.size.x, 54, 1.1,
		Color.WHITE, "Centre")

	var block: Rect2 = L.block
	var pitch: float = block.size.x + float(L.block_gap)

	for seat in range(_players):
		var x: float = block.position.x + seat * pitch
		var top: float = block.position.y
		var side: float = block.size.x
		var taken: bool = _handset_of_seat[seat] >= 0
		var tint := Color(1.4, 1.4, 1.4) if taken else Color(0.5, 0.52, 0.6)

		_sprite(offset, scale, "PortraitSurroundGrey", x, top, side, side, tint)
		_sprite(offset, scale, "ViewportBarGrey", x, top + side, side, side * 0.31, tint)

		_text(offset, scale, "RoundInstructionsSmall", "SPELER %d" % (seat + 1),
			x, top + side + 5, side, 18, 0.62, Color.WHITE, "Centre")

		# The colour that claims this seat.
		var icon: float = float(L.icon_size) * 1.4
		_sprite(offset, scale, ANSWER_SPRITES[seat],
			x + side * 0.5 - icon * 0.5, top - icon - 12, icon, icon,
			Color.WHITE if not taken else Color(0.45, 0.48, 0.55))

		var caption := "DRUK OP"
		if taken:
			caption = "PAD %d" % (_handset_of_seat[seat] + 1)
		elif _bot_seats.has(seat):
			caption = "BOT"
		_text(offset, scale, "RoundInstructionsSmall", caption,
			x, top + side * 0.42, side, 20, 0.6,
			Color(0.95, 0.83, 0.35) if taken else Color(0.78, 0.81, 0.88), "Centre")

	_text(offset, scale, "RoundInstructionsSmall",
		"NOG %0.0f SECONDEN - LEGE PLAATSEN GAAN NAAR DE BOT" % maxf(_bot_clock, 0.0),
		0, block.position.y + block.size.y + 62, PANEL.x, 22, 0.6,
		Color(0.6, 0.64, 0.72), "Centre")


func _draw_intro(offset: Vector2, scale: float) -> void:
	var icon: String = ROUND_ICONS.get(str(_round.get("id", "")), "")
	if icon != "":
		_sprite(offset, scale, icon, 26, 58, 74, 74)

	if not _queue.is_empty() and _leg < ORDINALS.size():
		_text(offset, scale, "RoundInstructionsSmall", "%s RONDE" % ORDINALS[_leg],
			112, 56, PANEL.x - 140, 20, 0.7, Color(0.72, 0.76, 0.86))

	_text(offset, scale, "RoundInstructionsLarge", RoundRules.title(_round, _bundle.text),
		L.title.position.x + 48, L.title.position.y + 60,
		L.title.size.x - 48, 54, 1.15, Color.WHITE)

	if int(_round.score) == RoundRules.Score.SPEED:
		_draw_speed_table(offset, scale)
		return

	var y := 190.0
	for line in RoundRules.lines(_round, _bundle.text):
		_text(offset, scale, "RoundInstructionsSmall", line,
			70, y, PANEL.x - 140, 60, 0.9, Color(0.82, 0.84, 0.9), "Centre")
		y += 70.0


## Wie Is Het Snelst prints its own scoring table on the intro, so the port
## prints the same one. These are where the tier values come from.
func _draw_speed_table(offset: Vector2, scale: float) -> void:
	_sprite(offset, scale, "RS_tickL", 40, 176, 52, 52)
	for i in range(RoundRules.SPEED_POINTS.size()):
		var y := 168.0 + i * 30.0
		_text(offset, scale, "RoundInstructionsSmall", "%dE GOEDE ANTWOORD" % (i + 1),
			110, y, 220, 26, 0.78, Color(0.88, 0.90, 0.95))
		_text(offset, scale, "RoundInstructionsSmall", "+%d PTN" % RoundRules.SPEED_POINTS[i],
			330, y, 150, 26, 0.78, Color(0.95, 0.88, 0.45), "Right")

	_sprite(offset, scale, "RS_crossL", 40, 312, 52, 52)
	_text(offset, scale, "RoundInstructionsSmall", "FOUTE ANTWOORDEN",
		110, 322, 220, 26, 0.78, Color(0.88, 0.90, 0.95))
	_text(offset, scale, "RoundInstructionsSmall", "0 PTN",
		330, 322, 150, 26, 0.78, Color(0.85, 0.60, 0.55), "Right")


# ---------------------------------------------------------------- question

func _draw_question(offset: Vector2, scale: float) -> void:
	var q: Dictionary = _questions[_index]
	var on_cue := int(_round.input) == RoundRules.Mode.BUZZ_ON_CUE

	_draw_clock(offset, scale)

	# Numbered the way the game numbers it, "2:TEXT". Flitsronde spends the
	# first part of the clock typing it out a letter at a time.
	var asked := str(q["question"])
	_text(offset, scale, "GeneralLarge", "%d:%s" % [_asked + 1, _typed(asked)],
		L.question.position.x, L.question.position.y,
		L.question.size.x, L.question.size.y, L.question_scale, Color.WHITE)

	if int(_round.score) == RoundRules.Score.BOMB and _phase == Phase.PLAYING:
		_text(offset, scale, "RoundInstructionsSmall", "%0.0f" % maxf(_fuse, 0.0),
			PANEL.x - 120, 30, 96, 24, 0.8, Color(0.95, 0.5, 0.35), "Right")

	var options: Array = q["options"]
	for i in range(options.size()):
		var box: Rect2 = L.answer
		var y: float = box.position.y + i * float(L.answer_y_inc)
		var x: float = box.position.x + i * float(L.answer_x_inc)
		var body := str(options[_order[i]])
		var shown := _typed(body)

		# On-cue rounds show one option at a time; the rest show all four.
		if on_cue and _phase == Phase.PLAYING and i != _cue:
			continue

		# A rule under each answer, drawn with the game's own hairline sprite.
		_sprite(offset, scale, "hor_line", x, y + box.size.y - 8.0,
			box.size.x, 2, Color(1, 1, 1, 0.22))

		# The colour squares are the game's art, not drawn boxes.
		var icon: float = float(L.icon_size)
		_sprite(offset, scale, ANSWER_SPRITES[i],
			x + float(L.icon_dx), y + float(L.icon_dy), icon, icon)

		var tint := Color.WHITE
		if _phase == Phase.REVEAL:
			tint = Color(0.59, 1.0, 0.67) if i == _correct else Color(0.55, 0.58, 0.64)
		elif shown.length() < body.length():
			# Still arriving: dimmer, so the eye follows the letters landing.
			tint = Color(0.78, 0.81, 0.88)

		if shown != "":
			# On-cue rounds have the screen to themselves, so their statement -
			# which is a whole sentence - gets the width back from the square.
			var room: float = box.size.x
			if on_cue:
				room = PANEL.x - x - 16.0
			_line(offset, scale, "GeneralLarge", shown,
				x, y, room, box.size.y, float(L.answer_scale), tint)

	# Snap and Trigger Finger deliberately show one option at a time; without
	# saying so it just looks like the others failed to draw.
	if on_cue and _phase == Phase.PLAYING:
		_text(offset, scale, "RoundInstructionsSmall",
			"DRUK OP DE ZOEMER BIJ HET JUISTE ANTWOORD",
			30, 92, PANEL.x - 60, 20, 0.7, Color(0.72, 0.76, 0.86), "Centre")
		for slot in range(4):
			_sprite(offset, scale, ANSWER_SPRITES[slot],
				PANEL.x * 0.5 - 30 + slot * 16, 326, 12, 12,
				Color.WHITE if slot == _cue else Color(0.3, 0.32, 0.38))

	if _note != "":
		_text(offset, scale, "ExtraLarge", _note,
			0, 316, PANEL.x, 34, 0.55, Color(0.95, 0.83, 0.35), "Centre")


## The pie clock the game puts in the top-left corner. The dial is the game's
## own sprite; the wedge eaten out of it is drawn over the top, clockwise from
## twelve, because that sprite is a single static disc.
func _draw_clock(offset: Vector2, scale: float) -> void:
	if _phase != Phase.PLAYING and _phase != Phase.PICKING:
		return

	var box: Rect2 = L.clock
	_sprite(offset, scale, "countdown", box.position.x, box.position.y, box.size.x, box.size.y)

	var span := maxf(float(_round.seconds), 0.001)
	var spent := clampf(1.0 - _clock / span, 0.0, 1.0)
	if spent <= 0.0:
		return

	var centre := offset + (box.position + box.size * 0.5) * scale
	var radius := minf(box.size.x, box.size.y) * 0.5 * scale * 0.82
	var points := PackedVector2Array([centre])
	var steps := maxi(int(spent * 64.0), 2)
	for i in range(steps + 1):
		var angle := -PI * 0.5 + TAU * spent * float(i) / float(steps)
		points.append(centre + Vector2(cos(angle), sin(angle)) * radius)
	_canvas.draw_colored_polygon(points, Color(0.06, 0.07, 0.10, 0.92))


# ---------------------------------------------------------------- players

## The seats along the bottom: a portrait surround with a name bar under it,
## both the game's own art. Lit or grey says who is live.
func _draw_cards(offset: Vector2, scale: float) -> void:
	var live := int(_round.input) in [RoundRules.Mode.ACTIVE, RoundRules.Mode.CHASE]
	var banks := int(_round.score) in [RoundRules.Score.TIME, RoundRules.Score.STAKE]
	var bomb := int(_round.score) == RoundRules.Score.BOMB

	for p in range(_players):
		var block: Rect2 = L.block
		var pitch: float = block.size.x + float(L.block_gap)
		var x: float = block.position.x + p * pitch
		var top: float = block.position.y
		var side: float = block.size.x
		var answered: bool = _answers[p] != -1
		var spot: bool = (live and p == _active) or (_winner == p)

		# In the bomb round only the player holding it is lit and the rest are
		# silhouetted, which is how the game shows who is out of danger. The
		# two surround sprites are named the other way round to how they read:
		# Grey is the bright cyan frame, White the plain one, so the lighting
		# is done with the tint rather than by picking between them.
		# Brightness only, never a hue: the surround sprite is cyan, and tinting
		# it warm turns the lit card green rather than lighting it.
		var tint := Color.WHITE
		if bomb:
			tint = Color(1.5, 1.5, 1.5) if spot else Color(0.26, 0.28, 0.34)
		elif spot:
			tint = Color(1.5, 1.5, 1.5)

		var bar: float = side * 0.31
		# The portrait first, then the surround over its edge. The frame sits
		# slightly outside the picture, which is what ViewportSurroundXOffset
		# and YOffset (-6, -8) describe.
		var face: Texture2D = _stage.portrait(p)
		if face != null:
			_canvas.draw_texture_rect(face, _at(offset, scale, x + 4, top + 4,
				side - 8, side - 8), false, tint)

		_sprite(offset, scale, "PortraitSurroundGrey", x, top, side, side, tint)
		_sprite(offset, scale, "ViewportBarGrey", x, top + side, side, bar, tint)

		# What the card holds: the running score, or banked seconds in the two
		# rounds that trade in time.
		var middle := str(_scores[p])
		if banks:
			middle = "%0.0fs" % _banked[p]
		var colour := Color.WHITE
		if _phase == Phase.REVEAL and _awarded[p] != 0:
			middle = ("+%d" % _awarded[p]) if _awarded[p] > 0 else str(_awarded[p])
			colour = Color(0.6, 1.0, 0.7) if _awarded[p] > 0 else Color(0.95, 0.5, 0.45)
		_text(offset, scale, "GeneralLarge", middle,
			x, top + 28, side, 28, 0.62, colour, "Centre")

		# The chosen answer goes in the bottom-right corner of the viewport,
		# inset by ViewportAnswerIconRightOffset and Bottom - which is exactly
		# what the round asks for: the trace has Points Builder calling
		# AddAnswerIconsAtBottomRightForAllContestants(3, 3).
		if answered:
			var swatch: Color = COLOURS[_answers[p]] if _phase == Phase.REVEAL else Color(0.55, 0.58, 0.66)
			var icon: float = float(L.icon_size)
			_sprite(offset, scale, BUZZER_SPRITES[_answers[p]],
				x + side - icon - float(L.answer_icon_right),
				top + side - icon - float(L.answer_icon_bottom), icon, icon,
				Color.WHITE if _phase == Phase.REVEAL else swatch)

		# The name bar carries the buzz time in the speed round, because that
		# is what the round is about, and the seat number otherwise.
		var label := "SPELER %d" % (p + 1)
		if int(_round.score) == RoundRules.Score.SPEED and answered:
			label = "%0.2f" % _times[p]
		_text(offset, scale, "RoundInstructionsSmall", label,
			x, top + side + 5, side, 18, 0.62, Color.WHITE, "Centre")

		# The rosette for where they finished, once the answers are in.
		if _phase == Phase.REVEAL and _places[p] >= 0:
			_sprite(offset, scale, PLACE_SPRITES[_places[p]],
				x + L.rank_icon.x - 50, top + L.rank_icon.y, 46, 35)

		if bomb and spot:
			_draw_bomb(offset, scale, x, top, side)


## The bomb rides with whoever is holding it, and goes off over their card.
func _draw_bomb(offset: Vector2, scale: float, x: float, top: float, side: float) -> void:
	if _burst > 0.0:
		_sprite(offset, scale, "PIP_flame",
			x - 28, top - 34, side + 56, 84, Color(1, 1, 1, minf(_burst, 1.0)))
		_sprite(offset, scale, "PIP_boom", x - 16, top + 6, side + 32, 58)
		return

	_sprite(offset, scale, "PIP_bomb", x + side - 22, top - 30, 38, 44)
	# The spark on the fuse quickens as the fuse runs down.
	if _fuse > 0.0 and fmod(_fuse, maxf(_fuse * 0.12, 0.18)) < 0.09:
		_sprite(offset, scale, "PIP_spark", x + side - 4, top - 40, 20, 20)


func _draw_scores(offset: Vector2, scale: float) -> void:
	_text(offset, scale, "ExtraLarge", "SCORES", 0, 56, PANEL.x, 50, 0.8, Color.WHITE, "Centre")

	for p in range(_players):
		var y := 150.0 + p * 62.0
		_sprite(offset, scale, "ViewportBarGrey", 96, y - 4, 320, 42)
		_text(offset, scale, "GeneralLarge", "SPELER %d" % (p + 1),
			112, y, 200, 34, 0.9, Color.WHITE)
		_text(offset, scale, "GeneralLarge", str(_scores[p]),
			200, y, 200, 34, 0.9, Color(0.95, 0.88, 0.45), "Right")


func _exit_tree() -> void:
	_lamps.all(false)
	_lamps.stop()
