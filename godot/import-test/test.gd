extends SceneTree

func _init():
    for name in ["AshCostume01", "AngieCostume01"]:
        var path = "res://%s.glb" % name
        var res = load(path)
        if res == null:
            print("FAILED to load ", path)
            continue
        var node = res.instantiate()
        var skels = []
        var meshes = []
        var players = []
        _walk(node, skels, meshes, players)
        print("=== ", name, " ===")
        print("  root: ", node.get_class(), "  children: ", node.get_child_count())
        print("  MeshInstance3D: ", meshes.size(), "  Skeleton3D: ", skels.size(), "  AnimationPlayer: ", players.size())
        for s in skels:
            print("    skeleton bones: ", s.get_bone_count())
        for m in meshes:
            var mesh = m.mesh
            var surf = mesh.get_surface_count() if mesh else 0
            print("    mesh '", m.name, "' surfaces=", surf, " skin=", "yes" if m.skin != null else "no")
        for p in players:
            var list = p.get_animation_list()
            print("    animations: ", list.size(), " first=", list[0] if list.size() > 0 else "-")
            if list.size() > 0:
                var a = p.get_animation(list[0])
                print("      length ", a.length, "s, tracks ", a.get_track_count())
    quit()

func _walk(n, skels, meshes, players):
    if n is Skeleton3D: skels.append(n)
    if n is MeshInstance3D: meshes.append(n)
    if n is AnimationPlayer: players.append(n)
    for c in n.get_children():
        _walk(c, skels, meshes, players)
