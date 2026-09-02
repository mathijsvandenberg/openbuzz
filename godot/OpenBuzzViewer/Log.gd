class_name Log
extends RefCounted

## The run log: an on-screen panel and a file.
##
## The game has its own debug output - the scripts call `DebugOutputAudioHandle
## Details`, `PrintObjectStoreStackCounts` and friends, and there is a
## `SetDisableFPSCounter` to go with them - so a port that means to run those
## scripts needs somewhere for that output to land.
##
## It is also how this thing gets checked. A screenshot says a round drew; it
## does not say which answer was correct, what the reveal rate resolved to, or
## which layout key fell back to its default. The log says all of that, and it
## can be read without a person looking at a picture.

enum Level { TRACE, INFO, WARN, ERROR }

const LEVEL_NAME := ["TRACE", "INFO", "WARN", "ERROR"]

## Lines kept for the on-screen panel. The file keeps everything.
const SCROLLBACK := 240

static var _lines: PackedStringArray = []
static var _file: FileAccess = null
static var _path := ""
static var _min_level: int = Level.INFO
static var _started_at := 0.0
static var _started := false


## Opens the log file. `--log <path>` overrides where it goes; without it the
## file sits next to the executable so it is findable without knowing Godot's
## user:// mapping.
## Opened on the first line written, not from a _ready, because a child node's
## _ready runs before its parent's - so the round had already set itself up and
## logged nothing by the time the root got a chance to open the file.
static func start(default_name := "openbuzz.log") -> void:
	if _started:
		return
	_started = true
	_started_at = Time.get_ticks_msec() / 1000.0

	var args := OS.get_cmdline_user_args()
	var at := args.find("--log")
	_path = args[at + 1] if at >= 0 and at + 1 < args.size() \
		else Bundle.base_dir().path_join(default_name)

	if args.has("--trace"):
		_min_level = Level.TRACE

	_file = FileAccess.open(_path, FileAccess.WRITE)
	if _file == null:
		# user:// always exists; the executable's own directory may be read-only.
		_path = "user://" + default_name
		_file = FileAccess.open(_path, FileAccess.WRITE)

	write(Level.INFO, "log", "started %s" % Time.get_datetime_string_from_system())
	write(Level.INFO, "log", "writing to %s" % _path)


static func path() -> String:
	return _path


## One line. `tag` is the subsystem, so the file can be grepped by area.
static func write(level: int, tag: String, message: String) -> void:
	if level < _min_level:
		return

	if not _started:
		start()

	var stamp := Time.get_ticks_msec() / 1000.0 - _started_at
	var line := "[%8.3f] %-5s %-8s %s" % [stamp, LEVEL_NAME[level], tag, message]

	_lines.append(line)
	if _lines.size() > SCROLLBACK:
		_lines = _lines.slice(_lines.size() - SCROLLBACK)

	if _file != null:
		_file.store_line(line)
		# Flushed every line: a crash or a --quit-after should still leave a
		# readable file behind, and this is not a hot path.
		_file.flush()


static func trace(tag: String, message: String) -> void:
	write(Level.TRACE, tag, message)


static func info(tag: String, message: String) -> void:
	write(Level.INFO, tag, message)


static func warn(tag: String, message: String) -> void:
	write(Level.WARN, tag, message)


static func error(tag: String, message: String) -> void:
	write(Level.ERROR, tag, message)


static func lines(count := SCROLLBACK) -> PackedStringArray:
	return _lines if _lines.size() <= count else _lines.slice(_lines.size() - count)


static func close() -> void:
	if _file != null:
		_file.flush()
		_file.close()
		_file = null
