namespace OpenBuzz.Cli.Lua;

/// <summary>
/// Recovers the constant tables and globals a data script assigns.
///
/// `GenericData.luaasm` is where the game keeps its screen layout, and it is
/// straight-line assignment, not logic:
///
///     _G["QuestionTextWidth"] = 430
///     RoundParameters[DefaultsID]["AnswerPositionYStart"] = 92
///
/// so the whole 640x480 layout - where the question sits, how far apart the
/// answers are, where the contestant blocks start and how wide they are - is
/// recoverable exactly rather than measured off a screenshot.
///
/// Like <see cref="LuaDataExtractor"/> this follows constant loads and register
/// moves and gives up on anything computed, so a value that was calculated
/// comes back missing rather than silently wrong.
/// </summary>
public static class LuaTableExtractor
{
    private sealed record Unknown;
    private sealed record GlobalRef(string Name);

    /// A table reached as `_G[Owner][Key]`, which is how RoundParameters is
    /// written: the round's id global picks the sub-table.
    private sealed record TableRef(string Owner, string Key);

    public sealed class Result
    {
        /// Globals assigned a constant, by name.
        public Dictionary<string, object?> Globals { get; } = [];

        /// `RoundParameters[<id>]` and friends: outer key, then field.
        public Dictionary<string, Dictionary<string, object?>> Tables { get; } = [];
    }

    public static Result Extract(LuaProto root)
    {
        var result = new Result();
        foreach (var proto in root.SelfAndDescendants()) Walk(proto, result);
        return result;
    }

    private static void Walk(LuaProto f, Result result)
    {
        var reg = new object?[Math.Max((int)f.MaxStackSize, 2) + 8];
        var isTarget = JumpTargets(f);

        for (int pc = 0; pc < f.Code.Length; pc++)
        {
            if (isTarget[pc]) Array.Fill(reg, new Unknown());

            uint ins = f.Code[pc];
            var op = LuaOpcodes.GetOp(ins);
            int a = LuaOpcodes.A(ins), b = LuaOpcodes.B(ins), c = LuaOpcodes.C(ins);

            switch (op)
            {
                case Op.GetGlobal:
                    Set(reg, a, new GlobalRef(ConstString(f, LuaOpcodes.Bx(ins))));
                    break;

                case Op.SetGlobal:
                {
                    var v = a < reg.Length ? reg[a] : null;
                    if (v is not Unknown)
                        result.Globals[ConstString(f, LuaOpcodes.Bx(ins))] = Now(result, v);
                    break;
                }

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

                case Op.GetTable:
                {
                    // R(A) := R(B)[RK(C)]. Only the shape `_G[name][id]` is
                    // followed, which is the one RoundParameters uses.
                    object? table = b < reg.Length ? reg[b] : null;
                    object? key = RK(f, reg, c);
                    Set(reg, a, table is GlobalRef owner && key is GlobalRef id
                        ? new TableRef(owner.Name, id.Name)
                        : new Unknown());
                    break;
                }

                case Op.SetTable:
                {
                    // R(A)[RK(B)] := RK(C)
                    object? target = a < reg.Length ? reg[a] : null;
                    object? key = RK(f, reg, b);
                    object? value = RK(f, reg, c);
                    if (target is TableRef t && key is string field && value is not Unknown)
                    {
                        if (!result.Tables.TryGetValue(t.Key, out var fields))
                            result.Tables[t.Key] = fields = [];
                        fields[field] = Now(result, value);
                    }
                    break;
                }

                default:
                    Set(reg, a, new Unknown());
                    break;
            }
        }
    }

    /// <summary>
    /// A reference to a global takes that global's value as it stands right
    /// now, not at the end of the script.
    ///
    /// This matters: the script is one long sequence of assignments and it
    /// reuses names, so resolving afterwards gave a field whatever its name
    /// last happened to mean. QuestionTextPositionX came out as 67 that way
    /// when the script had actually set it to 142 and moved on.
    /// </summary>
    private static object? Now(Result result, object? v, int depth = 0)
    {
        if (depth > 8 || v is not GlobalRef g) return v is Unknown ? null : v;
        return result.Globals.TryGetValue(g.Name, out var inner)
            ? Now(result, inner, depth + 1)
            : null;
    }

    /// This build encodes RK against a threshold of 250, not the 256 released
    /// Lua 5.0 uses - LuaOpcodes carries that finding. Hardcoding 256 here read
    /// every constant six slots off, which is how QuestionTextPositionX came
    /// back as 67 and an icon offset came back as the string "ExtraLarge".
    private static object? RK(LuaProto f, object?[] reg, int x) =>
        LuaOpcodes.IsK(x) ? ConstValue(f, LuaOpcodes.Indexk(x))
                          : (x < reg.Length ? reg[x] : new Unknown());

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
            LuaConstKind.String => k.Str,
            LuaConstKind.Number => k.Number,
            _ => null,
        };
    }
}
