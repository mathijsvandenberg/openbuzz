namespace OpenBuzz.Input;

/// <summary>
/// The five switches on a Buzz handset. The four answer buttons are listed in
/// physical order down the face of the unit, which is also the order the game
/// presents answers in.
/// </summary>
public enum BuzzButton
{
    Red = 0,
    Blue = 1,
    Orange = 2,
    Green = 3,
    Yellow = 4,
}

[Flags]
public enum BuzzButtons : byte
{
    None = 0,
    Red = 1 << 0,
    Blue = 1 << 1,
    Orange = 1 << 2,
    Green = 1 << 3,
    Yellow = 1 << 4,
}

public static class BuzzButtonExtensions
{
    /// The four answer buttons, top to bottom.
    public static readonly BuzzButton[] AnswerButtons =
        [BuzzButton.Blue, BuzzButton.Orange, BuzzButton.Green, BuzzButton.Yellow];

    public static readonly BuzzButton[] AllButtons =
        [BuzzButton.Red, BuzzButton.Blue, BuzzButton.Orange, BuzzButton.Green, BuzzButton.Yellow];

    public static BuzzButtons ToFlag(this BuzzButton b) => (BuzzButtons)(1 << (int)b);

    public static bool IsSet(this BuzzButtons flags, BuzzButton b) => (flags & b.ToFlag()) != 0;

    /// Answer index 0..3 for the coloured buttons; -1 for the red buzzer.
    public static int ToAnswerIndex(this BuzzButton b) =>
        b == BuzzButton.Red ? -1 : (int)b - 1;
}

public sealed class BuzzButtonEventArgs(int controller, BuzzButton button) : EventArgs
{
    public int Controller { get; } = controller;
    public BuzzButton Button { get; } = button;
}
