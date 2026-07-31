using System.Text;

namespace OpenBuzz.Cli.Lua;

/// Renders a prototype tree as readable assembly, resolving constant indices so
/// global names and string literals appear inline.
public static class LuaDisassembler
{
    public static string Disassemble(LuaProto root, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"; {title}");
        sb.AppendLine($"; source={root.Source} debug={(root.Lines.Length > 0 ? "present" : "stripped")}");
        sb.AppendLine();
        Emit(sb, root, "main", 0);
        return sb.ToString();
    }

    private static void Emit(StringBuilder sb, LuaProto f, string name, int depth)
    {
        var pad = new string(' ', depth * 2);
        sb.AppendLine($"{pad}function {name}  params={f.NumParams} vararg={f.IsVararg} slots={f.MaxStackSize} " +
                      $"upvals={f.Nups} consts={f.Constants.Length} protos={f.Protos.Length} code={f.Code.Length}");

        for (int i = 0; i < f.Constants.Length; i++)
            sb.AppendLine($"{pad}  .const {i,-4} {f.Constants[i]}");

        for (int pc = 0; pc < f.Code.Length; pc++)
        {
            uint ins = f.Code[pc];
            var op = LuaOpcodes.GetOp(ins);
            int a = LuaOpcodes.A(ins), b = LuaOpcodes.B(ins), c = LuaOpcodes.C(ins);
            string args = LuaOpcodes.Mode(op) switch
            {
                OpMode.ABx => $"{a} {LuaOpcodes.Bx(ins)}",
                OpMode.AsBx => $"{a} {LuaOpcodes.SBx(ins):+#;-#;0}",
                _ => $"{a} {b} {c}",
            };
            sb.Append($"{pad}  [{pc,4}] {ins:X8} {op,-9} {args,-14}");

            var note = Annotate(f, op, ins, pc);
            if (note.Length > 0) sb.Append("; ").Append(note);
            sb.AppendLine();
        }

        sb.AppendLine();
        for (int i = 0; i < f.Protos.Length; i++)
            Emit(sb, f.Protos[i], $"{name}/{i}", depth + 1);
    }

    private static string Annotate(LuaProto f, Op op, uint ins, int pc)
    {
        int a = LuaOpcodes.A(ins), b = LuaOpcodes.B(ins), c = LuaOpcodes.C(ins);
        switch (op)
        {
            case Op.GetGlobal: return $"R{a} := _G[{K(f, LuaOpcodes.Bx(ins))}]";
            case Op.SetGlobal: return $"_G[{K(f, LuaOpcodes.Bx(ins))}] := R{a}";
            case Op.LoadK: return $"R{a} := {K(f, LuaOpcodes.Bx(ins))}";
            case Op.GetTable: return $"R{a} := R{b}[{Rk(f, c)}]";
            case Op.SetTable: return $"R{a}[{Rk(f, b)}] := {Rk(f, c)}";
            case Op.Self: return $"R{a} := R{b}[{Rk(f, c)}]  (method call on R{b})";
            case Op.Call: return $"{Results(a, c)}R{a}({Args(a, b)})";
            case Op.TailCall: return $"return R{a}({Args(a, b)})";
            case Op.Closure: return $"R{a} := closure #{LuaOpcodes.Bx(ins)}";
            case Op.Jmp: return $"-> [{pc + 1 + LuaOpcodes.SBx(ins)}]";
            case Op.ForLoop: case Op.TForPrep: return $"-> [{pc + 1 + LuaOpcodes.SBx(ins)}]";
            default: return "";
        }
    }

    private static string Results(int a, int c) =>
        c == 0 ? $"R{a}.. := " : c == 1 ? "" : $"R{a}..R{a + c - 2} := ";

    private static string Args(int a, int b) =>
        b == 0 ? $"R{a + 1}.." : b == 1 ? "" : $"R{a + 1}..R{a + b - 1}";

    private static string K(LuaProto f, int idx) =>
        idx >= 0 && idx < f.Constants.Length ? f.Constants[idx].ToString() : $"K?{idx}";

    private static string Rk(LuaProto f, int x) =>
        LuaOpcodes.IsK(x) ? K(f, LuaOpcodes.Indexk(x)) : $"R{x}";
}
