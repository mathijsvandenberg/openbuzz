using System.Text;
using OpenBuzz.Cli.Lua;

namespace OpenBuzz.Cli;

/// <summary>
/// The A2D scripts bind objects to text by *name*, while `default.ndx` maps
/// 32-bit hashes to line numbers in `default.str`. Without the hash function the
/// names cannot be resolved to actual strings.
///
/// This cracks it by brute force over candidate hashes: the names are known
/// (from the scripts) and the hashes are known (from the index), so the right
/// function is the one whose outputs land in the index.
/// </summary>
public static class TextBindings
{
    private delegate uint HashFn(string s);

    private static readonly (string Name, HashFn Fn)[] Candidates =
    [
        ("djb2", s => Fold(s, 5381u, (h, c) => h * 33u + c)),
        ("djb2-xor", s => Fold(s, 5381u, (h, c) => (h * 33u) ^ c)),
        ("djb2 lower", s => Fold(s.ToLowerInvariant(), 5381u, (h, c) => h * 33u + c)),
        ("sdbm", s => Fold(s, 0u, (h, c) => c + (h << 6) + (h << 16) - h)),
        ("fnv1-32", s => Fold(s, 2166136261u, (h, c) => h * 16777619u ^ c)),
        ("fnv1a-32", s => Fold(s, 2166136261u, (h, c) => (h ^ c) * 16777619u)),
        ("fnv1a-32 lower", s => Fold(s.ToLowerInvariant(), 2166136261u, (h, c) => (h ^ c) * 16777619u)),
        ("crc32", s => Crc32(Encoding.ASCII.GetBytes(s))),
        ("crc32 lower", s => Crc32(Encoding.ASCII.GetBytes(s.ToLowerInvariant()))),
        ("crc32 +nul", s => Crc32(Encoding.ASCII.GetBytes(s + "\0"))),
        ("js", s => Fold(s, 1315423911u, (h, c) => h ^ ((h << 5) + c + (h >> 2)))),
        ("rot13ish", s => Fold(s, 0u, (h, c) => (h << 4) + c)),
        ("elf", ElfHash),
        ("sum32", s => Fold(s, 0u, (h, c) => h + c)),
        ("java31", s => Fold(s, 0u, (h, c) => h * 31u + c)),
        ("java31 upper", s => Fold(s.ToUpperInvariant(), 0u, (h, c) => h * 31u + c)),
        ("java31 lower", s => Fold(s.ToLowerInvariant(), 0u, (h, c) => h * 31u + c)),
        ("mul37", s => Fold(s, 0u, (h, c) => h * 37u + c)),
        ("mul131", s => Fold(s, 0u, (h, c) => h * 131u + c)),
        ("mul65599", s => Fold(s, 0u, (h, c) => h * 65599u + c)),
        ("crc32-msb", s => CrcMsb(Encoding.ASCII.GetBytes(s), final: true)),
        ("crc32-msb raw", s => CrcMsb(Encoding.ASCII.GetBytes(s), final: false)),
        ("crc32-msb lower", s => CrcMsb(Encoding.ASCII.GetBytes(s.ToLowerInvariant()), final: true)),
        ("crc32-msb upper", s => CrcMsb(Encoding.ASCII.GetBytes(s.ToUpperInvariant()), final: true)),
        ("djb2 upper", s => Fold(s.ToUpperInvariant(), 5381u, (h, c) => h * 33u + c)),
        ("fnv1a upper", s => Fold(s.ToUpperInvariant(), 2166136261u, (h, c) => (h ^ c) * 16777619u)),
        ("pjw", PjwHash),
    ];

