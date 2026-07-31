using System.Text;
using OpenBuzz.Cli.Lua;

namespace OpenBuzz.Cli;

/// <summary>
/// Works out how large the native surface behind the scripts is: every global
/// the scripts read, write, or call, plus every method name invoked via OP_SELF.
/// That set is the shim a port has to provide if it runs the original bytecode,
/// or the feature list to reimplement if it does not.
/// </summary>
public static class ApiScanner
{
    private sealed class Usage
    {
        public int Reads, Writes, Calls;
        public readonly SortedSet<int> Arities = [];
        public readonly SortedSet<string> Files = new(StringComparer.OrdinalIgnoreCase);
    }

    public static int Run(string scriptRoot, string reportPath, string? excludeDir = null)
    {
        var files = Directory.GetFiles(scriptRoot, "*.clu", SearchOption.AllDirectories)
                             .Where(p => excludeDir is null ||
                                         !p.Contains(Path.DirectorySeparatorChar + excludeDir + Path.DirectorySeparatorChar,
                                                     StringComparison.OrdinalIgnoreCase))
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0)
        {
            Console.Error.WriteLine($"No .clu files under {scriptRoot}.");
            return 1;
        }

        var globals = new Dictionary<string, Usage>(StringComparer.Ordinal);
        var methods = new Dictionary<string, Usage>(StringComparer.Ordinal);
        var fields = new Dictionary<string, Usage>(StringComparer.Ordinal);
        int failed = 0, protoCount = 0;

        foreach (var path in files)
        {
            var shortName = Path.GetFileName(path);
            try
            {
                var root = LuaUndump.Load(File.ReadAllBytes(path), shortName);
                foreach (var f in root.SelfAndDescendants())
                {
                    protoCount++;
                    ScanProto(f, shortName, globals, methods, fields);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  !! {shortName}: {ex.Message}");
                failed++;
            }
        }

        Console.WriteLine($"Parsed {files.Length - failed}/{files.Length} chunks, {protoCount} prototypes.");
        Console.WriteLine($"  globals={globals.Count}  methods={methods.Count}  table fields={fields.Count}");

        var sb = new StringBuilder();
        sb.AppendLine("# Host API surface");
        sb.AppendLine();
        sb.AppendLine($"Derived from {files.Length - failed} compiled Lua 5.0 chunks ({protoCount} prototypes).");
        sb.AppendLine("Arity is taken from the OP_CALL that consumes each global, so it is exact for");
        sb.AppendLine("straight-line calls and absent where the callee reached the call site indirectly.");
        sb.AppendLine();

        // A global the scripts never assign must come from the host — that is the
        // set a port has to provide. Anything the corpus assigns is defined in Lua.
        var native = globals.Where(kv => kv.Value.Calls > 0 && kv.Value.Writes == 0).ToList();
        var scriptDefined = globals.Where(kv => kv.Value.Calls > 0 && kv.Value.Writes > 0).ToList();
        var readOnlyData = globals.Where(kv => kv.Value.Calls == 0 && kv.Value.Writes == 0).ToList();
        var scriptState = globals.Where(kv => kv.Value.Calls == 0 && kv.Value.Writes > 0).ToList();

        Console.WriteLine($"  native functions={native.Count}  script-defined={scriptDefined.Count}  " +
                          $"host constants={readOnlyData.Count}  script state={scriptState.Count}");

        Section(sb, "Native functions — called but never assigned (the port must implement these)", native, showArity: true);
        Section(sb, "Script-defined functions — called and assigned in Lua", scriptDefined, showArity: true);
        Section(sb, "Host constants — read but never assigned", readOnlyData, showArity: false);
        Section(sb, "Script globals — assigned in Lua (game state)", scriptState, showArity: false);
        Section(sb, "Method names (OP_SELF — object model)", methods.ToList(), showArity: true);
        Section(sb, "Table field names (OP_GETTABLE / OP_SETTABLE constant keys)", fields.ToList(), showArity: false);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.WriteAllText(reportPath, sb.ToString());
        Console.WriteLine($"Wrote {reportPath}");
        return 0;
    }

