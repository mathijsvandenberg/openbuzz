extends PanelContainer

## Four Buzz handsets on screen, so the game can be driven without the hardware.
##
## Real buzzers are still first class - this sits beside them and both feed the
## same path. It exists because the flow needs at least two players and most
## testing happens with one person at a desk.
##
## The buttons are laid out the way the handset is: the big red buzzer on top,
## then the four colours running down. The slot numbers are the order the
## hardware reports, measured on a real set: 0 red, 1 yellow, 2 green,
## 3 orange, 4 blue - bottom to top, which is why the colours read upwards.

signal pressed(handset: int, slot: int)

const HANDSETS := 4
const SLOTS := 5

## slot -> label and colour, in hardware order.
const BUTTONS := [
	{slot = 0, label = "BUZZ", colour = Color(0.85, 0.16, 0.16), big = true},
	{slot = 4, label = "blue", colour = Color(0.29, 0.53, 0.91), big = false},
	{slot = 3, label = "orange", colour = Color(0.95, 0.60, 0.16), big = false},
	{slot = 2, label = "green", colour = Color(0.30, 0.76, 0.35), big = false},
	{slot = 1, label = "yellow", colour = Color(0.96, 0.85, 0.20), big = false},
]

var _lamp := [false, false, false, false]
var _seat := [-1, -1, -1, -1]
var _lamp_dots: Array[ColorRect] = []
var _seat_labels: Array[Label] = []


func _ready() -> void:
	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 10)
	add_child(column)

	var heading := Label.new()
	heading.text = "  Handsets"
	column.add_child(heading)

	var note := Label.new()
	note.text = "  Click a button, or use the real buzzers.\n  Keys 1-4 buzz; QWER ASDF ZXCV UIOP answer."
	note.add_theme_font_size_override("font_size", 10)
	note.add_theme_color_override("font_color", Color(0.6, 0.63, 0.7))
	note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	column.add_child(note)

	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)
	row.size_flags_vertical = Control.SIZE_EXPAND_FILL
	column.add_child(row)

	for handset in range(HANDSETS):
		row.add_child(_build_handset(handset))


func _build_handset(handset: int) -> Control:
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 5)
	box.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	var header := HBoxContainer.new()
	header.add_theme_constant_override("separation", 5)

	# The lamp, which the game drives and the hardware really has.
	var lamp := ColorRect.new()
	lamp.custom_minimum_size = Vector2(9, 9)
	lamp.color = Color(0.2, 0.2, 0.24)
	header.add_child(lamp)
	_lamp_dots.append(lamp)

	var name := Label.new()
	name.text = "Pad %d" % (handset + 1)
	name.add_theme_font_size_override("font_size", 11)
	header.add_child(name)
	box.add_child(header)

	# Which seat this handset claimed, which is not fixed.
	var seat := Label.new()
	seat.text = "-"
	seat.add_theme_font_size_override("font_size", 10)
	seat.add_theme_color_override("font_color", Color(0.58, 0.62, 0.7))
	box.add_child(seat)
	_seat_labels.append(seat)

	for spec in BUTTONS:
		var button := Button.new()
		button.text = str(spec.label)
		button.custom_minimum_size = Vector2(0, 30 if spec.big else 24)
		button.focus_mode = Control.FOCUS_NONE
		button.add_theme_font_size_override("font_size", 11 if spec.big else 10)

		var face := StyleBoxFlat.new()
		face.bg_color = spec.colour
		face.corner_radius_top_left = 5
		face.corner_radius_top_right = 5
		face.corner_radius_bottom_left = 5
		face.corner_radius_bottom_right = 5
		button.add_theme_stylebox_override("normal", face)

		var lit := face.duplicate() as StyleBoxFlat
		lit.bg_color = (spec.colour as Color).lightened(0.3)
		button.add_theme_stylebox_override("hover", lit)
		button.add_theme_stylebox_override("pressed", lit)

		button.add_theme_color_override("font_color", Color(0.06, 0.07, 0.1))
		button.add_theme_color_override("font_hover_color", Color(0.06, 0.07, 0.1))
		button.add_theme_color_override("font_pressed_color", Color(0.06, 0.07, 0.1))

		var slot: int = spec.slot
		button.pressed.connect(func(): pressed.emit(handset, slot))
		box.add_child(button)

	return box


## Mirrors the lamp state the game is driving, on the real hardware and here.
func set_lamps(on: Array) -> void:
	for i in range(min(on.size(), _lamp_dots.size())):
		_lamp[i] = bool(on[i])
		_lamp_dots[i].color = Color(1.0, 0.85, 0.3) if _lamp[i] else Color(0.2, 0.2, 0.24)


## Shows which seat each handset took.
func set_seats(seat_of_handset: Array) -> void:
	for i in range(min(seat_of_handset.size(), _seat_labels.size())):
		_seat[i] = int(seat_of_handset[i])
		_seat_labels[i].text = "-" if _seat[i] < 0 else "seat %d" % (_seat[i] + 1)
