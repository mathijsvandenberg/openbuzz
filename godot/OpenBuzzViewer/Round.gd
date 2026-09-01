extends Control

## Plays any of the ten round types on the real buzzers.
##
## What each round is comes from RoundRules, which reads it out of the game:
## the input model from <Name>Round.luaasm, the parameters from
## GenericData.luaasm, and the on-screen title and rules from the game's own
## text table. This file is the machine that runs them.
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

const CHASE_STEP := 0.16
const CUE_STEP := 1.0

enum Phase { INTRO, PLAYING, PICKING, REVEAL, DONE }

@onready var _canvas: Control = %RoundCanvas
@onready var _status: Label = %RoundStatus
@onready var _audio: AudioStreamPlayer = %Audio
@onready var _list: ItemList = %Rounds
@onready var _blurb: Label = %Blurb

var _bundle := Bundle.new()
var _lamps := Lamps.new()
var _pad := -1
var _held := {}
var _demo := false

var _round: Dictionary = {}
var _questions: Array = []
var _index := 0
var _phase: int = Phase.INTRO

var _answers := [-1, -1, -1, -1]
var _times := [0.0, 0.0, 0.0, 0.0]
var _scores := [0, 0, 0, 0]
var _awarded := [0, 0, 0, 0]
var _banked := [0.0, 0.0, 0.0, 0.0]

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

## A game is a queue of round ids. Empty means a single round, picked from the
## list for testing; scores and banked time carry across the whole queue.
var _queue: Array[String] = []
var _leg := 0
var _asked := 0


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

	for key in ["short", "medium", "long"]:
		_list.add_item("GAME - %s (%d rounds)" % [key.to_upper(), RoundRules.LENGTHS[key]])
	for r in RoundRules.all():
		_list.add_item(RoundRules.title(r, _bundle.text))
	_list.item_selected.connect(_pick)

	_find_pad()
	_lamps.start(Bundle.base_dir())
	_demo = OS.get_cmdline_user_args().has("--demo")

	var start := 3
	var args := OS.get_cmdline_user_args()
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
	_scores = [0, 0, 0, 0]
	_banked = [0.0, 0.0, 0.0, 0.0]
	_index = 0
	_active = 0
	_note = ""
	_phase = Phase.INTRO
	_audio.stop()
	_lamps.all(true)
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

	var path := _wav_dir.path_join("%s.wav" % str(q["clip"]))
	if FileAccess.file_exists(path):
		var stream := AudioStreamWAV.load_from_file(path)
		if stream != null:
			_audio.stream = stream
			_audio.play()


func _process(delta: float) -> void:
	_poll_buttons()

	if _phase == Phase.PLAYING:
		_clock -= delta
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
		if _clock <= 0.0:
			_next_question()

	_canvas.queue_redraw()
	var leg := "" if _queue.is_empty() else "game %d/%d - " % [_leg + 1, _queue.size()]
	_status.text = "%s%s   |   %s   |   pad %s   |   lamps %s   |   %s" % [
		leg, str(_round.get("id", "-")), _phase_name(),
		"none" if _pad < 0 else str(_pad),
		"on" if _lamps.available else _lamps.reason, _last_button]


## Everything that ticks while a question is live, per input model.
func _advance(delta: float) -> void:
	match int(_round.input):
		RoundRules.Mode.ALL:
			if _demo:
				for p in range(PLAYERS):
					if _answers[p] == -1 and _clock < float(_round.seconds) - 0.4 - p * 0.3:
						_answer(p, p % 4)
			if not _answers.has(-1) or _clock <= 0.0:
				_finish()

		RoundRules.Mode.BUZZ_THEN_ANSWER:
			if _demo and _winner < 0 and _clock < float(_round.seconds) - 0.5:
				_press(1, BUZZ)
			elif _demo and _winner >= 0 and _clock < float(_round.seconds) - 1.2:
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
				_active = (_active + 1) % PLAYERS
				_lamps.only(_active)
				_chase_clock = CHASE_STEP
			if not _audio.playing or _clock <= 0.0:
				_phase = Phase.PICKING
				_clock = 15.0
				_lamps.only(_active)
				_note = "SPELER %d" % (_active + 1)


