namespace OpenBuzz.Input;

/// <summary>
/// Four Buzz handsets, however they happen to be provided. The game talks to
/// this and nothing else, so a virtual panel and real USB hardware are
/// interchangeable behind it.
/// </summary>
public interface IBuzzInputSource : IDisposable
{
    /// Always 4 for a standard Buzz set.
    int ControllerCount { get; }

    /// Human-readable description of where the input is coming from.
    string Description { get; }

    BuzzButtons GetButtons(int controller);

    bool IsDown(int controller, BuzzButton button);

    /// True only on the update in which the button went down.
    bool WasPressed(int controller, BuzzButton button);

    BuzzLamp Lamp(int controller);

    event EventHandler<BuzzButtonEventArgs>? ButtonPressed;
    event EventHandler<BuzzButtonEventArgs>? ButtonReleased;

    /// Pumps the source: samples hardware, advances lamp flashing, and raises
    /// the press/release events. Call once per frame.
    void Update(TimeSpan elapsed);
}
