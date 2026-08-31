extends Node3D

var shots := 0
var times := []
var player: AnimationPlayer
var anim := ""
var outdir := "user://shots"

func _ready():
    var scene = load("res://AshCostume01.glb").instantiate()
    add_child(scene)

    player = _find(scene, "AnimationPlayer")
    anim = player.get_animation_list()[0]
    var a = player.get_animation(anim)
    for i in range(4):
        times.append(a.length * i / 4.0)

    # Frame the character from its own bounds.
    var aabb := AABB()
    var first := true
    for m in _all(scene, "MeshInstance3D"):
        var b = m.global_transform * m.get_aabb()
        aabb = b if first else aabb.merge(b)
        first = false
    var centre = aabb.get_center()
    var size = maxf(aabb.size.x, maxf(aabb.size.y, aabb.size.z))

    var cam = Camera3D.new()
    add_child(cam)
    cam.position = centre + Vector3(size * 0.55, size * 0.28, size * 1.9)
    cam.look_at(centre, Vector3.UP)
    cam.current = true

    var sun = DirectionalLight3D.new()
    add_child(sun)
    sun.rotation_degrees = Vector3(-45, 35, 0)
    sun.light_energy = 2.0

    var env = WorldEnvironment.new()
    var e = Environment.new()
    e.background_mode = Environment.BG_COLOR
    e.background_color = Color(0.09, 0.09, 0.11)
    e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
    e.ambient_light_color = Color(0.45, 0.45, 0.5)
    e.ambient_light_energy = 1.0
    env.environment = e
    add_child(env)

    DirAccess.make_dir_recursive_absolute(outdir)

func _process(_d):
    if shots >= times.size():
        print("SHOTS DONE ", ProjectSettings.globalize_path(outdir))
        get_tree().quit()
        return
    player.play(anim)
    player.seek(times[shots], true)
    await RenderingServer.frame_post_draw
    var img = get_viewport().get_texture().get_image()
    var p = "%s/shot%d.png" % [outdir, shots]
    img.save_png(p)
    print("saved ", ProjectSettings.globalize_path(p), " at t=", times[shots])
    shots += 1

func _find(n, cls):
    if n.get_class() == cls: return n
    for c in n.get_children():
        var r = _find(c, cls)
        if r: return r
    return null

func _all(n, cls, acc = []):
    if n.get_class() == cls: acc.append(n)
    for c in n.get_children(): _all(c, cls, acc)
    return acc
