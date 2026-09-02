namespace OpenBuzz.Cli.Lua;

using System.Globalization;

/// <summary>
/// The part of the Lua standard library the disc's scripts actually use.
///
/// Not a complete 5.0 library: only what the game reaches for, so that a
/// missing function shows up as a stub in the trace rather than being quietly
/// approximated. coroutine is the one that matters - every round is one.
/// </summary>
public static class LuaStdlib
{
    public static void Install(LuaVm vm)
    {
        vm.Register("type", a => [LuaValues.TypeName(Arg(a, 0))]);
        vm.Register("tostring", a => [LuaValues.ToStringValue(Arg(a, 0))]);

        vm.Register("tonumber", a =>
        {
            var v = Arg(a, 0);
            if (v is double) return [v];
            if (v is string s && double.TryParse(s, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var d)) return [d];
            return [null];
        });

        vm.Register("print", a =>
        {
            Console.WriteLine(string.Join("\t", a.Select(LuaValues.ToStringValue)));
            return [];
        });

        vm.Register("assert", a =>
        {
            if (!LuaValues.Truthy(Arg(a, 0)))
                throw new LuaError(Arg(a, 1) is string m ? m : "assertion failed!");
            return a;
        });

        vm.Register("error", a => throw new LuaError(LuaValues.ToStringValue(Arg(a, 0))));

        vm.Register("unpack", a =>
        {
            if (Arg(a, 0) is not LuaTable t) return [];
            var outv = new List<object?>();
            for (int i = 1; i <= t.Length; i++) outv.Add(t.Get((double)i));
            return [.. outv];
        });

        // next() drives generic for, which the VM lowers TFORPREP into.
        vm.Register("next", a =>
        {
            if (Arg(a, 0) is not LuaTable t) return [null];
            var pairs = t.Pairs().ToList();
            var key = Arg(a, 1);
            int at = key is null ? 0 : pairs.FindIndex(p => Equals(p.Key, key)) + 1;
            if (at < 0 || at >= pairs.Count) return [null];
            return [pairs[at].Key, pairs[at].Value];
        });

        Table(vm);
        Strings(vm);
        Maths(vm);
        Coroutines(vm);
    }

    private static void Coroutines(LuaVm vm)
    {
        var co = new LuaTable();

        co.Set("create", new LuaNative("coroutine.create",
            a => [new LuaCoroutine(vm, Arg(a, 0))]));

        co.Set("resume", new LuaNative("coroutine.resume", a =>
        {
            if (Arg(a, 0) is not LuaCoroutine c)
                throw new LuaError("resume expects a coroutine");
            try
            {
                var results = c.Resume([.. a.Skip(1)]);
                return [true, .. results];
            }
            catch (LuaError e)
            {
                return [false, e.Message];
            }
        }));

        co.Set("yield", new LuaNative("coroutine.yield", a =>
            LuaCoroutine.Current is { } c
                ? c.Yield(a)
                : throw new LuaError("attempt to yield from outside a coroutine")));

        co.Set("status", new LuaNative("coroutine.status",
            a => [Arg(a, 0) is LuaCoroutine c ? c.Status : "dead"]));

        vm.Globals.Set("coroutine", co);
    }

    private static void Table(LuaVm vm)
    {
        var t = new LuaTable();

        t.Set("insert", new LuaNative("table.insert", a =>
        {
            if (Arg(a, 0) is not LuaTable tbl) return [];
            if (a.Length >= 3) tbl.Set(Arg(a, 1), Arg(a, 2));
            else tbl.Set((double)(tbl.Length + 1), Arg(a, 1));
            return [];
        }));

        t.Set("getn", new LuaNative("table.getn",
            a => [Arg(a, 0) is LuaTable tbl ? (double)tbl.Length : 0.0]));

        vm.Globals.Set("table", t);
    }

    private static void Strings(LuaVm vm)
    {
        var s = new LuaTable();

        s.Set("len", new LuaNative("string.len",
            a => [(double)LuaValues.ToStringValue(Arg(a, 0)).Length]));

        s.Set("sub", new LuaNative("string.sub", a =>
        {
            var str = LuaValues.ToStringValue(Arg(a, 0));
            int from = (int)LuaValues.ToNumber(Arg(a, 1) ?? 1.0);
            int to = a.Length > 2 && a[2] is not null ? (int)LuaValues.ToNumber(a[2]) : str.Length;
            if (from < 0) from = Math.Max(str.Length + from + 1, 1);
            if (to < 0) to = str.Length + to + 1;
            from = Math.Max(from, 1);
            to = Math.Min(to, str.Length);
            return [from > to ? "" : str[(from - 1)..to]];
        }));

        s.Set("format", new LuaNative("string.format", a =>
        {
            // Enough of Lua's format for the scripts, which use %s and %d.
            var fmt = LuaValues.ToStringValue(Arg(a, 0));
            int next = 1;
            var outv = new System.Text.StringBuilder();
            for (int i = 0; i < fmt.Length; i++)
            {
                if (fmt[i] != '%' || i + 1 >= fmt.Length) { outv.Append(fmt[i]); continue; }
                char kind = fmt[++i];
                object? arg = next < a.Length ? a[next++] : null;
                outv.Append(kind switch
                {
                    '%' => "%",
                    'd' or 'i' => ((long)LuaValues.ToNumber(arg)).ToString(CultureInfo.InvariantCulture),
                    _ => LuaValues.ToStringValue(arg),
                });
            }
            return [outv.ToString()];
        }));

        vm.Globals.Set("string", s);
    }

    private static void Maths(LuaVm vm)
    {
        var m = new LuaTable();
        m.Set("floor", new LuaNative("math.floor", a => [Math.Floor(LuaValues.ToNumber(Arg(a, 0)))]));
        m.Set("ceil", new LuaNative("math.ceil", a => [Math.Ceiling(LuaValues.ToNumber(Arg(a, 0)))]));
        m.Set("abs", new LuaNative("math.abs", a => [Math.Abs(LuaValues.ToNumber(Arg(a, 0)))]));
        m.Set("max", new LuaNative("math.max", a => [a.Select(LuaValues.ToNumber).Max()]));
        m.Set("min", new LuaNative("math.min", a => [a.Select(LuaValues.ToNumber).Min()]));
        m.Set("mod", new LuaNative("math.mod",
            a => [LuaValues.ToNumber(Arg(a, 0)) % LuaValues.ToNumber(Arg(a, 1))]));
        vm.Globals.Set("math", m);
    }

    private static object? Arg(object?[] a, int i) => i < a.Length ? a[i] : null;
}
