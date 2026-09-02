class_name FrontEnd
extends RefCounted

## The menus and player setup that come before a round, in the game's own order.
##
## Nothing here is invented. The flow is what the scripts do: MainMenu offers
## MenuPlayMultiplayer / MenuPlaySingleplayer / MenuOptions / MenuExtras and,
## for multiplayer, calls ChoosePositions and ClearAndRestartMappingDevices
## before handing off to MultiPlayerGameLengthMenu. That menu sets
## NameOfGameToPlay to Short/Medium/LongMultiplayerGame and hands off to
## GameTypeMenu, whose three items call SetRoundHistoricalBiasNone, Early and
## Late. Then CharacterSelectMultiBeta runs its stages in the order its enums
## are declared: BUZZTOSTART, CHARTYPE, MODEL, BUZZER, NAMEENTRY, END.
##
## The button map is CharacterSelectMultiBeta's ProcessButtonPushes:
## BlueTriangleButton steps back through a list, YellowSquareButton steps
## forward, the Buzzer accepts, and GreenCrossButton returns the last choice.
##
## The titles, options and the 24 buzzer names are the disc's own Dutch, keyed
## through docs/text-key-map.txt.

signal finished(config: Dictionary)

enum Screen { MAIN, LENGTH, MUSIC, JOIN, CHARACTER, COSTUME, BUZZER, NAME, READY }

## Handset slots, as the hardware reports them.
const BUZZ := 0
const YELLOW := 1
const GREEN := 2
const BLUE := 4

const SEATS := 4

## The 16 contestants on the disc, each with three costumes - which is what
## OutfitOne, OutfitTwo and OutfitThree in CharacterSelectShared are.
const CAST := [
	"Angie", "Ash", "Barley", "Bradley", "Cinnamon", "Gina", "Jean", "Keiko",
	"Mercy", "Pelvis", "Punk", "Razor", "Stevie", "Tina", "Walrus", "Winona",
]
const COSTUMES := 3

## The buzzer sounds, in the order CharacterSelectShared lists them. The Dutch
## names come from the text table; this is the key order that indexes them.
const BUZZERS := [
	"Awooga", "AirHorn", "Alarm", "Belch", "CarHorn", "Cat", "Chicken",
	"Chipmunk", "DogBark", "Duck", "EvilLaugh", "Frog", "GirlLaugh", "Goose",
	"Horn", "Horse", "Monkey", "Sheep", "Siren", "Space", "Stadium", "Train",
	"Turkey", "Whistle",
]

## The name-entry alphabet, as GetUnLocalizedAlphabet builds it: A..Z, then
## backspace and done.
const ALPHABET := "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
const MAX_NAME := 10

var screen: int = Screen.MAIN
var config := {}

var _bundle
var _cursor := 0
var _length := ""
var _music := ""

## Per seat: whether they joined, and what they have chosen so far.
var _joined := [false, false, false, false]
var _pick := [0, 0, 0, 0]
var _character := [-1, -1, -1, -1]
var _costume := [0, 0, 0, 0]
var _buzzer := [-1, -1, -1, -1]
var _name := ["", "", "", ""]
var _settled := [false, false, false, false]


func _init(bundle) -> void:
	_bundle = bundle


func _t(key: String, fallback: String) -> String:
	var text: Dictionary = _bundle.text
	return str(text.get(key, fallback))


# ------------------------------------------------------------------ screens

## The item list for whichever menu is showing. Each entry is [label, tag].
func items() -> Array:
	match screen:
		Screen.MAIN:
			return [
				[_t("MenuPlayMultiplayer", "SPEL VOOR MEERDERE SPELERS"), "multi"],
				[_t("MenuPlaySingleplayer", "SPEL VOOR EEN SPELER"), "single"],
				[_t("MenuOptions", "OPTIEMENU"), "options"],
				[_t("MenuExtras", "EXTRA'S"), "extras"],
			]
		Screen.LENGTH:
			return [
				[_t("MultiplayerGameLengthShort", "KORT SPEL"), "short"],
				[_t("MultiplayerGameLengthMedium", "MIDDELLANG SPEL"), "medium"],
				[_t("MultiplayerGameLengthLong", "LANG SPEL"), "long"],
				[_t("OptionReturnToMain", "TERUG NAAR HOOFDMENU"), "back"],
			]
		Screen.MUSIC:
			return [
				[_t("MusicTypeOptionAll", "ALLE MUZIEK"), "all"],
				[_t("MusicTypeOptionOlder", "OUDE MUZIEK"), "old"],
				[_t("MusicTypeOptionNewer", "MODERNE MUZIEK"), "new"],
				[_t("OptionReturnToGameLength", "TERUG NAAR SPELDUUR"), "back"],
			]
	return []


