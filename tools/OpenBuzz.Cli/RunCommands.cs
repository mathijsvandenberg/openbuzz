namespace OpenBuzz.Cli;

using System.Text;
using OpenBuzz.Cli.Lua;

/// <summary>
/// Runs the disc's own scripts on the embedded VM.
///
/// Every native the scripts call is registered as a stub that records the call
/// and returns nil, so a run does not need the engine to exist first. What it
/// produces is the trace: which functions a round actually reaches, in order,
/// with the arguments it passes. That trace is the implementation order for
/// the real natives, instead of guessing which of the 688 matter.
/// </summary>
internal static class RunCommands
{
    public static int Run(string scriptDir, string chunk, string? entry, string? tracePath, int limit, int resumes, string preload, int players)
    {
        var loaded = new Dictionary<string, LuaProto>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(scriptDir, "*.clu", SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            try { loaded[name] = LuaUndump.Load(File.ReadAllBytes(path), name); }
            catch { /* a chunk that will not load is reported when asked for */ }
        }

        if (!loaded.ContainsKey(chunk))
        {
            Console.Error.WriteLine($"no chunk '{chunk}' under {scriptDir}");
            return 1;
        }

        var vm = new LuaVm();
        LuaStdlib.Install(vm);
        var trace = new StringBuilder();
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);
        var missing = new List<string>();
        int stubbed = 0, depth = 0;

        void Line(string s)
        {
            trace.AppendLine(s);
            if (trace.Length < 200_000) Console.WriteLine(s);
        }

        // The scripts load each other through IncludeScript, which is how a
        // round pulls in the support code it needs. Honouring it is what makes
        // running one round reach the rest of the game.
        vm.Register("IncludeScript", args =>
        {
            var name = args.Length > 0 ? LuaValues.ToStringValue(args[0]) : "";
            Line($"{new string(' ', depth * 2)}IncludeScript(\"{name}\")");
            if (!loaded.TryGetValue(name, out var proto))
            {
                Line($"{new string(' ', depth * 2)}  !! not on the disc");
                return [];
            }
            depth++;
            try { vm.Call(vm.Load(proto)); }
            finally { depth--; }
            return [];
        });

        // Anything else the scripts reach for becomes a recording stub.
        vm.OnMissingGlobal = name =>
        {
            missing.Add(name);
            stubbed++;
            vm.Register(name, args =>
            {
                calls[name] = calls.GetValueOrDefault(name) + 1;
                if (calls[name] <= limit)
                    Line($"{new string(' ', depth * 2)}{name}({Format(args)})");
                return [];
            });
        };

        // The natives that answer rather than record. Without these a script
        // that asks the engine a question gets nil and stops.
        var host = new LuaHost { Players = players };
        LoadText(host, scriptDir);
        host.Install(vm);

        // A few natives have to be real rather than stubbed, because the data
        // scripts use their return values to build the tables everything else
        // reads. These are the first of the 688 to be actually implemented.
        vm.RegisterVoid("AllowGlobalVariables", _ => { });
        vm.RegisterVoid("DisallowGlobalVariables", _ => { });
        vm.Register("TableCopy", args =>
        {
            if (args.Length == 0 || args[0] is not LuaTable src) return [new LuaTable()];
            var copy = new LuaTable();
            foreach (var (k, v) in src.Pairs()) copy.Set(k, v);
            return [copy];
        });

        // GenericData sets several hundred plain-value globals - timings,
        // layout, round parameters. Running it first means the trace shows the
        // real numbers instead of turning each one into a function stub the
        // moment a script reads it.
        foreach (var name in preload.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!loaded.TryGetValue(name.Trim(), out var proto)) continue;
            vm.Call(vm.Load(proto));
            Line($"-- preloaded {name.Trim()} --");
        }

        Line($"-- running {chunk} on the embedded VM, natives stubbed --");
        try
        {
            // The chunk itself only defines globals. The round is whatever it
            // left behind - startScript, for every round on this disc.
            vm.Call(vm.Load(loaded[chunk]));

            if (entry is not null)
            {
                var fn = vm.Globals.Get(entry);
                if (fn is null)
                {
                    Line($"!! {chunk} defines no '{entry}'");
                }
                else
                {
                    Line($"-- calling {entry}() --");
                    vm.Call(fn);
                }

                // A round is a coroutine: startScript builds it and stores it
                // as questionScript, and the round start script drives it. With
                // every native stubbed nothing ever completes, so this resumes
                // it a bounded number of times to walk the body.
                if (vm.Globals.Get("questionScript") is LuaCoroutine round)
                {
                    Line("-- resuming questionScript --");
                    for (int i = 0; i < resumes && round.Status != "dead"; i++)
                        round.Resume([]);
                    Line($"-- coroutine is {round.Status} --");
                }
            }
        }
        catch (LuaError e)
        {
            Line($"!! {e.Message}");
        }

        Line("");
        Line($"-- {calls.Count} distinct natives called, {stubbed} stubbed on demand --");
        foreach (var (name, count) in calls.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal))
            Line($"   {count,6}  {name}");

        if (tracePath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(tracePath))!);
            File.WriteAllText(tracePath, trace.ToString());
            Console.WriteLine($"\nTrace written to {tracePath}");
        }
        return 0;
    }

    /// The bundled text table, if `obz bundle` has been run, so the trace shows
    /// the strings the game would show rather than bare keys.
    private static void LoadText(LuaHost host, string scriptDir)
    {
        var root = Path.GetDirectoryName(scriptDir.TrimEnd(Path.DirectorySeparatorChar));
        var path = Path.Combine(root ?? ".", "godot2d", "text.json");
        if (!File.Exists(path)) return;

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                host.Text[p.Name] = p.Value.GetString() ?? "";
    }

    private static string Format(object?[] args) =>
        string.Join(", ", args.Select(a => a is string s ? $"\"{s}\"" : LuaValues.ToStringValue(a)));
}
