extends Control

## A playable round on the real hardware.
##
## The wired Buzz buzzers enumerate as one HID game controller - "Namtai Buzz",
## vendor 0x054C product 0x1000 - carrying all four handsets in a single report.
## SDL has no gamepad mapping for it, so the buttons are read raw, five per
## handset in report order.
##
## The report order was measured on the hardware, not assumed:
##
##     0  red buzzer      1  yellow      2  green      3  orange      4  blue
##
## That is bottom-to-top on the handset, while the answers are listed
## top-to-bottom on screen, so the four answer buttons map in reverse.

const CANVAS := Vector2(640, 480)
const PLAYERS := 4
const BUTTONS_PER_HANDSET := 5

## Index within a handset, in report order.
const BUZZ := 0

## Answer slot per on-screen option, top to bottom: blue, orange, green, yellow.
const ANSWER_BUTTONS := [4, 3, 2, 1]

const BUTTON_NAMES := ["red", "yellow", "green", "orange", "blue"]

const COLOURS := [
	Color(0.29, 0.53, 0.91),   # blue
	Color(0.95, 0.60, 0.16),  # orange
	Color(0.30, 0.76, 0.35),   # green
	Color(0.96, 0.85, 0.20),   # yellow
]

enum Phase { LISTENING, BUZZED, REVEAL, FINISHED }

@onready var _canvas: Control = %RoundCanvas
@onready var _status: Label = %RoundStatus
@onready var _audio: AudioStreamPlayer = %Audio

var _bundle := Bundle.new()
var _lamps := Lamps.new()
var _pad := -1
var _held := {}

var _questions: Array = []
var _index := 0
var _phase: int = Phase.LISTENING
var _buzzed := -1
var _chosen := -1
var _scores := [0, 0, 0, 0]
var _hold := 0.0
var _wav_dir := ""
var _last_button := ""
var _flash := 0.0


func _ready() -> void:
	if not _bundle.load_from(Bundle.base_dir()):
		_status.text = "Could not find extracted/godot2d. Run 'obz bundle' first."
		return

	_questions = _bundle.quiz
	if _questions.is_empty():
		_status.text = "No questions in the bundle. Run 'obz bundle' after 'obz audio decode'."
		return

	_questions.shuffle()
	_wav_dir = _bundle.dir.get_base_dir().path_join("wav")
	_canvas.draw.connect(_draw_round)
	_find_pad()
	_lamps.start(Bundle.base_dir())
	_start_question()


func _find_pad() -> void:
	_pad = -1
	for id in Input.get_connected_joypads():
		var info := Input.get_joy_info(id)
		# Sony 0x054C, Buzz buzzers 0x1000.
		if str(info.get("vendor_id", "")) == "1356" and str(info.get("product_id", "")) == "4096":
			_pad = id
			return
	# Fall back to the first pad so a substitute controller still drives it.
	var pads := Input.get_connected_joypads()
	if not pads.is_empty():
		_pad = pads[0]


func _start_question() -> void:
	_phase = Phase.LISTENING
	_buzzed = -1
	_chosen = -1
	_hold = 0.0

	# All four lit is the invitation to buzz.
	_lamps.all(true)

	var q: Dictionary = _questions[_index]
	var path := _wav_dir.path_join("%s.wav" % str(q["clip"]))
	if FileAccess.file_exists(path):
		var stream := AudioStreamWAV.load_from_file(path)
		if stream != null:
			_audio.stream = stream
			_audio.play()
	_canvas.queue_redraw()


func _process(delta: float) -> void:
	_poll_buttons()

	if _phase == Phase.REVEAL:
		_hold -= delta
		_flash += delta

		# A right answer blinks the lamp, a wrong one leaves it dark.
		if _chosen == int(_questions[_index]["correct"]):
			if fmod(_flash, 0.3) < 0.15:
				_lamps.only(_buzzed)
			else:
				_lamps.all(false)
		else:
			_lamps.all(false)

		if _hold <= 0.0:
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
		Phase.LISTENING: return "listening - hit a buzzer"
		Phase.BUZZED: return "player %d buzzed - pick a colour" % (_buzzed + 1)
		Phase.REVEAL: return "correct" if _chosen == int(_questions[_index]["correct"]) else "wrong"
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
	if _phase == Phase.LISTENING and slot == BUZZ:
		_buzzed = player
		_phase = Phase.BUZZED
		_audio.stop()
		_lamps.only(player)
		return

	if _phase == Phase.BUZZED and player == _buzzed:
		var choice := ANSWER_BUTTONS.find(slot)
		if choice < 0:
			return
		_chosen = choice
		var correct := int(_questions[_index]["correct"])
		_scores[player] += 1 if choice == correct else -1
		_phase = Phase.REVEAL
		_hold = 3.0
		_flash = 0.0


