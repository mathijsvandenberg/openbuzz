using OpenBuzz.Graphics;

namespace OpenBuzz.Cli;

/// <summary>
/// Searches for the horizontal pixel permutation.
///
/// Observation that makes this tractable: in the decoded flags the vertical
/// placement is already correct ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â colour bands do not bleed into each other ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â
/// and there is no diagonal skew, so the row stride is right and pixels never
/// move between rows. Whatever is wrong is a shuffle of x within a row.
///
/// PS2 address swizzles are bit manipulations, so the candidate space is a
/// permutation of the low k bits of x, optionally XORed by a constant. That is
/// k! * 2^k candidates ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â a few thousand ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â rather than the millions the earlier
/// formula sweep explored, and it can be scored exhaustively.
///
/// Scoring uses palette luminance rather than the index, because index
/// adjacency is meaningless: neighbouring indices are unrelated colours. Real
/// artwork is horizontally smooth, so the correct permutation minimises the
/// mean absolute luminance step along each row.
/// </summary>
public static class BitPermProbe
{
    public static int Run(string texPath, int maxBits)
    {
        var tex = Ps2Texture.Load(texPath);
        int w = tex.Width, h = tex.Height;

        // Undo the library's current unswizzle: work from raw row-major indices,
        // since the premise is that rows are already correct.
        var data = File.ReadAllBytes(texPath);
        int paletteBytes = tex.Depth == 8 ? 1024 : 64;
        int indexBytes = tex.Depth == 8 ? w * h : w * h / 2;
        var raw = data.AsSpan(data.Length - paletteBytes - indexBytes, indexBytes).ToArray();
        if (tex.Depth != 8) { Console.Error.WriteLine("4bpp not supported here."); return 1; }

        var lum = new double[tex.Palette.Length];
        for (int i = 0; i < tex.Palette.Length; i++)
        {
            uint c = tex.Palette[i];
            lum[i] = 0.299 * (c & 0xFF) + 0.587 * ((c >> 8) & 0xFF) + 0.114 * ((c >> 16) & 0xFF);
        }

        Console.WriteLine($"{Path.GetFileName(texPath)}  {w}x{h}@8bpp");
        Console.WriteLine($"baseline (identity): {Score(raw, w, h, 4, [0, 1, 2, 3], 0, 0, 0, true):F1} transitions/row");
        Console.WriteLine();

        var results = new List<(double Score, string Label)>();

        for (int k = 4; k <= maxBits; k++)
            foreach (var perm in Permutations([.. Enumerable.Range(0, k)]))
                for (int xor = 0; xor < 2; xor++)
                    foreach (int shift in new[] { 0, 1, 2, 3 })
                        foreach (int amount in new[] { 0, 1, 2, 4, 8, 16 })
                            foreach (bool add in new[] { true, false })
                            {
                                double s = Score(raw, w, h, k, perm, xor, shift, amount, add);
                                results.Add((s, $"k={k} [{string.Join(",", perm)}] x{xor} y>>{shift}*{amount} {(add ? "+" : "^")}"));
                            }

        Console.WriteLine($"{"candidate",-40} {"mean |step|",12}");
        foreach (var (score, label) in results.OrderBy(r => r.Score).Take(12))
            Console.WriteLine($"{label,-40} {score,12:F1}");

        var best = results.MinBy(r => r.Score);
        Console.WriteLine();
        Console.WriteLine($"best: {best.Label} at {best.Score:F1} transitions/row (lower is better)");
        return 0;
    }

    /// <summary>
    /// dst[y][x] reads src[y][map[x]]. The map depends on y through a swap
    /// term, which is what the standard PSMT8 formula does with
    /// ((y + 2) >> 2) & 1 - the horizontal shuffle alternates in bands of rows.
    /// Searching only y-independent permutations plateaued at 96 transitions
    /// per row against an expected sub-20, and the decoded output showed fine
    /// horizontal striping, which is the visible signature of exactly this.
    /// </summary>
    private static int[] BuildMap(int w, int k, int[] perm, int xor, int y, int shift, int amount, bool add)
    {
        int mask = (1 << k) - 1;
        int swap = ((y >> shift) & 1) * amount;
        var map = new int[w];

        for (int x = 0; x < w; x++)
        {
            int low = x & mask, shuffled = 0;
            for (int b = 0; b < k; b++)
                if ((low & (1 << b)) != 0)
                    shuffled |= 1 << perm[b];

            int combined = add ? (shuffled + swap) & mask : (shuffled ^ swap) & mask;
            map[x] = (x & ~mask) | (combined ^ xor);
        }

        return map;
    }

    private static int[] Identity(int w) => [.. Enumerable.Range(0, w)];

    /// <summary>
    /// Mean number of colour transitions per row; lower is better.
    ///
    /// Flags are a handful of solid bars, so a correct decode gives very few
    /// changes along a row - the Danish flag is red, white, red, which is two
    /// transitions. A wrong permutation shuffles pixels within their block and
    /// produces dither, which is dozens.
    ///
    /// This replaces two metrics that both failed. Mean luminance step rewarded
    /// interleaving, because shredding a feature across a dominant background
    /// still averages low. Longest run saturated, because large flat areas keep
    /// one long run alive no matter how the rest is scrambled - identity already
    /// scored 105 of a best 115. Counting every boundary avoids both traps.
    /// </summary>
    private static double Score(byte[] raw, int w, int h, int k, int[] perm, int xor,
                                int shift, int amount, bool add)
    {
        long transitions = 0, rows = 0;

        for (int y = 0; y < h; y += Math.Max(1, h / 96))
        {
            var map = BuildMap(w, k, perm, xor, y, shift, amount, add);
            int row = y * w;
            for (int x = 1; x < w; x++)
                if (raw[row + map[x]] != raw[row + map[x - 1]]) transitions++;
            rows++;
        }

        return rows == 0 ? double.MaxValue : (double)transitions / rows;
    }

    /// Kept for reference: the objective that misled, retained so the two can be
    /// compared side by side rather than swapped silently.
    private static double MeanStep(byte[] raw, int w, int h, double[] lum, int[] map)
    {
        double sum = 0;
        long count = 0;

        for (int y = 0; y < h; y += Math.Max(1, h / 64))
        {
            int row = y * w;
            double prev = lum[raw[row + map[0]]];
            for (int x = 1; x < w; x++)
            {
                double cur = lum[raw[row + map[x]]];
                sum += Math.Abs(cur - prev);
                prev = cur;
                count++;
            }
        }

        return count == 0 ? double.MaxValue : sum / count;
    }

    private static IEnumerable<int[]> Permutations(int[] items)
    {
        if (items.Length <= 1) { yield return items; yield break; }
        for (int i = 0; i < items.Length; i++)
        {
            var rest = items.Where((_, j) => j != i).ToArray();
            foreach (var tail in Permutations(rest))
                yield return [items[i], .. tail];
        }
    }
}
