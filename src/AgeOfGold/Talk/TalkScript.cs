using SoulsFormats;

namespace AgeOfGold.Talk;

/// <summary>Authors host talk scripts: the state machines that answer a player's interaction with a character.</summary>
internal static class TalkScript
{
    // The host's talk commands and functions used here, by their numbers in its scripting interface.
    private const int CommandBank = 1, OpenDialog = 17, ClearAction = 35, StayAwakeWithin = 38;
    private const int ActionPressed = 56, DialogOpenFor = 58, SecondsInState = 103;
    private const byte Greater = 0x94, Equal = 0x95, End = 0xA1;

    /// <summary>
    /// A character that answers its interaction with one message, as often as asked: wait for the action, open the
    /// message as a one-button dialog, wait for it to close, and wait again.
    /// </summary>
    // lazy: one fixed message; the source's own talk scripts, with their branching, services and flags, are not translated.
    internal static byte[] OneMessage(int action, int message)
    {
        if (action <= 0 || message <= 0) throw new InvalidDataException("A talk script needs an action and a message");
        var script = new ESD(true, 3);
        var states = Enumerable.Range(0, 6).ToDictionary(i => (long)i, _ => new ESD.State());
        void Leave(int from, int to, params byte[][] test) => states[from].Conditions.Add(new(to, [.. test.SelectMany(part => part), End]));
        void Enter(int state, int command, params byte[][] arguments) =>
            states[state].EntryCommands.Add(new(CommandBank, command, arguments.Select(a => (byte[])[.. a, End]).ToArray()));
        // The entry state runs no commands of its own in the host; a separate first state does the set-up.
        Leave(0, 1, Whole(1));
        Enter(1, StayAwakeWithin, Whole(30));
        Leave(1, 2, Whole(1));
        Leave(2, 3, Call(ActionPressed, Whole(action)));
        Enter(3, ClearAction);
        // A generic dialog: one button, this message, no further choices.
        Enter(3, OpenDialog, Whole(7), Whole(message), Whole(1), Whole(0), Whole(1));
        Leave(3, 4, Call(SecondsInState), Real(0.2), [Greater]);
        Leave(4, 5, Call(DialogOpenFor, Whole(0)), Whole(0), [Equal]);
        Enter(5, ClearAction);
        Leave(5, 2, Call(SecondsInState), Real(0.5), [Greater]);
        script.StateGroups.Add(1, states);
        return script.Write();
    }

    // A whole number near zero is one byte; any other takes four after a marker.
    private static byte[] Whole(int value) => value is >= -64 and <= 63 ? [(byte)(value + 64)] : [0x82, .. BitConverter.GetBytes(value)];

    // A real number takes four bytes when that holds it exactly, else eight.
    private static byte[] Real(double value) => (float)value == value ? [0x80, .. BitConverter.GetBytes((float)value)] : [0x81, .. BitConverter.GetBytes(value)];

    // A call pushes the function's number and its arguments, then the call marker counting the arguments.
    private static byte[] Call(int function, params byte[][] arguments) => [.. Whole(function), .. arguments.SelectMany(a => a), (byte)(0x84 + arguments.Length)];
}
