using SoulsFormats;

namespace AgeOfGold.Characters;

/// <summary>
/// Writes a source character's animation events — when an attack can hit, when it may turn — as a host event file. The
/// two games number the kinds of event alike and lay the common ones out alike; the host's are longer.
/// </summary>
internal static class HostCharacterEvents
{
    /// <param name="Numbers">Every animation the file lists, by host number.</param>
    /// <param name="Kept">Events carried over.</param>
    /// <param name="Dropped">Source events left out, by kind.</param>
    internal sealed record Written(byte[] File, long[] Numbers, int Kept, SortedDictionary<int, int> Dropped);

    /// <summary>A source animation number as the host numbers it: the source's two-digit set becomes the host's three-digit one.</summary>
    internal static long HostNumber(long source) => source < 0 ? throw new InvalidDataException("An animation number cannot be negative") : source / 10000 * 1000000 + source % 10000;

    /// <summary>The name of the host animation file with a number.</summary>
    internal static string HostName(long number) => $"a{number / 1000000:D3}_{number % 1000000:D6}";

    /// <summary>
    /// The common moves the two games number differently: the host's number, and the source animation that does the same
    /// thing. Measured from where each animation takes the body: the source walks and runs under 200 to 203 and 500 and
    /// turns on the spot under 602 to 605; the host walks under 2000 to 2003, runs under 2100 and turns under 5000 on.
    /// Attacks (3000 on) share their numbers.
    /// </summary>
    /// <remarks>
    /// Being struck is matched by measurement where it can be: both games keep three weights of flinch, told apart by
    /// rising length; the skeleton's heavy flinches throw the body left, right, back and forward in that order of number;
    /// being blown back goes about as far in both. The rest is by likeness and is marked so.
    /// lazy: guarding, falling, being thrown, the host's added flinches over another move, and its other deaths are numbered
    /// differently too and still play whatever the source keeps under the host's number, or its idle.
    /// </remarks>
    internal static readonly IReadOnlyDictionary<long, long> Moves = new SortedDictionary<long, long>
    {
        // Walking in battle, forward, back, left and right; walking with the guard up likewise, the source having one walk.
        [2000] = 200, [2001] = 201, [2002] = 202, [2003] = 203, [2030] = 200, [2031] = 201, [2032] = 202, [2033] = 203,
        // Running, and walking forward at ease.
        [2100] = 500, [2300] = 200,
        // Turning on the spot a quarter and a half turn each way: in battle, at ease, and with the guard up.
        [5000] = 602, [5001] = 603, [5002] = 604, [5003] = 605, [5010] = 602, [5011] = 603, [5012] = 604, [5013] = 605, [5020] = 602, [5021] = 603, [5022] = 604, [5023] = 605,
        // A light flinch, whichever way the blow came. By likeness: which of the source's three is from which side is not measured.
        [8030] = 2000, [8031] = 2000, [8032] = 2000, [8033] = 2000, [8034] = 2000, [8035] = 2000, [8036] = 2000, [8037] = 2000,
        // A middling flinch from the front, the left, the right, above and below, then from behind, the left and the right.
        // By likeness: taken to be in the heavy flinches' order of sides.
        [8040] = 2006, [8041] = 2004, [8042] = 2005, [8043] = 2006, [8044] = 2006, [8045] = 2007, [8046] = 2004, [8047] = 2005,
        // A heavy flinch, likewise: a blow from the front throws the body back, one from the left throws it right.
        [8050] = 2012, [8051] = 2010, [8052] = 2011, [8053] = 2012, [8054] = 2012, [8055] = 2013, [8056] = 2010, [8057] = 2011,
        // Blown back a few metres, and blown back far.
        [8100] = 2020, [8101] = 2014,
        // Dying where it stands. By likeness: the source's one animation of about this length that ends a little way off.
        [10000] = 2100,
    };

