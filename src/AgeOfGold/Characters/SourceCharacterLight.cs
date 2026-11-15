namespace AgeOfGold.Characters;

/// <summary>
/// The part of the source's light on a character that shadow leaves alone: a hemisphere colour running from a lower
/// colour on a surface facing down to an upper one on a surface facing up. The host's baked light holds it.
/// </summary>
internal static class SourceCharacterLight
{
    /// <summary>
    /// Light as a constant and three terms along the world's axes: a surface facing the unit direction n is lit by
    /// Constant + X n.x + Y n.y + Z n.z, each of red, green and blue in linear light.
    /// </summary>
    internal sealed record Terms(float[] Constant, float[] X, float[] Y, float[] Z)
    {
        /// <summary>The light on a surface facing a unit direction.</summary>
        internal float[] Along(float[] facing) => Enumerable.Range(0, 3).Select(c => Constant[c] + X[c] * facing[0] + Y[c] * facing[1] + Z[c] * facing[2]).ToArray();
    }

    /// <summary>The hemisphere's terms: lower + (upper - lower)(n.y + 1) / 2, so their mean is constant and half their difference runs upward.</summary>
    internal static Terms Hemisphere(float[] upper, float[] lower)
    {
        if (upper.Length != 3 || lower.Length != 3 || !upper.Concat(lower).All(v => float.IsFinite(v) && v >= 0)) throw new InvalidDataException("Hemisphere colours must be three finite values that are not negative");
        return new(upper.Zip(lower, (u, l) => (u + l) / 2).ToArray(), [0, 0, 0], upper.Zip(lower, (u, l) => (u - l) / 2).ToArray(), [0, 0, 0]);
    }
}
