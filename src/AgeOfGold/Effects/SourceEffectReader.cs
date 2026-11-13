using System.Numerics;
using SoulsFormats;

namespace AgeOfGold.Effects;

/// <summary>Reads the billboard clusters of a source effect into our own model; what it cannot classify is counted, never guessed.</summary>
internal static class SourceEffectReader
{
    /// <param name="Unbuilt">Appearances that are not translated yet, by kind.</param>
    internal sealed record Effect(BillboardCluster[] Clusters, SortedDictionary<string, int> Unbuilt);

    // The source's call that runs one cluster emitter, and the call positions of its settings.
    private const int ClusterCall = 2023, MoveAction = 35;

    internal static Effect Read(byte[] bytes)
    {
        var effect = FXR1.Read(bytes);
        if (effect.RootFXNode is not FXR1.FXNode.EffectCreateNode133 root) throw new InvalidDataException("Unclassified source effect root");
        var clusters = new List<BillboardCluster>();
        var unbuilt = new SortedDictionary<string, int>(StringComparer.Ordinal);
        void Count(string kind) => unbuilt[kind] = unbuilt.GetValueOrDefault(kind) + 1;

        // lazy: the effect's state transitions are not read; every branch is treated as running for as long as the effect is placed.
        void Visit(FXR1.FXNode node, Vector3 origin, bool insideCluster)
        {
            switch (node)
            {
                case FXR1.FXNode.EffectCallNode call when call.EffectID == ClusterCall:
                    // lazy: a cluster spawned from another cluster's particles needs per-particle child emitters.
                    if (insideCluster) Count("cluster emitted from particles");
                    else
                    {
                        try { clusters.Add(Cluster(call, origin)); }
                        catch (NotSupportedException reason) { Count("cluster: " + reason.Message); }
                    }
                    foreach (var child in call.Container.FXNodes) Visit(child, origin, true);
                    break;
                case FXR1.FXNode.EffectCallNode call:
                    if (call.Container is null) break;
                    var (offset, rotation) = Movement(call.Container.FXNodes);
                    // A turned group would turn everything beneath it; none is known, so one stops the read.
                    if (rotation != Vector3.Zero) throw new InvalidDataException("Unclassified rotated source effect group");
                    foreach (var child in call.Container.FXNodes) Visit(child, origin + offset, insideCluster);
                    break;
                case FXR1.FXNode.ActionCallNode action:
                    if (action.Container.ActionData is { } data && action.ActionType != 71 && !insideCluster) Count(data.GetType().Name);
                    else if (action.Container.ActionData is not null && insideCluster && action.ActionType is not (71 or 28 or 30 or 55 or 84)) Count(action.Container.ActionData.GetType().Name);
                    foreach (var child in action.Container.FXNodes) Visit(child, origin, insideCluster);
                    break;
            }
        }
        foreach (var container in new[] { root.Container1, root.Container2 })
            foreach (var node in container.FXNodes) Visit(node, Vector3.Zero, false);
        return new(clusters.ToArray(), unbuilt);
    }

    // A group's own placement: three offsets and three turns, held by its move action.
    private static (Vector3 Offset, Vector3 Rotation) Movement(IEnumerable<FXR1.FXNode> nodes)
    {
        var moves = nodes.OfType<FXR1.FXNode.ActionCallNode>().Where(a => a.ActionType == MoveAction).ToArray();
        if (moves.Length == 0) return default;
        if (moves.Length != 1) throw new InvalidDataException("Source effect group moves more than once");
        var values = moves[0].Container.FXNodes.Select(n => n is FXR1.FXNode.FloatNode f ? f.Value : throw new InvalidDataException("Unclassified source effect movement")).ToArray();
        if (values.Length != 6) throw new InvalidDataException("Unclassified source effect movement");
        return (new(values[0], values[1], values[2]), new(values[3], values[4], values[5]));
    }