func title() -> String:
	match screen:
		Screen.MAIN: return _t("MenuMainMenuText", "HOOFDMENU")
		Screen.LENGTH: return _t("MenuGameLengthText", "SPELDUUR")
		Screen.MUSIC: return _t("MenuMusicTypeText", "MUZIEKGENRE")
		Screen.JOIN: return _t("BuzzToJoinPrompt", "DRUK OP DE ZOEMER OM TE SPELEN")
		Screen.CHARACTER: return _t("PlayerSetupTitleCharacter", "KIES EEN PERSONAGE")
		Screen.COSTUME: return _t("PlayerSetupTitleCostume", "KIES KLEDING")
		Screen.BUZZER: return _t("PlayerSetupTitleBuzzer", "KIES EEN ZOEMER")
		Screen.NAME: return _t("PlayerSetupTitleNameEntry", "VOER JE NAAM IN")
	return ""


## What a seat is choosing between on the current stage. The stages all offer
## the same list to everyone; which one a seat is on is its own cursor.
func options_for(_seat: int) -> Array:
	match screen:
		Screen.CHARACTER:
			return CAST
		Screen.COSTUME:
			return range(1, COSTUMES + 1).map(func(n): return "OUTFIT %d" % n)
		Screen.BUZZER:
			return BUZZERS.map(func(b): return _t("BuzzerName" + b, b))
		Screen.NAME:
			var letters := []
			for c in ALPHABET:
				letters.append(c)
			letters.append(_t("NameEntryAlphabetBackspace", "SCHRAP"))
			letters.append(_t("NameEntryAlphabetDone", "GEREED"))
			return letters
	return []


func label_for(seat: int) -> String:
	return _name[seat] if _name[seat] != "" \
		else _t("PlayerAndNumber%d" % (seat + 1), "SPELER %d" % (seat + 1))


func joined(seat: int) -> bool:
	return _joined[seat]


func settled(seat: int) -> bool:
	return _settled[seat]


func cursor_of(seat: int) -> int:
	return _pick[seat]


func any_joined() -> int:
	var n := 0
	for j in _joined:
		if j:
			n += 1
	return n


# -------------------------------------------------------------------- input

## One button on one handset. Menus take input from any handset, the way the
## game lets whoever is holding one drive the clipboard; the setup stages are
## per seat.
func press(handset: int, slot: int) -> void:
	if screen == Screen.MAIN or screen == Screen.LENGTH or screen == Screen.MUSIC:
		_menu_press(slot)
		return
	if handset < 0 or handset >= SEATS:
		return
	_setup_press(handset, slot)


func _menu_press(slot: int) -> void:
	var list := items()
	if list.is_empty():
		return

	match slot:
		BLUE:
			_cursor = (_cursor - 1 + list.size()) % list.size()
		YELLOW:
			_cursor = (_cursor + 1) % list.size()
		BUZZ:
			_choose(str(list[_cursor][1]))
		GREEN:
			_back()


func _choose(tag: String) -> void:
	match screen:
		Screen.MAIN:
			# Only multiplayer is built. The other three are real menu items on
			# the disc and are left showing rather than hidden, so the menu is
			# the game's and not a trimmed version of it.
			if tag == "multi":
				screen = Screen.LENGTH
				_cursor = 0
		Screen.LENGTH:
			if tag == "back":
				_back()
			else:
				_length = tag
				screen = Screen.MUSIC
				_cursor = 0
		Screen.MUSIC:
			if tag == "back":
				_back()
			else:
				_music = tag
				screen = Screen.JOIN
				_cursor = 0


