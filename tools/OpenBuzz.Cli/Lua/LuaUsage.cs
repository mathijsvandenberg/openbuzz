namespace OpenBuzz.Cli.Lua;

/// Which globals a chunk writes, reads and calls.
public sealed class LuaUsage
{
    public HashSet<string> Set { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Read { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Called { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// A global loaded and then called is a call; loaded and used any other way
    /// is a read. Keeping those apart is what makes "assigned and never read"
    /// mean anything, because defining a function is an assignment too.
    /// </summary>
    public static LuaUsage Scan(LuaProto root)
    {
        var usage = new LuaUsage();
        foreach (var f in root.SelfAndDescendants())
        {
            var loaded = new string?[Math.Max((int)f.MaxStackSize, 2) + 8];

            for (int pc = 0; pc < f.Code.Length; pc++)
            {
                uint ins = f.Code[pc];
                var op = LuaOpcodes.GetOp(ins);
                int a = LuaOpcodes.A(ins);

                switch (op)
                {
                    case Op.GetGlobal:
                    {
                        var name = Const(f, LuaOpcodes.Bx(ins));
                        if (a < loaded.Length) loaded[a] = name;
                        if (name is not null) usage.Read.Add(name);
                        break;
                    }

                    case Op.SetGlobal:
                    {
                        var name = Const(f, LuaOpcodes.Bx(ins));
                        if (name is not null) usage.Set.Add(name);
                        break;
                    }

                    case Op.Call:
                    case Op.TailCall:
                    {
                        if (a < loaded.Length && loaded[a] is string fn) usage.Called.Add(fn);
                        for (int r = a; r < loaded.Length; r++) loaded[r] = null;
                        break;
                    }

                    default:
                        if (a >= 0 && a < loaded.Length) loaded[a] = null;
                        break;
                }
            }
        }

        // A name called anywhere counts as called rather than read.
        usage.Read.ExceptWith(usage.Called);
        return usage;
    }

    private static string? Const(LuaProto f, int idx) =>
        idx >= 0 && idx < f.Constants.Length && f.Constants[idx].Kind == LuaConstKind.String
            ? f.Constants[idx].Str
            : null;
}
