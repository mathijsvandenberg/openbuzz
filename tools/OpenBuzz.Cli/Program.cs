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
                _ => Fail($"unknown tex subcommand '{sub}'"),
            };
        }

        case "rw":

        {

            var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "summary";

            var inDir = Opt(args, "--in") ?? Path.Combine(defaultExtract, "RWStream");

            return sub switch

            {

                "summary" => RwCommands.Summary(inDir),

                "tree" => RwCommands.Tree(Opt(args, "--file") ?? Path.Combine(inDir, "AngieCostume01.rp2"),

                              int.Parse(Opt(args, "--depth") ?? "6")),

                "textures" => RwCommands.Textures(inDir, Opt(args, "--out") ?? Path.Combine(defaultExtract, "rwpng")),

                "texinfo" => RwTexInfo.Run(Path.Combine(defaultExtract, "Textures"), inDir,
                                 int.Parse(Opt(args, "--limit") ?? "8")),

                _ => Fail($"unknown rw subcommand '{sub}'"),

            };

        }


        case "video":
        {
            var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "info";
            var inDir = Opt(args, "--in") ?? Path.Combine(defaultExtract, "Videos");
            return sub switch
            {
                "info" => VideoCommands.Info(inDir),
                "split" => VideoCommands.Split(inDir, Opt(args, "--out") ?? Path.Combine(defaultExtract, "ipu")),
                _ => Fail($"unknown video subcommand '{sub}'"),
            };
        }

        case "bundle":
            return BundleCommands.Run(defaultExtract,
                       Opt(args, "--out") ?? Path.Combine(defaultExtract, "godot2d"),
                       Opt(args, "--locale") ?? "NET",
                       int.Parse(Opt(args, "--limit") ?? "400"));

        case "model":
        {
            var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "list";
            var inDir = Opt(args, "--in") ?? Path.Combine(defaultExtract, "RWStream");
            return sub switch
            {
                "list" => ModelCommands.List(inDir),
                "export" => ModelExport.Run(inDir,
                                Opt(args, "--out") ?? Path.Combine(defaultExtract, "models"),
                                Opt(args, "--only")),
                _ => Fail($"unknown model subcommand '{sub}'"),
            };
        }

        case "font":
        {
            var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "list";
            var inDir = Opt(args, "--in") ?? Path.Combine(defaultExtract, "RWStream");
            return sub switch
            {
                "list" => FontCommands.List(inDir),
                "sample" => FontCommands.Sample(inDir,
                                Opt(args, "--out") ?? Path.Combine(defaultExtract, "font-sample.png"),
                                Opt(args, "--text") ?? "BUZZ! DE MUZIEKQUIZ 0123456789",
                                int.Parse(Opt(args, "--scale") ?? "2")),
                _ => Fail($"unknown font subcommand '{sub}'"),
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




