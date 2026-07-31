using OpenBuzz.Audio;

namespace OpenBuzz.Cli;

public static class AudioCommands
{
    /// Validates the sector model against the state recorded in each trailer.
    public static int Probe(string root, int limit)
    {
        var files = Directory.GetFiles(root, "*.vgp", SearchOption.AllDirectories)
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) { Console.Error.WriteLine($"No .vgp files under {root}."); return 1; }

        // Sample evenly so a directory ordered by content type cannot skew things.
        int step = Math.Max(1, files.Length / limit);
        var sample = files.Where((_, i) => i % step == 0).Take(limit).ToArray();

        int splitWins = 0, interWins = 0, misaligned = 0, badMarker = 0, confident = 0;

        Console.WriteLine($"{"file",-24} {"sectors",8} {"split",6} {"intlv",6} {"mono",6} {"of",4}  layout");
        foreach (var path in sample)
        {
            var p = VgpFile.Probe(File.ReadAllBytes(path));
            if (p.TrailingBytes != 0) misaligned++;
            if (!p.MarkerOk) badMarker++;
            if (p.Confident) confident++;
            if (p.SplitMatches >= p.InterleavedMatches) splitWins++; else interWins++;

            if (sample.Length <= 24)
                Console.WriteLine($"{Path.GetFileName(path),-24} {p.Sectors,8} {p.SplitMatches,6} {p.InterleavedMatches,6} {p.MonoMatches,6} {p.Checked,4}  {p.Layout}");
        }

        Console.WriteLine();
        Console.WriteLine($"sampled {sample.Length} of {files.Length} files");
        Console.WriteLine($"  layout SplitHalves      : {splitWins}");
        Console.WriteLine($"  layout BlockInterleaved : {interWins}");
        Console.WriteLine($"  trailer state reproduced exactly on every sector : {confident}/{sample.Length}");
        Console.WriteLine($"  not a whole number of 2336-byte sectors          : {misaligned}");
        Console.WriteLine($"  trailer marker != 0x002C                         : {badMarker}");
        return 0;
    }

    /// Reports sample rates declared by the headered .vag files.
    public static int VagRates(string root)
    {
        var files = Directory.GetFiles(root, "*.vag", SearchOption.AllDirectories);
        if (files.Length == 0) { Console.Error.WriteLine($"No .vag files under {root}."); return 1; }

        var byRate = new SortedDictionary<int, int>();
        foreach (var path in files)
        {
            try
            {
                var v = VagFile.Parse(File.ReadAllBytes(path));
                byRate[v.SampleRate] = byRate.GetValueOrDefault(v.SampleRate) + 1;
            }
            catch (Exception ex) { Console.Error.WriteLine($"  !! {Path.GetFileName(path)}: {ex.Message}"); }
        }

        Console.WriteLine($"{files.Length} .vag files:");
        foreach (var (rate, count) in byRate) Console.WriteLine($"  {rate,6} Hz : {count}");
        return 0;
    }

    public static int Decode(string input, string outDir, int sampleRate, string? forceLayout, int limit)
    {
        var files = Directory.Exists(input)
            ? Directory.GetFiles(input, "*.vgp", SearchOption.AllDirectories)
                       .Concat(Directory.GetFiles(input, "*.vag", SearchOption.AllDirectories))
                       .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Take(limit).ToArray()
            : [input];

        if (files.Length == 0) { Console.Error.WriteLine($"Nothing to decode under {input}."); return 1; }

        Directory.CreateDirectory(outDir);
        int done = 0;
        double totalSeconds = 0;

        foreach (var path in files)
        {
            var bytes = File.ReadAllBytes(path);
            var dest = Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".wav");

            try
            {
                if (VagFile.LooksLikeVag(bytes))
                {
                    var vag = VagFile.Parse(bytes);
                    var mono = vag.DecodeMono();
                    WavWriter.Write(dest, mono, 1, vag.SampleRate);
                    totalSeconds += (double)mono.Length / vag.SampleRate;
                }
                else
                {
                    var layout = forceLayout?.ToLowerInvariant() switch
                    {
                        "split" => VgpLayout.SplitHalves,
                        "mono" => VgpLayout.Mono,
                        "interleaved" => VgpLayout.BlockInterleaved,
                        _ => VgpFile.Probe(bytes).Layout,
                    };
                    var pcm = VgpFile.Decode(bytes, layout, out int ch);
                    WavWriter.Write(dest, pcm, ch, sampleRate);
                    totalSeconds += (double)pcm.Length / ch / sampleRate;
                }
                done++;
            }
            catch (Exception ex) { Console.Error.WriteLine($"  !! {Path.GetFileName(path)}: {ex.Message}"); }
        }

        Console.WriteLine($"Decoded {done}/{files.Length} to {outDir}  ({totalSeconds / 60:F1} min of audio)");
        return 0;
    }
}

