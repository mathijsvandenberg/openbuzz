using OpenBuzz.Cli.Lua;

namespace OpenBuzz.Cli;

/// <summary>
/// Determines empirically where the register/constant split sits in RK operands.
/// Released Lua 5.0 uses BITRK=256; earlier work versions used MAXSTACK=250. The
/// two are indistinguishable unless the corpus actually contains operands in
/// 250..255, so this counts them and checks each interpretation for impossible
/// values (a register beyond maxstacksize, or a constant beyond the constant table).
/// </summary>
public static class RkProbe
{
    public static int Run(string scriptRoot)
    {
        var files = Directory.GetFiles(scriptRoot, "*.clu", SearchOption.AllDirectories);
        if (files.Length == 0) { Console.Error.WriteLine($"No .clu files under {scriptRoot}."); return 1; }

        long total = 0, inGap = 0, above255 = 0;
        int maxStackSeen = 0, maxRegOperandSeen = 0;
        long bad250 = 0, bad256 = 0;
        var gapSamples = new List<string>();

        foreach (var path in files)
        {
            var root = LuaUndump.Load(File.ReadAllBytes(path), Path.GetFileName(path));
            foreach (var f in root.SelfAndDescendants())
            {
                maxStackSeen = Math.Max(maxStackSeen, f.MaxStackSize);
                foreach (var ins in f.Code)
                {
                    foreach (int v in RkOperands(ins))
                    {
                        total++;
                        if (v is >= 250 and <= 255)
                        {
                            inGap++;
                            if (gapSamples.Count < 6)
                                gapSamples.Add($"{Path.GetFileNameWithoutExtension(path)}: {LuaOpcodes.GetOp(ins)} operand={v} " +
                                               $"(slots={f.MaxStackSize}, consts={f.Constants.Length})");
                        }
                        else if (v >= 256) above255++;
                        else maxRegOperandSeen = Math.Max(maxRegOperandSeen, v);

                        bad250 += Impossible(f, v, 250) ? 1 : 0;
                        bad256 += Impossible(f, v, 256) ? 1 : 0;
                    }
                }
            }
        }

        Console.WriteLine($"RK operands examined : {total}");
        Console.WriteLine($"  value < 250        : {total - inGap - above255}   (max seen {maxRegOperandSeen})");
        Console.WriteLine($"  value 250..255     : {inGap}      <-- only possible under MAXSTACK=250");
        Console.WriteLine($"  value >= 256       : {above255}");
        Console.WriteLine($"largest maxstacksize : {maxStackSeen}");
        Console.WriteLine();
        Console.WriteLine($"impossible operands, threshold 250 : {bad250}");
        Console.WriteLine($"impossible operands, threshold 256 : {bad256}");
        foreach (var s in gapSamples) Console.WriteLine($"  sample: {s}");
        return 0;
    }

    /// An operand is impossible if, under the given split, it names a register
    /// the function never allocates or a constant the function does not have.
    private static bool Impossible(LuaProto f, int v, int threshold) =>
        v >= threshold ? v - threshold >= f.Constants.Length
                       : v >= f.MaxStackSize;

    private static IEnumerable<int> RkOperands(uint ins)
    {
        int b = LuaOpcodes.B(ins), c = LuaOpcodes.C(ins);
        switch (LuaOpcodes.GetOp(ins))
        {
            case Op.GetTable:
            case Op.Self:
                yield return c; break;
            case Op.SetTable:
            case Op.Add: case Op.Sub: case Op.Mul: case Op.Div: case Op.Pow:
            case Op.Eq: case Op.Lt: case Op.Le:
                yield return b; yield return c; break;
        }
    }
}
