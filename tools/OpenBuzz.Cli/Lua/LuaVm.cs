namespace OpenBuzz.Cli.Lua;

/// <summary>
/// A Lua 5.0 interpreter for the disc's own bytecode.
///
/// The point of running the scripts rather than reimplementing them is that
/// the scripts are the design. Every time this port has hand-written a round,
/// something had to be guessed that the bytecode already states exactly - the
/// round order, the point tiers, the reveal rate. Executing them removes that
/// whole class of error.
///
/// Two things make this a 5.0 VM and not a 5.1 one, and both are already
/// settled in <see cref="LuaOpcodes"/>: the iABC field order is OP C B A, and
/// an RK operand counts as a constant above 250 rather than 256. A stock 5.1
/// VM decodes this bytecode into nonsense.
///
/// Anything unimplemented throws rather than being skipped. A quiz that runs
/// but silently drops an opcode is worse than one that stops and says which.
/// </summary>
public sealed class LuaVm
{
    public LuaTable Globals { get; } = new();

    /// Called for every native invocation, so a run can be traced.
    public Action<string, object?[], object?[]>? OnNativeCall;

    /// Called when a script asks for a global that has no value.
    public Action<string>? OnMissingGlobal;

    private readonly HashSet<string> _reportedMissing = new(StringComparer.Ordinal);

    public void Register(string name, Func<object?[], object?[]> body) =>
        Globals.Set(name, new LuaNative(name, body));

    /// Registers a native that returns nothing, which most of them do.
    public void RegisterVoid(string name, Action<object?[]> body) =>
        Register(name, args => { body(args); return []; });

    public object?[] Call(object? fn, params object?[] args)
    {
        switch (fn)
        {
            case LuaNative native:
            {
                var result = native.Body(args) ?? [];
                OnNativeCall?.Invoke(native.Name, args, result);
                return result;
            }

            case LuaClosure closure:
                return Execute(closure, args);

            default:
                throw new LuaError($"attempt to call a {LuaValues.TypeName(fn)} value");
        }
    }

    /// Loads a compiled chunk as a closure with no upvalues.
    public LuaClosure Load(LuaProto proto) => new(proto, []);

    // ------------------------------------------------------------------ core

    private object?[] Execute(LuaClosure closure, object?[] args)
    {
        var f = closure.Proto;
        var reg = new object?[Math.Max(f.MaxStackSize, (byte)2) + 8];

        for (int i = 0; i < f.NumParams && i < args.Length; i++) reg[i] = args[i];

        // Cells for locals that a nested closure captures. Created on demand so
        // the common case costs nothing.
        var cells = new LuaCell?[reg.Length];
        LuaCell CellFor(int r) => cells[r] ??= new LuaCell { Value = reg[r] };

        void SetReg(int r, object? v)
        {
            reg[r] = v;
            if (cells[r] is { } c) c.Value = v;
        }

        object? GetReg(int r) => cells[r] is { } c ? c.Value : reg[r];

        // Where the last open-ended call left its results, for B==0 / C==0.
        int top = f.NumParams;

        object? RK(int x) => LuaOpcodes.IsK(x)
            ? ConstOf(f, LuaOpcodes.Indexk(x))
            : GetReg(x);

