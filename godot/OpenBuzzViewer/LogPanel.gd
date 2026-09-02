extends PanelContainer

## The on-screen half of the log. F2 shows and hides it.
##
## Deliberately plain: a monospaced tail of the same lines that go to the file,
## so what is on screen and what is on disk cannot drift apart.

const REFRESH := 0.25

@onready var _text: RichTextLabel = $Margin/Text

var _clock := 0.0


func _ready() -> void:
	# Docked along the bottom rather than floating over the game, so what the
	# port is doing is readable while it does it. F2 still gets the space back.
	visible = true
	set_process(true)
	_redraw()


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo and event.keycode == KEY_F2:
		visible = not visible
		if visible:
			_redraw()
		get_viewport().set_input_as_handled()


func _process(delta: float) -> void:
	if not visible:
		return
	_clock -= delta
	if _clock <= 0.0:
		_clock = REFRESH
		_redraw()


func _redraw() -> void:
	if _text == null:
		return
	var out := PackedStringArray()
	for line in Log.lines(40):
		out.append(line)
	_text.text = "\n".join(out) + "\n\nF2 hides this  -  " + Log.path()
