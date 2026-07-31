namespace OpenBuzz.Input;

public enum LampMode { Off, On, Flashing }

/// <summary>
/// The lamp inside a handset's red buzzer. The game drives it directly (steady
/// on to arm a player) and also flashes it, so the flash is modelled as a mode
/// plus a period rather than something the caller has to toggle itself.
/// </summary>
public sealed class BuzzLamp
{
    private TimeSpan _phase;

    public LampMode Mode { get; private set; } = LampMode.Off;
    public TimeSpan Period { get; private set; } = TimeSpan.FromMilliseconds(400);

    /// Whether the lamp is emitting light right now.
    public bool IsLit { get; private set; }

    public void Off() => Set(LampMode.Off, Period);

    public void On() => Set(LampMode.On, Period);

    public void Flash(TimeSpan? period = null) => Set(LampMode.Flashing, period ?? Period);

    private void Set(LampMode mode, TimeSpan period)
    {
        // Restart the phase so a newly started flash always begins lit; without
        // this a flash inherits whatever point in the cycle the lamp was at.
        if (mode != Mode) _phase = TimeSpan.Zero;
        Mode = mode;
        Period = period > TimeSpan.Zero ? period : TimeSpan.FromMilliseconds(400);
        IsLit = mode == LampMode.On || (mode == LampMode.Flashing);
    }

    public void Update(TimeSpan elapsed)
    {
        switch (Mode)
        {
            case LampMode.Off:
                IsLit = false;
                break;
            case LampMode.On:
                IsLit = true;
                break;
            case LampMode.Flashing:
                _phase += elapsed;
                var full = Period + Period;
                while (_phase >= full) _phase -= full;
                IsLit = _phase < Period;
                break;
        }
    }
}