func _unhandled_input(event: InputEvent) -> void:
	# Keyboard stands in for the handsets: 1-4 buzz, QWER/ASDF/ZXCV/UIOP answer.
	if not (event is InputEventKey and event.pressed and not event.echo):
		return
	var key: int = event.keycode
	const BUZZ_KEYS := [KEY_1, KEY_2, KEY_3, KEY_4]
	const ANSWER_KEYS := [
		[KEY_Q, KEY_W, KEY_E, KEY_R],
		[KEY_A, KEY_S, KEY_D, KEY_F],
		[KEY_Z, KEY_X, KEY_C, KEY_V],
		[KEY_U, KEY_I, KEY_O, KEY_P],
	]
	var at := BUZZ_KEYS.find(key)
	if at >= 0:
		_on_press(at, BUZZ)
		return
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

	if _phase == Phase.FINISHED:
		_bundle.draw_wrapped(_canvas, "ExtraLarge", "EINDE",
			Rect2(offset, CANVAS * scale), scale, Color.WHITE)
		return

	var q: Dictionary = _questions[_index]

	# QuestionFontName is "GeneralLarge" at scaling 1, per GenericData.lua.
	_bundle.draw_wrapped(_canvas, "GeneralLarge",
		"%d. %s" % [_index + 1, str(q["question"])],
		Rect2(offset + Vector2(40, 26) * scale, Vector2(CANVAS.x - 80, 76) * scale),
		scale, Color.WHITE)

	var options: Array = q["options"]
	for i in range(options.size()):
		var y := 150.0 + i * 52.0
		var swatch := Rect2(offset + Vector2(72, y) * scale, Vector2(34, 34) * scale)
		_canvas.draw_rect(swatch, Color(0.07, 0.09, 0.12))
		_canvas.draw_rect(swatch, COLOURS[i], false, 3.0 * scale)

		var tint := Color.WHITE
		if _phase == Phase.REVEAL:
			tint = Color(0.59, 1.0, 0.67) if i == int(q["correct"]) else Color(0.59, 0.62, 0.67)

		# AnswerFontName is "GeneralLarge" at scaling 0.9.
		_bundle.draw_wrapped(_canvas, "GeneralLarge", str(options[i]),
			Rect2(offset + Vector2(122, y - 4) * scale, Vector2(CANVAS.x - 150, 42) * scale),
			scale * 0.9, tint, "Left")

		if _phase == Phase.REVEAL and i == _chosen:
			_canvas.draw_circle(offset + Vector2(59, y + 17) * scale, 6.0 * scale, Color.WHITE)

	for p in range(PLAYERS):
		var x := 60.0 + p * 140.0
		var box := Rect2(offset + Vector2(x, 392) * scale, Vector2(110, 56) * scale)
		var lit := _phase != Phase.LISTENING and _buzzed == p
		_canvas.draw_rect(box, Color(0.86, 0.16, 0.16) if lit else Color(0.13, 0.14, 0.18))
		_canvas.draw_rect(box, Color(0.4, 0.42, 0.5), false, 1.0 * scale)
		_bundle.draw_wrapped(_canvas, "RoundInstructionsSmall",
			"SPELER %d" % (p + 1),
			Rect2(box.position, Vector2(box.size.x, box.size.y * 0.5)), scale * 0.8, Color.WHITE)
		_bundle.draw_wrapped(_canvas, "GeneralLarge", str(_scores[p]),
			Rect2(box.position + Vector2(0, box.size.y * 0.42), Vector2(box.size.x, box.size.y * 0.5)),
			scale * 0.8, Color.WHITE)


func _exit_tree() -> void:
	_lamps.all(false)
	_lamps.stop()
