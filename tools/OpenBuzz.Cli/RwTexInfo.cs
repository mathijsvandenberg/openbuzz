using System.Buffers.Binary;
using OpenBuzz.Graphics;

namespace OpenBuzz.Cli;

/// <summary>
/// Diagnostic: prints the raster header of every texture and, crucially,
/// compares the two ways of locating the pixel block - anchored at the end of
/// the raster struct (how librw reads it, sequentially) versus anchored at the
/// end of the data. Any difference is trailing bytes the end-anchor would
/// mistake for payload.
/// </summary>
public static class RwTexInfo
{
    public static int Run(string texDir, string rwDir, int limit)
    {
        Console.WriteLine($"{"texture",-42} {"WxH",-10} {"bpp",3} {"ver",3} {"pixelSize",10} {"palSize",8} {"seqStart",9} {"endStart",9} {"delta",6}");

        int shown = 0, mismatched = 0;

        foreach (var (label, payload) in Payloads(texDir, rwDir))
        {
            if (shown++ >= limit && mismatched == 0) break;
            if (Report(label, payload)) mismatched++;
        }

        Console.WriteLine();
        Console.WriteLine($"{mismatched} textures where the two anchors disagree");
        return 0;
    }

    /// Yields every texture payload: standalone files first, then embedded ones.
    private static IEnumerable<(string Label, byte[] Data)> Payloads(string texDir, string rwDir)
    {
        if (Directory.Exists(texDir))
            foreach (var path in Directory.GetFiles(texDir, "*.tex").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                yield return ("tex/" + Path.GetFileNameWithoutExtension(path), File.ReadAllBytes(path));

        if (!Directory.Exists(rwDir)) yield break;

        foreach (var path in Directory.GetFiles(rwDir, "*.rp2").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var data = File.ReadAllBytes(path);
            var stream = Path.GetFileNameWithoutExtension(path);
            int i = 0;

            foreach (var node in RwStream.Flatten(RwStream.Parse(data)).Where(n => n.Id == RwId.TextureNative))
                yield return ($"{stream}#{++i}", data.AsSpan(node.DataOffset, node.Size).ToArray());
        }
    }

    /// Returns true when the sequential and end-of-data anchors disagree.
    private static bool Report(string label, byte[] data)
    {
        int structEnd = 0, width = 0, height = 0, depth = 0, version = 0, pixelSize = 0, paletteSize = 0;
        int stringsSeen = 0;

        foreach (var chunk in RwChunk.Walk(data, 0, data.Length))
        {
            if (chunk.Type == RwType.String) { stringsSeen++; continue; }
            if (chunk.Type != RwType.Struct || chunk.Size < 28 || width != 0 || stringsSeen < 2) continue;

            var raster = RwChunk.Read(data, chunk.DataOffset);
            int o = raster.DataOffset;
            if (o + 0x40 > data.Length) break;

            width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(o));
            height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(o + 4));
            depth = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(o + 8));
            version = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(o + 0x0E));
            pixelSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(o + 0x30));
            paletteSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(o + 0x34));

            // The pixel block follows the raster struct immediately.
            structEnd = o + 0x40;
        }

        if (width == 0) return false;

        int endAnchor = data.Length - paletteSize - pixelSize;
        int delta = endAnchor - structEnd;

        Console.WriteLine($"{label,-42} {width + "x" + height,-10} {depth,3} {version,3} {pixelSize,10:N0} {paletteSize,8:N0} " +
                          $"{structEnd,9} {endAnchor,9} {delta,6}");

        return delta != 0;
    }
}
