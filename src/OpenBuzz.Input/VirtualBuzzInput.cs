namespace OpenBuzz.Input;

/// <summary>
/// Four handsets driven by software rather than hardware. A UI (or a test)
/// calls <see cref="SetButton"/>; everything downstream sees exactly what it
/// would see from real controllers, including lamp state.
/// </summary>
public sealed class VirtualBuzzInput : IBuzzInputSource
{
    public const int DefaultControllerCount = 4;

    private readonly BuzzButtons[] _current;
    private readonly BuzzButtons[] _previous;
    private readonly BuzzButtons[] _pending;
    private readonly BuzzLamp[] _lamps;

    public VirtualBuzzInput(int controllers = DefaultControllerCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(controllers, 1);
        ControllerCount = controllers;
        _current = new BuzzButtons[controllers];
        _previous = new BuzzButtons[controllers];
        _pending = new BuzzButtons[controllers];
        _lamps = new BuzzLamp[controllers];
        for (int i = 0; i < controllers; i++) _lamps[i] = new BuzzLamp();
    }

    public int ControllerCount { get; }

    public string Description => $"Virtual ({ControllerCount} handsets)";

    public event EventHandler<BuzzButtonEventArgs>? ButtonPressed;
    public event EventHandler<BuzzButtonEventArgs>? ButtonReleased;

    public BuzzButtons GetButtons(int controller) => _current[controller];

    public bool IsDown(int controller, BuzzButton button) => _current[controller].IsSet(button);

    public bool WasPressed(int controller, BuzzButton button) =>
        _current[controller].IsSet(button) && !_previous[controller].IsSet(button);

    public BuzzLamp Lamp(int controller) => _lamps[controller];

    /// <summary>
    /// Sets a button's state. Buffered until the next <see cref="Update"/> so
    /// that a press is observed for at least one full frame however briefly the
    /// UI held it - a fast click must never be missed.
    /// </summary>
    public void SetButton(int controller, BuzzButton button, bool down)
    {
        if ((uint)controller >= (uint)ControllerCount) return;
        if (down) _pending[controller] |= button.ToFlag();
        else _pending[controller] &= ~button.ToFlag();
    }

    public void ReleaseAll()
    {
        for (int i = 0; i < ControllerCount; i++) _pending[i] = BuzzButtons.None;
    }

    public void Update(TimeSpan elapsed)
    {
        for (int i = 0; i < ControllerCount; i++)
        {
            _previous[i] = _current[i];
            _current[i] = _pending[i];

            foreach (var button in BuzzButtonExtensions.AllButtons)
            {
                bool now = _current[i].IsSet(button), before = _previous[i].IsSet(button);
                if (now && !before) ButtonPressed?.Invoke(this, new BuzzButtonEventArgs(i, button));
                else if (!now && before) ButtonReleased?.Invoke(this, new BuzzButtonEventArgs(i, button));
            }

            _lamps[i].Update(elapsed);
        }
    }

    public void Dispose() { }
}
