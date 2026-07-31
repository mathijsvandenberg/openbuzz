using System.Buffers.Binary;
using OpenBuzz.Graphics;

namespace OpenBuzz.Cli;

/// <summary>
/// Works out how a texture's indices are laid out by scoring candidate decodes
/// on image coherence rather than by eye.
///
/// Real artwork has large flat regions, so neighbouring pixels usually share a
/// palette index. A wrong de-interleave scatters them and that fraction
/// collapses. The metric is palette-independent, which separates a swizzle
/// problem from a CLUT-order problem instead of confusing the two.
/// </summary>
public static class SwizzleProbe
{
    public static int Run(string texPath)
    {
        var data = File.ReadAllBytes(texPath);
        var tex = Ps2Texture.Parse(data, Path.GetFileNameWithoutExtension(texPath));
        int w = tex.Width, h = tex.Height;

        int paletteBytes = tex.Depth == 8 ? 1024 : 64;
        int indexBytes = tex.Depth == 8 ? w * h : w * h / 2;
        int indexStart = data.Length - paletteBytes - indexBytes;
        var raw = data.AsSpan(indexStart, indexBytes).ToArray();

        Console.WriteLine($"{Path.GetFileName(texPath)}  {w}x{h}@{tex.Depth}bpp");
        Console.WriteLine();

        var linear = raw.Length >= w * h ? raw[..(w * h)] : raw;
        var (lh, lv) = Coherence(linear, w, h);
        Console.WriteLine($"baseline linear: horiz {lh:P1}  vert {lv:P1}  score {(lh + lv) / 2:P1}");
        Console.WriteLine();

        // Vertical coherence collapses when the assumed row length is wrong, so
        // sweeping the stride locates the real one without assuming anything
        // about how the data is interleaved.
        Console.WriteLine("stride sweep (vertical coherence of the raw buffer):");
        var strides = new List<(int Stride, double Vertical)>();
        for (int s = 8; s <= 2048; s += 4)
        {
            int rows = linear.Length / s;
            if (rows < 8) break;
            long same = 0, total = 0;
            for (int y = 1; y < rows; y++)
                for (int x = 0; x < s; x++)
                {
                    total++;
                    if (linear[y * s + x] == linear[(y - 1) * s + x]) same++;
                }
            strides.Add((s, total == 0 ? 0 : (double)same / total));
        }
        foreach (var (s, v) in strides.OrderByDescending(t => t.Vertical).Take(8))
            Console.WriteLine($"    stride {s,5} : {v:P1}");
        Console.WriteLine();

        // Flags are solid bands, so a correct decode yields many rows that are
        // almost entirely one palette index. That discriminates far better than
        // counting equal neighbours, which stays high even when the layout is
        // wrong because scattered pixels still often match.
        var results = new List<(double Score, string Name, byte[] Indices)>
        {
            (BandScore(linear, w, h), "linear (no unswizzle)", linear),
        };

        var std = Ps2Swizzle.UnswizzlePsmt8(raw, w, h);
        results.Add((BandScore(std, w, h), "PSMT8 standard", std));

        // The stride peak at 2*width says two image rows share one buffer row.
        // These are the ways that pairing can be arranged.
        results.Add((BandScore(Pair(raw, w, h, halves: true), w, h), "row pairs: side-by-side", Pair(raw, w, h, true)));
        results.Add((BandScore(Pair(raw, w, h, halves: false), w, h), "row pairs: byte-interleaved", Pair(raw, w, h, false)));

        foreach (int bw in new[] { 7, 15, 31 })
            foreach (int bh in new[] { 7, 15, 31 })
                foreach (int swap in new[] { 0, 2 })
                    foreach (int colScale in new[] { 1, 2 })
                    {
                        var idx = Generic(raw, w, h, bw, bh, swap, colScale);
                        results.Add((BandScore(idx, w, h), $"bw={bw,-2} bh={bh,-2} swap={swap} cs={colScale}", idx));
                    }

        Console.WriteLine($"{"variant",-32} {"med run",10} {"coherence",10}");
        foreach (var (score, name, idx) in results.OrderByDescending(r => r.Score).Take(10))
        {
            var (hz, vt) = Coherence(idx, w, h);
            Console.WriteLine($"{name,-32} {score,10:F0} {(hz + vt) / 2,10:P1}");
        }

        var best = results.MaxBy(r => r.Score);
        Console.WriteLine();
        Console.WriteLine($"best by median run: {best.Name} ({best.Score:F0}px)");
        Console.WriteLine("Treat a winner as suspect unless coherence agrees: long runs with low");
        Console.WriteLine("vertical coherence is self-contradictory and means the metric is lying.");
        return 0;
    }

    /// <summary>
    /// Unpacks a buffer whose rows are twice the image width, so each buffer
    /// row carries two image rows â€” either as two contiguous halves or with the
    /// two rows' bytes alternating.
    /// </summary>
    private static byte[] Pair(byte[] src, int w, int h, bool halves)
    {
        var dst = new byte[w * h];
        int stride = w * 2;

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int row = y >> 1, odd = y & 1;
                int i = halves
                    ? row * stride + odd * w + x
                    : row * stride + x * 2 + odd;
                dst[y * w + x] = (uint)i < (uint)src.Length ? src[i] : (byte)0;
            }

