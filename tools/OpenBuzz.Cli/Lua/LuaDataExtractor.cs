using System.Globalization;

namespace OpenBuzz.Cli.Lua;

/// A global the script named: loaded into a register, and either called or
/// passed on as a value. Round ids travel this way.
public sealed record LuaGlobal(string Name)
{
    public override string ToString() => Name;
}

/// A table built inline, as `{a, b, c}` is: the constants a SetList gathered.
public sealed record LuaList(IReadOnlyList<object?> Items)
{
    public override string ToString() => "{" + string.Join(", ", Items) + "}";
}

/// <summary>
/// A value computed from a constant and something unknown, kept as the
/// expression rather than thrown away.
///
/// The speech ids need this. A per-contestant line is not written out four
/// times; the script computes it, as in `530200 + seat - 1`. Discarding that
/// would hide four lines per round that the disc plainly has.
/// </summary>
public sealed record LuaExpr(string Text, double Constant)
{
    public override string ToString() => Text;
}

/// A recovered call: a global function name and its constant arguments.
public sealed record LuaCall(string Function, IReadOnlyList<object?> Args)
{
    public double? Number(int i) => i < Args.Count && Args[i] is double d ? d : null;
    public string? Text(int i) => i < Args.Count ? Args[i] as string : null;
    public LuaList? List(int i) => i < Args.Count ? Args[i] as LuaList : null;
    public LuaExpr? Expr(int i) => i < Args.Count ? Args[i] as LuaExpr : null;
    public string? Global(int i) => i < Args.Count ? (Args[i] as LuaGlobal)?.Name : null;

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
/// guessing - a data file that needed real evaluation would show up as nulls
/// instead of silently wrong numbers.
/// </summary>
public static class LuaDataExtractor
{
    /// Sentinel for a register whose value we could not determine.
    private sealed record Unknown;

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

        // How many array slots the NewTable in a register asked for, so the
        // SetList that fills it knows how far up the stack to read.
        var pendingSize = new Dictionary<int, int>();

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
                    Set(reg, a, new LuaGlobal(ConstString(f, LuaOpcodes.Bx(ins))));
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

                case Op.NewTable:
                    pendingSize[a] = b;
                    Set(reg, a, new Unknown());
                    break;

                case Op.SetList:
                case Op.SetListO:
                {
                    // The elements sit in the registers just above the table.
                    int n = pendingSize.GetValueOrDefault(a);
                    var items = new List<object?>(n);
                    for (int i = 1; i <= n; i++)
                        items.Add(a + i < reg.Length && reg[a + i] is not Unknown ? reg[a + i] : null);
                    if (n > 0) Set(reg, a, new LuaList(items));
                    break;
                }

                case Op.Add:
                case Op.Sub:
                {
                    // Only the shape that matters here: a constant and
                    // something the script works out at run time. Anything
                    // else stays unknown rather than being half-guessed.
                    var left = Rk(f, reg, b);
                    var right = Rk(f, reg, c);
                    Set(reg, a, Arith(left, right, op == Op.Add) ?? new Unknown());
                    break;
                }

                case Op.Call:
                case Op.TailCall:
                {
                    if (a < reg.Length && reg[a] is LuaGlobal g)
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

    /// A register or a constant, the way the arithmetic opcodes address them.
    private static object? Rk(LuaProto f, object?[] reg, int x) =>
        LuaOpcodes.IsK(x) ? ConstValue(f, LuaOpcodes.Indexk(x))
                          : (x < reg.Length ? reg[x] : new Unknown());

    /// <summary>
    /// Folds one arithmetic step. Two constants collapse to a number; a
    /// constant against a run-time value carries on as an expression, which is
    /// how the per-seat speech ids survive; anything else is unknown.
    /// </summary>
    private static object? Arith(object? left, object? right, bool add)
    {
        double sign = add ? 1 : -1;

        if (left is double x && right is double y) return x + sign * y;

        if (left is double c && right is not double)
            return Term(right) is { } t ? new LuaExpr($"{Num(c)} {(add ? "+" : "-")} {t}", c) : null;

        if (right is double k && left is not double)
            return Term(left) is { } t
                ? new LuaExpr($"{t} {(add ? "+" : "-")} {Num(k)}",
                              (left as LuaExpr)?.Constant + sign * k ?? sign * k)
                : null;

        return null;
    }

    /// The readable form of a non-constant operand, or nothing if it is opaque.
    private static string? Term(object? v) => v switch
    {
        LuaExpr e => e.Text,
        // The seat index is usually a named global, and saying which one is
        // worth more than calling it n.
        LuaGlobal g => g.Name,
        Unknown => "n",
        null => "n",
        _ => null,
    };

    private static string Num(double d) =>
        d == Math.Floor(d) && Math.Abs(d) < 1e15
            ? ((long)d).ToString(CultureInfo.InvariantCulture)
            : d.ToString("0.######", CultureInfo.InvariantCulture);

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
