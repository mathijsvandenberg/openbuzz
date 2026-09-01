using OpenBuzz.Graphics;

namespace OpenBuzz.Cli;

/// Reads the `.ipu` videos and splits them on their `.ipx` index.
public static class VideoCommands
{
    public static int Info(string dir)
    {
        var files = Directory.GetFiles(dir, "*.ipu").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) { Console.Error.WriteLine($"No .ipu files under {dir}."); return 1; }

        Console.WriteLine($"{"video",-14} {"size",-12} {"frames",7} {"index",7} {"bytes",10}");
        foreach (var path in files)
        {
            var ipu = IpuFile.Load(path);
            Console.WriteLine($"{ipu.Name,-14} {ipu.Width + "x" + ipu.Height,-12} {ipu.FrameCount,7} " +
                              $"{(ipu.HasIndex ? ipu.FrameOffsets.Length.ToString() : "none"),7} " +
                              $"{new FileInfo(path).Length,10:N0}" +
                              (ipu.HasIndex && !ipu.IndexMatches ? "   !! index and header disagree" : ""));
        }
        return 0;
    }

    /// <summary>
    /// Splits every video into one file per frame. The codec itself is left to
    /// FFmpeg, which has decoded IPU since 4.4:
    ///
    ///   ffmpeg -i Logo01_0000.ipu frame.png
    /// </summary>
    public static int Split(string dir, string outDir)
    {
        var files = Directory.GetFiles(dir, "*.ipu").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) { Console.Error.WriteLine($"No .ipu files under {dir}."); return 1; }

        int total = 0;
        foreach (var path in files)
        {
            var ipu = IpuFile.Load(path);
            if (!ipu.HasIndex)
            {
                Console.Error.WriteLine($"  !! {ipu.Name}: no .ipx index, skipped");
                continue;
            }

            int n = ipu.SplitFrames(path, Path.Combine(outDir, ipu.Name));
            Console.WriteLine($"{ipu.Name}: {n} frames -> {Path.Combine(outDir, ipu.Name)}");
            total += n;
        }

        Console.WriteLine($"Split {total} frames.");
        return 0;
    }
}
