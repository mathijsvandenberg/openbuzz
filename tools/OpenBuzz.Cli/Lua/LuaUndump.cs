using System.Text;

namespace OpenBuzz.Cli.Lua;

/// <summary>
/// Reader for Lua 5.0 precompiled chunks (lundump.c). The PS2 build uses
/// little-endian, 4-byte int/size_t/Instruction and a 4-byte float lua_Number,
/// which is unusual enough that generic Lua tooling often chokes on it - hence
/// this reader honours whatever the chunk header declares.
/// </summary>
public sealed class LuaUndump
{
    private readonly byte[] _d;
    private int _p;

    private int _sizeInt = 4, _sizeSizeT = 4, _sizeInstruction = 4, _sizeNumber = 8;

    public string SourceName { get; private set; } = "";

    private LuaUndump(byte[] data) => _d = data;

    public static bool LooksLikeLua50(byte[] d) =>
        d.Length > 12 && d[0] == 0x1B && d[1] == (byte)'L' && d[2] == (byte)'u' && d[3] == (byte)'a' && d[4] == 0x50;

    public static LuaProto Load(byte[] data, string displayName)
    {
        var s = new LuaUndump(data);
        s.ReadHeader();
        var proto = s.ReadFunction(displayName);
        return proto;
    }

    private void ReadHeader()
    {
        if (!LooksLikeLua50(_d))
            throw new InvalidDataException("Not a Lua 5.0 precompiled chunk.");
        _p = 5; // signature + version

        byte endian = U8();
        if (endian != 1) throw new NotSupportedException("Big-endian chunks are not supported.");

        _sizeInt = U8();
        _sizeSizeT = U8();
        _sizeInstruction = U8();

        int sizeOp = U8(), sizeA = U8(), sizeB = U8(), sizeC = U8();
        if (sizeOp != 6 || sizeA != 8 || sizeB != 9 || sizeC != 9)
            throw new NotSupportedException($"Unexpected instruction layout OP{sizeOp} A{sizeA} B{sizeB} C{sizeC}.");

        _sizeNumber = U8();
        if (_sizeInt != 4 || _sizeSizeT != 4 || _sizeInstruction != 4)
            throw new NotSupportedException($"Unexpected widths int{_sizeInt} size_t{_sizeSizeT} instr{_sizeInstruction}.");
        if (_sizeNumber is not (4 or 8))
            throw new NotSupportedException($"Unexpected lua_Number width {_sizeNumber}.");

        _p += _sizeNumber; // TEST_NUMBER, already validated by the width check
    }

    private LuaProto ReadFunction(string parentSource)
    {
        var f = new LuaProto();
        f.Source = ReadString() ?? parentSource;
        if (SourceName.Length == 0) SourceName = f.Source;

        f.LineDefined = I32();
        f.Nups = U8();
        f.NumParams = U8();
        f.IsVararg = U8();
        f.MaxStackSize = U8();

        int n = I32();
        f.Lines = new int[n];
        for (int i = 0; i < n; i++) f.Lines[i] = I32();

        n = I32();
        f.Locals = new LuaLocal[n];
        for (int i = 0; i < n; i++) f.Locals[i] = new LuaLocal(ReadString() ?? "", I32(), I32());

        n = I32();
        f.Upvalues = new string[n];
        for (int i = 0; i < n; i++) f.Upvalues[i] = ReadString() ?? "";

        // LoadConstants: values first, then nested prototypes.
        n = I32();
        f.Constants = new LuaConstant[n];
        for (int i = 0; i < n; i++)
        {
            byte t = U8();
            f.Constants[i] = t switch
            {
                0 => LuaConstant.Nil(),                     // LUA_TNIL
                3 => LuaConstant.Num(Number()),             // LUA_TNUMBER
                4 => LuaConstant.Text(ReadString() ?? ""),  // LUA_TSTRING
                _ => throw new InvalidDataException($"Bad constant type {t} at 0x{_p:X}."),
            };
        }

        n = I32();
        f.Protos = new LuaProto[n];
        for (int i = 0; i < n; i++) f.Protos[i] = ReadFunction(f.Source);

        n = I32();
        f.Code = new uint[n];
        for (int i = 0; i < n; i++) f.Code[i] = U32();

        return f;
    }

    private byte U8() => _d[_p++];

    private int I32()
    {
        int v = BitConverter.ToInt32(_d, _p);
        _p += 4;
        return v;
    }

    private uint U32()
    {
        uint v = BitConverter.ToUInt32(_d, _p);
        _p += 4;
        return v;
    }

    private double Number()
    {
        double v = _sizeNumber == 4 ? BitConverter.ToSingle(_d, _p) : BitConverter.ToDouble(_d, _p);
        _p += _sizeNumber;
        return v;
    }

    /// Strings are length-prefixed and include their terminating NUL.
    private string? ReadString()
    {
        int len = I32();
        if (len == 0) return null;
        var s = Encoding.Latin1.GetString(_d, _p, len - 1);
        _p += len;
        return s;
    }
}
