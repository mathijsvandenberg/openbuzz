using OpenBuzz.Cli;
using OpenBuzz.Cli.Lua;

if (args.Length == 0) { Usage(); return 1; }

var repoRoot = FindRepoRoot();
var defaultExtract = Path.Combine(repoRoot, "extracted");

// Escape hatch for re-testing the RK encoding against the corpus.
if (Opt(args, "--rk") is { } rk) OpenBuzz.Cli.Lua.LuaOpcodes.RkThreshold = int.Parse(rk);

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "extract":
        {
            var disc = Opt(args, "--disc") ?? "D:\\";
            var outDir = Opt(args, "--out") ?? defaultExtract;
            bool all = args.Contains("--all");
            Console.WriteLine($"Extracting {disc} -> {outDir}");
            return PakExtractor.Run(disc, outDir, all);
        }

        case "lua":
        {
            var inDir = Opt(args, "--in") ?? Path.Combine(defaultExtract, "Scripts");
            var outDir = Opt(args, "--out") ?? Path.Combine(repoRoot, "docs", "disasm");
            return DisassembleAll(inDir, outDir);
        }

        case "api":
        {
            var inDir = Opt(args, "--in") ?? Path.Combine(defaultExtract, "Scripts");
            var report = Opt(args, "--out") ?? Path.Combine(repoRoot, "docs", "host-api.md");
            return ApiScanner.Run(inDir, report, Opt(args, "--exclude"));
        }

        case "tex":
        {
            var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "info";
            var inDir = Opt(args, "--in") ?? Path.Combine(defaultExtract, "Textures");
            return sub switch
            {
                "info" => TextureCommands.Info(inDir),
                "decode" => TextureCommands.Decode(inDir, Opt(args, "--out") ?? Path.Combine(defaultExtract, "png")),
                "atlas" => TextureCommands.Atlas(inDir),
                "probe" => SwizzleProbe.Run(Opt(args, "--file") ?? Path.Combine(inDir, "BZ_Language_flags.tex")),
                "strip" => SwizzleProbe.Strip(Opt(args, "--file") ?? Path.Combine(inDir, "BZ_Language_flags.tex"), int.Parse(Opt(args, "--y0") ?? "0"), int.Parse(Opt(args, "--y1") ?? "255")),
                _ => Fail($"unknown tex subcommand '{sub}'"),
            };
        }

        case "quiz":
        {
            var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "stats";
            var extracted = Opt(args, "--in") ?? defaultExtract;
            var locale = Opt(args, "--locale") ?? "NET";
            return sub switch
            {
                "stats" => QuizCommands.Stats(extracted, locale),
                "dump" => QuizCommands.Dump(extracted, locale, Opt(args, "--pool") ?? "qall",
                              Opt(args, "--out") ?? Path.Combine(extracted, $"questions-{locale}.txt")),
                _ => Fail($"unknown quiz subcommand '{sub}'"),
            };
        }

        case "a2d":

        {

            var sub2 = args.Length > 1 ? args[1].ToLowerInvariant() : "stats";

            var inDir2 = Opt(args, "--in") ?? Path.Combine(defaultExtract, "Scripts", "A2d");
                    var locale2 = Opt(args, "--locale") ?? "NET";

            return sub2 switch

            {

                "stats" => A2dCommands.Stats(inDir2),
                        "text" => TextBindings.Show(inDir2),
                        "crack" => TextBindings.Crack(inDir2, Path.Combine(defaultExtract, "BM1", "Text", locale2)),
                        "sweep" => HashHunt.Sweep(inDir2, Path.Combine(defaultExtract, "BM1", "Text", locale2)),
                        "elf" => HashHunt.ScanElf(Opt(args, "--elf") ?? "D:\\SCES_533.05"),
                        "export" => A2dCommands.Export(inDir2, Opt(args, "--out") ?? Path.Combine(defaultExtract, "a2d")),

                "dump" => A2dCommands.Dump(inDir2, Opt(args, "--chunk") ?? "BZ_FE_PIP_STATES", Opt(args, "--out") ?? Path.Combine(repoRoot, "docs", "a2d-sample.txt")),

                _ => Fail($"unknown a2d subcommand '{sub2}'"),

            };

        }

        

        case "rkprobe":
            return RkProbe.Run(Opt(args, "--in") ?? Path.Combine(defaultExtract, "Scripts"));

        case "audio":
        {
            var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "probe";
            var inDir = Opt(args, "--in") ?? Path.Combine(defaultExtract, "Sound");
            return sub switch
            {
                "probe" => AudioCommands.Probe(inDir, int.Parse(Opt(args, "--limit") ?? "16")),
                "rates" => AudioCommands.VagRates(inDir),
                "decode" => AudioCommands.Decode(inDir,
                                Opt(args, "--out") ?? Path.Combine(repoRoot, "extracted", "wav"),
                                int.Parse(Opt(args, "--rate") ?? OpenBuzz.Audio.VgpFile.DefaultSampleRate.ToString()),
                                Opt(args, "--layout"),
                                int.Parse(Opt(args, "--limit") ?? "50")),
                _ => Fail($"unknown audio subcommand '{sub}'"),
            };
        }

        default:
            Usage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static int DisassembleAll(string inDir, string outDir)
{
    var files = Directory.GetFiles(inDir, "*.clu", SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
    if (files.Length == 0) { Console.Error.WriteLine($"No .clu files under {inDir}."); return 1; }

    Directory.CreateDirectory(outDir);
    int ok = 0;
    foreach (var path in files)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            var proto = LuaUndump.Load(File.ReadAllBytes(path), name);
            File.WriteAllText(Path.Combine(outDir, name + ".luaasm"), LuaDisassembler.Disassemble(proto, name));
            ok++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  !! {name}: {ex.Message}");
        }
    }
    Console.WriteLine($"Disassembled {ok}/{files.Length} chunks to {outDir}");
    return ok == files.Length ? 0 : 2;
}

static int Fail(string message)
{
    Console.Error.WriteLine($"error: {message}");
    return 1;
}

static string? Opt(string[] args, string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "OpenBuzz.sln"))) return d.FullName;
    return Directory.GetCurrentDirectory();
}

static void Usage()
{
    Console.WriteLine("""
        obz - OpenBuzz asset tooling

          obz extract [--disc D:\] [--out DIR] [--all]
              Unpack the disc's .PAK archives. Skips FRA/GER/ITA locale packs
              unless --all is given.

          obz lua [--in DIR] [--out DIR]
              Disassemble Lua 5.0 .clu chunks to readable .luaasm.

          obz api [--in DIR] [--out FILE]
              Report the native API surface the scripts depend on.
        """);
}




