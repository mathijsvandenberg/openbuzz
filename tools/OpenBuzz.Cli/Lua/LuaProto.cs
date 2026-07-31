namespace OpenBuzz.Cli.Lua;

public enum LuaConstKind { Nil, Number, String }

public readonly record struct LuaConstant(LuaConstKind Kind, double Number, string? Str)
{
    public static LuaConstant Nil() => new(LuaConstKind.Nil, 0, null);
    public static LuaConstant Num(double d) => new(LuaConstKind.Number, d, null);
    public static LuaConstant Text(string s) => new(LuaConstKind.String, 0, s);

    public override string ToString() => Kind switch
    {
        LuaConstKind.Nil => "nil",
        LuaConstKind.Number => Number == Math.Floor(Number) && Math.Abs(Number) < 1e15
            ? ((long)Number).ToString()
            : Number.ToString("R"),
        _ => "\"" + Escape(Str ?? "") + "\"",
    };

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
}

public readonly record struct LuaLocal(string Name, int StartPc, int EndPc);

public sealed class LuaProto
{
    public string Source = "";
    public int LineDefined;
    public byte Nups, NumParams, IsVararg, MaxStackSize;
    public int[] Lines = [];
    public LuaLocal[] Locals = [];
    public string[] Upvalues = [];
    public LuaConstant[] Constants = [];
    public LuaProto[] Protos = [];
    public uint[] Code = [];

    public IEnumerable<LuaProto> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Protos)
            foreach (var d in child.SelfAndDescendants())
                yield return d;
    }
}