    private static BillboardCluster Cluster(FXR1.FXNode.EffectCallNode call, Vector3 origin)
    {
        var nodes = call.Container.FXNodes;
        // Settings by call position: two lifetimes, a flag, two unused actions, a delay, particles per emission, the number of
        // emissions, the interval, a count, the particles alive at once, then appearance, emitter, movement and placement.
        if (nodes.Count != 18 || nodes[8] is not FXR1.FXNode.TickNode interval || nodes[10] is not FXR1.FXNode.IntNode concurrent
            || nodes[6] is not FXR1.FXNode.IntSequenceNode5 { Ticks: [{ Value: 1 }] } || nodes[7] is not FXR1.FXNode.IntNode { Value: -1 }
            || nodes[9] is not FXR1.FXNode.IntNode { Value: 1 } || nodes[5] is not FXR1.FXNode.TickNode { Tick: 0 }
            || nodes[11] is not FXR1.FXNode.ActionCallNode { ActionType: 71 } appearance
            || nodes[12] is not FXR1.FXNode.ActionCallNode emitter || nodes[13] is not FXR1.FXNode.ActionCallNode movement)
            throw new NotSupportedException("unclassified emission settings");
        if (appearance.Container.ActionData is not FXR1.FXActionData.ClusterAppearanceBillboardActionData71 look) throw new InvalidDataException("Source cluster lacks its billboard appearance");
        if (look.DurationMult != 1 || look.LifetimeVariance != 0) throw new NotSupportedException("varied particle lifetime");
        if (look.AttachedBool != 0 || look.BlendMode != 0 || look.AlphaMode != 0) throw new NotSupportedException("unclassified attachment or blending");
        var (offset, rotation) = Movement(nodes);

        float? disk = null; float distribution = 0; Span speed;
        switch (emitter.Container.ActionData)
        {
            case FXR1.FXActionData.ClusterEmitterCircleActionData30 circle:
                if (circle.EmitterDegree is not FXR1.FXField.Empty || circle.EmitterSpread is not FXR1.FXField.Empty) throw new NotSupportedException("spread emission");
                disk = Fixed(circle.EmitterRadius); distribution = circle.EmitterDistribution; speed = Range(circle.EmitterSpeed, 0);
                break;
            case FXR1.FXActionData.ClusterEmitterConeActionData28 cone:
                // A cone with no opening emits from its tip along its axis.
                if (cone.EmitterDegree is not FXR1.FXField.Empty || cone.EmitterSpread is not FXR1.FXField.Empty) throw new NotSupportedException("spread emission");
                speed = Range(cone.EmitterSpeed, 0);
                break;
            default: throw new NotSupportedException("unclassified emitter shape");
        }
        if (movement.Container.ActionData is not FXR1.FXActionData.ClusterMovementAccelerationActionData55 push) throw new NotSupportedException("unclassified particle movement");
        if (push.MovementFalloff is not FXR1.FXField.Empty || Fixed(push.MovementFalloffMult) != 1) throw new NotSupportedException("damped particle movement");
        foreach (var unused in new[] { look.RotX, look.RotY, look.RotSpeedX, look.RotSpeedY })
            if (unused is not FXR1.FXField.Empty) throw new NotSupportedException("particles turned off the view axis");
        foreach (var white in new[] { look.Color1R, look.Color1G, look.Color1B, look.Color1A })
            if (Fixed(white) != 1) throw new NotSupportedException("tinted first colour");

        // The remaster multiplies the colour by its own factor and a brightness that runs over the particle's life.
        var extra = look.DS1RData ?? throw new NotSupportedException("no remaster colour");
        var brightness = Keys(extra.DS1R_ColorModMult, 1);
        Track Colour(FXR1.FXField channel, FXR1.FXField factor)
        {
            var own = Keys(channel, 1); float scale = Fixed(factor);
            var times = own.Times.Concat(brightness.Times).Distinct().Order().ToArray();
            return new(times, times.Select(t => own.At(t) * brightness.At(t) * scale).ToArray());
        }
        var alpha = Keys(look.Color2A, 1);
        float opacity = Fixed(extra.DS1R_ColorModA);
        const float degrees = 180 / MathF.PI;
        var turn = Range(look.RotZ, 0); var turnSpeed = Range(look.RotSpeedZ, 0);
        var width = Range(look.Scale1X, 1);
        return new(look.TextureID, look.AnimFrameSliceCountPerRow, look.AnimFrameTotalCount, look.OrientationMode,
            interval.Tick, concurrent.Value, look.Duration,
            disk, distribution, speed, push.Gravity is FXR1.FXField.Empty ? 0 : Fixed(push.Gravity),
            // A particle without its own height is square.
            width, Keys(look.Scale2X, 1), look.Scale1Y is FXR1.FXField.Empty ? width : Range(look.Scale1Y, 1), Keys(look.Scale2Y, 1),
            new(turn.Low * degrees, turn.High * degrees), new(turnSpeed.Low * degrees, turnSpeed.High * degrees),
            Range(look.AnimFrameInitial, 0), Keys(look.AnimFrameTimeline, 0),
            Colour(look.Color2R, extra.DS1R_ColorModR), Colour(look.Color2G, extra.DS1R_ColorModG), Colour(look.Color2B, extra.DS1R_ColorModB),
            new(alpha.Times, alpha.Values.Select(v => v * opacity).ToArray()),
            origin + offset, rotation);
    }

    private static float Fixed(FXR1.FXField field) => field is FXR1.FXField.ConstFloat value ? value.Value : throw new NotSupportedException("a varying value where a constant is expected");

    // A random value is a base scaled by a factor drawn between a floor and that floor plus a width.
    private static Span Range(FXR1.FXField field, float absent) => field switch
    {
        FXR1.FXField.Empty => Span.Of(absent),
        FXR1.FXField.ConstFloat value => Span.Of(value.Value),
        FXR1.FXField.FieldType25 random => Order(random.Base * random.Min, random.Base * (random.Min + random.Max)),
        _ => throw new NotSupportedException("unclassified random value"),
    };

    private static Span Order(float a, float b) => new(MathF.Min(a, b), MathF.Max(a, b));

    private static Track Keys(FXR1.FXField field, float absent) => field switch
    {
        FXR1.FXField.Empty => Track.Of(absent),
        FXR1.FXField.ConstFloat value => Track.Of(value.Value),
        FXR1.FXField.FloatSequence { Ticks.Count: > 0 } keys when keys.Ticks.Zip(keys.Ticks.Skip(1)).All(p => p.Second.Time > p.First.Time)
            => new(keys.Ticks.Select(t => t.Time).ToArray(), keys.Ticks.Select(t => t.Value).ToArray()),
        _ => throw new NotSupportedException("unclassified value over time"),
    };
}
