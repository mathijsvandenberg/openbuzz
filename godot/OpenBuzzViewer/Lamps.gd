class_name Lamps
extends RefCounted

## Drives the red lamps on the Buzz buzzers.
##
## Lighting them means writing a HID output report, which the engine cannot do,
## so a helper does it: `obz-lamps --serve` holds the device open and takes one
## four-digit pattern per line. Running it as a server rather than a process per
## change matters - a round changes the lamps several times a second.

const HELPER := "obz-lamps.exe"

var available := false
var reason := "not started"

var _pipe: Dictionary = {}
var _stdio: FileAccess = null
var _last := ""


func start(base_dir: String) -> bool:
	var path := _find(base_dir)
	if path.is_empty():
		reason = "%s not found" % HELPER
		return false

	_pipe = OS.execute_with_pipe(path, ["--serve"])
	if _pipe.is_empty() or not _pipe.has("stdio"):
		reason = "could not start %s" % HELPER
		return false

	_stdio = _pipe["stdio"]
	available = true
	reason = path
	return true


## Looks next to the executable first, then at the build output, so it works
## from dist/ and from the editor alike.
static func _find(base_dir: String) -> String:
	var candidates := [
		base_dir.path_join(HELPER),
		base_dir.path_join("dist").path_join(HELPER),
	]

	var d := base_dir
	for i in range(6):
		candidates.append(d.path_join("dist").path_join(HELPER))
		candidates.append(d.path_join("tools/OpenBuzz.Lamps/bin/Release/net9.0-windows").path_join(HELPER))
		var up := d.get_base_dir()
		if up == d:
			break
		d = up

	for c in candidates:
		if FileAccess.file_exists(c):
			return c
	return ""


## `lit` is one bool per handset. Repeats are dropped.
func set_lamps(lit: Array) -> void:
	if not available or _stdio == null:
		return

	var pattern := ""
	for i in range(4):
		pattern += "1" if i < lit.size() and lit[i] else "0"

	if pattern == _last:
		return
	_last = pattern
	_stdio.store_line(pattern)


func all(on: bool) -> void:
	set_lamps([on, on, on, on])


func only(index: int) -> void:
	var lit := [false, false, false, false]
	if index >= 0 and index < 4:
		lit[index] = true
	set_lamps(lit)


func stop() -> void:
	if not available:
		return
	if _stdio != null:
		_stdio.store_line("quit")
	if _pipe.has("pid"):
		OS.kill(_pipe["pid"])
	available = false
