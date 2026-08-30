using System.Buffers.Binary;

namespace OpenBuzz.Graphics;

/// <summary>
/// A decoded `.tex`: a RenderWare PS2 native texture.
///
/// The raster header is librw's `StreamRasterExt` and it declares its own
/// payload sizes, which is what makes decoding reliable: `pixelSize` and
/// `paletteSize` at +0x30 and +0x34 give the exact extent of each block, and
/// each block opens with an **0x50-byte GIF/DMA header** before the actual
/// pixels or CLUT. In librw that shows up as
/// `read8(raster->palette - 0x50, paletteSize)`.
///
/// This code originally inferred the payload location instead, taking the CLUT
/// as the last 1024 bytes and the indices as the W*H bytes before it. The
/// arithmetic appeared to check out - every file is `W*H + 1024 + ~332` - but
/// those 332 bytes are partly the two block headers, so the indices were read
/// exactly 80 bytes early in every single texture. No swizzle model can correct
/// a shifted input, which is why a long series of increasingly elaborate
/// de-interleave theories each moved the corruption around without removing it.
/// The lesson: the transform was never the problem; the bytes fed into it were.
/// </summary>
public sealed class Ps2Texture
{
    /// Both payload blocks open with an 0x50-byte GIF/DMA header before the
    /// actual pixels or CLUT.
    public const int BlockHeader = 0x50;

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

    /// Raw TEX0 register from the raster header.
    public required ulong Tex0 { get; init; }

    /// Texture buffer width in units of 64 texels - the GS's own statement of
    /// the row stride the data is stored at.
    public int Tbw => (int)((Tex0 >> 14) & 0x3F);

    /// Buffer stride in texels.
    public int BufferWidth => Tbw * 64;

    /// GS pixel storage format: 0x13 = PSMT8, 0x14 = PSMT4.
    public int Psm => (int)((Tex0 >> 20) & 0x3F);

    /// Width and height as log2, per the register.
    public int Tw => (int)((Tex0 >> 26) & 0x0F);
    public int Th => (int)((Tex0 >> 30) & 0x0F);

    public static Ps2Texture Load(string path) => Parse(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));

    public static Ps2Texture Parse(byte[] data, string fallbackName)
    {
        string name = fallbackName, mask = "";
        int width = 0, height = 0, depth = 0;
        uint format = 0;
        ulong tex0 = 0;
        int version = 0, pixelSize = 0, paletteSize = 0;

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
                    // librw's StreamRasterExt, 0x40 bytes:
                    //   int32 width, height, depth
                    //   uint16 rasterFormat; int16 version
                    //   uint64 tex0; uint32 paletteOffset, tex1low
                    //   uint64 miptbp1, miptbp2
                    //   uint32 pixelSize, paletteSize, totalSize, mipmapVal
                    var raster = RwChunk.Read(data, chunk.DataOffset);
                    int o = raster.DataOffset;
                    if (o + 0x40 > data.Length) break;

                    width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(o));
                    height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(o + 4));
                    depth = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(o + 8));
                    format = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(o + 0x0C));
                    version = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(o + 0x0E));
                    tex0 = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(o + 0x10));
                    pixelSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(o + 0x30));
                    paletteSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(o + 0x34));
                    break;
                }
            }
        }

        if (width <= 0 || height <= 0 || (depth != 8 && depth != 4))
            throw new InvalidDataException($"{fallbackName}: unsupported raster {width}x{height}@{depth}bpp.");

        int paletteEntries = depth == 8 ? 256 : 16;
        int indexBytes = depth == 8 ? width * height : width * height / 2;

        // Both payload blocks carry an 0x50-byte GIF header. Taking the pixels
        // as "the W*H bytes before the palette" lands 80 bytes early - which is
        // what made every decode structurally displaced no matter how the
        // swizzle was modelled.
        int paletteBlock = data.Length - paletteSize;
        int pixelBlock = paletteBlock - pixelSize;

        if (pixelBlock < 0 || paletteBlock + BlockHeader + paletteEntries * 4 > data.Length)
            throw new InvalidDataException($"{fallbackName}: payload sizes do not fit the file.");

        int paletteStart = paletteBlock + BlockHeader;
        int indexStart = pixelBlock + BlockHeader;

        var raw = data.AsSpan(indexStart, indexBytes);

        var palette = new uint[paletteEntries];
        for (int i = 0; i < paletteEntries; i++)
        {
            int o = paletteStart + i * 4;
            palette[i] = (uint)(data[o] | (data[o + 1] << 8) | (data[o + 2] << 16) |
                                (Ps2Swizzle.ExpandAlpha(data[o + 3]) << 24));
        }
        if (depth == 8) Ps2Swizzle.UnshuffleClut256(palette);

        // Version 0 rasters are not swizzled at all; version 1 swizzles 8-bit.
        // Version 2 is "new style" and swizzles 8-bit as well.
        bool swizzled = depth == 8 && version != 0;

        var indices = depth == 8
            ? (swizzled ? RwSwizzle.UnswizzlePsmt8(raw, width, height) : raw.ToArray())
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
            Tex0 = tex0,
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

