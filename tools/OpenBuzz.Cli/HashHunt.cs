using System.Buffers.Binary;

namespace OpenBuzz.Cli;

/// <summary>
/// Hunts for the `default.ndx` string hash.
///
/// Two independent attacks. The first sweeps the whole multiply-accumulate
/// family parametrically rather than testing named functions one at a time —
/// almost every hand-rolled engine hash is `h = h*M + c` or a near variant, so
/// searching M directly covers far more ground than a list of famous constants.
/// A hit is decisive on its own: a wrong hash lands in the 659-entry index with
/// probability about 1.5e-7, so even one match is a signal, not noise.
///
/// The second scans the game ELF for a CRC lookup table, which is what a
/// table-driven hash leaves behind and which no amount of guessing would find.
/// </summary>
public static class HashHunt
{
    private const int MaxMultiplier = 1 << 16;

    private static readonly uint[] Seeds = [0u, 1u, 5381u, 2166136261u, 0xFFFFFFFFu];

    public static int Sweep(string a2dDir, string textDir)
    {
        var hashes = LoadIndex(Path.Combine(textDir, "default.ndx"));
        if (hashes.Count == 0) { Console.Error.WriteLine("No hashes loaded."); return 1; }

        var names = TextBindings.CollectTextNames(a2dDir);
        Console.WriteLine($"{hashes.Count} index hashes, {names.Count} keys");
        Console.WriteLine($"sweeping M in 1..{MaxMultiplier - 1} x {Seeds.Length} seeds x 6 forms x 3 cases");
        Console.WriteLine();

        var variants = new (string Label, Func<string, string> Case)[]
        {
            ("as-is", s => s),
            ("lower", s => s.ToLowerInvariant()),
            ("upper", s => s.ToUpperInvariant()),
        };

        var found = new List<(int Hits, string Description)>();

        foreach (var (caseLabel, transform) in variants)
        {
            var keys = names.Select(transform).ToArray();

            for (int form = 0; form < 6; form++)
                foreach (uint seed in Seeds)
                    for (uint m = 1; m < MaxMultiplier; m++)
                    {
                        int hits = Count(keys, hashes, form, seed, m);
                        if (hits > 0)
                            found.Add((hits, $"form {form}  seed 0x{seed:X8}  M {m}  case {caseLabel}"));
                    }
        }

        if (found.Count == 0)
        {
            Console.WriteLine("No multiply-accumulate variant produces a single hit.");
            Console.WriteLine("The hash is table-driven or structurally different.");
            return 2;
        }

        foreach (var (hits, description) in found.OrderByDescending(f => f.Hits).Take(20))
            Console.WriteLine($"  {hits,3}/{names.Count}  {description}");
        return 0;
    }

    /// The six shapes hand-rolled hashes almost always take.
    private static uint Step(int form, uint h, uint c, uint m) => form switch
    {
        0 => h * m + c,
        1 => (h ^ c) * m,
        2 => h * m ^ c,
        3 => (h + c) * m,
        4 => h * m - c,
        _ => (h << 5) + h + c * m,
    };

    private static int Count(string[] keys, HashSet<uint> hashes, int form, uint seed, uint m)
    {
        int hits = 0;
        foreach (var key in keys)
        {
            uint h = seed;
            foreach (char ch in key) h = Step(form, h, ch, m);
            if (hashes.Contains(h)) hits++;
        }
        return hits;
    }

    private static HashSet<uint> LoadIndex(string path)
    {
        var set = new HashSet<uint>();
        if (!File.Exists(path)) return set;

        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && long.TryParse(parts[0], out long h))
                set.Add(unchecked((uint)(int)h));
        }
        return set;
    }

    /// <summary>
    /// Scans a binary for a 256-entry CRC lookup table. A generated table is
    /// fully determined by its second entry, so regenerating from t[1] and
    /// comparing the rest confirms it without knowing the polynomial in advance.
    /// </summary>
    public static int ScanElf(string elfPath)
    {
        if (!File.Exists(elfPath)) { Console.Error.WriteLine($"Missing {elfPath}"); return 1; }

        var data = File.ReadAllBytes(elfPath);
        Console.WriteLine($"{Path.GetFileName(elfPath)}  {data.Length:N0} bytes");

        int found = 0;
        for (int offset = 0; offset + 1024 <= data.Length; offset += 4)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)) != 0) continue;

            uint t1 = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
            if (t1 == 0) continue;

            if (MatchesLsbTable(data, offset, t1))
            {
                Console.WriteLine($"  CRC table (LSB-first) at 0x{offset:X}  poly 0x{Reflect(t1):X8}  t[1]=0x{t1:X8}");
                found++;
            }
            else if (MatchesMsbTable(data, offset, t1))
            {
                Console.WriteLine($"  CRC table (MSB-first) at 0x{offset:X}  poly 0x{t1:X8}");
                found++;
            }
        }

        Console.WriteLine(found == 0
            ? "  no CRC lookup table found"
            : $"  {found} table(s) found");
        return 0;
    }

    private static bool MatchesLsbTable(byte[] data, int offset, uint t1)
    {
        uint poly = Reflect(t1);
        for (int i = 2; i < 16; i++)
        {
            uint c = (uint)i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? poly ^ (c >> 1) : c >> 1;
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + i * 4)) != c) return false;
        }
        return true;
    }

    private static bool MatchesMsbTable(byte[] data, int offset, uint poly)
    {
        for (int i = 2; i < 16; i++)
        {
            uint c = (uint)i << 24;
            for (int k = 0; k < 8; k++) c = (c & 0x80000000u) != 0 ? (c << 1) ^ poly : c << 1;
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + i * 4)) != c) return false;
        }
        return true;
    }

    /// t[1] of an LSB-first table is the reflected polynomial shifted; recover it.
    private static uint Reflect(uint t1)
    {
        // t[1] = poly ^ (1 >> 1) chain; for the standard table t[1] == poly >> 0
        // after eight shifts of the value 1, which reduces to poly itself when
        // the low bit drives every step.
        uint c = 1;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? t1 ^ (c >> 1) : c >> 1;
        return t1;
    }
}
