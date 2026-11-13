using SoulsFormats;

namespace AgeOfGold.Effects;

/// <summary>Writes billboard clusters as a host effect, on the scaffolding of an installed one-emitter billboard effect.</summary>
internal static class HostEffectWriter
{
    // The host's slots in an emitter node, in order: node life, placement, two spare, emission timing, emitter shape,
    // particle spread, particle launch, particle life, appearance, particle forces, spare, level of detail, two spare.
    private const int Life = 0, Placement = 1, Timing = 4, Shape = 5, Spread = 6, Launch = 7, ParticleLife = 8, Appearance = 9, Forces = 10, Detail = 12;
    private const short PeriodicEmitter = 300, PointShape = 400, DiskShape = 401, NoSpread = 500, Billboard = 603, Acceleration = 55, NodeContainer = 2200, NodeEffect = 1004;
    // A property modifier adding a uniform random offset (seed, width), and one scaling by a uniform random factor (seed, half-width).
    private static readonly (ushort A, uint B) RandomOffset = (53376, 4), RandomFactor = (61520, 16);
    // The host's blend mode that adds a particle's light to the scene.
    private const float AddBlend = 4;
    /// <summary>The alpha bias, as it reaches the host's particle stages, that marks a particle for the source's colour response.</summary>
    internal const float ResponseMarker = 32769;
    // The host divides the authored alpha bias by this before its particle stages see it.
    private const float BiasScale = 255;

    /// <summary>Whether an installed effect has the one-emitter billboard shape this writer builds on.</summary>
    internal static bool IsTemplate(FXR3 effect) =>
        effect.RootContainer is { Id: 2000, Effects.Count: 0, Containers: [{ Id: NodeContainer, Containers.Count: 0, Effects: [{ Id: NodeEffect } node] }] } root
        && root.Actions.Select(a => (int)a.Id).SequenceEqual([700, 10100, 10400, 10500])
        && node.Actions.Count == 15 && node.Actions[Timing].Id == PeriodicEmitter && node.Actions[Appearance].Id == Billboard
        && node.Actions[Appearance] is { Fields1.Count: 18, Properties1.Count: 25, Properties2.Count: 7 }
        && node.Actions[Forces].Id == Acceleration && node.Actions[Detail] is { Id: 130, Properties1.Count: 8 };

    /// <param name="textures">The host texture identity of each source texture.</param>
    internal static byte[] Write(byte[] template, int id, IReadOnlyList<BillboardCluster> clusters, IReadOnlyDictionary<int, int> textures)
    {
        var effect = FXR3.Read(template);
        if (!IsTemplate(effect)) throw new InvalidDataException("Host effect template is not a one-emitter billboard effect");
        if (clusters.Count == 0) throw new InvalidDataException("An effect needs at least one cluster");
        effect.Id = id;
        // The effect runs for as long as it is placed: one state with no leaving condition and no outside inputs.
        effect.RootStateMap.States = [new FXR3.State()];
        effect.ExternalValueList.Clear(); effect.ReferenceList.Clear();
        effect.RootContainer.Containers = clusters.Select((cluster, index) =>
        {
            // Each cluster starts from a fresh reading of the template's node.
            var container = FXR3.Read(template).RootContainer.Containers[0];
            Fill(container.Effects[0].Actions, cluster, index, textures);
            return container;
        }).ToList();
        return effect.Write();
    }

