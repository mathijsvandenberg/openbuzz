extends TabContainer

## Holds the two views and, for verification, renders one frame and exits.
##
## `--tab <n> --shot <file>` is how the build gets checked without a person
## having to watch a window.

var _shot_path := ""
var _delay := 0


func _ready() -> void:
	Log.start()
	Log.info("boot", "args %s" % str(OS.get_cmdline_user_args()))
	_release_the_joypad()

	var args := OS.get_cmdline_user_args()

	var tab := args.find("--tab")
	if tab >= 0 and tab + 1 < args.size():
		current_tab = clampi(int(args[tab + 1]), 0, get_tab_count() - 1)

	if args.has("--selftest"):
		current_tab = 0
		await get_tree().process_frame
		await get_tree().process_frame
		await get_child(0).run_self_test()

		# With --shot as well, capture the moved camera so the result can be
		# seen and not just asserted.
		var want := args.find("--shot")
		if want >= 0 and want + 1 < args.size():
			await RenderingServer.frame_post_draw
			get_viewport().get_texture().get_image().save_png(args[want + 1])
			print("wrote ", args[want + 1])

		get_tree().quit()
		return

	var shot := args.find("--shot")
	if shot >= 0 and shot + 1 < args.size():
		_shot_path = args[shot + 1]
		_delay = 45

	# Later frames let a capture land well into a game rather than on its first
	# question.
	var after := args.find("--shot-after")
	if after >= 0 and after + 1 < args.size():
		_delay = int(args[after + 1])


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


## Godot binds joypad buttons to its UI actions by default, so button 0 is
## ui_accept and the shoulder buttons page the tabs. On a Buzz handset that
## means a red buzzer presses whatever has focus and answering flips to another
## tab. The game reads the pad directly, so the UI has no use for those
## bindings at all: strip every joypad event off the ui_ actions and stop the
## tab strip taking focus.
func _release_the_joypad() -> void:
	for action in InputMap.get_actions():
		if not str(action).begins_with("ui_"):
			continue
		for event in InputMap.action_get_events(action):
			if event is InputEventJoypadButton or event is InputEventJoypadMotion:
				InputMap.action_erase_event(action, event)

	focus_mode = Control.FOCUS_NONE


func _exit_tree() -> void:
	Log.close()
