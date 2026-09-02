namespace OpenBuzz.Cli.Lua;

using System.Globalization;

/// <summary>
/// Lua values, as plain CLR objects.
///
/// nil is null, booleans are bool, numbers are double, strings are string, and
/// the rest are the classes below. Using object rather than a tagged struct
/// keeps the interpreter short; this VM runs a quiz, not a benchmark.
/// </summary>
public static class LuaValues
{
    public static bool Truthy(object? v) => v is not null && v is not false;

    public static string TypeName(object? v) => v switch
    {
        null => "nil",
        bool => "boolean",
        double => "number",
        string => "string",
        LuaTable => "table",
        LuaClosure or LuaNative => "function",
        _ => "userdata",
    };

    public static string ToStringValue(object? v) => v switch
    {
        null => "nil",
        bool b => b ? "true" : "false",
        double d => d == Math.Floor(d) && Math.Abs(d) < 1e15
            ? ((long)d).ToString(CultureInfo.InvariantCulture)
            : d.ToString("G14", CultureInfo.InvariantCulture),
        string s => s,
        LuaTable => "table",
        LuaClosure c => "function:" + c.Proto.Source,
        LuaNative n => "native:" + n.Name,
        _ => v.ToString() ?? "?",
    };

    /// Lua coerces strings to numbers in arithmetic. Rare here, but free.
    public static double ToNumber(object? v) => v switch
    {
        double d => d,
        bool b => b ? 1 : 0,
        string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) => d,
        _ => throw new LuaError($"attempt to do arithmetic on a {TypeName(v)} value"),
    };
}

public sealed class LuaError(string message) : Exception(message);

/// A Lua table: the array part and the hash part, kept apart the way Lua does.
public sealed class LuaTable
{
    private readonly List<object?> _array = [];
    private readonly Dictionary<object, object?> _hash = [];

    public int Length => _array.Count;

    public object? Get(object? key)
    {
        if (key is null) return null;
        if (key is double d && IsIndex(d, out int i) && i <= _array.Count) return _array[i - 1];
        return _hash.TryGetValue(key, out var v) ? v : null;
    }

    public void Set(object? key, object? value)
    {
        if (key is null) throw new LuaError("table index is nil");

        if (key is double d && IsIndex(d, out int i))
        {
            if (i <= _array.Count) { _array[i - 1] = value; return; }
            if (i == _array.Count + 1)
            {
                _array.Add(value);
                // A hash entry may now be reachable from the array part.
                while (_hash.TryGetValue((double)(_array.Count + 1), out var next))
                {
                    _hash.Remove((double)(_array.Count + 1));
                    _array.Add(next);
                }
                return;
            }
        }

        if (value is null) _hash.Remove(key); else _hash[key] = value;
    }

    public IEnumerable<KeyValuePair<object, object?>> Pairs()
    {
        for (int i = 0; i < _array.Count; i++)
            if (_array[i] is not null) yield return new((double)(i + 1), _array[i]);
        foreach (var kv in _hash) yield return kv;
    }

    private static bool IsIndex(double d, out int i)
    {
        i = (int)d;
        return d == i && i >= 1;
    }
}

/// A function written in Lua: its prototype plus the upvalues it closed over.
public sealed class LuaClosure(LuaProto proto, LuaCell[] upvalues)
{
    public LuaProto Proto { get; } = proto;
    public LuaCell[] Upvalues { get; } = upvalues;
}

/// One upvalue. A box, because several closures can share the same variable.
public sealed class LuaCell
{
    public object? Value;
}

/// A function the host provides. `args` in, results out.
public sealed class LuaNative(string name, Func<object?[], object?[]> body)
{
    public string Name { get; } = name;
    public Func<object?[], object?[]> Body { get; } = body;
}
