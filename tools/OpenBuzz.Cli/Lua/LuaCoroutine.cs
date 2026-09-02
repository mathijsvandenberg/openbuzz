namespace OpenBuzz.Cli.Lua;

/// <summary>
/// A Lua coroutine, run on its own thread.
///
/// The rounds need these: every one of them wraps its body in
/// `coroutine.create` and drives it from the round start script, and the waits
/// inside a round - WaitSeconds, and everything that waits on a buzzer - are
/// yields. Without coroutines a round cannot get past its first line.
///
/// A tree-walking interpreter cannot suspend a C# call stack, so each
/// coroutine gets a thread and the two hand control back and forth through a
/// pair of semaphores. Only one ever runs at a time, which is exactly Lua's
/// own rule, so the threads buy suspension without buying concurrency.
/// </summary>
public sealed class LuaCoroutine
{
    [ThreadStatic] private static LuaCoroutine? _current;

    private readonly SemaphoreSlim _resumeSignal = new(0, 1);
    private readonly SemaphoreSlim _yieldSignal = new(0, 1);
    private readonly LuaVm _vm;
    private readonly object? _body;

    private Thread? _thread;
    private object?[] _transfer = [];
    private LuaError? _failure;

    public string Status { get; private set; } = "suspended";

    public LuaCoroutine(LuaVm vm, object? body)
    {
        _vm = vm;
        _body = body;
    }

    public static LuaCoroutine? Current => _current;

    /// Hands control to the coroutine and blocks until it yields or returns.
    public object?[] Resume(object?[] args)
    {
        if (Status == "dead") throw new LuaError("cannot resume dead coroutine");
        if (Status == "running") throw new LuaError("cannot resume non-suspended coroutine");

        _transfer = args;
        Status = "running";

        if (_thread is null)
        {
            _thread = new Thread(Body, 8 * 1024 * 1024) { IsBackground = true, Name = "lua-coroutine" };
            _thread.Start();
        }
        else
        {
            _resumeSignal.Release();
        }

        _yieldSignal.Wait();

        if (_failure is not null)
        {
            var error = _failure;
            _failure = null;
            throw error;
        }
        return _transfer;
    }

    /// Suspends the running coroutine and returns control to whoever resumed it.
    public object?[] Yield(object?[] values)
    {
        _transfer = values;
        Status = "suspended";
        _yieldSignal.Release();
        _resumeSignal.Wait();
        Status = "running";
        return _transfer;
    }

    private void Body()
    {
        _current = this;
        try
        {
            _transfer = _vm.Call(_body, _transfer);
        }
        catch (LuaError e)
        {
            _failure = e;
            _transfer = [];
        }
        catch (Exception e)
        {
            _failure = new LuaError(e.Message);
            _transfer = [];
        }
        finally
        {
            Status = "dead";
            _yieldSignal.Release();
        }
    }
}