    /// <param name="exemplar">A host character's event file; the new file takes its header.</param>
    /// <param name="identity">The host character the events are for, as "c" and four digits.</param>
    /// <param name="also">Host animation numbers to list without events when the source has none of that number.</param>
    /// <param name="only">When given, the source animations to carry and the host number each takes; the rest of the source's are left out.</param>
    /// <param name="moves">Host animations that take a source animation of another number, by host number.</param>
    internal static Written Write(byte[] sourceFile, byte[] exemplar, string identity, IEnumerable<long> also, IReadOnlyDictionary<long, long>? only = null, IReadOnlyDictionary<long, long>? moves = null)
    {
        var source = TAE.Read(sourceFile); var host = TAE.Read(exemplar);
        if (source.Format != TAE.TAEFormat.DS1 || source.BigEndian || host.Format != TAE.TAEFormat.SDT || host.BigEndian) throw new InvalidDataException("Unsupported event file format");
        // The host file names its character twice: by number, and in the name of a file the host no longer reads.
        string named = Path.GetFileNameWithoutExtension(host.SibName);
        if (identity.Length != 5 || identity[0] != 'c' || !int.TryParse(identity[1..], out int number) || named.Length != 5 || !int.TryParse(named[1..], out int exemplarNumber) || host.ID != 200000 + exemplarNumber)
            throw new InvalidDataException("A host event file is numbered after its character");
        host.ID = 200000 + number; host.SibName = host.SibName.Replace(named, identity, StringComparison.Ordinal);

        if (only?.Keys.Except(source.Animations.Select(a => a.ID)).Any() == true) throw new InvalidDataException("The source event file lacks a chosen animation");
        var bySource = only is null ? source.Animations.ToDictionary(a => HostNumber(a.ID)) : source.Animations.Where(a => only.ContainsKey(a.ID)).ToDictionary(a => only[a.ID]);
        // A host move numbered otherwise takes the source animation that does the same thing, where the source has it.
        var byOwnNumber = source.Animations.ToDictionary(a => a.ID);
        foreach (var (hostNumber, sourceNumber) in moves ?? new Dictionary<long, long>())
            if (byOwnNumber.TryGetValue(sourceNumber, out var same)) bySource[hostNumber] = same;
        var dropped = new SortedDictionary<int, int>(); int kept = 0;
        var animations = new List<TAE.Animation>();
        foreach (long listed in also.Concat(bySource.Keys).Distinct().Order())
        {
            bySource.TryGetValue(listed, out var from);
            TAE.Animation.AnimMiniHeader header = from?.MiniHeader switch
            {
                null => new TAE.Animation.AnimMiniHeader.Standard(),
                TAE.Animation.AnimMiniHeader.Standard standard => new TAE.Animation.AnimMiniHeader.Standard
                {
                    IsLoopByDefault = standard.IsLoopByDefault, AllowDelayLoad = standard.AllowDelayLoad, ImportsHKX = standard.ImportsHKX,
                    ImportHKXSourceAnimID = standard.ImportsHKX ? checked((int)HostNumber(standard.ImportHKXSourceAnimID)) : -1,
                },
                TAE.Animation.AnimMiniHeader.ImportOtherAnim import => new TAE.Animation.AnimMiniHeader.ImportOtherAnim { ImportFromAnimID = checked((int)HostNumber(import.ImportFromAnimID)), Unknown = import.Unknown },
                _ => throw new InvalidDataException("Unsupported source animation header"),
            };
            var animation = new TAE.Animation(listed, header, HostName(listed) + ".hkt");
            // The host files each kind of event of an animation under one group.
            var groups = new Dictionary<int, TAE.EventGroup>();
            foreach (var e in from?.Events ?? [])
            {
                byte[] given = e.GetParameterBytes(false);
                // lazy: only what a fight needs is carried. Effects, sounds, status effects, projectiles and the kinds whose
                // meaning is unread are counted and left out; each needs its own translation of what it refers to.
                byte[]? parameters = e.Type switch
                {
                    // A named moment: its number and two fields as the source has them, then the host's closing value.
                    0 when given.Length == 12 => [.. given, 6, 0, 0, 0],
                    // An attack: its kind, its index and the behaviour it asks for, then the host's closing fields.
                    1 when given.Length == 12 => [.. given, 0xff, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
                    // A blend from the animation before: the host states only its span.
                    16 => [],
                    // How fast it may turn, in degrees a second, then the host's closing fields.
                    224 when given.Length == 4 => [.. given, 0, 0xff, 0, 0],
                    _ => null,
                };
                if (parameters is null) { dropped[e.Type] = dropped.GetValueOrDefault(e.Type) + 1; continue; }
                if (!groups.TryGetValue(e.Type, out var group)) { groups[e.Type] = group = new TAE.EventGroup(e.Type); animation.EventGroups.Add(group); }
                animation.Events.Add(new TAE.Event(e.StartTime, e.EndTime, e.Type, 0, parameters, false) { Group = group }); kept++;
            }
            animations.Add(animation);
        }
        host.Animations = animations;
        return new(host.Write(), animations.Select(a => a.ID).ToArray(), kept, dropped);
    }
}
