using System.Buffers.Binary;
using System.Text;

namespace OpenBuzz.Graphics;

/// <summary>
/// A `.ipu` video: Sony's PlayStation 2 IPU codec, with its frame index in a
/// matching `.ipx`.
///
/// The container is simple - a 16-byte header then the frames end to end:
///
/// <code>
///   char[4] "ipum"
///   u32     payload size
///   u16     width
///   u16     height
///   u32     frame count
/// </code>
///
/// **The frames are not self-delimiting.** Nothing in the bitstream marks where
/// one ends, which is what the `.ipx` is for: one u32 offset per frame, counted
/// from the start of the file, and its entry count matches the header's frame
/// count exactly on both videos here. Feeding the whole file to a decoder that
/// expects to find frame boundaries itself fails on almost every frame; split
/// on the index first and each one decodes.
///
/// The codec is MPEG-2-class intra coding and is not decoded here. FFmpeg has
/// had an IPU decoder since 4.4, so <see cref="SplitFrames"/> writes each frame
/// as a standalone one-frame `.ipu` for it to read.
/// </summary>
public sealed class IpuFile
{
    public const int HeaderSize = 16;
    public const string Magic = "ipum";

    public required string Name { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int FrameCount { get; init; }

    /// Declared payload size, which trails the real file length by a little
    /// padding on both videos here.
    public required int DeclaredSize { get; init; }

    /// Byte offset of each frame, from the index; empty when there is no `.ipx`.
    public required int[] FrameOffsets { get; init; }

    public bool HasIndex => FrameOffsets.Length > 0;

    /// Whether the index agrees with the header about how many frames there are.
    public bool IndexMatches => FrameOffsets.Length == FrameCount;

    public static IpuFile Load(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < HeaderSize || Encoding.ASCII.GetString(data, 0, 4) != Magic)
            throw new InvalidDataException($"{Path.GetFileName(path)}: not an {Magic} file.");

        var indexPath = Path.ChangeExtension(path, ".ipx");
        var offsets = Array.Empty<int>();
        if (File.Exists(indexPath))
        {
            var index = File.ReadAllBytes(indexPath);
            offsets = new int[index.Length / 4];
            for (int i = 0; i < offsets.Length; i++)
                offsets[i] = BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(i * 4));
        }

        return new IpuFile
        {
            Name = Path.GetFileNameWithoutExtension(path),
            DeclaredSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)),
            Width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8)),
            Height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(10)),
            FrameCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12)),
            FrameOffsets = offsets,
        };
    }

    /// <summary>
    /// Writes each frame as its own one-frame `.ipu`, which is the form a
    /// decoder can read: the same header with a frame count of one, then that
    /// frame's bytes.
    /// </summary>
    public int SplitFrames(string path, string outDir)
    {
        if (!HasIndex) throw new InvalidOperationException($"{Name}: no .ipx index, so frames cannot be split.");

        var data = File.ReadAllBytes(path);
        Directory.CreateDirectory(outDir);

        int written = 0;
        for (int i = 0; i < FrameOffsets.Length; i++)
        {
            int start = FrameOffsets[i];
            int end = i + 1 < FrameOffsets.Length ? FrameOffsets[i + 1] : data.Length;
            if (start < HeaderSize || end <= start || end > data.Length) continue;

            var frame = new byte[HeaderSize + (end - start)];
            Encoding.ASCII.GetBytes(Magic).CopyTo(frame, 0);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), end - start);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(8), (ushort)Width);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(10), (ushort)Height);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(12), 1);
            data.AsSpan(start, end - start).CopyTo(frame.AsSpan(HeaderSize));

            File.WriteAllBytes(Path.Combine(outDir, $"{Name}_{i:D4}.ipu"), frame);
            written++;
        }

        return written;
    }
}
