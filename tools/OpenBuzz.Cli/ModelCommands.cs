using OpenBuzz.Graphics;

namespace OpenBuzz.Cli;

/// Reads the meshes out of the `.rp2` clumps and exports them as glTF.
public static class ModelCommands
{
    public static int List(string dir)
    {
        var files = Directory.GetFiles(dir, "*.rp2").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) { Console.Error.WriteLine($"No .rp2 files under {dir}."); return 1; }

        Console.WriteLine($"{"stream",-30} {"plain",6} {"native",7} {"verts",8} {"tris",8}");
        int plainTotal = 0, nativeTotal = 0;

        foreach (var path in files)
        {
            var geometries = RwGeometry.LoadAll(File.ReadAllBytes(path));
            int plain = geometries.Count(g => !g.IsNative);
            int native = geometries.Count - plain;
            plainTotal += plain;
            nativeTotal += native;

            if (plain == 0 && native == 0) continue;
            Console.WriteLine($"{Path.GetFileNameWithoutExtension(path),-30} {plain,6} {native,7} " +
                              $"{geometries.Where(g => !g.IsNative).Sum(g => g.VertexCount),8} " +
                              $"{geometries.Where(g => !g.IsNative).Sum(g => g.Triangles.Length),8}");
        }

        Console.WriteLine();
        Console.WriteLine($"{plainTotal} plain geometries exportable, {nativeTotal} native still to do");
        return 0;
    }

    /// <summary>
    /// Exports every stream that has plain geometry to a `.glb`, with the
    /// stream's own textures embedded so materials resolve.
    /// </summary>
    public static int Export(string dir, string outDir, string? only)
    {
        var files = Directory.GetFiles(dir, "*.rp2")
                             .Where(p => only is null || Path.GetFileNameWithoutExtension(p)
                                          .Contains(only, StringComparison.OrdinalIgnoreCase))
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) { Console.Error.WriteLine($"No matching .rp2 files under {dir}."); return 1; }

        Directory.CreateDirectory(outDir);
        int written = 0, skipped = 0;

        foreach (var path in files)
        {
            var data = File.ReadAllBytes(path);
            var geometries = RwGeometry.LoadAll(data).Where(g => !g.IsNative && g.Positions.Length > 0).ToList();
            if (geometries.Count == 0) { skipped++; continue; }

            var glb = new GlbWriter();
            var textures = EmbedTextures(glb, data);
            var materials = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            int index = 0;
            foreach (var geometry in geometries)
            {
                var groups = new Dictionary<int, List<int>>();
                foreach (var t in geometry.Triangles)
                {
                    var textureName = t.Material < geometry.MaterialTextures.Length
                        ? geometry.MaterialTextures[t.Material] : "";

                    if (!materials.TryGetValue(textureName, out int material))
                    {
                        material = glb.AddMaterial(
                            string.IsNullOrEmpty(textureName) ? "untextured" : textureName,
                            textures.TryGetValue(textureName, out int tex) ? tex : null);
                        materials[textureName] = material;
                    }

                    if (!groups.TryGetValue(material, out var list)) groups[material] = list = [];
                    list.Add(t.A); list.Add(t.B); list.Add(t.C);
                }

                glb.AddMesh($"mesh{index++}", geometry.Positions, geometry.Normals, geometry.TexCoords, groups);
            }

            var outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".glb");
            glb.Write(outPath);
            written++;
        }

        Console.WriteLine($"Wrote {written} models to {outDir}" + (skipped > 0 ? $", {skipped} had no plain geometry" : ""));
        return 0;
    }

    /// Embeds every texture in the stream, keyed by the name materials use.
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
