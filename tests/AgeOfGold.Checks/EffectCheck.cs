using System.Numerics;
using AgeOfGold.Effects;
using SoulsFormats;

internal static class EffectCheck
{
    internal static void Run()
    {
        var template = Template().Write();
        // Two authored clusters: a disk of fixed-width particles whose height grows, and a point of turning particles.
        var disk = new BillboardCluster(700, 4, 16, 1, 0.25f, 8, 2, 0.5f, -0.75f, Span.Of(0.03f), 0,
            new(0.3f, 0.5f), Track.Of(1), new(0.15f, 0.25f), new([0, 2], [0.5f, 2.5f]), Span.Of(0), Span.Of(0), new(0, 12), new([0, 2], [0, 15]),
            new([0, 1, 2], [10, 10, 5]), Track.Of(3), Track.Of(0.5f), new([0, 0.5f, 1.5f, 2], [0, 1, 1, 0]), new(0.1f, -0.2f, 0.3f), new(90, 0, 0));
        var point = disk with { Texture = 701, Disk = null, Orientation = 4, Speed = new(0.075f, 0.125f), Gravity = -0.1f, Width = Span.Of(0.2f), WidthScale = new([0, 2], [1, 2.5f]), Height = Span.Of(0.2f), HeightScale = new([0, 2], [1, 2.5f]),
            Turn = new(0, 720), TurnSpeed = new(-22.5f, 22.5f), Interval = 0.5f, Concurrent = 4, Rotation = Vector3.Zero };
        var textures = new Dictionary<int, int> { [700] = 990300, [701] = 990301 };
        var bytes = HostEffectWriter.Write(template, 990100300, [disk, point], textures);
        if (!bytes.SequenceEqual(HostEffectWriter.Write(template, 990100300, [disk, point], textures))) throw new Exception("Effect writing does not repeat");
        var effect = FXR3.Read(bytes);
        if (effect.Id != 990100300 || effect.RootStateMap.States.Count != 1 || effect.RootStateMap.States[0].Conditions.Count != 0 || effect.ExternalValueList.Count != 0
            || effect.RootContainer.Containers.Count != 2 || !effect.RootContainer.Actions.Select(a => (int)a.Id).SequenceEqual([700, 10100, 10400, 10500]))
            throw new Exception("Written effect lost its identity, its endless state or its one node per cluster");
        var first = effect.RootContainer.Containers[0].Effects.Single().Actions; var second = effect.RootContainer.Containers[1].Effects.Single().Actions;
        static float N(FXR3.Field field) => field is FXR3.Field.FieldFloat f ? f.Value : BitConverter.Int32BitsToSingle(((FXR3.Field.FieldInt)field).Value);
        static int Whole(FXR3.Field field) => field is FXR3.Field.FieldInt i ? i.Value : BitConverter.SingleToInt32Bits(((FXR3.Field.FieldFloat)field).Value);
        static float[] Values(FXR3.Property property) => property.Fields.Select(N).ToArray();
        void Near(float actual, float expected, string what) { if (MathF.Abs(actual - expected) > 1e-6f * MathF.Max(1, MathF.Abs(expected))) throw new Exception($"Effect check failed: {what}: {actual} != {expected}"); }
        // Emission: the interval, an endless count, and the number alive at once.
        Near(Values(first[4].Properties1[0])[0], 0.25f, "interval"); Near(Values(first[4].Properties1[3])[0], 8, "particles alive");
        Near(Values(first[4].Properties1[2])[0], -1, "endless emission"); Near(Values(second[4].Properties1[0])[0], 0.5f, "second interval");
        if (first[5].Id != 401 || second[5].Id != 400) throw new Exception("Emitter shapes differ");
        Near(Values(first[5].Properties1[0])[0], 0.5f, "disk radius"); Near(Values(first[5].Properties1[1])[0], -0.75f, "disk distribution");
        // Placement: the source's depth axis is reversed; the turn is kept.
        if (!first[1].Fields1.Take(5).Select(N).SequenceEqual(new[] { 0.1f, -0.2f, -0.3f, 90, 0 })) throw new Exception("Cluster placement differs");
        Near(Values(first[7].Properties1[0])[0], 0.03f, "fixed speed"); Near(Values(first[8].Properties1[0])[0], 2, "particle life");
        // A random range is its upper bound with an offset down to the lower.
        var speed = second[7].Properties1[0];
        Near(Values(speed)[0], 0.125f, "upper speed"); Near(N(speed.Modifiers.Single().Fields[1]), -0.05f, "speed range");
        var look = first[9]; var turning = second[9];
        if (look.Id != 603 || Whole(look.Fields1[0]) != 1 || Whole(turning.Fields1[0]) != 4 || Whole(look.Fields1[6]) != 4 || Whole(look.Fields1[7]) != 16)
            throw new Exception("Billboard orientation or frame grid differs");
        Near(Values(look.Properties1[0])[0], 990300, "texture"); Near(Values(turning.Properties1[0])[0], 990301, "second texture");
        Near(Values(look.Properties1[1])[0], 4, "additive blending");
        // The host divides the alpha bias by 255 on its way to the particle stage, where the marker must arrive whole.
        Near(N(look.Properties1[10].Fields[0]), HostEffectWriter.ResponseMarker * 255, "source response marker");
        // Width: fixed scale, so the range itself. Height: keys of the mid height with a random factor of a quarter.
        Near(Values(look.Properties1[5])[0], 0.5f, "upper width"); Near(N(look.Properties1[5].Modifiers.Single().Fields[1]), -0.2f, "width range");
        var height = look.Properties1[6];
        if (height.InterpolationType != FXR3.PropertyInterpolationType.Linear || Whole(height.Fields[0]) != 2
            || !Values(height).Skip(1).Zip(new[] { 0.1f, 0.5f, 0, 2, 0.1f, 0.5f }).All(p => MathF.Abs(p.First - p.Second) < 1e-6f))
            throw new Exception("Keyed height differs: " + string.Join(",", Values(height)));
        Near(N(height.Modifiers.Single().Fields[1]), 0.25f, "height variation");
        if (height.Modifiers[0].TypeEnumA == look.Properties1[5].Modifiers[0].TypeEnumA
            || Whole(height.Modifiers[0].Fields[0]) == Whole(look.Properties1[5].Modifiers[0].Fields[0])) throw new Exception("Random modifiers share a kind or a seed");
        // Colour: the union of all channels' key times, each key holding every channel there.
        var colour = look.Properties1[9];
        float[] expected = [4, /* least */ 5, 3, 0.5f, 0, /* greatest */ 10, 3, 0.5f, 1, /* times */ 0, 0.5f, 1, 1.5f, 2,
            10, 3, 0.5f, 0, 10, 3, 0.5f, 1, 10, 3, 0.5f, 1, 7.5f, 3, 0.5f, 1, 5, 3, 0.5f, 0];
        expected[0] = BitConverter.Int32BitsToSingle(5);
        if (colour.PropertyType != FXR3.PropertyType.Color || !Values(colour).Zip(expected).All(p => MathF.Abs(p.First - p.Second) < 1e-6f) || Values(colour).Length != expected.Length)
            throw new Exception("Keyed colour differs: " + string.Join(",", Values(colour)));
        Near(Values(turning.Properties1[13])[0], 720, "turn"); Near(N(turning.Properties1[13].Modifiers.Single().Fields[1]), -720, "turn range");
        Near(Values(turning.Properties1[18])[0], 22.5f, "turn speed"); Near(N(turning.Properties1[18].Modifiers.Single().Fields[1]), -45, "turn speed range");
        if (look.Properties1[13].InterpolationType != FXR3.PropertyInterpolationType.Zero) throw new Exception("A still particle turns");
        // A particle that is not square is not turned: the host would lean the stretched sheet over, which the source never does.
        var tall = FXR3.Read(HostEffectWriter.Write(template, 1, [point with { Height = Span.Of(0.4f) }], textures)).RootContainer.Containers.Single().Effects.Single().Actions[9];
        if (tall.Properties1[13].InterpolationType != FXR3.PropertyInterpolationType.Zero || tall.Properties1[18].InterpolationType != FXR3.PropertyInterpolationType.Zero)
            throw new Exception("A tall particle turns");
        if (!Values(look.Properties1[21]).Skip(1).SequenceEqual(new[] { 0f, 15, 0, 2, 0, 15 })) throw new Exception("Frame keys differ");
        Near(Values(look.Properties1[22])[0], 12, "first frame"); Near(N(look.Properties1[22].Modifiers.Single().Fields[1]), -12, "first frame range");
        Near(Values(second[10].Properties1[0])[0], -0.1f, "gravity");
        if (first[10].Properties1[0].InterpolationType != FXR3.PropertyInterpolationType.Zero || look.Properties1.Count != 25 || look.Properties2.Count != 7 || look.Fields1.Count != 18)
            throw new Exception("Billboard layout differs from the host's");

        Reject(() => HostEffectWriter.Write(template, 1, [disk], new Dictionary<int, int>()), "texture");
        Reject(() => HostEffectWriter.Write(template, 1, [disk], new Dictionary<int, int> { [700] = 1 << 24 }), "range");
        Reject(() => HostEffectWriter.Write(template, 1, [], textures), "cluster");
        Reject(() => HostEffectWriter.Write(template, 1, [disk with { Interval = 0 }], textures), "timing");
        var other = Template(); other.RootContainer.Containers[0].Effects[0].Actions[9].Id = 600;
        Reject(() => HostEffectWriter.Write(other.Write(), 1, [disk], textures), "template");
        // The bundle gains the effect after the last installed one, its texture list at the host's distance, and one image per source texture.
        var image = new AgeOfGold.Scene.TextureWriter(AuthoredImage("authored")).Write("s31_a", AuthoredDds(), false);
        var host = new BND4();
        host.Files.Add(new(Binder.FileFlags.Flag1, 7, @"N:\authored\sfx\effect\f000000003.fxr", [1, 2, 3]));
        host.Files.Add(new(Binder.FileFlags.Flag1, 9, @"N:\authored\sfx\effect\f000000005.fxr", template));
        host.Files.Add(new(Binder.FileFlags.Flag1, 100004, @"N:\authored\sfx\tex\s31_a.tpf", image));
        host.Files.Add(new(Binder.FileFlags.Flag1, 400009, @"N:\authored\sfx\ResourceList\f000000005.ffxreslist", "s31_a.tif\r\n"u8.ToArray()));
        var hostBytes = host.Write();
        var added = EffectBundle.Add(hostBytes, 990100300, 990300, [disk, point], id => id is 700 or 701 ? AuthoredDds((byte)id) : throw new Exception("Unexpected source texture"));
        var read = BND4.Read(added.Bundle);
        BinderFile Member(string leaf) => read.Files.Single(f => f.Name.EndsWith("\\" + leaf, StringComparison.Ordinal));
        if (!added.Textures.SequenceEqual(new SortedDictionary<int, int> { [700] = 990300, [701] = 990301 }) || added.Template != "f000000005.fxr"
            || Member("f990100300.fxr").ID != 10 || !Member("f990100300.fxr").Name.StartsWith(@"N:\authored\sfx\effect\", StringComparison.Ordinal)
            || !Member("f990100300.fxr").Bytes.SequenceEqual(bytes)
            || Member("f990100300.ffxreslist").ID != 400010 || System.Text.Encoding.ASCII.GetString(Member("f990100300.ffxreslist").Bytes) != "s990300_a.tif\r\ns990301_a.tif\r\n"
            || Member("s990300_a.tpf").ID != 100005 || Member("s990301_a.tpf").ID != 100006
            || !read.Files.Select(f => f.ID).SequenceEqual(read.Files.Select(f => f.ID).Order()) || read.Files.Count != 8)
            throw new Exception("Effect bundle members differ");
        var stored = AgeOfGold.Scene.TextureReader.ReadPackage(Member("s990301_a.tpf").Bytes).Single();
        if (stored.Name != "s990301_a" || stored.Bytes[^1] != 701 % 256) throw new Exception("Effect image was not carried into the bundle");
        foreach (var member in host.Files)
            if (!read.Files.Single(f => f.ID == member.ID).Bytes.SequenceEqual(member.Bytes)) throw new Exception("Installed effect bundle member changed");
        if (!added.Bundle.SequenceEqual(EffectBundle.Add(hostBytes, 990100300, 990300, [disk, point], id => AuthoredDds((byte)id)).Bundle)) throw new Exception("Effect bundle does not repeat");
        Reject(() => EffectBundle.Add(added.Bundle, 990100300, 990400, [disk], _ => AuthoredDds()), "occupied");
        Reject(() => EffectBundle.Add(added.Bundle, 990100400, 990300, [disk], _ => AuthoredDds()), "occupied");
        host.Files.RemoveAt(1);
        Reject(() => EffectBundle.Add(host.Write(), 990100300, 990300, [disk], _ => AuthoredDds()), "build on");
        Console.WriteLine("PASS: billboard clusters write as host effect nodes with emission, placement, random ranges, keyed size, colour and frames; unsupported input fails");
    }

    // An authored effect in the shape of the host's one-emitter billboard effects, with its own unrelated settings.
    private static FXR3 Template()
    {
        static FXR3.Action Action(short id, int fields = 0, int properties = 0, int more = 0)
        {
            var action = new FXR3.Action { Id = id, Unk03 = true };
            for (int i = 0; i < fields; i++) action.Fields1.Add(new FXR3.Field.FieldInt(i + 1));
            for (int i = 0; i < properties; i++) action.Properties1.Add(new FXR3.Property { InterpolationType = FXR3.PropertyInterpolationType.Constant, Fields = [new FXR3.Field.FieldFloat(0.5f + i)] });
            for (int i = 0; i < more; i++) action.Properties2.Add(new FXR3.Property { InterpolationType = FXR3.PropertyInterpolationType.One });
            return action;
        }
        var effect = new FXR3 { Version = FXR3.FXRVersion.Sekiro, Id = 5 };
        effect.ExternalValueList.Add(0);
        effect.RootStateMap.States.Add(new FXR3.State());
        effect.RootContainer.Id = 2000;
        effect.RootContainer.Actions.AddRange([Action(700), Action(10100, 56), Action(10400, 65), Action(10500, 10, 1)]);
        var node = new FXR3.Container { Id = 2200 };
        node.Actions.Add(Action(199));
        var emitter = new FXR3.Effect { Id = 1004 };
        emitter.Actions.AddRange([Action(128, 4, 1), Action(35, 6), Action(1, 3, 4), Action(0), Action(300, 1, 4), Action(400, 1), Action(501, 1, 2), Action(131, 1, 5),
            Action(129, 1, 1), Action(603, 18, 25, 7), Action(55, 2, 3), Action(0), Action(130, 9, 8), Action(0), Action(0)]);
        node.Effects.Add(emitter);
        effect.RootContainer.Containers.Add(node);
        return effect;
    }

    // One 4x4 block-compressed image whose last byte tells images apart.
    private static byte[] AuthoredDds(byte mark = 0)
    {
        var dds = new DDS { dwWidth = 4, dwHeight = 4, dwMipMapCount = 1, ddspf = new DDS.PIXELFORMAT { dwFourCC = "DXT1", dwFlags = DDS.DDPF.FOURCC } };
        return dds.Write([0, 0, 0, 0, 0, 0, 0, mark]);
    }

    // A one-image PC package in the host's layout, for the writer to follow.
    private static byte[] AuthoredImage(string name)
    {
        var dds = AuthoredDds();
        var label = System.Text.Encoding.Unicode.GetBytes(name + '\0');
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("TPF\0"u8); writer.Write(dds.Length); writer.Write(1); writer.Write(new byte[] { 0, 3, 1, 0 });
        writer.Write(36 + label.Length); writer.Write(dds.Length); writer.Write(new byte[] { 0, 0, 1, 0 }); writer.Write(36); writer.Write(0);
        writer.Write(label); writer.Write(dds);
        return stream.ToArray();
    }

    private static void Reject(Action action, string reason)
    {
        try { action(); }
        catch (InvalidDataException e) when (e.Message.Contains(reason, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new Exception("Expected effect rejection: " + reason);
    }
}
