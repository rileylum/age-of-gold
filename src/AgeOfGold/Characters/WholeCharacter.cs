using System.Text.RegularExpressions;
using AgeOfGold.Animation;
using AgeOfGold.Archives;
using AgeOfGold.Collision;
using SoulsFormats;

namespace AgeOfGold.Characters;

/// <summary>
/// Builds a source character whole for the host: its own body, skeleton, animations, physics and attack timing, under
/// a host identity the build owns. What the source has no counterpart for — a behaviour graph, an action script — is an
/// installed host enemy's, which plays animations by the numbers both games share.
/// </summary>
internal static class WholeCharacter
{
    /// <param name="Behaviour">An installed host enemy whose behaviour, action script, event-file header and mesh exemplars the character takes.</param>
    /// <param name="Physics">An installed host character whose physics file uses every kind of object the source's needs.</param>
    /// <param name="Gaze">The source bones the host turns to look at something, from the neck's first to its last and the spine's first to its last; null for a body that does not turn to look.</param>
    internal sealed record Hosts(string Behaviour, string Physics, (string NeckFrom, string NeckTo, string SpineFrom, string SpineTo)? Gaze);

    /// <param name="Files">The character's host files, by path within a build.</param>
    /// <param name="Stood">Host animation numbers the source has no animation for, which play its idle.</param>
    internal sealed record Built(SortedDictionary<string, byte[]> Files, int Bones, int Animations, int Stood, HostCharacterEvents.Written Events, HostCharacterPhysics.Written Physics);

