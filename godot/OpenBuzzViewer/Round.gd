extends Control

## Point Builder - "Punten verdienen".
##
## The rules come from the game's own scripts rather than from memory.
## PointsBuilderRound.luaasm calls AllowAllContestantsToAnswer, so every player
## answers the same question at once - there is no buzzing in - under a
## ShowCountdownTimer of 15. GenericData.luaasm sets PointsReduceWithTime only
## for SpeedTimeBuilder, and SinglePlayerRound only for the Time Builder rounds,
## so here the award is flat and everyone plays.
##
## The one number not recovered from the data is the size of that award: it is
## handed out by the engine, not by Lua. POINTS below is a stand-in.
##
## Handset report order, measured on the hardware:
##     0  red buzzer   1  yellow   2  green   3  orange   4  blue
## That is bottom-to-top, while answers are listed top-to-bottom, so the four
## answer buttons map in reverse.

const CANVAS := Vector2(640, 480)
const PLAYERS := 4
const BUTTONS_PER_HANDSET := 5

const BUZZ := 0
const ANSWER_BUTTONS := [4, 3, 2, 1]
const BUTTON_NAMES := ["red", "yellow", "green", "orange", "blue"]

## ShowCountdownTimer is called with 15 in PointsBuilderRound.
const ANSWER_SECONDS := 15.0

## Not in the scripts - the engine awards it. A stand-in.
const POINTS := 1000

const COLOURS := [
	Color(0.29, 0.53, 0.91),   # blue
	Color(0.95, 0.60, 0.16),   # orange
	Color(0.30, 0.76, 0.35),   # green
	Color(0.96, 0.85, 0.20),   # yellow
]

enum Phase { INTRO, ANSWERING, REVEAL, SCORES, FINISHED }

@onready var _canvas: Control = %RoundCanvas
@onready var _status: Label = %RoundStatus
@onready var _audio: AudioStreamPlayer = %Audio

var _bundle := Bundle.new()
var _lamps := Lamps.new()
var _pad := -1
var _held := {}

var _questions: Array = []
var _index := 0
var _phase: int = Phase.INTRO
var _answers := [-1, -1, -1, -1]
var _scores := [0, 0, 0, 0]
var _awarded := [0, 0, 0, 0]
var _clock := 0.0
var _wav_dir := ""
var _last_button := ""

## --demo plays itself, so the phases can be captured without a person at the
## buzzers. Verification only; it changes nothing about how the round runs.
var _demo := false


func _ready() -> void:
	if not _bundle.load_from(Bundle.base_dir()):
		_status.text = "Could not find extracted/godot2d. Run 'obz bundle' first."
		return

	_questions = _bundle.quiz
	if _questions.is_empty():
		_status.text = "No questions. Run 'obz audio decode' then 'obz bundle'."
		return

	_questions.shuffle()
	_wav_dir = _bundle.dir.get_base_dir().path_join("wav")
	_canvas.draw.connect(_draw_round)
	_find_pad()
	_lamps.start(Bundle.base_dir())
	_demo = OS.get_cmdline_user_args().has("--demo")
	if _demo:
		_start_question()
	else:
		_enter_intro()


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


func _enter_intro() -> void:
	_phase = Phase.INTRO
	_clock = 0.0
	_lamps.all(true)


func _start_question() -> void:
	_phase = Phase.ANSWERING
	_answers = [-1, -1, -1, -1]
	_awarded = [0, 0, 0, 0]
	_clock = ANSWER_SECONDS

	# Everyone is live, so every lamp is lit; a lamp goes out once that player
	# has locked an answer in.
	_lamps.all(true)

	var q: Dictionary = _questions[_index]
	var path := _wav_dir.path_join("%s.wav" % str(q["clip"]))
	if FileAccess.file_exists(path):
		var stream := AudioStreamWAV.load_from_file(path)
		if stream != null:
			_audio.stream = stream
			_audio.play()


func _finish_question() -> void:
	_phase = Phase.REVEAL
	_clock = 3.0
	_audio.stop()
	_lamps.all(false)

	var correct := int(_questions[_index]["correct"])
	for p in range(PLAYERS):
		if _answers[p] == correct:
			_awarded[p] = POINTS
			_scores[p] += POINTS


