using SoulsFormats;

static class FirelinkAreaTitle
{
    /// <summary>Bind the map's area announcement to text read from the source install.</summary>
    public static (byte[] Events, byte[] Names) Apply(string sourceText, byte[] events, byte[] names)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) throw new InvalidDataException("Source area name is empty");
        var script = EMEVD.Read(events); var messages = FMG.Read(names);
        var candidates = script.Events.SelectMany(e => e.Instructions.Select((i, n) => (Event: e, Instruction: i, Index: n)))
            .Where(x => x.Instruction.Bank == 2007 && x.Instruction.ID == 14).ToArray();
        // lazy: one area announcement per map until another translated area shares this map.
        if (candidates.Length != 1) throw new InvalidDataException("Expected one explicit area announcement");
        var target = candidates.Single();
        if (target.Instruction.ArgData.Length != 4 || target.Event.Parameters.Any(p => p.InstructionIndex == target.Index))
            throw new InvalidDataException("Area announcement must have a literal text reference");
        var previous = BitConverter.ToInt32(target.Instruction.ArgData);
        if (messages.Entries.Count(e => e.ID == previous && e.Text is not null) != 1
            || messages.Entries.Select(e => e.ID).Distinct().Count() != messages.Entries.Count)
            throw new InvalidDataException("Area announcement text is missing or ambiguous");
        var previousText = messages[previous];
        // Keep the shared reference so announcements outside this event resolve to the same name.
        messages[previous] = sourceText;
        var outputNames = messages.Write(); var actualNames = FMG.Read(outputNames);
        if (actualNames[previous] != sourceText)
            throw new InvalidDataException("Area announcement binding did not round trip");
        actualNames[previous] = previousText;
        if (!actualNames.Write().AsSpan().SequenceEqual(FMG.Read(names).Write()))
            throw new InvalidDataException("Area announcement changed unrelated text");
        return (events, outputNames);
    }

    /// <summary>Check an authored announcement while preserving unrelated text and event bytes.</summary>
    public static void Check()
    {
        var events = new EMEVD(EMEVD.Game.DarkSouls3);
        var trigger = new EMEVD.Event(42);
        trigger.Instructions.Add(new EMEVD.Instruction(2007, 14, new object[] { 5 }));
        trigger.Instructions.Add(new EMEVD.Instruction(2007, 13, new object[] { 8 }));
        events.Events.Add(trigger);
        var names = new FMG();
        names.Entries.Add(new(5, "Our old area"));
        names.Entries.Add(new(8, "Our subarea"));
        var beforeEvents = events.Write(); var beforeNames = names.Write();
        var result = Apply("Our clearing", beforeEvents, beforeNames);
        var actualEvents = EMEVD.Read(result.Events); var actualNames = FMG.Read(result.Names);
        var announcement = actualEvents.Events.Single().Instructions[0];
        if (BitConverter.ToInt32(announcement.ArgData) != 5 || actualNames[5] != "Our clearing"
            || actualNames.Entries.Count != 2)
            throw new InvalidDataException("Source area text must also serve the host's implicit area-title reference");
        actualNames[5] = "Our old area";
        if (!actualEvents.Write().AsSpan().SequenceEqual(beforeEvents)
            || !actualNames.Write().AsSpan().SequenceEqual(beforeNames))
            throw new InvalidDataException("Area binding changed existing text or unrelated event data");
        Console.WriteLine("PASS: source area announcement bound; unrelated text and event bytes preserved");
    }
}
