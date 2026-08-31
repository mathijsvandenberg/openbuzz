using System.Text;
using System.Text.Json;

namespace OpenBuzz.Graphics;

/// <summary>
/// Writes glTF 2.0 binary (`.glb`).
///
/// The point of exporting rather than rendering is that it needs no engine: the
/// result opens in Blender, Windows 3D Viewer or any online viewer, so the
/// geometry can be checked on its own before any decision about how to draw it.
/// </summary>
public sealed class GlbWriter
{
    private const uint Magic = 0x46546C67;      // "glTF"
    private const uint JsonChunk = 0x4E4F534A;  // "JSON"
    private const uint BinChunk = 0x004E4942;   // "BIN"

    private readonly MemoryStream _bin = new();
    private readonly List<object> _bufferViews = [];
    private readonly List<object> _accessors = [];
    private readonly List<object> _meshes = [];
    private readonly List<object> _nodes = [];
    private readonly List<object> _materials = [];
    private readonly List<object> _textures = [];
    private readonly List<object> _images = [];

    /// Embeds a PNG and returns its texture index.
    public int AddTexture(byte[] png, string name)
    {
        int view = AddView(png, null);
        _images.Add(new { bufferView = view, mimeType = "image/png", name });
        _textures.Add(new { source = _images.Count - 1 });
        return _textures.Count - 1;
    }

    public int AddMaterial(string name, int? texture)
    {
        object pbr = texture is null
            ? new { metallicFactor = 0.0, roughnessFactor = 1.0 }
            : new { baseColorTexture = new { index = texture.Value }, metallicFactor = 0.0, roughnessFactor = 1.0 };

        // Cut-outs in the character textures need alpha to be honoured.
        _materials.Add(new { name, pbrMetallicRoughness = pbr, alphaMode = "MASK", alphaCutoff = 0.5, doubleSided = true });
        return _materials.Count - 1;
    }

    /// <summary>
    /// Adds a mesh. <paramref name="groups"/> maps a material index to the
    /// triangle indices that use it, so each becomes one primitive.
    /// </summary>
    public void AddMesh(string name, float[] positions, float[] normals, float[] uvs,
                        IReadOnlyDictionary<int, List<int>> groups)
    {
        var attributes = new Dictionary<string, int> { ["POSITION"] = AddVec3(positions, bounds: true) };
        if (normals.Length == positions.Length) attributes["NORMAL"] = AddVec3(normals, bounds: false);
        if (uvs.Length == positions.Length / 3 * 2) attributes["TEXCOORD_0"] = AddVec2(uvs);

        var primitives = new List<object>();
        foreach (var (material, indices) in groups)
        {
            if (indices.Count == 0) continue;
            primitives.Add(new { attributes, indices = AddIndices(indices), material, mode = 4 });
        }

        if (primitives.Count == 0) return;

        _meshes.Add(new { name, primitives });
        _nodes.Add(new { name, mesh = _meshes.Count - 1 });
    }

    public void Write(string path)
    {
        var gltf = new Dictionary<string, object>
        {
            ["asset"] = new { version = "2.0", generator = "OpenBuzz" },
            ["scene"] = 0,
            ["scenes"] = new[] { new { nodes = Enumerable.Range(0, _nodes.Count).ToArray() } },
            ["nodes"] = _nodes,
            ["meshes"] = _meshes,
            ["accessors"] = _accessors,
            ["bufferViews"] = _bufferViews,
            ["buffers"] = new[] { new { byteLength = _bin.Length } },
        };

        if (_materials.Count > 0) gltf["materials"] = _materials;
        if (_textures.Count > 0)
        {
            gltf["textures"] = _textures;
            gltf["images"] = _images;
        }

        var json = Pad(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(gltf)), 0x20);
        var bin = Pad(_bin.ToArray(), 0x00);

        using var file = File.Create(path);
        using var w = new BinaryWriter(file);
        w.Write(Magic);
        w.Write(2u);
        w.Write((uint)(12 + 8 + json.Length + 8 + bin.Length));
        w.Write((uint)json.Length); w.Write(JsonChunk); w.Write(json);
        w.Write((uint)bin.Length); w.Write(BinChunk); w.Write(bin);
    }

    private static byte[] Pad(byte[] data, byte with)
    {
        int extra = (4 - data.Length % 4) % 4;
        if (extra == 0) return data;

        var padded = new byte[data.Length + extra];
        data.CopyTo(padded, 0);
        for (int i = data.Length; i < padded.Length; i++) padded[i] = with;
        return padded;
    }

    private int AddView(byte[] bytes, int? target)
    {
        while (_bin.Length % 4 != 0) _bin.WriteByte(0);
        int offset = (int)_bin.Length;
        _bin.Write(bytes, 0, bytes.Length);

        _bufferViews.Add(target is null
            ? new { buffer = 0, byteOffset = offset, byteLength = bytes.Length }
            : (object)new { buffer = 0, byteOffset = offset, byteLength = bytes.Length, target = target.Value });

        return _bufferViews.Count - 1;
    }

    private int AddVec3(float[] values, bool bounds)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        int view = AddView(bytes, 34962);

        var accessor = new Dictionary<string, object>
        {
            ["bufferView"] = view, ["componentType"] = 5126,
            ["count"] = values.Length / 3, ["type"] = "VEC3",
        };

        if (bounds)
        {
            float[] min = [float.MaxValue, float.MaxValue, float.MaxValue];
            float[] max = [float.MinValue, float.MinValue, float.MinValue];
            for (int i = 0; i < values.Length; i += 3)
                for (int k = 0; k < 3; k++)
                {
                    min[k] = Math.Min(min[k], values[i + k]);
                    max[k] = Math.Max(max[k], values[i + k]);
                }
            accessor["min"] = min;
            accessor["max"] = max;
        }

        _accessors.Add(accessor);
        return _accessors.Count - 1;
    }

    private int AddVec2(float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        int view = AddView(bytes, 34962);
        _accessors.Add(new { bufferView = view, componentType = 5126, count = values.Length / 2, type = "VEC2" });
        return _accessors.Count - 1;
    }

    private int AddIndices(List<int> indices)
    {
        var bytes = new byte[indices.Count * 4];
        for (int i = 0; i < indices.Count; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), (uint)indices[i]);

        int view = AddView(bytes, 34963);
        _accessors.Add(new { bufferView = view, componentType = 5125, count = indices.Count, type = "SCALAR" });
        return _accessors.Count - 1;
    }
}
