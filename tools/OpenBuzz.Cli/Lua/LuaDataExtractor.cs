using System.Globalization;

namespace OpenBuzz.Cli.Lua;

/// A recovered call: a global function name and its constant arguments.
public sealed record LuaCall(string Function, IReadOnlyList<object?> Args)
{
    public double? Number(int i) => i < Args.Count && Args[i] is double d ? d : null;
    public string? Text(int i) => i < Args.Count ? Args[i] as string : null;

    public override string ToString() =>
        $"{Function}({string.Join(", ", Args.Select(Format))})";

    private static string Format(object? a) => a switch
    {
        null => "nil",
        string s => "\"" + s + "\"",
        double d => d == Math.Floor(d) && Math.Abs(d) < 1e15
            ? ((long)d).ToString(CultureInfo.InvariantCulture)
            : d.ToString("0.######", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        _ => a.ToString() ?? "?",
    };
}

/// <summary>
/// Statically evaluates the straight-line call sequences that make up the A2D
/// scripts, which use Lua purely as a data format: load constants into
/// registers, call a global, repeat.
///
/// This is not an interpreter. It follows constant loads and register moves and
/// gives up on anything computed, marking those arguments unknown rather than
/// guessing — a data file that needed real evaluation would show up as nulls
/// instead of silently wrong numbers.
/// </summary>
public static class LuaDataExtractor
{
    /// Sentinel for a register whose value we could not determine.
    private sealed record Unknown;

    /// A global that has been loaded but not yet called.
    private sealed record GlobalRef(string Name);

    public static List<LuaCall> Extract(LuaProto root)
    {
        var calls = new List<LuaCall>();
        foreach (var proto in root.SelfAndDescendants()) ExtractProto(proto, calls);
        return calls;
    }

    private static void ExtractProto(LuaProto f, List<LuaCall> calls)
    {
        var reg = new object?[Math.Max((int)f.MaxStackSize, 2) + 8];
        var isTarget = JumpTargets(f);

        for (int pc = 0; pc < f.Code.Length; pc++)
        {
            // Control flow merges invalidate the shadow register file.
            if (isTarget[pc]) Array.Fill(reg, new Unknown());

            uint ins = f.Code[pc];
            var op = LuaOpcodes.GetOp(ins);
            int a = LuaOpcodes.A(ins), b = LuaOpcodes.B(ins), c = LuaOpcodes.C(ins);

            switch (op)
            {
                case Op.GetGlobal:
                    Set(reg, a, new GlobalRef(ConstString(f, LuaOpcodes.Bx(ins))));
                    break;

                case Op.LoadK:
                    Set(reg, a, ConstValue(f, LuaOpcodes.Bx(ins)));
                    break;

                case Op.LoadNil:
                    for (int r = a; r <= b && r < reg.Length; r++) reg[r] = null;
                    break;

                case Op.LoadBool:
                    Set(reg, a, b != 0);
                    if (c != 0) pc++;
                    break;

                case Op.Move:
                    Set(reg, a, b < reg.Length ? reg[b] : new Unknown());
                    break;

                case Op.Call:
                case Op.TailCall:
                {
                    if (a < reg.Length && reg[a] is GlobalRef g)
                    {
                        int argc = b == 0 ? 0 : b - 1;   // vararg calls carry no static args
                        var args = new List<object?>(argc);
                        for (int i = 1; i <= argc; i++)
                        {
                            object? v = a + i < reg.Length ? reg[a + i] : new Unknown();
                            args.Add(v is Unknown ? null : v);
                        }
                        calls.Add(new LuaCall(g.Name, args));
                    }
                    for (int r = a; r < reg.Length; r++) reg[r] = new Unknown();
                    break;
                }

                default:
                    Set(reg, a, new Unknown());
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

    private static void Set(object?[] reg, int i, object? v)
    {
        if (i >= 0 && i < reg.Length) reg[i] = v;
    }

    private static string ConstString(LuaProto f, int idx) =>
        idx >= 0 && idx < f.Constants.Length && f.Constants[idx].Kind == LuaConstKind.String
            ? f.Constants[idx].Str!
            : $"<k{idx}>";

    private static object? ConstValue(LuaProto f, int idx)
    {
        if (idx < 0 || idx >= f.Constants.Length) return null;
        var k = f.Constants[idx];
        return k.Kind switch
        {
            LuaConstKind.Number => k.Number,
            LuaConstKind.String => k.Str,
            _ => null,
        };
    }
}