## Nobody picked in time: the question just ends, unscored.
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
		_active = (_active + 1) % PLAYERS

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

	match int(_round.score):
		RoundRules.Score.FLAT:
			for p in range(PLAYERS):
				if _answers[p] == _correct:
					_awarded[p] = RoundRules.POINTS
					_scores[p] += RoundRules.POINTS

		RoundRules.Score.SPEED:
			# Ranked by how quickly the correct answer came in.
			var right := []
			for p in range(PLAYERS):
				if _answers[p] == _correct:
					right.append({p = p, t = _times[p]})
			right.sort_custom(func(a, b): return a.t < b.t)
			for rank in range(right.size()):
				var pts: int = RoundRules.SPEED_POINTS[mini(rank, RoundRules.SPEED_POINTS.size() - 1)]
				_awarded[right[rank].p] = pts
				_scores[right[rank].p] += pts

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


func _explode() -> void:
	_phase = Phase.REVEAL
	_clock = 3.0
	_audio.stop()
	_lamps.all(false)
	_scores[_active] -= RoundRules.POINTS
	_awarded[_active] = -RoundRules.POINTS
	_note = "BOEM"
	_fuse = randf_range(20.0, 40.0)


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
				_press(player, slot)


func _press(player: int, slot: int) -> void:
	if _phase == Phase.INTRO:
		_start_question()
		return

	if _phase == Phase.PICKING:
		_picked(player, slot)
		return

	if _phase != Phase.PLAYING:
		return

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

	if int(_round.input) == RoundRules.Mode.ALL:
		_lamps.set_lamps([_answers[0] == -1, _answers[1] == -1, _answers[2] == -1, _answers[3] == -1])


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


func _phase_name() -> String:
	match _phase:
		Phase.INTRO: return "press a button to start"
		Phase.PLAYING: return "playing - %0.1fs" % maxf(_clock, 0.0)
		Phase.PICKING: return "waiting for a pick"
		Phase.REVEAL: return "revealing"
		_: return "finished"


# ---------------------------------------------------------------- drawing

func _draw_round() -> void:
	var view := _canvas.size
	var scale := minf(view.x / CANVAS.x, view.y / CANVAS.y)
	var offset := (view - CANVAS * scale) * 0.5
	_canvas.draw_rect(Rect2(offset, CANVAS * scale), Color(0.07, 0.08, 0.11))

	if _questions.is_empty() or _round.is_empty():
		return

	match _phase:
		Phase.INTRO:
			_draw_intro(offset, scale)
		Phase.DONE:
			_draw_scores(offset, scale)
		_:
			_draw_question(offset, scale)
			_draw_podiums(offset, scale)


func _draw_intro(offset: Vector2, scale: float) -> void:
	_bundle.draw_wrapped(_canvas, "RoundInstructionsLarge",
		RoundRules.title(_round, _bundle.text),
		Rect2(offset + Vector2(30, 96) * scale, Vector2(CANVAS.x - 60, 60) * scale),
		scale * 1.2, Color.WHITE)

	var y := 190.0
	for line in RoundRules.lines(_round, _bundle.text):
		_bundle.draw_wrapped(_canvas, "RoundInstructionsSmall", line,
			Rect2(offset + Vector2(70, y) * scale, Vector2(CANVAS.x - 140, 60) * scale),
			scale * 0.95, Color(0.82, 0.84, 0.9))
		y += 70.0