    /// CRC-32 with the Ethernet polynomial applied MSB-first (unreflected),
    /// which is common in mid-2000s console engines.
    private static uint CrcMsb(byte[] data, bool final)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc ^= (uint)b << 24;
            for (int i = 0; i < 8; i++)
                crc = (crc & 0x80000000u) != 0 ? (crc << 1) ^ 0x04C11DB7u : crc << 1;
        }
        return final ? ~crc : crc;
    }

    private static uint PjwHash(string s)
    {
        const int bits = 32, threeQuarters = bits * 3 / 4, oneEighth = bits / 8;
        const uint highBits = 0xFFFFFFFFu << (bits - oneEighth);
        uint h = 0;
        foreach (char c in s)
        {
            h = (h << oneEighth) + c;
            uint test = h & highBits;
            if (test != 0) h = (h ^ (test >> threeQuarters)) & ~test;
        }
        return h;
    }

    private static uint Fold(string s, uint seed, Func<uint, uint, uint> step)
    {
        uint h = seed;
        foreach (char c in s) h = step(h, c);
        return h;
    }

    private static uint ElfHash(string s)
    {
        uint h = 0;
        foreach (char c in s)
        {
            h = (h << 4) + c;
            uint high = h & 0xF0000000u;
            if (high != 0) h ^= high >> 24;
            h &= ~high;
        }
        return h;
    }

    private static readonly uint[] CrcTable = BuildCrc();

    private static uint[] BuildCrc()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    private static uint Crc32(byte[] data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    public static int Crack(string a2dDir, string textDir)
    {
        var ndxPath = Path.Combine(textDir, "default.ndx");
        if (!File.Exists(ndxPath)) { Console.Error.WriteLine($"Missing {ndxPath}"); return 1; }

        // default.ndx: one "hash id" pair per line, hash written as a signed int.
        var hashes = new Dictionary<uint, int>();
        foreach (var line in File.ReadAllLines(ndxPath))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && long.TryParse(parts[0], out long h) && int.TryParse(parts[1], out int id))
                hashes[unchecked((uint)(int)h)] = id;
        }

        var names = CollectTextNames(a2dDir);
        Console.WriteLine($"{hashes.Count} hashes in default.ndx, {names.Count} distinct text names in A2D");
        Console.WriteLine();
        Console.WriteLine($"{"hash function",-18} {"names found in index",22}");

        (string Name, int Hits) best = ("", 0);
        foreach (var (label, fn) in Candidates)
        {
            int hits = names.Count(n => hashes.ContainsKey(fn(n)));
            Console.WriteLine($"{label,-18} {$"{hits}/{names.Count}",22}");
            if (hits > best.Hits) best = (label, hits);
        }

        Console.WriteLine();
        Console.WriteLine(best.Hits == 0
            ? "No candidate matches. The hash is something else."
            : $"best: {best.Name} - {best.Hits}/{names.Count}");
        return 0;
    }

    /// Distinct text names bound by the A2D SetActorToText* calls.
    public static List<string> CollectTextNames(string a2dDir)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var path in Directory.GetFiles(a2dDir, "*.clu", SearchOption.AllDirectories))
        {
            var root = LuaUndump.Load(File.ReadAllBytes(path), Path.GetFileName(path));
            foreach (var call in LuaDataExtractor.Extract(root))
            {
                if (!call.Function.StartsWith("SetActorToTextMapping", StringComparison.Ordinal)) continue;
                if (call.Text(1) is { Length: > 0 } name) names.Add(name);
            }
        }

        return [.. names];
    }

    /// Shows the raw binding calls so the extra arguments can be identified.
    public static int Show(string a2dDir)
    {
        var calls = new List<LuaCall>();
        foreach (var path in Directory.GetFiles(a2dDir, "*.clu", SearchOption.AllDirectories))
        {
            var root = LuaUndump.Load(File.ReadAllBytes(path), Path.GetFileName(path));
            calls.AddRange(LuaDataExtractor.Extract(root)
                .Where(c => c.Function.StartsWith("SetActorToTextMapping", StringComparison.Ordinal)));
        }

        Console.WriteLine($"{calls.Count} text-binding calls");
        foreach (var group in calls.GroupBy(c => c.Function))
        {
            Console.WriteLine();
            Console.WriteLine($"{group.Key}  ({group.Count()})");
            int argc = group.Max(c => c.Args.Count);
            for (int i = 0; i < argc; i++)
            {
                var vals = group.Select(c => i < c.Args.Count ? c.Args[i] : null).ToArray();
                var distinct = vals.Where(v => v is not null).Select(v => v!.ToString()!).Distinct().Order().ToArray();
                Console.WriteLine($"  arg[{i}] {distinct.Length,4} distinct" +
                                  (distinct.Length <= 6 ? $" : {string.Join(", ", distinct)}" : ""));
            }
            foreach (var c in group.Take(3)) Console.WriteLine($"    e.g. {c}");
        }

        return 0;
    }
}
