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
## What is *not* settled yet is which line belongs to which moment. The round
## scripts open and play named comment contexts - QuestionTransition,
## PlayerAnswerReveal, AnswerReaction, ScoreScreen - and the mapping from a
## context to its line ids has not been recovered. So the plumbing here is
## real and the choice of line is not: `speak_any` picks from a bucket, which
## makes the host audible without pretending it is saying the right thing.

var dir := ""
var commentary := {}
var fixed := {}


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
	return true


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