func _process(delta: float) -> void:
	_poll_buttons()
	_clock -= delta

	match _phase:
		Phase.ANSWERING:
			if _demo and _clock < ANSWER_SECONDS - 0.5:
				for p in range(PLAYERS):
					if _answers[p] == -1:
						_on_press(p, ANSWER_BUTTONS[p % 4])
			# The timer ends the question, and so does everyone answering.
			if _clock <= 0.0 or not _answers.has(-1):
				_finish_question()
		Phase.REVEAL:
			if _clock <= 0.0:
				_phase = Phase.SCORES
				_clock = 3.0
		Phase.SCORES:
			if _clock <= 0.0:
				_index += 1
				if _index >= _questions.size():
					_phase = Phase.FINISHED
					_lamps.all(false)
				else:
					_start_question()

	_canvas.queue_redraw()
	_status.text = "%s   |   pad %s   |   lamps %s   |   %s" % [
		_phase_name(), "none" if _pad < 0 else str(_pad),
		"on" if _lamps.available else _lamps.reason, _last_button]


func _phase_name() -> String:
	match _phase:
		Phase.INTRO: return "press any answer button to start"
		Phase.ANSWERING: return "answering - %0.1fs" % maxf(_clock, 0.0)
		Phase.REVEAL: return "revealing"
		Phase.SCORES: return "scores"
		_: return "finished"


## Edge-detects every handset button, so a held button fires once.
func _poll_buttons() -> void:
	if _pad < 0:
		return
	for player in range(PLAYERS):
		for slot in range(BUTTONS_PER_HANDSET):
			var button := player * BUTTONS_PER_HANDSET + slot
			var down := Input.is_joy_button_pressed(_pad, button)
			var was: bool = _held.get(button, false)
			_held[button] = down
			if down and not was:
				_last_button = "player %d, %s (raw %d)" % [player + 1, BUTTON_NAMES[slot], button]
				_on_press(player, slot)


func _on_press(player: int, slot: int) -> void:
	if _phase == Phase.INTRO:
		_start_question()
		return

	if _phase != Phase.ANSWERING:
		return

	var choice := ANSWER_BUTTONS.find(slot)
	if choice < 0:
		return

	# First answer stands; there is no changing it.
	if _answers[player] != -1:
		return

	_answers[player] = choice
	_lamps.set_lamps([_answers[0] == -1, _answers[1] == -1, _answers[2] == -1, _answers[3] == -1])


func _unhandled_input(event: InputEvent) -> void:
	# Keyboard stands in: QWER / ASDF / ZXCV / UIOP answer.
	if not (event is InputEventKey and event.pressed and not event.echo):
		return
	var key: int = event.keycode
	const ANSWER_KEYS := [
		[KEY_Q, KEY_W, KEY_E, KEY_R],
		[KEY_A, KEY_S, KEY_D, KEY_F],
		[KEY_Z, KEY_X, KEY_C, KEY_V],
		[KEY_U, KEY_I, KEY_O, KEY_P],
	]
	for player in range(PLAYERS):
		var slot: int = ANSWER_KEYS[player].find(key)
		if slot >= 0:
			_on_press(player, ANSWER_BUTTONS[slot])
			return


func _draw_round() -> void:
	var view := _canvas.size
	var scale := minf(view.x / CANVAS.x, view.y / CANVAS.y)
	var offset := (view - CANVAS * scale) * 0.5
	_canvas.draw_rect(Rect2(offset, CANVAS * scale), Color(0.07, 0.08, 0.11))

	if _questions.is_empty():
		return

	match _phase:
		Phase.INTRO:
			_draw_intro(offset, scale)
		Phase.FINISHED:
			_bundle.draw_wrapped(_canvas, "ExtraLarge", "EINDE",
				Rect2(offset, CANVAS * scale), scale, Color.WHITE)
		Phase.SCORES:
			_draw_scores(offset, scale)
		_:
			_draw_question(offset, scale)
			_draw_podiums(offset, scale)


## The round title and its rule line, in the fonts the A2D bindings name for
## them - RoundInstructionsLarge and RoundInstructionsSmall.
func _draw_intro(offset: Vector2, scale: float) -> void:
	_bundle.draw_wrapped(_canvas, "RoundInstructionsLarge",
		str(_bundle.text.get("RulesPointsBuilderTitle", "PUNTEN VERDIENEN")),
		Rect2(offset + Vector2(40, 110) * scale, Vector2(CANVAS.x - 80, 60) * scale),
		scale * 1.25, Color.WHITE)

	_bundle.draw_wrapped(_canvas, "RoundInstructionsSmall",
		str(_bundle.text.get("RulesPointsBuilderLine1", "")),
		Rect2(offset + Vector2(80, 210) * scale, Vector2(CANVAS.x - 160, 90) * scale),
		scale, Color(0.82, 0.84, 0.9))


