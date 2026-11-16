using System.Numerics;
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
    internal sealed record Built(SortedDictionary<string, byte[]> Files, int Bones, int Animations, int Stood, HostCharacterEvents.Written Events, HostCharacterPhysics.Written Physics)
    {
        /// <summary>How many bones that carry cloth have what follows them hung where it would come to rest.</summary>
        internal int Hung { get; init; }
    }

    private static readonly Regex HostAnimation = new(@"^a(\d{3})_(\d{6})\.hkx$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SourceAnimation = new(@"^a(\d{2})_(\d{4})\.hkx$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <param name="name">The source character, as "c" and four digits.</param>
    /// <param name="identity">The host identity the build gives it; the host install must have no character of that identity.</param>
    /// <param name="parts">The source meshes to draw, each with the exemplar mesh of the behaviour host's model whose layout and material it takes.</param>
    internal static Built Write(Install source, HostArchive archive, string name, string identity, Hosts hosts, FLVER2 model, IReadOnlyList<HostCharacterModel.Part> parts)
    {
        var animations = BND3.Read(source.Read($"chr/{name}.anibnd.dcx"));
        var own = new SortedDictionary<long, byte[]>();
        foreach (var file in animations.Files)
            if (SourceAnimation.Match(Member(file)) is { Success: true } match) own.Add(HostCharacterEvents.HostNumber(long.Parse(match.Groups[1].Value) * 10000 + long.Parse(match.Groups[2].Value)), file.Bytes);
        // The moves the games number differently: the host's number takes the source animation that does the same thing.
        var bySourceNumber = own.ToDictionary(o => o.Key, o => o.Value);
        foreach (var (hostNumber, sourceNumber) in HostCharacterEvents.Moves)
            if (bySourceNumber.TryGetValue(HostCharacterEvents.HostNumber(sourceNumber), out var same)) own[hostNumber] = same;
        var files = BND3.Read(source.Read($"chr/{name}.chrbnd.dcx"));
        // A character with cloth has a cloth file beside its physics file. What its chains carry is hung where it would
        // come to rest (see HostCharacterCloth).
        var hung = files.Files.SingleOrDefault(f => Member(f).Equals(name + "_c.hkx", StringComparison.OrdinalIgnoreCase)) is { } cloth ? Hung(cloth.Bytes, model) : null;
        return Assemble(archive, name, identity, hosts, One(animations, "skeleton.hkx", name, "skeleton"), One(files, name + ".hkx", name, "physics file"),
            animations.Files.SingleOrDefault(f => Member(f).EndsWith(".tae", StringComparison.OrdinalIgnoreCase))?.Bytes ?? throw new InvalidDataException("Source character has no event file: " + name),
            own, null, HostCharacterEvents.Moves, (hostModel, bones) => HostCharacterModel.WriteWhole(hostModel, model, parts, bones, hung)) with { Hung = hung?.Count ?? 0 };
    }

    /// <summary>
    /// A source human whole. Every human shares one skeleton, one physics file and one great stock of animations; a
    /// person is that skeleton under the models of what it wears and holds, standing in the pose it starts in.
    /// </summary>
    /// <param name="skeleton">The source human's model: the skeleton's nodes and attachment points, with no mesh.</param>
    /// <param name="pieces">The models of what the person wears and holds.</param>
    /// <param name="pose">The source animation the person starts in; it stands as the host's idle.</param>
    internal static Built WritePerson(Install source, HostArchive archive, string identity, Hosts hosts, FLVER2 skeleton, IReadOnlyList<HostCharacterModel.Body> pieces, int pose)
    {
        const string human = "c0000";
        var main = BND3.Read(source.Read($"chr/{human}.anibnd.dcx"));
        // The first set of the human's animations is kept in three binders by how much detail each is worth.
        string clip = $"a00_{pose:D4}.hkx";
        byte[] posed = new[] { "hi", "md", "lo" }.Select(detail => BND3.Read(source.Read($"chr/{human}_a00_{detail}.anibnd.dcx")).Files.SingleOrDefault(f => Member(f).Equals(clip, StringComparison.OrdinalIgnoreCase))?.Bytes)
            .FirstOrDefault(bytes => bytes is not null) ?? throw new InvalidDataException("The source human has no animation " + clip);
        // lazy: only the pose a person starts in, as every move. People do not walk, fight or flinch until the human's
        // sets of animations, chosen by what it holds, are matched to the host's moves.
        return Assemble(archive, human, identity, hosts, One(main, "skeleton.hkx", human, "skeleton"), One(BND3.Read(source.Read($"chr/{human}.chrbnd.dcx")), human + ".hkx", human, "physics file"),
            One(main, "a00.tae", human, "first event file"), new SortedDictionary<long, byte[]> { [0] = posed }, new Dictionary<long, long> { [pose] = 0 }, null,
            (hostModel, bones) => HostCharacterModel.WritePieces(hostModel, skeleton, pieces, bones));
    }

    private static Dictionary<string, Matrix4x4> Hung(byte[] cloth, FLVER2 model)
    {
        var (moving, still) = HostCharacterCloth.Bodies(cloth);
        var chains = HostCharacterCloth.Chains(moving, still, model.Nodes.Select(n => (n.Name, (int)n.ParentIndex)).ToList());
        var world = new Matrix4x4[model.Nodes.Count];
        for (int i = 0; i < world.Length; i++)
        {
            int parent = model.Nodes[i].ParentIndex;
            world[i] = parent < 0 ? model.Nodes[i].ComputeLocalTransform() : parent < i ? model.Nodes[i].ComputeLocalTransform() * world[parent] : throw new InvalidDataException("Source model lists a node before its parent");
        }
        return HostCharacterCloth.Hung(chains, chains.SelectMany(c => c.Bones).ToDictionary(b => b, b => world[model.Nodes.FindIndex(n => n.Name == b)].Translation, StringComparer.Ordinal));
    }

    private static string Member(BinderFile file) => file.Name.Replace('\\', '/').Split('/')[^1];

    private static byte[] One(BND3 binder, string member, string name, string what) =>
        binder.Files.SingleOrDefault(f => Member(f).Equals(member, StringComparison.OrdinalIgnoreCase))?.Bytes ?? throw new InvalidDataException($"Source character has no {what}: {name}");

    /// <param name="own">The source's animations, by host number; number nought is its idle.</param>
    /// <param name="only">When given, the source animations whose events are carried and the host number each takes.</param>
    /// <param name="moves">Host moves that take the events of a source animation of another number.</param>
    /// <param name="model">Writes the body into the behaviour host's model, given the names of the skeleton's bones.</param>
    private static Built Assemble(HostArchive archive, string name, string identity, Hosts hosts, byte[] sourceSkeleton, byte[] sourcePhysics, byte[] sourceEvents,
        SortedDictionary<long, byte[]> own, IReadOnlyDictionary<long, long>? only, IReadOnlyDictionary<long, long>? moves, Func<byte[], IReadOnlyCollection<string>, byte[]> model)
    {
        foreach (string kind in new[] { "chrbnd", "anibnd", "behbnd" })
            if (archive.Contains($"chr/{identity}.{kind}.dcx")) throw new InvalidDataException("The host already has a character " + identity);
        string exemplar = hosts.Behaviour;
        string Renamed(string path) => path.Replace(exemplar, identity, StringComparison.OrdinalIgnoreCase);
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var bones = SourcePoseReader.ReadSkeleton(sourceSkeleton).Select(b => b.Name).ToList();
        int Bone(string bone) => bones.IndexOf(bone) is >= 0 and var found ? found : throw new InvalidDataException($"Source character {name} has no bone {bone}");

        // The model binder: the body on its own nodes, and its own physics. The exemplar's cloth has no place on this
        // body; neither has its map from physics bodies to hit regions.
        // lazy: no hit regions, so a hit to the head counts as any other; the source's region map awaits the host's format being read.
        var binder = BND4.Read(archive.Read($"chr/{exemplar}.chrbnd.dcx"));
        var modelFile = binder.Files.SingleOrDefault(f => Member(f).Equals(exemplar + ".flver", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Host character has no model: " + exemplar);
        var physicsFile = binder.Files.SingleOrDefault(f => Member(f).Equals(exemplar + ".hkx", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Host character has no physics file: " + exemplar);
        modelFile.Bytes = model(modelFile.Bytes, bones);
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
        // The moves the host may ask of the character are the exemplar's own animations, in every set it has.
        var asked = new SortedSet<long>();
        for (int part = 0; archive.Contains($"chr/{exemplar}_div{part:00}.anibnd.dcx"); part++)
            foreach (var file in BND4.Read(archive.Read($"chr/{exemplar}_div{part:00}.anibnd.dcx")).Files)
                if (HostAnimation.Match(Member(file)) is { Success: true } match) asked.Add(long.Parse(match.Groups[1].Value) * 1000000 + long.Parse(match.Groups[2].Value));
        var events = HostCharacterEvents.Write(sourceEvents, eventFile.Bytes, identity, asked, only, moves, HostCharacterEvents.HostsOwn);
        eventFile.Bytes = events.File;
        if (!own.TryGetValue(0, out var idle)) throw new InvalidDataException("Source character has no idle animation: " + name);
        // lazy: a move the host asks for that the source has no animation of the same number for plays the source's idle.
        // Match each such move (being struck, dying, turning on the spot) to the source's own once the shared graph's moves are read.
        byte[]? stand = null; int stood = 0;
        // The binder keeps its other members: without the empty one that marks its version the host does not stand the
        // character at all. Only the lists the host's packer left behind, which name the split binders, are dropped.
        animations.Files.RemoveAll(f => Member(f).EndsWith(".blf", StringComparison.OrdinalIgnoreCase));
        if (animations.Files.Any(f => HostAnimation.IsMatch(Member(f)))) throw new InvalidDataException("Host character keeps animations in its main binder: " + exemplar);
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