        return dst;
    }

    /// <summary>
    /// Median of the longest run of identical indices in each row.
    ///
    /// An earlier version asked what fraction of rows were >=80% one index,
    /// which was miscalibrated: the flags are 256px wide inside a 512px atlas,
    /// so two different flags share every row and no row can reach 80%. That
    /// made every candidate fail regardless of correctness. Longest-run has no
    /// threshold to get wrong â€” solid bands produce long runs whatever else
    /// shares the row.
    /// </summary>
    private static double BandScore(byte[] idx, int w, int h)
    {
        var longest = new int[h];

        for (int y = 0; y < h; y++)
        {
            int best = 1, run = 1;
            for (int x = 1; x < w; x++)
            {
                if (idx[y * w + x] == idx[y * w + x - 1]) run++;
                else run = 1;
                if (run > best) best = run;
            }
            longest[y] = best;
        }

        Array.Sort(longest);
        return longest[h / 2];
    }

    /// Fraction of neighbouring pixels sharing a palette index.
    private static (double Horizontal, double Vertical) Coherence(byte[] idx, int w, int h)
    {
        long hSame = 0, hTot = 0, vSame = 0, vTot = 0;

        for (int y = 0; y < h; y++)
            for (int x = 1; x < w; x++)
            {
                hTot++;
                if (idx[y * w + x] == idx[y * w + x - 1]) hSame++;
            }

        for (int y = 1; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                vTot++;
                if (idx[y * w + x] == idx[(y - 1) * w + x]) vSame++;
            }

        return (hTot == 0 ? 0 : (double)hSame / hTot, vTot == 0 ? 0 : (double)vSame / vTot);
    }

    /// The PSMT8 de-interleave with its block and column terms parameterised.
    private static byte[] Generic(byte[] src, int w, int h, int bw, int bh, int swapOffset, int colScale)
    {
        var dst = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int blockLocation = (y & ~bh) * w + (x & ~bw) * 2;
                int swapSelector = (((y + swapOffset) >> 2) & 0x01) * 4;
                int posY = (((y & ~3) >> 1) + (y & 1)) & 0x07;
                int columnLocation = posY * w * colScale + ((x + swapSelector) & 0x07) * 4;
                int byteSelector = ((y >> 1) & 1) + ((x >> 2) & 2);

                int i = blockLocation + columnLocation + byteSelector;
                dst[y * w + x] = (uint)i < (uint)src.Length ? src[i] : (byte)0;
            }
        return dst;
    }

    /// Treats the standard formula as a write mapping instead of a read mapping.
    private static byte[] InversePsmt8(byte[] src, int w, int h)
    {
        var dst = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int blockLocation = (y & ~0x0F) * w + (x & ~0x0F) * 2;
                int swapSelector = (((y + 2) >> 2) & 0x01) * 4;
                int posY = (((y & ~3) >> 1) + (y & 1)) & 0x07;
                int columnLocation = posY * w * 2 + ((x + swapSelector) & 0x07) * 4;
                int byteSelector = ((y >> 1) & 1) + ((x >> 2) & 2);

                int i = blockLocation + columnLocation + byteSelector;
                if (i < dst.Length) dst[i] = src[y * w + x];
            }
        return dst;
    }

    /// Plain 8x2-pixel column de-interleave, without the block term.
    private static byte[] UnswizzleColumn(byte[] src, int w, int h)
    {
        var dst = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int posY = (((y & ~3) >> 1) + (y & 1)) & 0x07;
                int col = posY * w * 2 + (x & 0x07) * 4;
                int blk = (y & ~0x0F) * w + (x & ~0x07) * 2;
                int sel = ((y >> 1) & 1) + ((x >> 2) & 2);
                int i = blk + col + sel;
                dst[y * w + x] = i < src.Length ? src[i] : (byte)0;
            }
        return dst;
    }

    /// Reports the dominant colours of a horizontal strip, for checking a flag
    /// against what it is supposed to look like.
    public static int Strip(string texPath, int y0, int y1)
    {
        var tex = Ps2Texture.Load(texPath);
        var rgba = tex.ToRgba();

        Console.WriteLine($"{Path.GetFileName(texPath)} rows {y0}..{y1} of {tex.Height}");
        for (int y = y0; y <= y1 && y < tex.Height; y += Math.Max(1, (y1 - y0) / 12))
        {
            var counts = new Dictionary<uint, int>();
            for (int x = 0; x < tex.Width; x++)
            {
                uint c = rgba[y * tex.Width + x];
                counts[c] = counts.GetValueOrDefault(c) + 1;
            }
            var top = counts.OrderByDescending(kv => kv.Value).First();
            uint c0 = top.Key;
            Console.WriteLine($"  row {y,4}: dominant #{c0 & 0xFF:X2}{(c0 >> 8) & 0xFF:X2}{(c0 >> 16) & 0xFF:X2} " +
                              $"a={(c0 >> 24) & 0xFF:X2}  {top.Value * 100 / tex.Width}% of row, " +
                              $"{counts.Count} distinct");
        }
        return 0;
    }
}