func _draw_question(offset: Vector2, scale: float) -> void:
	var q: Dictionary = _questions[_index]
	var correct := int(q["correct"])

	_bundle.draw_wrapped(_canvas, "GeneralLarge",
		"%d. %s" % [_index + 1, str(q["question"])],
		Rect2(offset + Vector2(40, 18) * scale, Vector2(CANVAS.x - 80, 70) * scale),
		scale, Color.WHITE)

	if _phase == Phase.ANSWERING:
		_canvas.draw_rect(Rect2(offset + Vector2(40, 98) * scale,
			Vector2((CANVAS.x - 80) * clampf(_clock / ANSWER_SECONDS, 0.0, 1.0), 5) * scale),
			Color(0.85, 0.78, 0.25))

	var options: Array = q["options"]
	for i in range(options.size()):
		var y := 140.0 + i * 50.0
		var swatch := Rect2(offset + Vector2(60, y) * scale, Vector2(32, 32) * scale)
		_canvas.draw_rect(swatch, Color(0.07, 0.09, 0.12))
		_canvas.draw_rect(swatch, COLOURS[i], false, 3.0 * scale)

		var tint := Color.WHITE
		if _phase == Phase.REVEAL:
			tint = Color(0.59, 1.0, 0.67) if i == correct else Color(0.55, 0.58, 0.64)

		_bundle.draw_wrapped(_canvas, "GeneralLarge", str(options[i]),
			Rect2(offset + Vector2(108, y - 4) * scale, Vector2(CANVAS.x - 150, 40) * scale),
			scale * 0.9, tint, "Left")


## One podium per player. During the question only the fact of an answer shows,
## never which one - that is what stops the table copying each other.
func _draw_podiums(offset: Vector2, scale: float) -> void:
	var correct := int(_questions[_index]["correct"])

	for p in range(PLAYERS):
		var box := Rect2(offset + Vector2(48 + p * 140, 372) * scale, Vector2(120, 84) * scale)
		var answered: bool = _answers[p] != -1
		_canvas.draw_rect(box, Color(0.13, 0.15, 0.2) if answered else Color(0.10, 0.11, 0.14))
		_canvas.draw_rect(box, Color(0.38, 0.4, 0.48), false, 1.0 * scale)

		_bundle.draw_wrapped(_canvas, "RoundInstructionsSmall", "SPELER %d" % (p + 1),
			Rect2(box.position + Vector2(0, 4 * scale), Vector2(box.size.x, 18 * scale)),
			scale * 0.75, Color.WHITE)

		if answered:
			var swatch := Rect2(box.position + Vector2(box.size.x * 0.5 - 11 * scale, 28 * scale),
				Vector2(22, 22) * scale)
			_canvas.draw_rect(swatch,
				COLOURS[_answers[p]] if _phase == Phase.REVEAL else Color(0.55, 0.58, 0.66))

		if _phase == Phase.REVEAL:
			var right: bool = _answers[p] == correct
			_bundle.draw_wrapped(_canvas, "GeneralLarge",
				("+%d" % _awarded[p]) if right else "-",
				Rect2(box.position + Vector2(0, 56 * scale), Vector2(box.size.x, 24 * scale)),
				scale * 0.7, Color(0.6, 1.0, 0.7) if right else Color(0.7, 0.42, 0.42))
		else:
			_bundle.draw_wrapped(_canvas, "GeneralLarge", str(_scores[p]),
				Rect2(box.position + Vector2(0, 56 * scale), Vector2(box.size.x, 24 * scale)),
				scale * 0.7, Color.WHITE)


func _draw_scores(offset: Vector2, scale: float) -> void:
	_bundle.draw_wrapped(_canvas, "ExtraLarge", "SCORES",
		Rect2(offset + Vector2(0, 60) * scale, Vector2(CANVAS.x, 50) * scale),
		scale * 0.8, Color.WHITE)

	for p in range(PLAYERS):
		var y := 170.0 + p * 60.0
		_bundle.draw_wrapped(_canvas, "GeneralLarge", "SPELER %d" % (p + 1),
			Rect2(offset + Vector2(120, y) * scale, Vector2(200, 34) * scale),
			scale, Color.WHITE, "Left")
		_bundle.draw_wrapped(_canvas, "GeneralLarge", str(_scores[p]),
			Rect2(offset + Vector2(320, y) * scale, Vector2(200, 34) * scale),
			scale, Color.WHITE, "Right")


func _exit_tree() -> void:
	_lamps.all(false)
	_lamps.stop()