    private static readonly Regex HostAnimation = new(@"^a(\d{3})_(\d{6})\.hkx$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SourceAnimation = new(@"^a(\d{2})_(\d{4})\.hkx$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <param name="name">The source character, as "c" and four digits.</param>
    /// <param name="identity">The host identity the build gives it; the host install must have no character of that identity.</param>
    /// <param name="parts">The source meshes to draw, each with the exemplar mesh of the behaviour host's model whose layout and material it takes.</param>
    internal static Built Write(Install source, HostArchive archive, string name, string identity, Hosts hosts, FLVER2 model, IReadOnlyList<HostCharacterModel.Part> parts)
    {
        foreach (string kind in new[] { "chrbnd", "anibnd", "behbnd" })
            if (archive.Contains($"chr/{identity}.{kind}.dcx")) throw new InvalidDataException("The host already has a character " + identity);
        string exemplar = hosts.Behaviour;
        string Renamed(string path) => path.Replace(exemplar, identity, StringComparison.OrdinalIgnoreCase);
        static string Member(BinderFile file) => file.Name.Replace('\\', '/').Split('/')[^1];
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);

        var sourceAnimations = BND3.Read(source.Read($"chr/{name}.anibnd.dcx"));
        byte[] sourceSkeleton = sourceAnimations.Files.SingleOrDefault(f => Member(f).Equals("skeleton.hkx", StringComparison.OrdinalIgnoreCase))?.Bytes ?? throw new InvalidDataException("Source character has no skeleton: " + name);
        var bones = SourcePoseReader.ReadSkeleton(sourceSkeleton).Select(b => b.Name).ToList();
        int Bone(string bone) => bones.IndexOf(bone) is >= 0 and var found ? found : throw new InvalidDataException($"Source character {name} has no bone {bone}");

        // The model binder: the body on its own nodes, and its own physics. The exemplar's cloth has no place on this
        // body; neither has its map from physics bodies to hit regions.
        // lazy: no hit regions, so a hit to the head counts as any other; the source's region map awaits the host's format being read.
        var binder = BND4.Read(archive.Read($"chr/{exemplar}.chrbnd.dcx"));
        var modelFile = binder.Files.SingleOrDefault(f => Member(f).Equals(exemplar + ".flver", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Host character has no model: " + exemplar);
        var physicsFile = binder.Files.SingleOrDefault(f => Member(f).Equals(exemplar + ".hkx", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Host character has no physics file: " + exemplar);
        modelFile.Bytes = HostCharacterModel.WriteWhole(modelFile.Bytes, model, parts, bones);
        var sourceBinder = BND3.Read(source.Read($"chr/{name}.chrbnd.dcx"));
        byte[] sourcePhysics = sourceBinder.Files.SingleOrDefault(f => Member(f).Equals(name + ".hkx", StringComparison.OrdinalIgnoreCase))?.Bytes ?? throw new InvalidDataException("Source character has no physics file: " + name);
        byte[] physicsExemplar = BND4.Read(archive.Read($"chr/{hosts.Physics}.chrbnd.dcx")).Files.SingleOrDefault(f => Member(f).Equals(hosts.Physics + ".hkx", StringComparison.OrdinalIgnoreCase))?.Bytes
            ?? throw new InvalidDataException("Host character has no physics file: " + hosts.Physics);
        var physics = HostCharacterPhysics.Write(sourcePhysics, physicsExemplar);
        if (physics.AnimationBones != bones.Count) throw new InvalidDataException($"Source character {name}'s physics file is for another skeleton");
        physicsFile.Bytes = physics.File;
        binder.Files = [modelFile, physicsFile];
        foreach (var file in binder.Files) file.Name = Renamed(file.Name);
        files.Add($"chr/{identity}.chrbnd.dcx", binder.Write());

        // The animation binder: the skeleton, the events, and every animation in the one binder. The host splits a
        // character's animations over further binders, each with a table of types its files share; a file that
        // describes itself needs neither.
        var animations = BND4.Read(archive.Read($"chr/{exemplar}.anibnd.dcx"));
        var skeletonFile = animations.Files.SingleOrDefault(f => Member(f).Equals("skeleton.hkx", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Host character has no skeleton: " + exemplar);
        var eventFile = animations.Files.SingleOrDefault(f => Member(f).EndsWith(".tae", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Host character has no event file: " + exemplar);
        skeletonFile.Bytes = HostAnimationWriter.WriteSkeleton(sourceSkeleton, identity, HostTagfile.Read(skeletonFile.Bytes, out _));
        if (!archive.Contains($"chr/{exemplar}_div00.anibnd.dcx")) throw new InvalidDataException("Host character keeps no animations of its own: " + exemplar);
        var split = BND4.Read(archive.Read($"chr/{exemplar}_div00.anibnd.dcx"));
        var animationTypes = HostTagfile.ReadShared(split.Files.Single(f => Member(f).EndsWith(".compendium", StringComparison.OrdinalIgnoreCase)).Bytes);
        var like = split.Files.First(f => HostAnimation.IsMatch(Member(f)));
        // The moves the host's shared graph may ask for are the exemplar's first set of animations.
        var asked = new SortedSet<long>();
        for (int part = 0; archive.Contains($"chr/{exemplar}_div{part:00}.anibnd.dcx"); part++)
            foreach (var file in BND4.Read(archive.Read($"chr/{exemplar}_div{part:00}.anibnd.dcx")).Files)
                if (HostAnimation.Match(Member(file)) is { Success: true } match && match.Groups[1].Value == "000") asked.Add(long.Parse(match.Groups[2].Value));
        var events = HostCharacterEvents.Write(sourceAnimations.Files.SingleOrDefault(f => Member(f).EndsWith(".tae", StringComparison.OrdinalIgnoreCase))?.Bytes ?? throw new InvalidDataException("Source character has no event file: " + name),
            eventFile.Bytes, identity, asked);
        eventFile.Bytes = events.File;
        var own = new SortedDictionary<long, byte[]>();
        foreach (var file in sourceAnimations.Files)
            if (SourceAnimation.Match(Member(file)) is { Success: true } match) own.Add(HostCharacterEvents.HostNumber(long.Parse(match.Groups[1].Value) * 10000 + long.Parse(match.Groups[2].Value)), file.Bytes);
        if (!own.TryGetValue(0, out var idle)) throw new InvalidDataException("Source character has no idle animation: " + name);
        // lazy: a move the host asks for that the source has no animation of the same number for plays the source's idle.
        // Match each such move (being struck, dying, turning on the spot) to the source's own once the shared graph's moves are read.
        byte[]? stand = null; int stood = 0;
        animations.Files = [skeletonFile, eventFile];
        foreach (long number in asked.Union(own.Keys).Order())
        {
            byte[] bytes;
            if (own.TryGetValue(number, out var sourceFile)) bytes = HostAnimationWriter.WriteAnimation(sourceFile, identity, animationTypes);
            else { bytes = stand ??= HostAnimationWriter.WriteAnimation(idle, identity, animationTypes); stood++; }
            animations.Files.Add(new BinderFile(like.Flags, checked((int)(1_000_000_000 + number)), like.Name[..^Member(like).Length] + HostCharacterEvents.HostName(number) + ".hkx", bytes));
        }
        foreach (var file in animations.Files) file.Name = Renamed(file.Name);
        animations.Files = animations.Files.OrderBy(f => f.ID).ToList();
        files.Add($"chr/{identity}.anibnd.dcx", animations.Write());

        var gaze = hosts.Gaze is { } named ? new HostCharacterBehaviour.Gaze(Bone(named.NeckFrom), Bone(named.NeckTo), Bone(named.SpineFrom), Bone(named.SpineTo)) : null;
        files.Add($"chr/{identity}.behbnd.dcx", HostCharacterBehaviour.Write(archive.Read($"chr/{exemplar}.behbnd.dcx"), exemplar, identity, gaze));
        // The action script the character's behaviour names; it only chooses among the shared graph's moves.
        files.Add($"action/script/{identity}.hks", archive.Read($"action/script/{exemplar}.hks"));
        return new(files, bones.Count, own.Count, stood, events, physics);
    }
}