func _draw_question(offset: Vector2, scale: float) -> void:
	var q: Dictionary = _questions[_index]
	var on_cue := int(_round.input) == RoundRules.Mode.BUZZ_ON_CUE

	_bundle.draw_wrapped(_canvas, "GeneralLarge",
		"%d. %s" % [_index + 1, str(q["question"])],
		Rect2(offset + Vector2(36, 14) * scale, Vector2(CANVAS.x - 72, 66) * scale),
		scale * 0.95, Color.WHITE)

	if _phase == Phase.PLAYING:
		var span := maxf(float(_round.seconds), 0.001)
		_canvas.draw_rect(Rect2(offset + Vector2(36, 92) * scale,
			Vector2((CANVAS.x - 72) * clampf(_clock / span, 0.0, 1.0), 5) * scale),
			Color(0.85, 0.78, 0.25))

	if int(_round.score) == RoundRules.Score.BOMB and _phase == Phase.PLAYING:
		_bundle.draw_wrapped(_canvas, "RoundInstructionsSmall", "BOM  %0.0f" % maxf(_fuse, 0.0),
			Rect2(offset + Vector2(CANVAS.x - 160, 100) * scale, Vector2(130, 24) * scale),
			scale * 0.8, Color(0.95, 0.5, 0.35), "Right")

	var options: Array = q["options"]
	for i in range(options.size()):
		var y := 132.0 + i * 48.0

		# On-cue rounds show one option at a time; the rest show all four.
		var visible := (not on_cue) or _phase != Phase.PLAYING or i == _cue
		if not visible:
			continue

		var swatch := Rect2(offset + Vector2(56, y) * scale, Vector2(30, 30) * scale)
		_canvas.draw_rect(swatch, Color(0.07, 0.09, 0.12))
		_canvas.draw_rect(swatch, COLOURS[i], false, 3.0 * scale)

		var tint := Color.WHITE
		if _phase == Phase.REVEAL:
			tint = Color(0.59, 1.0, 0.67) if i == _correct else Color(0.55, 0.58, 0.64)

		_bundle.draw_wrapped(_canvas, "GeneralLarge", str(options[_order[i]]),
			Rect2(offset + Vector2(102, y - 4) * scale, Vector2(CANVAS.x - 140, 38) * scale),
			scale * 0.85, tint, "Left")

	# Snap and Trigger Finger deliberately show one option at a time; without
	# saying so it just looks like the others failed to draw.
	if on_cue and _phase == Phase.PLAYING:
		_bundle.draw_wrapped(_canvas, "RoundInstructionsSmall",
			"DRUK OP DE ZOEMER BIJ HET JUISTE ANTWOORD",
			Rect2(offset + Vector2(40, 104) * scale, Vector2(CANVAS.x - 80, 22) * scale),
			scale * 0.7, Color(0.72, 0.76, 0.86))

		for slot in range(4):
			var dot := Rect2(offset + Vector2(CANVAS.x * 0.5 - 34 + slot * 18, 330) * scale,
				Vector2(11, 11) * scale)
			_canvas.draw_rect(dot, COLOURS[slot] if slot == _cue else Color(0.22, 0.24, 0.30))

	if _note != "":
		_bundle.draw_wrapped(_canvas, "ExtraLarge", _note,
			Rect2(offset + Vector2(0, 322) * scale, Vector2(CANVAS.x, 36) * scale),
			scale * 0.55, Color(0.95, 0.83, 0.35))


func _draw_podiums(offset: Vector2, scale: float) -> void:
	var live := int(_round.input) in [RoundRules.Mode.ACTIVE, RoundRules.Mode.CHASE]
	var banks := int(_round.score) in [RoundRules.Score.TIME, RoundRules.Score.STAKE]

	for p in range(PLAYERS):
		var box := Rect2(offset + Vector2(44 + p * 140, 372) * scale, Vector2(124, 88) * scale)
		var answered: bool = _answers[p] != -1
		var spot: bool = (live and p == _active) or (_winner == p)

		_canvas.draw_rect(box, Color(0.20, 0.17, 0.10) if spot else (
			Color(0.13, 0.15, 0.2) if answered else Color(0.10, 0.11, 0.14)))
		_canvas.draw_rect(box, Color(0.85, 0.7, 0.3) if spot else Color(0.38, 0.4, 0.48),
			false, (2.0 if spot else 1.0) * scale)

		_bundle.draw_wrapped(_canvas, "RoundInstructionsSmall", "SPELER %d" % (p + 1),
			Rect2(box.position + Vector2(0, 4 * scale), Vector2(box.size.x, 18 * scale)),
			scale * 0.72, Color.WHITE)

		if answered:
			var swatch := Rect2(box.position + Vector2(box.size.x * 0.5 - 11 * scale, 26 * scale),
				Vector2(22, 22) * scale)
			_canvas.draw_rect(swatch,
				COLOURS[_answers[p]] if _phase == Phase.REVEAL else Color(0.55, 0.58, 0.66))

		var label := str(_scores[p])
		var colour := Color.WHITE
		if banks:
			label = "%0.0fs" % _banked[p]
		if _phase == Phase.REVEAL and _awarded[p] != 0:
			label = ("+%d" % _awarded[p]) if _awarded[p] > 0 else str(_awarded[p])
			colour = Color(0.6, 1.0, 0.7) if _awarded[p] > 0 else Color(0.95, 0.5, 0.45)

		_bundle.draw_wrapped(_canvas, "GeneralLarge", label,
			Rect2(box.position + Vector2(0, 56 * scale), Vector2(box.size.x, 26 * scale)),
			scale * 0.68, colour)


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
