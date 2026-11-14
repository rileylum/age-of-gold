using AgeOfGold.Events;
using SoulsFormats;

internal static class SourceEventCheck
{
    internal static void Run()
    {
        static byte[] Bytes(params object[] values)
        {
            var bytes = new List<byte>();
            foreach (object value in values) bytes.AddRange(value switch { int i => BitConverter.GetBytes(i), byte b => [b], _ => throw new ArgumentException("Unsupported argument") });
            return bytes.ToArray();
        }
        static EMEVD.Instruction Line(int bank, int id, params object[] values) => new(bank, id) { ArgData = Bytes(values) };
        var script = new EMEVD(EMEVD.Game.DarkSouls1);
        // The map's opening event starts each of the others once.
        var opening = new EMEVD.Event(0);
        foreach (int started in new[] { 100, 200, 300 }) opening.Instructions.Add(Line(2000, 0, 0, started, 0));
        // Started with a collision as its parameter: the opening event's fourth value fills the instruction's first.
        opening.Instructions.Add(Line(2000, 0, 0, 400, 5005));
        script.Events.Add(opening);
        // A two-state event: unless it already ran, it switches one collision off, waits for flags, then swaps the pair.
        var swap = new EMEVD.Event(100);
        swap.Instructions.AddRange([Line(1003, 1, (byte)4, (byte)1, (byte)1, (byte)0, 0), Line(2005, 3, 9001, (byte)0, (byte)0, (byte)0, (byte)0), Line(2011, 1, 5001, (byte)0, (byte)0, (byte)0, (byte)0),
            Line(3, 0, (byte)1, (byte)1, (byte)0, (byte)0, 777), Line(0, 0, (byte)0, (byte)1, (byte)1, (byte)0), Line(2011, 1, 5001, (byte)1, (byte)0, (byte)0, (byte)0), Line(2011, 1, 5000, (byte)0, (byte)0, (byte)0, (byte)0)]);
        script.Events.Add(swap);
        // Skipped while its flag is off: these switches do not run in a new game.
        var later = new EMEVD.Event(200);
        later.Instructions.AddRange([Line(1003, 1, (byte)1, (byte)0, (byte)0, (byte)0, 888), Line(2011, 1, 5002, (byte)0, (byte)0, (byte)0, (byte)0), Line(2011, 1, 5003, (byte)0, (byte)0, (byte)0, (byte)0),
            Line(2011, 1, 5003, (byte)1, (byte)0, (byte)0, (byte)0)]);
        script.Events.Add(later);
        // Waits before it switches anything.
        var waiting = new EMEVD.Event(300);
        waiting.Instructions.AddRange([Line(3, 0, (byte)0, (byte)1, (byte)0, (byte)0, 710), Line(2011, 1, 5004, (byte)0, (byte)0, (byte)0, (byte)0)]);
        script.Events.Add(waiting);
        var given = new EMEVD.Event(400);
        given.Instructions.Add(Line(2011, 1, 0, (byte)0, (byte)0, (byte)0, (byte)0));
        given.Parameters.Add(new EMEVD.Parameter { InstructionIndex = 0, TargetStartByte = 0, SourceStartByte = 0, ByteCount = 4 });
        script.Events.Add(given);

        var off = SourceEventReader.CollisionsOffAtStart(script);
        // 5001 is switched off before the wait; 5002's switch is skipped; 5003 is switched off and on again; 5004 waits;
        // 5005 arrives as a parameter.
        if (!off.SequenceEqual([5001, 5005])) throw new Exception("Collisions off at the start: " + string.Join(", ", off));
        if (!SourceEventReader.CollisionsOffAtStart(EMEVD.Read(script.Write())).SequenceEqual([5001, 5005])) throw new Exception("A written script reads differently");
        // An event that is started but missing stops the read.
        opening.Instructions.Add(Line(2000, 0, 0, 999, 0));
        try { SourceEventReader.CollisionsOffAtStart(script); throw new Exception("A missing event was accepted"); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: collisions a source map's events switch off as it loads are read up to each event's first wait");
    }
}
