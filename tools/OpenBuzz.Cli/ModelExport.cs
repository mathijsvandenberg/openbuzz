using OpenBuzz.Graphics;

namespace OpenBuzz.Cli;

/// <summary>
/// Exports a `.rp2` clump as glTF, rigged: the bone hierarchy, the skin
/// weights, and the clips from the matching `*Animations.rp2`.
/// </summary>
public static class ModelExport
{
    public static int Run(string dir, string outDir, string? only)
    {
        var files = Directory.GetFiles(dir, "*.rp2")
                             .Where(p => !Path.GetFileName(p).Contains("Animation", StringComparison.OrdinalIgnoreCase))
                             .Where(p => only is null || Path.GetFileNameWithoutExtension(p)
                                          .Contains(only, StringComparison.OrdinalIgnoreCase))
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) { Console.Error.WriteLine($"No matching .rp2 files under {dir}."); return 1; }

        Directory.CreateDirectory(outDir);
        int written = 0, rigged = 0, animated = 0;

        foreach (var path in files)
        {
            try
            {
            var data = File.ReadAllBytes(path);
            var tree = RwStream.Parse(data);

            // A costume ships the same meshes three times over - once as plain
            // geometry and twice more as PS2 native - in three clumps. Exporting
            // all of them just stacks identical bodies on top of each other, so
            // take the first clump that has usable geometry.
            var geometries = Clumps(tree)
                .Select(c => RwStream.Flatten([c])
                                     .Where(n => n.Id == RwId.Geometry)
                                     .Select(n => RwGeometry.Parse(data, n))
                                     .Where(g => g.Positions.Length > 0)
                                     .ToList())
                .FirstOrDefault(list => list.Count > 0)
                ?? RwGeometry.LoadAll(data).Where(g => g.Positions.Length > 0).ToList();

            if (geometries.Count == 0) continue;

            var glb = new GlbWriter();
            var textures = EmbedTextures(glb, data);
            var materials = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var frameList = RwStream.Flatten(tree).FirstOrDefault(n => n.Id == RwId.FrameList);
            var skeleton = frameList is null ? null : RwSkeleton.Parse(data, frameList);

            int[] joints = [];
            // Pick the inverse-bind set that actually inverts the bind pose.
            // Every skinned geometry ships a full-length array, but one that
            // only uses a single bone has garbage in the other entries.
            // The full-body skin is the one that binds to the most bones; a
            // part hanging off a single bone ships a full-length inverse-bind
            // array whose other entries are meaningless.
            var skinned = skeleton is null ? null
                : geometries.FirstOrDefault(g => g.InverseBind.Length >= skeleton.BoneCount * 16);

            if (skeleton is { BoneCount: > 0 } && skinned is not null)
            {
                var locals = new float[skeleton.BoneCount][];
                for (int b = 0; b < skeleton.BoneCount; b++)
                {
                    var frame = skeleton.Frames[skeleton.BoneFrames[b]];
                    locals[b] = [.. frame.Matrix, .. frame.Translation];
                }

                joints = glb.AddSkeleton(skeleton.BoneParents, locals);
                rigged++;
            }

            foreach (var (geometry, index) in geometries.Select((g, i) => (g, i)))
            {
                var groups = new Dictionary<int, List<int>>();
                foreach (var t in geometry.Triangles)
                {
                    var name = t.Material < geometry.MaterialTextures.Length ? geometry.MaterialTextures[t.Material] : "";
                    if (!materials.TryGetValue(name, out int material))
                    {
                        material = glb.AddMaterial(string.IsNullOrEmpty(name) ? "untextured" : name,
                                                   textures.TryGetValue(name, out int tex) ? tex : null);
                        materials[name] = material;
                    }
                    if (!groups.TryGetValue(material, out var list)) groups[material] = list = [];
                    list.Add(t.A); list.Add(t.B); list.Add(t.C);
                }

                // Every mesh gets its own skin, built from its own inverse
                // binds, because those arrays are only meaningful for the bones
                // that mesh actually uses.
                bool canSkin = joints.Length > 0 &&
                               geometry.BoneIndices.Length == geometry.Positions.Length / 3 * 4 &&
                               geometry.InverseBind.Length >= joints.Length * 16;

                glb.AddMesh($"mesh{index}", geometry.Positions, geometry.Normals, geometry.TexCoords, groups,
                            canSkin ? Clamp(geometry.BoneIndices, joints.Length) : null,
                            canSkin ? geometry.Weights : null,
                            canSkin ? glb.AddSkin(joints, geometry.InverseBind) : null);
            }

            if (joints.Length > 0)
                animated += AddClips(glb, dir, path, joints);

            glb.Write(Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".glb"));
            written++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  !! {Path.GetFileNameWithoutExtension(path)}: {ex.Message}");
            }
        }

        Console.WriteLine($"Wrote {written} models to {outDir}; {rigged} rigged, {animated} clips");
        return 0;
    }

    private static List<RwNode> Clumps(List<RwNode> tree) =>
        [.. RwStream.Flatten(tree).Where(n => n.Id == RwId.Clump)];

    /// Bone indices out of range would make a viewer reject the file.
    private static byte[] Clamp(byte[] indices, int bones)
    {
        var result = (byte[])indices.Clone();
        for (int i = 0; i < result.Length; i++)
            if (result[i] >= bones) result[i] = 0;
        return result;
    }

    /// <summary>
    /// Finds the animation stream that goes with a costume - AngieCostume01
    /// takes AngieAnimations and AngieWinAnimation - and adds every clip.
    /// </summary>
    private static int AddClips(GlbWriter glb, string dir, string modelPath, int[] joints)
    {
        var stem = Path.GetFileNameWithoutExtension(modelPath);
        int cut = stem.IndexOf("Costume", StringComparison.OrdinalIgnoreCase);
        var character = cut > 0 ? stem[..cut] : stem;

        var streams = Directory.GetFiles(dir, character + "*Animation*.rp2")
                               .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        int added = 0;

        foreach (var stream in streams)
        {
            var data = File.ReadAllBytes(stream);
            var clips = RwAnimation.LoadAll(data);

            for (int c = 0; c < clips.Count; c++)
            {
                var clip = clips[c];
                if (clip.BoneCount == 0) continue;

                int bones = Math.Min(joints.Length, clip.BoneCount);
                var times = new float[joints.Length][];
                var rotations = new float[joints.Length][];
                var translations = new float[joints.Length][];
                for (int b = 0; b < joints.Length; b++)
                {
                    times[b] = [];
                    rotations[b] = [];
                    translations[b] = [];
                }

                var byBone = clip.KeyframesByBone();
                for (int b = 0; b < bones; b++)
                {
                    var keys = byBone[b];
                    if (keys.Count == 0) continue;

                    times[b] = new float[keys.Count];
                    rotations[b] = new float[keys.Count * 4];
                    translations[b] = new float[keys.Count * 3];

                    for (int k = 0; k < keys.Count; k++)
                    {
                        var f = clip.Keyframes[keys[k]];
                        times[b][k] = f.Time;
                        rotations[b][k * 4] = f.Qx;
                        rotations[b][k * 4 + 1] = f.Qy;
                        rotations[b][k * 4 + 2] = f.Qz;
                        rotations[b][k * 4 + 3] = f.Qw;
                        translations[b][k * 3] = f.Tx;
                        translations[b][k * 3 + 1] = f.Ty;
                        translations[b][k * 3 + 2] = f.Tz;
                    }
                }

                glb.AddAnimation($"{Path.GetFileNameWithoutExtension(stream)}_{c}", joints, times, rotations, translations);
                added++;
            }
        }

        return added;
    }

    private static Dictionary<string, int> EmbedTextures(GlbWriter glb, byte[] data)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in RwStream.Flatten(RwStream.Parse(data)).Where(n => n.Id == RwId.TextureNative))
        {
            try
            {
                var tex = Ps2Texture.Parse(data.AsSpan(node.DataOffset, node.Size).ToArray(), "texture");
                if (result.ContainsKey(tex.Name)) continue;

                using var png = new MemoryStream();
                PngWriter.Write(png, tex.ToRgba(), tex.Width, tex.Height);
                result[tex.Name] = glb.AddTexture(png.ToArray(), tex.Name);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  !! texture: {ex.Message}");
            }
        }

        return result;
    }
}
