using System.Numerics;
using AgeOfGold;
using AgeOfGold.Animation;
using AgeOfGold.Archives;
using AgeOfGold.Characters;
using AgeOfGold.Collision;
using HKLib.hk2018;
using SoulsFormats;

/// <summary>A source character built whole for the host: its events, its physics shapes, and, from the installs, every file.</summary>
internal static class WholeCharacterCheck
{
    internal static void Run()
    {
        // A capsule from a to b: a thin box about the axis, a hundredth of the radius thick, numbered by end and side.
        var (corners, planes) = HostCharacterPhysics.CapsuleBox(new(0, 0.5f, 0), new(0, -0.5f, 0), 0.2f);
        static void Near(float actual, float expected, string what) { if (MathF.Abs(actual - expected) > 1e-5f) throw new Exception($"{what}: {actual}, expected {expected}"); }
        for (int i = 0; i < 8; i++)
        {
            // Odd corners lie at a's end, even at b's; each lies the box's thinness beyond the axis every way.
            Near(corners[i].Y, (i & 1) != 0 ? 0.502f : -0.502f, "corner end " + i);
            Near(MathF.Abs(corners[i].X), 0.002f, "corner side " + i); Near(MathF.Abs(corners[i].Z), 0.002f, "corner side " + i);
            if (BitConverter.SingleToInt32Bits(corners[i].W) != (0x3f000000 | i)) throw new Exception("A corner does not carry its number");
            // Every corner lies on three of the planes and inside the others.
            int on = planes.Count(p => MathF.Abs(Vector3.Dot(new(p.X, p.Y, p.Z), new(corners[i].X, corners[i].Y, corners[i].Z)) + p.W) < 1e-5f);
            if (on != 3 || planes.Any(p => Vector3.Dot(new(p.X, p.Y, p.Z), new(corners[i].X, corners[i].Y, corners[i].Z)) + p.W > 1e-5f)) throw new Exception("A corner is not where three planes of the box meet");
        }
        if (corners.Select(c => new Vector3(c.X, c.Y, c.Z)).Distinct().Count() != 8) throw new Exception("The box's corners are not distinct");
        // The first plane faces out of b's end, the second out of a's; a slanted capsule still gets a square box.
        if (!(planes[0].Y < -0.99f) || !(planes[1].Y > 0.99f)) throw new Exception("The end planes do not face out of their ends");
        var slanted = HostCharacterPhysics.CapsuleBox(new(1, 2, 3), new(2, 4, 1), 0.5f);
        var axis = Vector3.Normalize(new Vector3(1, 2, -2));
        foreach (var p in slanted.Planes.Skip(2)) Near(Vector3.Dot(new(p.X, p.Y, p.Z), axis), 0, "side plane along the axis");
        Near(Vector3.Distance(new(slanted.Corners[0].X, slanted.Corners[0].Y, slanted.Corners[0].Z), new(slanted.Corners[1].X, slanted.Corners[1].Y, slanted.Corners[1].Z)), 3.01f, "box length");
        try { HostCharacterPhysics.CapsuleBox(Vector3.Zero, Vector3.UnitX, 0); throw new Exception("A capsule without a radius was accepted"); }
        catch (InvalidDataException) { }
        // A box (a held shield or weapon) is stood for by the capsule along its longest side that is as wide as its next longest.
        var stood = HostCharacterPhysics.BoxCapsule(new(0.085f, 0.014f, 0.218f), new(0.1f, 0, 0.5f));
        Near(stood.Radius, 0.085f, "box capsule radius"); Near(stood.A.X, 0.1f, "box capsule centre"); Near(stood.A.Y, 0, "box capsule centre");
        // The capsule's rounded ends reach the box's ends: its axis stops a radius short of them.
        Near(MathF.Min(stood.A.Z, stood.B.Z), 0.5f - (0.218f - 0.085f), "box capsule end"); Near(MathF.Max(stood.A.Z, stood.B.Z), 0.5f + (0.218f - 0.085f), "box capsule end");
        // A box no longer than it is wide becomes a ball.
        var ball = HostCharacterPhysics.BoxCapsule(new(0.2f, 0.2f, 0.1f), Vector3.Zero);
        if (ball.A != ball.B || ball.Radius != 0.2f) throw new Exception("A squat box is not stood for by a ball");
        try { HostCharacterPhysics.BoxCapsule(new(0.1f, 0, 0.1f), Vector3.Zero); throw new Exception("A box without thickness was accepted"); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: a capsule becomes the thin numbered box the host stores, square to its axis");

        // Events: an authored source file with an attack, a named moment, a blend, a turn limit and a sound, over an authored host file.
        TAE.Event Event(int kind, float from, float to, params byte[] parameters) => new(from, to, kind, 0, parameters, false);
        var source = new TAE { Format = TAE.TAEFormat.DS1, ID = 202540, Flags = [1, 0, 1, 2, 2, 0, 1, 1], SkeletonName = "skeleton.HKXwin", SibName = "c2540.sib", Animations = [] };
        var attack = new TAE.Animation(3000, new TAE.Animation.AnimMiniHeader.Standard(), "a00_3000.hkxwin");
        attack.Events.AddRange([Event(1, 0.8f, 1, 0, 0, 0, 0, 0, 0, 0, 0, 100, 0, 0, 0), Event(0, 0, 0.3f, 7, 0, 0, 0, 0, 0, 0, 0, 255, 255, 255, 255), Event(16, 0, 0.25f, new byte[16]),
            Event(224, 0, 0.5f, BitConverter.GetBytes(90f)), Event(128, 0.4f, 0.5f, 1, 0, 0, 0, 5, 0, 0, 0), Event(0, 1.2f, 1.5f, 23, 0, 0, 0, 0, 0, 0, 0, 255, 255, 255, 255)]);
        var second = new TAE.Animation(13001, new TAE.Animation.AnimMiniHeader.ImportOtherAnim { ImportFromAnimID = 3000 }, "a01_3001.hkxwin");
        source.Animations.AddRange([attack, second]);
        var host = new TAE { Format = TAE.TAEFormat.SDT, ID = 204300, Flags = [1, 0, 1, 2, 2, 1, 1, 1], SkeletonName = "Skeleton.hkt", SibName = "c4300.sib", EventBank = 36, Animations = [] };
        var hostIdle = new TAE.Animation(0, new TAE.Animation.AnimMiniHeader.Standard(), "a000_000000.hkt");
        var hostGroup = new TAE.EventGroup(224); hostIdle.EventGroups.Add(hostGroup);
        hostIdle.Events.Add(new TAE.Event(0, 1, 224, 0, [0, 0, 160, 64, 0, 255, 0, 0], false) { Group = hostGroup });
        host.Animations.Add(hostIdle);
        byte[] sourceBytes = source.Write(), hostBytes = host.Write();
        var written = HostCharacterEvents.Write(sourceBytes, hostBytes, "c6405", [0, 2000, 3000]);
        var events = TAE.Read(written.File);
        if (events.Format != TAE.TAEFormat.SDT || events.ID != 206405 || events.SibName != "c6405.sib" || events.EventBank != 36 || events.SkeletonName != "Skeleton.hkt")
            throw new Exception("The event file does not carry the host's header under the new character's number");
        // Every number asked for is listed, and the source's own beside them, in the host's numbering.
        if (!written.Numbers.SequenceEqual([0, 2000, 3000, 1003001]) || !events.Animations.Select(a => (a.ID, a.AnimFileName)).SequenceEqual([(0L, "a000_000000.hkt"), (2000L, "a000_002000.hkt"), (3000L, "a000_003000.hkt"), (1003001L, "a001_003001.hkt")]))
            throw new Exception("The event file does not list the asked and the source's animations: " + string.Join(",", written.Numbers));
        if (events.Animations[0].Events.Count != 0 || events.Animations[1].Events.Count != 0 || events.Animations[3].MiniHeader is not TAE.Animation.AnimMiniHeader.ImportOtherAnim { ImportFromAnimID: 3000 })
            throw new Exception("An animation without source events gained some, or a borrowed animation lost its lender");
        var carried = events.Animations[2].Events.OrderBy(e => e.Type).ThenBy(e => e.StartTime).Select(e => (e.Type, e.StartTime, e.EndTime, Hex: Convert.ToHexString(e.GetParameterBytes(false)), Group: e.Group?.GroupType)).ToArray();
        string[] expected = ["0700000000000000FFFFFFFF06000000", "1700000000000000FFFFFFFF06000000", "000000000000000064000000FF0000000000000000000000", "", "0000B44200FF0000"];
        if (carried.Length != 5 || !carried.Select(c => c.Type).SequenceEqual([0, 0, 1, 16, 224]) || carried.Any(c => c.Group != c.Type) || carried[2].StartTime != 0.8f || carried[2].EndTime != 1
            || Enumerable.Range(0, 5).Any(i => !carried[i].Hex.StartsWith(expected[i], StringComparison.Ordinal) || carried[i].Hex[expected[i].Length..].Any(c => c != '0')))
            throw new Exception("Events were not carried in the host's layout: " + string.Join(" | ", carried.Select(c => $"{c.Type}:{c.Hex}")));
        if (events.Animations[2].EventGroups.Count != 4 || written.Kept != 5 || written.Dropped.Count != 1 || written.Dropped[128] != 1) throw new Exception("Events were not grouped by kind, or the count of kept and dropped events is wrong");
        if (!written.File.SequenceEqual(HostCharacterEvents.Write(sourceBytes, hostBytes, "c6405", [0, 2000, 3000]).File)) throw new Exception("Event file differs across runs");
        if (HostCharacterEvents.HostNumber(13001) != 1003001 || HostCharacterEvents.HostName(1003001) != "a001_003001" || HostCharacterEvents.HostName(3000) != "a000_003000") throw new Exception("Animation numbering differs");
        // Chosen animations only, each under the host number given: a person's starting pose stands as its idle.
        var posed = TAE.Read(HostCharacterEvents.Write(sourceBytes, hostBytes, "c6405", [0, 2000], new Dictionary<long, long> { [3000] = 0 }).File);
        if (!posed.Animations.Select(a => (a.ID, a.AnimFileName, a.Events.Count)).SequenceEqual([(0L, "a000_000000.hkt", 5), (2000L, "a000_002000.hkt", 0)]))
            throw new Exception("Chosen animations were not carried alone under their given numbers: " + string.Join(",", posed.Animations.Select(a => $"{a.ID}/{a.Events.Count}")));
        try { HostCharacterEvents.Write(sourceBytes, hostBytes, "c6405", [], new Dictionary<long, long> { [4000] = 0 }); throw new Exception("A chosen animation the source lacks was accepted"); }
        catch (InvalidDataException) { }
        // The two games number their common moves differently: each host move takes the source animation that does the same thing.
        // The source's walk is 200 and its quarter turn 602; the host's are 2000 and 5000. A source animation whose own number a
        // host move holds (its flinch, 2000) gives way to the move's.
        if (HostCharacterEvents.Moves[2000] != 200 || HostCharacterEvents.Moves[2003] != 203 || HostCharacterEvents.Moves[2100] != 500 || HostCharacterEvents.Moves[5000] != 602 || HostCharacterEvents.Moves[5013] != 605
            || HostCharacterEvents.Moves.Keys.Any(k => k is >= 3000 and < 5000)) throw new Exception("The moves the games number differently are not matched as measured");
        // Being struck: the host's small, middling and heavy flinches take the source's by rising length; a heavy one by which
        // way it throws the body (measured on the skeleton); being blown back by how far. Dying takes the source's death.
        var moves = HostCharacterEvents.Moves;
        if (Enumerable.Range(8030, 8).Any(n => moves[n] != 2000) || moves[8040] != 2006 || moves[8045] != 2007 || moves[8041] != 2004 || moves[8042] != 2005
            || moves[8050] != 2012 || moves[8055] != 2013 || moves[8051] != 2010 || moves[8056] != 2010 || moves[8052] != 2011 || moves[8057] != 2011
            || moves[8100] != 2020 || moves[8101] != 2014 || moves[10000] != 2100) throw new Exception("Being struck and dying are not matched as measured");
        var walk = new TAE.Animation(200, new TAE.Animation.AnimMiniHeader.Standard(), "a00_0200.hkxwin"); walk.Events.Add(Event(224, 0, 1, BitConverter.GetBytes(45f)));
        var flinch = new TAE.Animation(2000, new TAE.Animation.AnimMiniHeader.Standard(), "a00_2000.hkxwin"); flinch.Events.Add(Event(0, 0, 0.2f, 9, 0, 0, 0, 0, 0, 0, 0, 255, 255, 255, 255));
        source.Animations.AddRange([walk, flinch]);
        var moved = TAE.Read(HostCharacterEvents.Write(source.Write(), hostBytes, "c6405", [2000, 2001], null, new Dictionary<long, long> { [2000] = 200, [2001] = 201 }).File);
        var hostWalk = moved.Animations.Single(a => a.ID == 2000);
        if (hostWalk.Events.Single().Type != 224 || moved.Animations.Single(a => a.ID == 200).Events.Count != 1 || moved.Animations.Single(a => a.ID == 2001).Events.Count != 0)
            throw new Exception("A host move does not take the events of the source animation that does the same thing");
        source.Animations.RemoveRange(source.Animations.Count - 2, 2);
        foreach (var bad in new Action[]
        {
            () => HostCharacterEvents.Write(hostBytes, hostBytes, "c6405", []),
            () => HostCharacterEvents.Write(sourceBytes, sourceBytes, "c6405", []),
            () => HostCharacterEvents.Write(sourceBytes, hostBytes, "6405", []),
            () => { host.ID = 204301; try { HostCharacterEvents.Write(sourceBytes, host.Write(), "c6405", []); } finally { host.ID = 204300; } },
        })
        {
            try { bad(); throw new Exception("An unusable event conversion was accepted"); }
            catch (InvalidDataException) { }
        }
        Console.WriteLine("PASS: a source character's attacks, named moments, blends and turn limits become host events under its own number; the rest are counted");
    }

    /// <summary>Against both installs: the hollow soldier's host files are written, and read back as the host's own kinds of file.</summary>
    internal static void Installs()
    {
        var source = new Install(Environment.GetEnvironmentVariable("AOG_DARK_SOULS_REMASTERED") ?? throw new Exception("Set AOG_DARK_SOULS_REMASTERED"), "DarkSoulsRemastered.exe");
        var host = new Install(Environment.GetEnvironmentVariable("AOG_ELDEN_RING") ?? throw new Exception("Set AOG_ELDEN_RING"), "eldenring.exe");
        Oodle.SearchPaths.Add(host.Root);
        var archive = new HostArchive(host);
        const string name = "c2540", identity = "c6405", exemplar = "c4300";
        var hosts = new WholeCharacter.Hosts(exemplar, "c3020", ("Neck", "Head", "Spine", "Spine1"));
        static string Member(BinderFile file) => file.Name.Replace('\\', '/').Split('/')[^1];
        var sourceModel = AgeOfGold.Scene.ModelReader.Read(BND3.Read(source.Read($"chr/{name}.chrbnd.dcx")).Files.Single(f => Member(f) == name + ".flver").Bytes).Model;
        var hostModel = FLVER2.Read(BND4.Read(archive.Read($"chr/{exemplar}.chrbnd.dcx")).Files.Single(f => Member(f) == exemplar + ".flver").Bytes);
        int like = hostModel.Meshes.FindIndex(m => hostModel.Materials[m.MaterialIndex].MTD.Contains("_Belt_Leather.", StringComparison.Ordinal));
        // The body's unmarked meshes, each drawn with the exemplar's own textures.
        var parts = Enumerable.Range(0, sourceModel.Meshes.Count).Where(i => !sourceModel.Materials[sourceModel.Meshes[i].MaterialIndex].Name.StartsWith('#')).Select(i => new HostCharacterModel.Part(i, like, new Dictionary<string, string>())).ToArray();
        var built = WholeCharacter.Write(source, archive, name, identity, hosts, sourceModel, parts);
        if (!built.Files.Keys.SequenceEqual(["action/script/c6405.hks", "chr/c6405.anibnd.dcx", "chr/c6405.behbnd.dcx", "chr/c6405.chrbnd.dcx"])) throw new Exception("Unexpected files: " + string.Join(", ", built.Files.Keys));
        var again = WholeCharacter.Write(source, archive, name, identity, hosts, sourceModel, parts);
        if (built.Files.Any(f => !f.Value.SequenceEqual(again.Files[f.Key]))) throw new Exception("A whole character's files differ across runs");

        // The model binder: the body on the source's nodes, and physics the Havok library reads as the host's.
        var binder = BND4.Read(built.Files["chr/c6405.chrbnd.dcx"]);
        if (!binder.Files.Select(Member).SequenceEqual(["c6405.flver", "c6405.HKX"], StringComparer.OrdinalIgnoreCase)) throw new Exception("Unexpected model binder members: " + string.Join(", ", binder.Files.Select(Member)));
        var model = FLVER2.Read(binder.Files[0].Bytes);
        if (model.Nodes.Count != sourceModel.Nodes.Count || model.Meshes.Count != parts.Length || model.Dummies.Count != sourceModel.Dummies.Count || !model.Nodes.Select(n => n.Name).SequenceEqual(sourceModel.Nodes.Select(n => n.Name)))
            throw new Exception("The model is not the source's body on its own nodes");
        HostTagfile.Read(binder.Files[1].Bytes, out var physicsRoot);
        var ragdoll = physicsRoot.m_namedVariants.Select(v => v.m_variant).OfType<hknpRagdollData>().First();
        var mappers = physicsRoot.m_namedVariants.Select(v => v.m_variant).OfType<hkaSkeletonMapper>().ToArray();
        if (ragdoll.m_bodyCinfos.Count != built.Physics.Bodies || built.Physics.Bodies < 10 || ragdoll.m_constraintCinfos.Count != built.Physics.Bodies - 1 || ragdoll.m_bodyCinfos.Any(b => b.m_shape is not hknpCapsuleShape { m_vertices.Count: 8 } || !(b.m_mass > 0))
            || ragdoll.m_skeleton!.m_bones.Count != built.Physics.RagdollBones || ragdoll.m_boneToBodyMap.Count != built.Physics.RagdollBones || ragdoll.m_materials.Count != built.Physics.Bodies || ragdoll.m_motionProperties.Count != built.Physics.Bodies)
            throw new Exception("The physics file does not hold the source's bodies and joints");
        if (ragdoll.m_constraintCinfos.Any(c => c.m_bodyA.m_serialAndIndex >= ragdoll.m_bodyCinfos.Count || c.m_bodyB.m_serialAndIndex >= ragdoll.m_bodyCinfos.Count || c.m_bodyA.m_serialAndIndex == c.m_bodyB.m_serialAndIndex))
            throw new Exception("A joint does not join two of the bodies");
        if (mappers.Length != 2 || mappers.Any(m => m.m_mapping.m_simpleMappings.Count == 0 || m.m_mapping.m_skeletonA == m.m_mapping.m_skeletonB) || !mappers.Any(m => m.m_mapping.m_skeletonA == ragdoll.m_skeleton) || !mappers.Any(m => m.m_mapping.m_skeletonB == ragdoll.m_skeleton)
            || mappers.SelectMany(m => new[] { m.m_mapping.m_skeletonA, m.m_mapping.m_skeletonB }).Where(s => s != ragdoll.m_skeleton).Any(s => s!.m_bones.Count != built.Bones))
            throw new Exception("The mappers do not join the ragdoll's skeleton and the animation skeleton both ways");
        // The first body of the source is rebuilt exactly: a capsule's ends are the stored a and b, its radius beside a.
        var first = (hknpCapsuleShape)ragdoll.m_bodyCinfos[0].m_shape!;
        var (corners, _) = HostCharacterPhysics.CapsuleBox(new(first.m_a.X, first.m_a.Y, first.m_a.Z), new(first.m_b.X, first.m_b.Y, first.m_b.Z), first.m_a.W);
        if (!first.m_vertices.SequenceEqual(corners) || MathF.Abs(first.m_convexRadius - first.m_a.W * 0.99f) > 1e-6f) throw new Exception("A body's capsule is not stored as its box");

        // The animation binder: skeleton, events and each animation, every one a file that describes itself.
        var animations = BND4.Read(built.Files["chr/c6405.anibnd.dcx"]);
        HostTagfile.Read(animations.Files.Single(f => Member(f).Equals("skeleton.hkx", StringComparison.OrdinalIgnoreCase)).Bytes, out var skeletonRoot);
        var skeleton = ((hkaAnimationContainer)skeletonRoot.m_namedVariants.Single().m_variant!).m_skeletons.Single()!;
        if (skeleton.m_name != identity || skeleton.m_bones.Count != built.Bones || built.Bones != 49) throw new Exception("The skeleton is not the source's under the new name");
        var events = TAE.Read(animations.Files.Single(f => Member(f).EndsWith(".tae", StringComparison.Ordinal)).Bytes);
        var clips = animations.Files.Where(f => Member(f).EndsWith(".hkx", StringComparison.Ordinal) && Member(f).StartsWith('a')).ToArray();
        if (events.ID != 206405 || !events.Animations.Select(a => a.ID).SequenceEqual(built.Events.Numbers) || clips.Length != built.Animations + built.Stood || built.Animations < 100 || built.Stood == 0
            || !clips.Select(Member).SequenceEqual(clips.Select(Member).Order(StringComparer.Ordinal)) || clips.Any(f => f.ID != 1_000_000_000 + (int)(long.Parse(Member(f)[1..4]) * 1000000 + long.Parse(Member(f)[5..11]))))
            throw new Exception("The animation binder does not hold the listed animations in order");
        // Whatever else the exemplar's binder holds is kept, but for its packing lists: the host needs the binder's version marker.
        var kept = BND4.Read(archive.Read($"chr/{exemplar}.anibnd.dcx")).Files.Select(Member).Where(m => !m.EndsWith(".blf", StringComparison.OrdinalIgnoreCase) && !m.EndsWith(".tae", StringComparison.Ordinal)).ToArray();
        if (!kept.Contains("ver_0001.txt") || kept.Except(animations.Files.Select(Member), StringComparer.OrdinalIgnoreCase).Any() || animations.Files.Any(f => Member(f).EndsWith(".blf", StringComparison.OrdinalIgnoreCase)))
            throw new Exception("The animation binder does not keep the exemplar's other members, or keeps its packing lists");
        var swing = events.Animations.Single(a => a.ID == 3000);
        if (swing.Events.Count(e => e.Type == 1) != 1 || built.Events.Kept < 100) throw new Exception("The first attack's events were not carried");
        HostTagfile.Read(clips.Single(f => Member(f) == "a000_003000.hkx").Bytes, out var clipRoot);
        var clip = (hkaAnimationContainer)clipRoot.m_namedVariants.Single().m_variant!;
        if (clip.m_animations.Single() is not hkaSplineCompressedAnimation { m_numberOfTransformTracks: 49, m_data.Count: > 0 } || clip.m_bindings.Single()!.m_originalSkeletonName != identity || clip.m_bindings.Single()!.m_transformTrackToBoneIndices.Count != 49)
            throw new Exception("An animation is not bound to the new skeleton track for bone");
        // A move the source lacks plays its idle: the same bytes as the idle's own file.
        var stoodFor = clips.First(f => !built.Events.Numbers.Contains(0) || BitConverter.ToString(f.Bytes) == BitConverter.ToString(clips.Single(c => Member(c) == "a000_000000.hkx").Bytes) && Member(f) != "a000_000000.hkx");
        Console.WriteLine($"  {built.Animations} source animations, {built.Stood} host moves standing idle (first {Member(stoodFor)}); events kept {built.Events.Kept}, dropped {string.Join(" ", built.Events.Dropped.Select(d => d.Key + "x" + d.Value))}");
        Console.WriteLine($"  physics: {built.Physics.Bodies} bodies, {built.Physics.Joints} joints, {built.Physics.RagdollBones} ragdoll bones; {built.Physics.Kept.Length} joint values kept from the host");

        // The behaviour binder: every name the new character's, no limb placement, the gaze on the source's bones.
        var behaviour = BND4.Read(built.Files["chr/c6405.behbnd.dcx"]);
        if (behaviour.Files.Count(f => Member(f).Equals("c6405.hkx", StringComparison.OrdinalIgnoreCase)) != 3 || behaviour.Files.Any(f => f.Name.Contains(exemplar, StringComparison.OrdinalIgnoreCase))) throw new Exception("The behaviour binder still names its exemplar");
        var kinds = behaviour.Files.Where(f => Member(f).Equals("c6405.hkx", StringComparison.OrdinalIgnoreCase)).Select(f => { HostTagfile.Read(f.Bytes, out var root); return root.m_namedVariants.Single().m_variant!; }).ToArray();
        var character = kinds.OfType<hkbCharacterData>().Single(); var names = character.m_stringData!;
        int Property(string property) => character.m_characterPropertyValues!.m_wordVariableValues[names.m_characterPropertyNames.IndexOf(property)].m_value;
        if (names.m_name != identity || !names.m_luaFiles.Contains("c6405.hks") || names.m_luaFiles.Any(n => n!.Contains(exemplar)) || character.m_propertySheets.Any(s => s is hkbFootIkDriverInfo or hkbHandIkDriverInfo)
            || skeleton.m_bones[Property("RefTwistNeckStart")].m_name != "Neck" || skeleton.m_bones[Property("RefTwistNeckEnd")].m_name != "Head" || skeleton.m_bones[Property("RefTwistSpineStart")].m_name != "Spine" || skeleton.m_bones[Property("RefTwistSpineEnd")].m_name != "Spine1")
            throw new Exception("The character file is not the new character's, on the source's bones");
        if (kinds.OfType<hkbBehaviorGraph>().Single().m_rootGenerator is not hkbStateMachine { m_states: [{ m_generator: hkbBehaviorReferenceGenerator }] } || !kinds.OfType<hkbProjectData>().Single().m_stringData!.m_characterFilenames.Single()!.Contains(identity))
            throw new Exception("The character's graph does not play the shared graph directly, or the project names another character");
        if (!built.Files["action/script/c6405.hks"].SequenceEqual(archive.Read($"action/script/{exemplar}.hks"))) throw new Exception("The action script is not the exemplar's");
        try { WholeCharacter.Write(source, archive, name, exemplar, hosts, sourceModel, parts); throw new Exception("An identity the host already has was accepted"); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: the hollow soldier is written whole from the installs: model, physics, skeleton, animations, events, behaviour and script, the same on each run");
    }
}
