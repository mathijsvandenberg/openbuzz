using System.Buffers.Binary;

namespace OpenBuzz.Graphics;

/// <summary>
/// A decoded `.tex`: a RenderWare PS2 native texture.
///
/// The chunk tree gives the name and dimensions. The pixel payload is wrapped
/// in GS transfer packets, so rather than walking those, the indices and CLUT
/// are taken from the end of the file — the palette is always the final
/// 1024 bytes (256 RGBA entries) with the indices immediately before it. Every
/// file on the disc matches `width*height + palette + header` exactly, which is
/// what makes that safe.
/// </summary>
public sealed class Ps2Texture
{
    public required string Name { get; init; }
    public required string MaskName { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Depth { get; init; }
    public required uint RasterFormat { get; init; }

    /// One byte per pixel, un-swizzled, indexing <see cref="Palette"/>.
    public required byte[] Indices { get; init; }

    /// RGBA8888, alpha already expanded from the PS2's 0..128 range.
    public required uint[] Palette { get; init; }

    public static Ps2Texture Load(string path) => Parse(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));

    public static Ps2Texture Parse(byte[] data, string fallbackName)
    {
        string name = fallbackName, mask = "";
        int width = 0, height = 0, depth = 0;
        uint format = 0;

        // The files begin mid-tree: STRUCT("PS2"), STRING(name), STRING(mask),
        // STRUCT(raster info), then the pixel payload.
        int stringsSeen = 0;
        foreach (var chunk in RwChunk.Walk(data, 0, data.Length))
        {
            switch (chunk.Type)
            {
                case RwType.String:
                    if (stringsSeen++ == 0) name = RwChunk.ReadString(data, chunk);
                    else mask = RwChunk.ReadString(data, chunk);
                    break;

                // The raster lives one level down: this struct's payload opens
                // with another chunk header, and the dimensions follow that.
                case RwType.Struct when chunk.Size >= 28 && width == 0 && stringsSeen >= 2:
                {
                    var raster = RwChunk.Read(data, chunk.DataOffset);
                    int o = raster.DataOffset;
                    if (o + 16 > data.Length) break;
                    width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(o));
                    height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(o + 4));
                    depth = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(o + 8));
                    format = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(o + 12));
                    break;
                }
            }
        }

        if (width <= 0 || height <= 0 || (depth != 8 && depth != 4))
            throw new InvalidDataException($"{fallbackName}: unsupported raster {width}x{height}@{depth}bpp.");

        int paletteEntries = depth == 8 ? 256 : 16;
        int paletteBytes = paletteEntries * 4;
        int indexBytes = depth == 8 ? width * height : width * height / 2;

        if (data.Length < paletteBytes + indexBytes)
            throw new InvalidDataException($"{fallbackName}: file too small for {width}x{height}@{depth}bpp.");

        int paletteStart = data.Length - paletteBytes;
        int indexStart = paletteStart - indexBytes;

        var palette = new uint[paletteEntries];
        for (int i = 0; i < paletteEntries; i++)
        {
            int o = paletteStart + i * 4;
            palette[i] = (uint)(data[o] | (data[o + 1] << 8) | (data[o + 2] << 16) |
                                (Ps2Swizzle.ExpandAlpha(data[o + 3]) << 24));
        }
        if (depth == 8) Ps2Swizzle.UnshuffleClut256(palette);

        var raw = data.AsSpan(indexStart, indexBytes);
        var indices = depth == 8
            ? Ps2Swizzle.UnswizzlePsmt8(raw, width, height)
            : Ps2Swizzle.UnswizzlePsmt4(raw, width, height);

        return new Ps2Texture
        {
            Name = name,
            MaskName = mask,
            Width = width,
            Height = height,
            Depth = depth,
            RasterFormat = format,
            Indices = indices,
            Palette = palette,
        };
    }

    /// Expands to straight RGBA8888, one uint per pixel.
    public uint[] ToRgba()
    {
        var pixels = new uint[Width * Height];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte index = Indices[i];
            pixels[i] = index < Palette.Length ? Palette[index] : 0;
        }
        return pixels;
    }
}
