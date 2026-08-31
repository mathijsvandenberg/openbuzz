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
    private readonly List<object> _skins = [];
    private readonly List<object> _animations = [];
    private int? _skinForMeshes;

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
    /// Adds the joint nodes and the skin they belong to. Returns the node index
    /// of each joint, in bone order.
    ///
    /// glTF stores matrices column-major and multiplies column vectors;
    /// RenderWare stores a row-major basis and multiplies row vectors, so the
    /// RenderWare rows become the glTF columns and the layout maps across
    /// directly once the padding is replaced by a proper bottom row.
    /// </summary>
    public int[] AddSkeleton(int[] parents, float[][] localMatrices, float[] inverseBind)
    {
        int bones = parents.Length;
        int first = _nodes.Count;

        var children = new List<int>[bones];
        for (int b = 0; b < bones; b++) children[b] = [];
        for (int b = 0; b < bones; b++)
            if (parents[b] >= 0) children[parents[b]].Add(first + b);

        for (int b = 0; b < bones; b++)
        {
            var m = localMatrices[b];
            var node = new Dictionary<string, object>
            {
                ["name"] = $"bone{b}",
                ["matrix"] = new float[]
                {
                    m[0], m[1], m[2], 0,
                    m[3], m[4], m[5], 0,
                    m[6], m[7], m[8], 0,
                    m[9], m[10], m[11], 1,
                },
            };
            if (children[b].Count > 0) node["children"] = children[b];
            _nodes.Add(node);
        }

        var matrices = new float[bones * 16];
        for (int b = 0; b < bones; b++)
        {
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    matrices[b * 16 + r * 4 + c] = inverseBind[b * 16 + r * 4 + c];

            for (int c = 0; c < 3; c++) matrices[b * 16 + 12 + c] = inverseBind[b * 16 + 12 + c];
            matrices[b * 16 + 15] = 1f;
        }

        _skins.Add(new
        {
            joints = Enumerable.Range(first, bones).ToArray(),
            inverseBindMatrices = AddMat4(matrices),
            skeleton = first + Math.Max(0, Array.IndexOf(parents, -1)),
        });

        _skinForMeshes = _skins.Count - 1;
        return [.. Enumerable.Range(first, bones)];
    }

    /// Adds one clip; times, rotations and translations are given per bone.
    public void AddAnimation(string name, int[] jointNodes, float[][] times,
                             float[][] rotations, float[][] translations)
    {
        var channels = new List<object>();
        var samplers = new List<object>();

        for (int b = 0; b < jointNodes.Length; b++)
        {
            if (times[b].Length == 0) continue;
            int input = AddScalarFloat(times[b]);

            samplers.Add(new { input, output = AddVec4(rotations[b]), interpolation = "LINEAR" });
            channels.Add(new { sampler = samplers.Count - 1, target = new { node = jointNodes[b], path = "rotation" } });

            samplers.Add(new { input, output = AddVec3(translations[b], bounds: false), interpolation = "LINEAR" });
            channels.Add(new { sampler = samplers.Count - 1, target = new { node = jointNodes[b], path = "translation" } });
        }

        if (channels.Count > 0) _animations.Add(new { name, channels, samplers });
    }

    /// <summary>
    /// Adds a mesh. <paramref name="groups"/> maps a material index to the
    /// triangle indices that use it, so each becomes one primitive.
    /// </summary>
    public void AddMesh(string name, float[] positions, float[] normals, float[] uvs,
                        IReadOnlyDictionary<int, List<int>> groups,
                        byte[]? joints = null, float[]? weights = null)
    {
        int vertices = positions.Length / 3;
        var attributes = new Dictionary<string, int> { ["POSITION"] = AddVec3(positions, bounds: true) };
        if (normals.Length == positions.Length) attributes["NORMAL"] = AddVec3(normals, bounds: false);
        if (uvs.Length == vertices * 2) attributes["TEXCOORD_0"] = AddVec2(uvs);

        bool skinned = joints is not null && weights is not null &&
                       joints.Length == vertices * 4 && weights.Length == vertices * 4;
        if (skinned)
        {
            attributes["JOINTS_0"] = AddJoints(joints!);
            attributes["WEIGHTS_0"] = AddVec4(Normalise(weights!));
        }

        var primitives = new List<object>();
        foreach (var (material, indices) in groups)
        {
            if (indices.Count == 0) continue;
            primitives.Add(new { attributes, indices = AddIndices(indices), material, mode = 4 });
        }

        if (primitives.Count == 0) return;

        _meshes.Add(new { name, primitives });

        if (skinned && _skinForMeshes is { } skin)
            _nodes.Add(new { name, mesh = _meshes.Count - 1, skin });
        else
            _nodes.Add(new { name, mesh = _meshes.Count - 1 });
    }

    public void Write(string path)
    {
        var gltf = new Dictionary<string, object>
        {
            ["asset"] = new { version = "2.0", generator = "OpenBuzz" },
            ["scene"] = 0,
            ["scenes"] = new[] { new { nodes = RootNodes() } },
            ["nodes"] = _nodes,
            ["meshes"] = _meshes,
            ["accessors"] = _accessors,
            ["bufferViews"] = _bufferViews,
            ["buffers"] = new[] { new { byteLength = _bin.Length } },
        };

        if (_skins.Count > 0) gltf["skins"] = _skins;
        if (_animations.Count > 0) gltf["animations"] = _animations;
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

    /// Only nodes that are not a child of another; listing a joint as a scene root as
    /// well as a child applies its parent transform twice in some viewers.
    private int[] RootNodes()
    {
        var child = new HashSet<int>();
        foreach (var node in _nodes)
            if (node is Dictionary<string, object> d && d.TryGetValue("children", out var kids) && kids is List<int> list)
                child.UnionWith(list);

        return [.. Enumerable.Range(0, _nodes.Count).Where(i => !child.Contains(i))];
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

    /// Weights have to sum to one per vertex or viewers shrink the mesh.
    private static float[] Normalise(float[] weights)
    {
        var result = (float[])weights.Clone();
        for (int v = 0; v + 3 < result.Length; v += 4)
        {
            float sum = result[v] + result[v + 1] + result[v + 2] + result[v + 3];
            if (sum <= 0) { result[v] = 1f; continue; }
            for (int k = 0; k < 4; k++) result[v + k] /= sum;
        }
        return result;
    }

    private int AddJoints(byte[] joints)
    {
        int view = AddView(joints, 34962);
        _accessors.Add(new { bufferView = view, componentType = 5121, count = joints.Length / 4, type = "VEC4" });
        return _accessors.Count - 1;
    }

    private int AddVec4(float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        int view = AddView(bytes, 34962);
        _accessors.Add(new { bufferView = view, componentType = 5126, count = values.Length / 4, type = "VEC4" });
        return _accessors.Count - 1;
    }

    private int AddMat4(float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        int view = AddView(bytes, null);
        _accessors.Add(new { bufferView = view, componentType = 5126, count = values.Length / 16, type = "MAT4" });
        return _accessors.Count - 1;
    }

    /// Animation inputs need min and max or some viewers reject the sampler.
    private int AddScalarFloat(float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        int view = AddView(bytes, null);
        _accessors.Add(new Dictionary<string, object>
        {
            ["bufferView"] = view, ["componentType"] = 5126,
            ["count"] = values.Length, ["type"] = "SCALAR",
            ["min"] = new[] { values.Min() }, ["max"] = new[] { values.Max() },
        });
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
