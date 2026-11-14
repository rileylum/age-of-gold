using SoulsFormats;

namespace AgeOfGold.Events;

/// <summary>Reads what a source map's event script does as the map loads in a new game.</summary>
internal static class SourceEventReader
{
    // Instructions by bank and number: start an event, skip lines by a flag, switch a collision.
    private const int Start = 2000, SkipByFlag = 1003, Collision = 2011, FirstAction = 2000;

    internal static SortedSet<int> CollisionsOffAtStart(Install source) => CollisionsOffAtStart(EMEVD.Read(source.Read("event/m10_02_00_00.emevd.dcx")));

    /// <summary>
    /// The collisions, by entity, that are off once the map's opening event has started its events and each has run to
    /// its first wait. In a new game no flag is set yet.
    /// </summary>
    // lazy: only what runs before an event's first wait is followed, with every flag off; conditions, flags that persist
    // and the common script are not evaluated. A full event translation replaces this.
    internal static SortedSet<int> CollisionsOffAtStart(EMEVD script)
    {
        var events = script.Events.ToDictionary(e => e.ID);
        if (!events.TryGetValue(0, out var opening)) throw new InvalidDataException("Source event script has no opening event");
        var state = new SortedDictionary<int, bool>();
        foreach (var start in opening.Instructions.Where(i => i.Bank == Start && i.ID == 0))
        {
            if (start.ArgData.Length < 8) throw new InvalidDataException("Unclassified source event start");
            long id = BitConverter.ToUInt32(start.ArgData, 4);
            if (!events.TryGetValue(id, out var started)) throw new InvalidDataException("Source event script starts a missing event: " + id);
            for (int line = 0; line < started.Instructions.Count; line++)
            {
                var instruction = started.Instructions[line];
                var arguments = instruction.ArgData.ToArray();
                // An event's parameters arrive after the slot and identity in the instruction that starts it.
                foreach (var parameter in started.Parameters.Where(p => p.InstructionIndex == line))
                {
                    if (8 + parameter.SourceStartByte + parameter.ByteCount > start.ArgData.Length || parameter.TargetStartByte + parameter.ByteCount > arguments.Length)
                        throw new InvalidDataException("Source event parameter lies outside its instruction");
                    Array.Copy(start.ArgData, 8 + parameter.SourceStartByte, arguments, parameter.TargetStartByte, parameter.ByteCount);
                }
                if (instruction.Bank == SkipByFlag && instruction.ID == 1)
                {
                    // Lines to skip, then the flag state that skips them: with every flag off, only "off" skips.
                    if (arguments.Length < 2) throw new InvalidDataException("Unclassified source event skip");
                    if (arguments[1] == 0) line += arguments[0];
                }
                else if (instruction.Bank == Collision && instruction.ID == 1)
                {
                    if (arguments.Length < 5 || arguments[4] > 1) throw new InvalidDataException("Unclassified source collision switch");
                    state[BitConverter.ToInt32(arguments, 0)] = arguments[4] == 1;
                }
                // Other actions change something else; anything that is not an action is a wait or a branch this reader does not follow.
                else if (instruction.Bank < FirstAction) break;
            }
        }
        return new(state.Where(s => !s.Value).Select(s => s.Key));
    }
}