        for (int pc = 0; pc < f.Code.Length; pc++)
        {
            uint ins = f.Code[pc];
            var op = LuaOpcodes.GetOp(ins);
            int a = LuaOpcodes.A(ins), b = LuaOpcodes.B(ins), c = LuaOpcodes.C(ins);

            switch (op)
            {
                case Op.Move: SetReg(a, GetReg(b)); break;
                case Op.LoadK: SetReg(a, ConstOf(f, LuaOpcodes.Bx(ins))); break;

                case Op.LoadBool:
                    SetReg(a, b != 0);
                    if (c != 0) pc++;
                    break;

                case Op.LoadNil:
                    for (int r = a; r <= b; r++) SetReg(r, null);
                    break;

                case Op.GetUpval: SetReg(a, closure.Upvalues[b].Value); break;
                case Op.SetUpval: closure.Upvalues[b].Value = GetReg(a); break;

                case Op.GetGlobal:
                {
                    var name = StringConst(f, LuaOpcodes.Bx(ins));
                    var v = Globals.Get(name);
                    if (v is null && _reportedMissing.Add(name))
                    {
                        OnMissingGlobal?.Invoke(name);
                        // The handler is allowed to supply the global - that is
                        // how the host registers a native on first use - so read
                        // it again rather than handing the script the nil.
                        v = Globals.Get(name);
                    }
                    SetReg(a, v);
                    break;
                }

                case Op.SetGlobal:
                    Globals.Set(StringConst(f, LuaOpcodes.Bx(ins)), GetReg(a));
                    break;

                case Op.GetTable: SetReg(a, Index(GetReg(b), RK(c))); break;
                case Op.SetTable: NewIndex(GetReg(a), RK(b), RK(c)); break;

                case Op.NewTable: SetReg(a, new LuaTable()); break;

                case Op.Self:
                {
                    var obj = GetReg(b);
                    SetReg(a + 1, obj);
                    SetReg(a, Index(obj, RK(c)));
                    break;
                }

                case Op.Add: SetReg(a, LuaValues.ToNumber(RK(b)) + LuaValues.ToNumber(RK(c))); break;
                case Op.Sub: SetReg(a, LuaValues.ToNumber(RK(b)) - LuaValues.ToNumber(RK(c))); break;
                case Op.Mul: SetReg(a, LuaValues.ToNumber(RK(b)) * LuaValues.ToNumber(RK(c))); break;
                case Op.Div: SetReg(a, LuaValues.ToNumber(RK(b)) / LuaValues.ToNumber(RK(c))); break;
                case Op.Pow: SetReg(a, Math.Pow(LuaValues.ToNumber(RK(b)), LuaValues.ToNumber(RK(c)))); break;
                case Op.Unm: SetReg(a, -LuaValues.ToNumber(GetReg(b))); break;
                case Op.Not: SetReg(a, !LuaValues.Truthy(GetReg(b))); break;

                case Op.Concat:
                {
                    var sb = new System.Text.StringBuilder();
                    for (int r = b; r <= c; r++) sb.Append(LuaValues.ToStringValue(GetReg(r)));
                    SetReg(a, sb.ToString());
                    break;
                }

                case Op.Jmp: pc += LuaOpcodes.SBx(ins); break;

                // A comparison skips the following JMP when it does not match A.
                case Op.Eq:
                    if (Equals(RK(b), RK(c)) != (a != 0)) pc++;
                    break;
                case Op.Lt:
                    if (Less(RK(b), RK(c)) != (a != 0)) pc++;
                    break;
                case Op.Le:
                    if (LessEqual(RK(b), RK(c)) != (a != 0)) pc++;
                    break;

                case Op.Test:
                    // 5.0: if truthiness of R(B) matches C, R(A) := R(B); else skip.
                    if (LuaValues.Truthy(GetReg(b)) == (c != 0)) SetReg(a, GetReg(b));
                    else pc++;
                    break;

                case Op.Call:
                {
                    int argc = b == 0 ? top - a - 1 : b - 1;
                    var callArgs = new object?[argc];
                    for (int i = 0; i < argc; i++) callArgs[i] = GetReg(a + 1 + i);

                    var results = Call(GetReg(a), callArgs);

                    int want = c - 1;
                    if (want < 0)
                    {
                        for (int i = 0; i < results.Length; i++) SetReg(a + i, results[i]);
                        top = a + results.Length;
                    }
                    else
                    {
                        for (int i = 0; i < want; i++)
                            SetReg(a + i, i < results.Length ? results[i] : null);
                    }
                    break;
                }

                case Op.TailCall:
                {
                    int argc = b == 0 ? top - a - 1 : b - 1;
                    var callArgs = new object?[argc];
                    for (int i = 0; i < argc; i++) callArgs[i] = GetReg(a + 1 + i);
                    return Call(GetReg(a), callArgs);
                }

                case Op.Return:
                {
                    int count = b == 0 ? top - a : b - 1;
                    var results = new object?[Math.Max(count, 0)];
                    for (int i = 0; i < results.Length; i++) results[i] = GetReg(a + i);
                    return results;
                }

                case Op.ForLoop:
                {
                    double step = LuaValues.ToNumber(GetReg(a + 2));
                    double idx = LuaValues.ToNumber(GetReg(a)) + step;
                    double limit = LuaValues.ToNumber(GetReg(a + 1));
                    if (step > 0 ? idx <= limit : idx >= limit)
                    {
                        SetReg(a, idx);
                        SetReg(a + 3, idx);
                        pc += LuaOpcodes.SBx(ins);
                    }
                    break;
                }

                case Op.TForPrep:
                    // 5.0 turns `for k,v in t` into the next() form before looping.
                    if (GetReg(a) is LuaTable)
                    {
                        SetReg(a + 1, GetReg(a));
                        SetReg(a, Globals.Get("next"));
                    }
                    pc += LuaOpcodes.SBx(ins);
                    break;

                case Op.TForLoop:
                {
                    var iter = GetReg(a);
                    var state = GetReg(a + 1);
                    var control = GetReg(a + 2);
                    var results = Call(iter, state, control);

                    for (int i = 0; i < c; i++)
                        SetReg(a + 3 + i, i < results.Length ? results[i] : null);

                    if (GetReg(a + 3) is null) pc++;
                    else SetReg(a + 2, GetReg(a + 3));
                    break;
                }

                case Op.SetList:
                case Op.SetListO:
                {
                    var table = GetReg(a) as LuaTable
                        ?? throw new LuaError("SETLIST on a non-table");
                    int batch = op == Op.SetList ? LuaOpcodes.Bx(ins) : LuaOpcodes.Bx(ins);
                    int start = batch / 50 * 50;
                    int last = op == Op.SetListO ? top - a - 1 : batch % 50 + 1;
                    for (int i = 1; i <= last; i++)
                        table.Set((double)(start + i), GetReg(a + i));
                    break;
                }

                case Op.Close:
                    // Locals leaving scope keep their own cell; nothing to do,
                    // because a cell is created per register and never reused
                    // across a closure boundary in these scripts.
                    break;

                case Op.Closure:
                {
                    var proto = f.Protos[LuaOpcodes.Bx(ins)];
                    var ups = new LuaCell[proto.Nups];
                    for (int i = 0; i < proto.Nups; i++)
                    {
                        uint pseudo = f.Code[++pc];
                        ups[i] = LuaOpcodes.GetOp(pseudo) == Op.Move
                            ? CellFor(LuaOpcodes.B(pseudo))
                            : closure.Upvalues[LuaOpcodes.B(pseudo)];
                    }
                    SetReg(a, new LuaClosure(proto, ups));
                    break;
                }

                default:
                    throw new LuaError($"unimplemented opcode {op} at pc {pc} of {f.Source}");
            }
        }