func _back() -> void:
	match screen:
		Screen.LENGTH:
			screen = Screen.MAIN
		Screen.MUSIC:
			screen = Screen.LENGTH
	_cursor = 0


func _setup_press(seat: int, slot: int) -> void:
	if screen == Screen.JOIN:
		if slot == BUZZ and not _joined[seat]:
			_joined[seat] = true
			Log.info("setup", "seat %d joined" % (seat + 1))
		return

	if not _joined[seat] or _settled[seat]:
		# An undo reopens a settled seat, which is what GreenCrossButton does.
		if slot == GREEN and _settled[seat]:
			_settled[seat] = false
		return

	var list := options_for(seat)
	if list.is_empty():
		return

	match slot:
		BLUE:
			_pick[seat] = (_pick[seat] - 1 + list.size()) % list.size()
		YELLOW:
			_pick[seat] = (_pick[seat] + 1) % list.size()
		GREEN:
			_undo(seat)
		BUZZ:
			_accept(seat, list)


func _accept(seat: int, list: Array) -> void:
	match screen:
		Screen.CHARACTER:
			# A character somebody already took cannot be taken again -
			# CanWeTakeThisFieldIndex, and the wheel greys it out.
			if _taken(_character, _pick[seat]):
				return
			_character[seat] = _pick[seat]
			_settled[seat] = true
		Screen.COSTUME:
			_costume[seat] = _pick[seat]
			_settled[seat] = true
		Screen.BUZZER:
			if _taken(_buzzer, _pick[seat]):
				return
			_buzzer[seat] = _pick[seat]
			_settled[seat] = true
		Screen.NAME:
			var at: int = _pick[seat]
			if at == list.size() - 1:
				if _name[seat] == "":
					_name[seat] = "SPELER %d" % (seat + 1)
				_settled[seat] = true
			elif at == list.size() - 2:
				_name[seat] = _name[seat].substr(0, maxi(_name[seat].length() - 1, 0))
			elif _name[seat].length() < MAX_NAME:
				_name[seat] += ALPHABET[at]


func _undo(seat: int) -> void:
	if screen == Screen.NAME and _name[seat] != "":
		_name[seat] = _name[seat].substr(0, _name[seat].length() - 1)
		return
	_settled[seat] = false


func _taken(table: Array, index: int) -> bool:
	for v in table:
		if v == index:
			return true
	return false


# --------------------------------------------------------------- advancing

## Called each frame. Moves the setup on when everyone active has finished the
## stage, which is HaveAllActivePlayersCompletedThisStage.
func advance(join_seconds: float) -> void:
	if screen == Screen.JOIN:
		if any_joined() >= 2 and join_seconds <= 0.0:
			_start_stage(Screen.CHARACTER)
		return

	if screen < Screen.CHARACTER or screen > Screen.NAME:
		return

	for seat in range(SEATS):
		if _joined[seat] and not _settled[seat]:
			return

	match screen:
		Screen.CHARACTER: _start_stage(Screen.COSTUME)
		Screen.COSTUME: _start_stage(Screen.BUZZER)
		Screen.BUZZER: _start_stage(Screen.NAME)
		Screen.NAME: _finish()


func _start_stage(next: int) -> void:
	screen = next
	for seat in range(SEATS):
		_settled[seat] = not _joined[seat]
		# The costume wheel opens on the character's own first outfit, and the
		# others open at the top of their list.
		_pick[seat] = _costume[seat] if next == Screen.COSTUME else 0
	Log.info("setup", "stage %s" % title())


func _finish() -> void:
	var players := []
	for seat in range(SEATS):
		if not _joined[seat]:
			continue
		players.append({
			seat = seat,
			character = CAST[_character[seat]] if _character[seat] >= 0 else CAST[seat],
			costume = _costume[seat] + 1,
			buzzer = BUZZERS[_buzzer[seat]] if _buzzer[seat] >= 0 else BUZZERS[seat],
			name = _name[seat],
		})

	config = {length = _length, music = _music, players = players}
	screen = Screen.READY
	Log.info("setup", "%s / %s, %d players" % [_length, _music, players.size()])
	finished.emit(config)
