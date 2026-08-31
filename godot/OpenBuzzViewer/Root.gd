extends TabContainer

## Holds the two views and, for verification, renders one frame and exits.
##
## `--tab <n> --shot <file>` is how the build gets checked without a person
## having to watch a window.

var _shot_path := ""
var _delay := 0


func _ready() -> void:
	var args := OS.get_cmdline_user_args()

	var tab := args.find("--tab")
	if tab >= 0 and tab + 1 < args.size():
		current_tab = clampi(int(args[tab + 1]), 0, get_tab_count() - 1)

	var shot := args.find("--shot")
	if shot >= 0 and shot + 1 < args.size():
		_shot_path = args[shot + 1]
		_delay = 45


func _process(_delta: float) -> void:
	if _shot_path.is_empty():
		return

	_delay -= 1
	if _delay > 0:
		return

	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(_shot_path)
	print("wrote ", _shot_path)
	get_tree().quit()
