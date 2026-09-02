class_name Speech
extends RefCounted

## Buzz and Rose, in Dutch, off the disc.
##
## `NETSpeechInfo` declares every line the Dutch build can speak, and the clips
## sit beside it: `C_<id>_<variation>` for commentary and `F_<id>_<variation>`
## for fixed speech. The two agree exactly - 285 commentary lines and 138 fixed,
## with no line declared that has no clip and no clip that is not declared - so
## `obz speech` writes the index this reads.
##
## Which line belongs to which moment comes from `speech-cues.json`, read back
## out of the scripts by `obz speech cues`. Every round opens with the same
## call - `DoRoundIntroduction(speaker, round, round, a, b, c, intro, rules,
## 111000)` - and `RoundIntroduction` plays those arguments on its own clock:
## the round announcement at timing marker 5, the shared line 111000 opened at
## 8.4, then the rules table walked in order, waiting for each to finish.
##
## Per-contestant lines are computed there rather than written out, as in
## `530200 + seat - 1`, and the four ids each of those resolves to are one per
## place on the stage.
##
## What is still a bucket is the running commentary. The round scripts choose
## those through named comment contexts opened by natives, and that selection
## has not been recovered - so `any_clip` stays, used only where no cue is
## known, and it is not claiming to be the right line.

var dir := ""
var commentary := {}
var fixed := {}

## round name -> {speaker, announce, shared, rules[]}
var rounds := {}
## "<script>.<native>" -> {expression, ids[]}
var families := {}


func load_from(bundle_dir: String, wav_dir: String) -> bool:
	dir = wav_dir
	var path := bundle_dir.path_join("speech.json")
	if not FileAccess.file_exists(path):
		return false

	var parsed = JSON.parse_string(FileAccess.get_file_as_string(path))
	if not (parsed is Dictionary):
		return false

	commentary = parsed.get("commentary", {})
	fixed = parsed.get("fixed", {})

	var cues := bundle_dir.path_join("speech-cues.json")
	if FileAccess.file_exists(cues):
		var sheet = JSON.parse_string(FileAccess.get_file_as_string(cues))
		if sheet is Dictionary:
			rounds = sheet.get("rounds", {})
			families = sheet.get("families", {})
	return true


## Which cue sheet belongs to a round, by the two links the scripts themselves
## draw.
##
## First the round id its start script passes to SetCurrentRoundID. That settles
## nine of the ten; two cue sheets share an id, because QuickfireQuiz reuses
## FastestFingerFirstID and QuizMaster reuses PointsBuilderRoundID, so the round
## logic name breaks the tie.
##
## Then the follow-on script, for the one the ids cannot reach. GenericData
## defines both TimeBuilderRoundID (16) and SpeedTimeBuilderRoundID (17), and
## nothing outside GenericData ever reads the first - but the tuned parameters
## are filed under it, while the shipping start script sets the second and hands
## off to TimeBuilderRound. So the id says one thing and the follow-on says
## another, and the follow-on is the one that names the round being played.
func cue_round(round_id: String, rules_name: String) -> String:
	var hits := []
	for name in rounds:
		if str((rounds[name] as Dictionary).get("roundId", "")) == round_id:
			hits.append(name)

	for name in hits:
		if str(name).begins_with(rules_name):
			return str(name)
	if not hits.is_empty():
		return str(hits[0])

	for name in rounds:
		if str((rounds[name] as Dictionary).get("follows", "")) == rules_name + "Round":
			return str(name)
	return ""


## The round's introduction, in the order RoundIntroduction plays it: the
## announcement, then the shared line, then each rule. Empty if this round has
## no cue sheet, which is honest - better silence than the wrong line.
func intro_lines(round_name: String) -> Array:
	if not rounds.has(round_name):
		return []
	var cue: Dictionary = rounds[round_name]
	var order := []
	for key in ["announce", "shared"]:
		if cue.get(key, null) != null:
			order.append(str(cue[key]))
	for rule in cue.get("rules", []):
		if rule != null:
			order.append(str(rule))
	return order


## Who introduces this round - "Host" or "Hostess", as the script says.
func intro_speaker(round_name: String) -> String:
	if not rounds.has(round_name):
		return ""
	return str((rounds[round_name] as Dictionary).get("speaker", ""))


## One member of a computed per-seat family, by seat. The script does the same
## arithmetic; here the ids it resolves to are already listed.
func seat_line(family: String, seat: int) -> String:
	if not families.has(family):
		return ""
	var ids: Array = (families[family] as Dictionary).get("ids", [])
	return "" if seat < 0 or seat >= ids.size() else str(ids[seat])


func line_count() -> int:
	return commentary.size() + fixed.size()


## The file for one line, choosing among the variations that exist. The engine
## picks a variation up to the count the script declares; here the index only
## ever lists variations actually on the disc, so a pick cannot miss.
func clip_for(kind: String, id: String) -> String:
	var table: Dictionary = commentary if kind == "commentary" else fixed
	if not table.has(id):
		return ""

	var variations: Array = table[id]
	if variations.is_empty():
		return ""

	var pick: int = int(variations[randi() % variations.size()])
	var prefix := "C_" if kind == "commentary" else "F_"
	return dir.path_join("%s%s_%03d.wav" % [prefix, id, pick])


## Any line from a bucket. A stand-in until the contexts are read: it makes the
## host and hostess audible, and it is not claiming to be the right line.
func any_clip(kind: String) -> String:
	var table: Dictionary = commentary if kind == "commentary" else fixed
	if table.is_empty():
		return ""
	var ids: Array = table.keys()
	return clip_for(kind, str(ids[randi() % ids.size()]))
