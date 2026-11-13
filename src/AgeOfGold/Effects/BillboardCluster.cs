using System.Numerics;

namespace AgeOfGold.Effects;

/// <summary>A value drawn uniformly between two bounds, once per particle.</summary>
internal readonly record struct Span(float Low, float High)
{
    internal static Span Of(float value) => new(value, value);
    internal bool Fixed => Low == High;
}

/// <summary>Linear keys over a particle's life, in seconds; one key is a constant.</summary>
internal sealed record Track(float[] Times, float[] Values)
{
    internal static Track Of(float value) => new([0], [value]);

    internal float At(float time)
    {
        if (time <= Times[0]) return Values[0];
        for (int i = 1; i < Times.Length; i++)
            if (time <= Times[i]) return Values[i - 1] + (Values[i] - Values[i - 1]) * (time - Times[i - 1]) / (Times[i] - Times[i - 1]);
        return Values[^1];
    }
}

/// <summary>
/// One emitter of camera-facing textured particles, between the games: how often it emits, where, how each particle
/// moves, and how its size, frame and colour run over its life. Angles are degrees; lengths are metres.
/// </summary>
/// <param name="Disk">The radius of a flat round emitter, or null for a point.</param>
/// <param name="Offset">The emitter's position from the effect's origin; <paramref name="Rotation"/> turns it, in degrees about each axis.</param>
internal sealed record BillboardCluster(
    int Texture, int Columns, int Frames, int Orientation,
    float Interval, int Concurrent, float Duration,
    float? Disk, float DiskDistribution, Span Speed, float Gravity,
    Span Width, Track WidthScale, Span Height, Track HeightScale,
    Span Turn, Span TurnSpeed, Span FirstFrame, Track Frame,
    Track Red, Track Green, Track Blue, Track Alpha,
    Vector3 Offset, Vector3 Rotation);
