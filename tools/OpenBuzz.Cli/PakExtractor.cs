using System.IO.Compression;

namespace OpenBuzz.Cli;

/// <summary>
/// The .PAK files on the disc are plain ZIP archives with every entry stored
/// uncompressed, so BCL ZipFile reads them directly. The only real work here is
/// deciding which packs are worth the disk space.
/// </summary>
public static class PakExtractor
{
    /// Language packs we skip unless --all is passed. Dutch (NET) is the target locale.
    private static readonly string[] SkippedLanguagePrefixes = ["FRA", "GER", "ITA"];

    public static int Run(string discRoot, string outRoot, bool all)
    {
        if (!Directory.Exists(discRoot))
        {
            Console.Error.WriteLine($"Disc root not found: {discRoot}");
            return 1;
        }

        var paks = Directory.GetFiles(discRoot, "*.PAK", SearchOption.TopDirectoryOnly)
                            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                            .ToList();
        if (paks.Count == 0)
        {
            Console.Error.WriteLine($"No .PAK files under {discRoot}. Is the disc mounted?");
            return 1;
        }

        Directory.CreateDirectory(outRoot);

        long totalBytes = 0;
        int totalFiles = 0, skipped = 0;

        foreach (var pak in paks)
        {
            var name = Path.GetFileNameWithoutExtension(pak).ToUpperInvariant();
            if (!all && SkippedLanguagePrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            {
                Console.WriteLine($"  skip  {name,-10} (other locale; use --all to include)");
                skipped++;
                continue;
            }

            using var zip = ZipFile.OpenRead(pak);
            long bytes = 0;
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;

                var dest = SafeCombine(outRoot, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
                bytes += entry.Length;
                totalFiles++;
            }
            totalBytes += bytes;
            Console.WriteLine($"  ok    {name,-10} {zip.Entries.Count,5} entries  {Mib(bytes),8:F1} MiB");
        }

        Console.WriteLine();
        Console.WriteLine($"Extracted {totalFiles} files, {Mib(totalBytes):F1} MiB to {outRoot}");
        if (skipped > 0) Console.WriteLine($"Skipped {skipped} non-Dutch locale packs.");
        return 0;
    }

    private static double Mib(long b) => b / 1024.0 / 1024.0;

    /// Guard against archive entries that try to escape the output directory.
    private static string SafeCombine(string root, string entryName)
    {
        var rel = entryName.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, rel));
        var rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Archive entry escapes output directory: {entryName}");
        return full;
    }
}
