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

    /// <param name="exemplar">A host character's event file; the new file takes its header.</param>
    /// <param name="identity">The host character the events are for, as "c" and four digits.</param>
    /// <param name="also">Host animation numbers to list without events when the source has none of that number.</param>
    internal static Written Write(byte[] sourceFile, byte[] exemplar, string identity, IEnumerable<long> also)
    {
        var source = TAE.Read(sourceFile); var host = TAE.Read(exemplar);
        if (source.Format != TAE.TAEFormat.DS1 || source.BigEndian || host.Format != TAE.TAEFormat.SDT || host.BigEndian) throw new InvalidDataException("Unsupported event file format");
        // The host file names its character twice: by number, and in the name of a file the host no longer reads.
        string named = Path.GetFileNameWithoutExtension(host.SibName);
        if (identity.Length != 5 || identity[0] != 'c' || !int.TryParse(identity[1..], out int number) || named.Length != 5 || !int.TryParse(named[1..], out int exemplarNumber) || host.ID != 200000 + exemplarNumber)
            throw new InvalidDataException("A host event file is numbered after its character");
        host.ID = 200000 + number; host.SibName = host.SibName.Replace(named, identity, StringComparison.Ordinal);

        var bySource = source.Animations.ToDictionary(a => HostNumber(a.ID));
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
