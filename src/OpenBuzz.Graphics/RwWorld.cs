namespace OpenBuzz.Graphics;

using System.Buffers.Binary;

/// One sector of a world, decoded to triangles.
public sealed class RwWorldSector
{
    public required float[] Positions { get; init; }
    public required float[] Normals { get; init; }
    public required float[] TexCoords { get; init; }
    public required RwTriangle[] Triangles { get; init; }
}

/// <summary>
/// `StudioScene.rp2`: WORLD_STUDIO, the static set the props stand in.
///
/// This is where the round screen actually is. StudioModels.rp2 holds the
/// props - podiums, jumbotron cabinet, audience - but CAMERA_SCREEN does not
/// point at any of them; it points into the world, which had never been
/// parsed at all.
///
/// An RpWorld is a BSP of plane sectors ending in atomic sectors. On PS2 each
/// atomic sector carries its geometry exactly the way a model geometry does -
/// a BINMESH giving one split per material and a NATIVEDATA of VU1 DMA chains
/// - so <see cref="RwPs2Native"/> reads them unchanged. The one difference is
/// that the materials live on the world and each sector indexes into them
/// through its own matListWindowBase.
/// </summary>
public static class RwWorld
{
    /// The world header, which among other things says whether the sectors
    /// carry prelighting and how many UV sets they have.
    public sealed class Header
    {
        public required int TriangleCount { get; init; }
        public required int VertexCount { get; init; }
        public required int SectorCount { get; init; }
        public required int Format { get; init; }

        public bool Prelit => (Format & RwGeometry.FlagPrelit) != 0;
        public int NumUVs => (Format >> 16) & 0xFF;
        public bool IsNative => (Format & 0x01000000) != 0;
    }

    public static RwNode? Find(List<RwNode> tree) =>
        RwStream.Flatten(tree).FirstOrDefault(n => n.Id == RwId.World);

    public static Header ReadHeader(byte[] d, RwNode world)
    {
        var body = world.Children.FirstOrDefault(c => c.Id == RwId.Struct)
                   ?? throw new InvalidDataException("world has no struct");

        int p = body.DataOffset;
        return new Header
        {
            // 4 bytes rootIsWorldSector, then 12 bytes inverse world origin.
            TriangleCount = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p + 16)),
            VertexCount = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p + 20)),
            SectorCount = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p + 28)),
            Format = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p + 36)),
        };
    }

    /// The texture name of every material on the world, in list order.
    public static string[] ReadMaterialTextures(byte[] d, RwNode world)
    {
        var matList = world.Children.FirstOrDefault(c => c.Id == RwId.MatList);
        if (matList is null) return [];

        var names = new List<string>();
        foreach (var material in matList.Children.Where(c => c.Id == RwId.Material))
        {
            var texture = material.Children.FirstOrDefault(c => c.Id == RwId.Texture);
            var name = texture?.Children.FirstOrDefault(c => c.Id == RwId.String);
            names.Add(name is null ? "" : RwStream.ReadString(d, name));
        }
        return [.. names];
    }

    /// Every atomic sector with geometry, in file order.
    public static List<RwWorldSector> ReadSectors(byte[] d, RwNode world, Header header)
    {
        var sectors = new List<RwWorldSector>();
        Walk(world);
        return sectors;

        void Walk(RwNode node)
        {
            foreach (var child in node.Children)
            {
                if (child.Id == RwId.AtomicSector)
                {
                    var sector = ReadSector(d, child, header);
                    if (sector is not null) sectors.Add(sector);
                }
                if (child.Id is RwId.AtomicSector or RwId.PlaneSector) Walk(child);
            }
        }
    }

    private static RwWorldSector? ReadSector(byte[] d, RwNode sector, Header header)
    {
        var body = sector.Children.FirstOrDefault(c => c.Id == RwId.Struct);
        if (body is null || body.Size < 44) return null;

        // matListWindowBase, then the triangle and vertex counts.
        int windowBase = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(body.DataOffset));
        int vertexCount = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(body.DataOffset + 8));
        if (vertexCount == 0) return null;

        var extension = sector.Children.FirstOrDefault(c => c.Id == RwId.Extension);
        var binMesh = extension?.Children.FirstOrDefault(c => c.Id == RwId.Mesh);
        var nativeData = extension?.Children.FirstOrDefault(c => c.Id == RwId.NativeData);
        if (binMesh is null || nativeData is null) return null;

        var splits = ReadBinMesh(d, binMesh);
        var mesh = RwPs2Native.Read(d, nativeData.DataOffset, nativeData.End,
                                    [.. splits.Select(s => s.Indices)], header.Prelit, header.NumUVs);

        var triangles = new List<RwTriangle>();
        for (int i = 0; i < mesh.Strips.Count && i < splits.Count; i++)
        {
            // The sector's materials are a window onto the world's list.
            ushort material = (ushort)(windowBase + splits[i].Material);
            foreach (var (a, b, c) in RwPs2Native.Triangulate(mesh.Strips[i], mesh.Positions))
                triangles.Add(new RwTriangle((ushort)a, (ushort)b, (ushort)c, material));
        }

        if (triangles.Count == 0) return null;

        return new RwWorldSector
        {
            Positions = [.. mesh.Positions],
            Normals = [.. mesh.Normals],
            TexCoords = [.. mesh.TexCoords],
            Triangles = [.. triangles],
        };
    }

    /// As the geometry reader's, but a world's BINMESH never carries indices.
    private static List<(int Indices, int Material)> ReadBinMesh(byte[] d, RwNode binMesh)
    {
        int p = binMesh.DataOffset;
        int meshCount = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p + 4));
        p += 12;

        bool hasIndices = binMesh.Size > 12 + meshCount * 8;

        var result = new List<(int, int)>();
        for (int i = 0; i < meshCount && p + 8 <= binMesh.End; i++)
        {
            int indices = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p));
            int material = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p + 4));
            p += 8;
            if (hasIndices) p += indices * 4;
            result.Add((indices, material));
        }
        return result;
    }
}