    private static void Section(StringBuilder sb, string title, List<KeyValuePair<string, Usage>> items, bool showArity)
    {
        sb.AppendLine($"## {title} ({items.Count})");
        sb.AppendLine();
        sb.AppendLine(showArity ? "| Name | Calls | Arity | Files |" : "| Name | Reads | Writes | Files |");
        sb.AppendLine(showArity ? "|---|---:|---|---:|" : "|---|---:|---:|---:|");
        foreach (var (name, u) in items.OrderByDescending(kv => kv.Value.Calls + kv.Value.Reads + kv.Value.Writes)
                                       .ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.AppendLine(showArity
                ? $"| `{name}` | {u.Calls} | {(u.Arities.Count > 0 ? string.Join(",", u.Arities.Select(Arity)) : "?")} | {u.Files.Count} |"
                : $"| `{name}` | {u.Reads} | {u.Writes} | {u.Files.Count} |");
        }
        sb.AppendLine();
    }

    private static string Arity(int n) => n < 0 ? "var" : n.ToString();

    private static void ScanProto(LuaProto f, string file,
        Dictionary<string, Usage> globals, Dictionary<string, Usage> methods, Dictionary<string, Usage> fields)
    {
        // Shadow the register file with "what name loaded this slot", so an
        // OP_CALL can be attributed back to the global or method it invokes.
        var reg = new string?[Math.Max((int)f.MaxStackSize, 2) + 8];
        var isTarget = JumpTargets(f);

        for (int pc = 0; pc < f.Code.Length; pc++)
        {
            if (isTarget[pc]) Array.Clear(reg); // control flow merges; stop trusting the shadow

            uint ins = f.Code[pc];
            var op = LuaOpcodes.GetOp(ins);
            int a = LuaOpcodes.A(ins), b = LuaOpcodes.B(ins), c = LuaOpcodes.C(ins);

            switch (op)
            {
                case Op.GetGlobal:
                {
                    var name = Str(f, LuaOpcodes.Bx(ins));
                    Get(globals, name, file).Reads++;
                    Set(reg, a, name);
                    break;
                }
                case Op.SetGlobal:
                    Get(globals, Str(f, LuaOpcodes.Bx(ins)), file).Writes++;
                    break;

                case Op.Self:
                {
                    var name = LuaOpcodes.IsK(c) ? Str(f, LuaOpcodes.Indexk(c)) : null;
                    Set(reg, a, name is null ? null : ":" + name);
                    Set(reg, a + 1, null);
                    break;
                }

                case Op.GetTable:
                    if (LuaOpcodes.IsK(c)) Get(fields, Str(f, LuaOpcodes.Indexk(c)), file).Reads++;
                    Set(reg, a, null);
                    break;

                case Op.SetTable:
                    if (LuaOpcodes.IsK(b)) Get(fields, Str(f, LuaOpcodes.Indexk(b)), file).Writes++;
                    break;

                case Op.Call:
                case Op.TailCall:
                {
                    var name = a < reg.Length ? reg[a] : null;
                    if (name is not null)
                    {
                        // B==0 means "arguments run to the top of stack" (vararg call).
                        // For a method call the receiver occupies R(A+1), so it is not a user argument.
                        bool isMethod = name.StartsWith(':');
                        int nargs = b == 0 ? -1 : b - 1 - (isMethod ? 1 : 0);
                        var table = isMethod ? methods : globals;
                        var u = Get(table, isMethod ? name[1..] : name, file);
                        u.Calls++;
                        u.Arities.Add(nargs);
                    }
                    for (int r = a; r < reg.Length; r++) reg[r] = null;
                    break;
                }

                case Op.LoadNil:
                    for (int r = a; r <= b && r < reg.Length; r++) reg[r] = null;
                    break;

                case Op.Jmp:
                    Array.Clear(reg);
                    break;

                default:
                    Set(reg, a, null); // conservative: most opcodes write R(A)
                    break;
            }
        }
    }

    private static bool[] JumpTargets(LuaProto f)
    {
        var t = new bool[f.Code.Length + 1];
        for (int pc = 0; pc < f.Code.Length; pc++)
        {
            var op = LuaOpcodes.GetOp(f.Code[pc]);
            if (!LuaOpcodes.IsJump(op)) continue;
            int dest = pc + 1 + LuaOpcodes.SBx(f.Code[pc]);
            if (dest >= 0 && dest < t.Length) t[dest] = true;
        }
        return t;
    }

    private static void Set(string?[] reg, int i, string? v)
    {
        if (i >= 0 && i < reg.Length) reg[i] = v;
    }

    private static string Str(LuaProto f, int idx) =>
        idx >= 0 && idx < f.Constants.Length && f.Constants[idx].Kind == LuaConstKind.String
            ? f.Constants[idx].Str!
            : $"<k{idx}>";

    private static Usage Get(Dictionary<string, Usage> d, string name, string file)
    {
        if (!d.TryGetValue(name, out var u)) d[name] = u = new Usage();
        u.Files.Add(file);
        return u;
    }
}
