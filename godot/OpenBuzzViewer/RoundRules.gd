class_name RoundRules
extends RefCounted

## What each round type is, taken from the game rather than from memory.
##
## Three sources agree on every entry below:
##
##   * `<Name>Round.luaasm` says how players answer -
##     AllowAllContestantsToAnswer, AllowActiveContestantToAnswer or
##     AllowSingleContestantToBuzzIn - and how long ShowCountdownTimer runs.
##   * `GenericData.luaasm` sets RoundParameters per round: TopIconName is
##     InputAnswers or InputBuzzer, and PointsReduceWithTime and
##     SinglePlayerRound mark the two Time Builder variants.
##   * The Rules<Name>Title and Rules<Name>Line keys in the text table are the
##     game's own description, in Dutch, shown on the round intro.
##
## Where a mechanic needs data the port has not decoded - the statements Snap
## scrolls, for one - the entry keeps the real input model and approximates the
## content, and `approximates` says so on screen.

## How players are allowed to answer.
enum Mode {
	ALL,                ## everyone answers at once
	BUZZ_THEN_ANSWER,   ## a buzz-in race, then the winner picks
	BUZZ_ON_CUE,        ## options come up one at a time; buzz on the right one
	ACTIVE,             ## one player is live, and the turn rotates
	CHASE,              ## the lamp travels, stops on someone, they answer
}

## How the points are worked out.
enum Score {
	FLAT,     ## fixed award for a correct answer
	SPEED,    ## ranked by how quickly the correct answer came
	STEAL,    ## the winner takes points off an opponent they choose
	TIME,     ## banks seconds rather than points
	STAKE,    ## spends banked time, stakes unbanked points
	BOMB,     ## a correct answer passes the bomb on
}

const POINTS := 1000

## The game prints these on the Wie Is Het Snelst intro screen itself:
## 1E GOEDE ANTWOORD +400 PTN down to 4E +100, and FOUTE ANTWOORDEN 0 PTN.
## Read off that screen, not guessed.
const SPEED_POINTS := [400, 300, 200, 100]

## Ordered as the game lists them in RoundParameters.
static func all() -> Array[Dictionary]:
	return [
		{
			id = "points_builder", rules = "PointsBuilder",
			input = Mode.ALL, score = Score.FLAT, seconds = 15.0,
			blurb = "Everyone answers the same question. AllowAllContestantsToAnswer, 15 seconds, flat award.",
			approximates = "",
		},
		{
			id = "fastest_finger", rules = "FastestFinger",
			input = Mode.ALL, score = Score.SPEED, seconds = 15.0,
			blurb = "Everyone answers; the quickest correct answer is worth the most.",
			approximates = "",
		},
		{
			id = "quickfire", rules = "Quickfire",
			input = Mode.BUZZ_THEN_ANSWER, score = Score.FLAT, seconds = 15.0,
			blurb = "The question and answers arrive a letter at a time. Buzz as soon as you think you know, then pick.",
			reveals = true,
			approximates = "The reveal finishes about two fifths into the clock, set against the reference shot; the real rate is engine-side.",
		},
		{
			id = "snap", rules = "Snap",
			input = Mode.BUZZ_ON_CUE, score = Score.FLAT, seconds = 12.0,
			blurb = "Statements come up one at a time; buzz on the one that fits the clip.",
			approximates = "The game scrolls its own statements; these are the question's four options.",
		},
		{
			id = "trigger_finger", rules = "TriggerFinger",
			input = Mode.BUZZ_ON_CUE, score = Score.STEAL, seconds = 12.0,
			blurb = "Buzz when the correct answer shows, then take points off a player of your choosing.",
			approximates = "The game scrolls answers on a timer; these come up one per second.",
		},
		{
			id = "buzz_stop", rules = "BuzzStop",
			input = Mode.CHASE, score = Score.FLAT, seconds = 15.0,
			blurb = "The clip plays and the lamp travels. When the music stops, whoever is lit takes the question.",
			approximates = "",
		},
		{
			id = "off_loader", rules = "OffLoader",
			input = Mode.ACTIVE, score = Score.FLAT, seconds = 15.0,
			blurb = "Each player in turn hears a clip and pushes the question onto somebody else.",
			approximates = "",
		},
		{
			id = "pass_the_bomb", rules = "PassTheBomb",
			input = Mode.ACTIVE, score = Score.BOMB, seconds = 8.0,
			blurb = "Answer right and the bomb goes to the player beside you. Whoever holds it when it goes off loses.",
			approximates = "The fuse is random between 20 and 40 seconds; the real one is engine-side.",
		},
		{
			id = "time_builder", rules = "TimeBuilder",
			input = Mode.ACTIVE, score = Score.TIME, seconds = 15.0,
			blurb = "One player at a time. Answer quickly to bank seconds for the last round.",
			approximates = "Seconds banked are the time left on the clock; the real curve is engine-side.",
		},
		{
			id = "hot_seat", rules = "HotSeat",
			input = Mode.ACTIVE, score = Score.STAKE, seconds = 60.0,
			blurb = "One player spends the time they banked answering as many questions as they can.",
			approximates = "Without a Time Builder round before it, the clock starts at 60 seconds.",
		},
	]



## How many questions a round runs in a game. Not recoverable from the scripts.
const QUESTIONS_PER_ROUND := 4

## Rounds per game, for the three lengths the game offers. The menu names them
## Short, Medium and Long; the counts here are a choice, not a finding.
const LENGTHS := {"short": 3, "medium": 5, "long": 7}


## <summary>
## The order of rounds in a game.
##
## Two things do come from the game. Hot Seat is the finale - it is the only
## round with its own HotSeatRoundEnd script - and Time Builder feeds it, since
## its own rules line says the time won is for "de laatste ronde".
##
## The rest of the order is not recoverable: the length menu sets
## NameOfGameToPlay to ShortMultiplayerGame and friends, and no script by those
## names is on the disc, so the sequence lives in the executable. The middle of
## the game is therefore shuffled.
## </summary>
static func session(length: String) -> Array[String]:
	var count: int = LENGTHS.get(length, 3)

	var middle: Array[String] = []
	for r in all():
		if r.id != "hot_seat" and r.id != "time_builder":
			middle.append(str(r.id))
	middle.shuffle()

	var order: Array[String] = []
	for i in range(maxi(count - 2, 0)):
		order.append(middle[i % middle.size()])

	# Time Builder banks the time Hot Seat spends, so it comes directly before.
	order.append("time_builder")
	order.append("hot_seat")
	return order

static func by_id(id: String) -> Dictionary:
	for r in all():
		if r.id == id:
			return r
	return all()[0]


## The round's own title, from the text table, falling back to the key.
static func title(round: Dictionary, text: Dictionary) -> String:
	return str(text.get("Rules%sTitle" % round.rules, round.rules.to_upper()))


## Its rule lines, in order, skipping any that did not resolve.
static func lines(round: Dictionary, text: Dictionary) -> Array[String]:
	var out: Array[String] = []
	for n in [1, 2]:
		var key := "Rules%sLine%d" % [round.rules, n]
		if text.has(key):
			out.append(str(text[key]))
	return out