    private static void Fill(List<FXR3.Action> slots, BillboardCluster cluster, int index, IReadOnlyDictionary<int, int> textures)
    {
        if (!(cluster.Interval > 0) || cluster.Concurrent < 1 || !(cluster.Duration > 0) || cluster.Columns < 1 || cluster.Frames < 1)
            throw new InvalidDataException("Unsupported cluster timing or frame grid");
        if (!textures.TryGetValue(cluster.Texture, out int texture)) throw new InvalidDataException("Cluster texture has no host identity");
        // Identities above this are not exact in the single-precision property that carries them.
        if (texture is < 1 or >= 1 << 24) throw new InvalidDataException("Host effect texture identity is out of range");
        int seed = (index + 1) * 1000;
        FXR3.Action Clear(int slot, short kind, params FXR3.Field[] fields)
        {
            var action = slots[slot];
            action.Id = kind; action.Fields1 = [.. fields]; action.Fields2 = []; action.Properties1 = []; action.Properties2 = []; action.UnkFieldLists = [];
            return action;
        }
        // The node never ends and stays where the effect is placed.
        Clear(Life, 128, I(0), I(1), I(1), I(0)).Properties1 = [Constant(-1)];
        if (cluster.Rotation.Z != 0) throw new InvalidDataException("Unsupported cluster roll");
        // The source's depth axis runs the other way.
        Clear(Placement, 35, F(cluster.Offset.X), F(cluster.Offset.Y), F(-cluster.Offset.Z), F(cluster.Rotation.X), F(cluster.Rotation.Y), I(0));
        Clear(2, 0); Clear(3, 0);
        // One particle per interval, without end, up to the source's count alive at once.
        Clear(Timing, PeriodicEmitter, I(1)).Properties1 = [Constant(cluster.Interval), One(), Constant(-1), Constant(cluster.Concurrent)];
        if (cluster.Disk is { } radius) Clear(Shape, DiskShape, I(1)).Properties1 = [Constant(radius), Constant(cluster.DiskDistribution)];
        else Clear(Shape, PointShape, I(1));
        Clear(Spread, NoSpread);
        Clear(Launch, 131, I(0)).Properties1 = [Ranged(cluster.Speed, ref seed), One(), One(), One(), One(colour: true)];
        Clear(ParticleLife, 129, I(0)).Properties1 = [Constant(cluster.Duration)];

        var look = Clear(Appearance, Billboard,
            // Orientation, then: no normal map, independent width and height, the frame grid with blended frames,
            // and the remaining switches at rest.
            I(cluster.Orientation), I(0), F(1), F(1), I(0), I(1), I(cluster.Columns), I(cluster.Frames), I(1), I(0), I(0), F(-1), I(0), I(0), I(0), I(1), I(1), I(0));
        // Shared render settings at rest: untinted glow, no distance fades, unlit.
        look.Fields2 = [I(0), I(0), I(8), I(0), I(0), F(1), F(1), F(1), I(0), I(0), I(0), I(0), I(0), I(0), F(-1), F(-1), F(-1), F(-1), F(-1), F(-1),
            I(0), I(0), I(0), I(0), I(0), F(1), I(0), I(0), I(0), I(0), I(0), I(0), I(0), I(0), F(0.25f), I(-1), I(-2), I(0), F(0.5f), I(1), I(0), I(0), I(0), I(0), I(0)];
        // lazy: the source turns a particle's image before stretching it, so a tall particle stays upright; the host turns the
        // stretched sheet and would lean it over. Until the host's particle vertex stage is translated, only square particles turn.
        bool square = cluster.Width == cluster.Height && cluster.WidthScale.Times.SequenceEqual(cluster.HeightScale.Times) && cluster.WidthScale.Values.SequenceEqual(cluster.HeightScale.Values);
        var turn = square ? cluster.Turn : Span.Of(0); var turnSpeed = square ? cluster.TurnSpeed : Span.Of(0);
        var colour = new FXR3.Property { PropertyType = FXR3.PropertyType.Color, InterpolationType = FXR3.PropertyInterpolationType.Linear };
        {
            var times = new[] { cluster.Red, cluster.Green, cluster.Blue, cluster.Alpha }.SelectMany(t => t.Times).Distinct().Order().ToArray();
            var keys = times.Select(t => new[] { cluster.Red.At(t), cluster.Green.At(t), cluster.Blue.At(t), cluster.Alpha.At(t) }).ToArray();
            if (times.Length < 2) { times = [0, cluster.Duration]; keys = [keys[0], keys[0]]; }
            // A keyed property lists its key count, the least and greatest value of each component, the times, then the values.
            colour.Fields = [I(times.Length), .. Enumerable.Range(0, 4).Select(c => F(keys.Min(k => k[c]))), .. Enumerable.Range(0, 4).Select(c => F(keys.Max(k => k[c]))),
                .. times.Select(F), .. keys.SelectMany(k => k.Select(F))];
        }
        look.Properties1 =
        [
            Constant(texture), Constant(AddBlend), Zero(), Zero(), Zero(),
            Sized(cluster.Width, cluster.WidthScale, ref seed), Sized(cluster.Height, cluster.HeightScale, ref seed),
            One(colour: true), One(colour: true), colour,
            // The alpha bias carries the response marker; then the turn about each axis, its speed and that speed's multiplier.
            Constant(ResponseMarker * BiasScale), Zero(), Zero(), Ranged(turn, ref seed), Zero(), One(), Zero(), One(), Ranged(turnSpeed, ref seed), One(), Zero(),
            Keyed(cluster.Frame, 1), Ranged(cluster.FirstFrame, ref seed), Zero(), Zero(),
        ];
        look.Properties2 = [One(), One(), Zero(), One(colour: true), One(colour: true), One(colour: true), Zero()];
        Clear(Forces, Acceleration, I(0), I(0)).Properties1 = [cluster.Gravity == 0 ? Zero() : Constant(cluster.Gravity), Zero(), One()];
        Clear(11, 0);
        Clear(Detail, 130, I(1), I(0), I(0), I(0), I(0), I(0), I(0), I(0), I(0)).Properties1 = Enumerable.Range(0, 8).Select(_ => Zero()).ToList();
        Clear(13, 0); Clear(14, 0);
    }

