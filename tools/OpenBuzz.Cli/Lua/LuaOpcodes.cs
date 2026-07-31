namespace OpenBuzz.Cli.Lua;

/// Opcode set and instruction layout for Lua 5.0 (lopcodes.h), which is what the
/// disc's .clu chunks are compiled against.
public enum Op
{
    Move, LoadK, LoadBool, LoadNil, GetUpval,
    GetGlobal, GetTable,
    SetGlobal, SetUpval, SetTable,
    NewTable,
    Self,
    Add, Sub, Mul, Div, Pow, Unm, Not,
    Concat,
    Jmp,
    Eq, Lt, Le,
    Test,
    Call, TailCall, Return,
    ForLoop, TForLoop, TForPrep,
    SetList, SetListO,
    Close, Closure,
}

public enum OpMode { ABC, ABx, AsBx }

public static class LuaOpcodes
{
    // Lua 5.0 packs iABC as OP(0..5) C(6..14) B(15..23) A(24..31); Bx overlays C+B.
    // (Lua 5.1 later reordered this to OP A C B — do not confuse the two.)
    public const int MaxArgSBx = (1 << 18) / 2 - 1;

    /// <summary>
    /// Threshold above which an RK operand denotes a constant rather than a register.
    /// Released Lua 5.0 uses BITRK (256); this build uses the older MAXSTACK-relative
    /// encoding (250), confirmed by decoding the whole corpus both ways — see
    /// docs/lua-format.md.
    /// </summary>
    public static int RkThreshold { get; set; } = 250;

    public static Op GetOp(uint i) => (Op)(i & 0x3F);
    public static int C(uint i) => (int)((i >> 6) & 0x1FF);
    public static int B(uint i) => (int)((i >> 15) & 0x1FF);
    public static int A(uint i) => (int)((i >> 24) & 0xFF);
    public static int Bx(uint i) => (int)((i >> 6) & 0x3FFFF);
    public static int SBx(uint i) => Bx(i) - MaxArgSBx;

    public static bool IsK(int x) => x >= RkThreshold;
    public static int Indexk(int x) => x - RkThreshold;

    public static OpMode Mode(Op op) => op switch
    {
        Op.LoadK or Op.GetGlobal or Op.SetGlobal or Op.Closure => OpMode.ABx,
        Op.Jmp or Op.ForLoop or Op.TForPrep => OpMode.AsBx,
        _ => OpMode.ABC,
    };

    /// True when the instruction is a branch whose target we need for the
    /// conservative register-tracking reset in <see cref="ApiScanner"/>.
    public static bool IsJump(Op op) => op is Op.Jmp or Op.ForLoop or Op.TForPrep;
}