        return [];
    }

    // -------------------------------------------------------------- helpers

    private static object? ConstOf(LuaProto f, int i)
    {
        if (i < 0 || i >= f.Constants.Length) return null;
        var k = f.Constants[i];
        return k.Kind switch
        {
            LuaConstKind.String => k.Str,
            LuaConstKind.Number => k.Number,
            _ => null,
        };
    }

    private static string StringConst(LuaProto f, int i) =>
        ConstOf(f, i) as string ?? $"<k{i}>";

    private static object? Index(object? obj, object? key) => obj switch
    {
        LuaTable t => t.Get(key),
        null => throw new LuaError($"attempt to index a nil value (key '{LuaValues.ToStringValue(key)}')"),
        _ => throw new LuaError($"attempt to index a {LuaValues.TypeName(obj)} value"),
    };

    private static void NewIndex(object? obj, object? key, object? value)
    {
        if (obj is LuaTable t) t.Set(key, value);
        else throw new LuaError($"attempt to index a {LuaValues.TypeName(obj)} value");
    }

    private static bool Less(object? x, object? y) => x is string a && y is string b
        ? string.CompareOrdinal(a, b) < 0
        : LuaValues.ToNumber(x) < LuaValues.ToNumber(y);

    private static bool LessEqual(object? x, object? y) => x is string a && y is string b
        ? string.CompareOrdinal(a, b) <= 0
        : LuaValues.ToNumber(x) <= LuaValues.ToNumber(y);
}