    private static FXR3.Field I(int value) => new FXR3.Field.FieldInt(value);
    private static FXR3.Field F(float value) => new FXR3.Field.FieldFloat(value);
    private static FXR3.Property Zero() => new() { InterpolationType = FXR3.PropertyInterpolationType.Zero };
    private static FXR3.Property One(bool colour = false) => new() { PropertyType = colour ? FXR3.PropertyType.Color : FXR3.PropertyType.Scalar, InterpolationType = FXR3.PropertyInterpolationType.One };
    private static FXR3.Property Constant(float value) => new() { InterpolationType = FXR3.PropertyInterpolationType.Constant, Fields = [F(value)] };

    private static FXR3.Property Keyed(Track track, float scale)
    {
        if (track.Times.Length == 1) return track.Values[0] * scale == 0 ? Zero() : Constant(track.Values[0] * scale);
        var values = track.Values.Select(v => v * scale).ToArray();
        return new() { InterpolationType = FXR3.PropertyInterpolationType.Linear, Fields = [I(values.Length), F(values.Min()), F(values.Max()), .. track.Times.Select(F), .. values.Select(F)] };
    }

    // A fixed value, or the upper bound with a random offset reaching down to the lower.
    private static FXR3.Property Ranged(Span span, ref int seed)
    {
        if (span.Fixed) return span.High == 0 ? Zero() : Constant(span.High);
        var property = Constant(span.High);
        property.Modifiers.Add(new() { TypeEnumA = RandomOffset.A, TypeEnumB = RandomOffset.B, Fields = [I(++seed), F(span.Low - span.High), I(0)] });
        return property;
    }

    // A size that runs over the particle's life, scaled by its particle's random base.
    private static FXR3.Property Sized(Span span, Track scale, ref int seed)
    {
        if (scale.Times.Length == 1)
            return Ranged(new(span.Low * scale.Values[0], span.High * scale.Values[0]), ref seed);
        float middle = (span.Low + span.High) / 2;
        var property = Keyed(scale, middle);
        if (!span.Fixed) property.Modifiers.Add(new() { TypeEnumA = RandomFactor.A, TypeEnumB = RandomFactor.B, Fields = [I(++seed), F((span.High - span.Low) / (span.High + span.Low))] });
        return property;
    }
}
