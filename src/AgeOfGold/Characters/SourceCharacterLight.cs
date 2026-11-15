using AgeOfGold.Scene;

namespace AgeOfGold.Characters;

/// <summary>
/// The light the source gives a character under one lighting row: an environment cube read along the surface's facing,
/// times a gain, plus a colour running from a lower to an upper one as the surface turns upward.
/// </summary>
internal static class SourceCharacterLight
{
    /// <summary>
    /// The light on a surface facing n, as a multiple of its linear colour: Constant + X n.x + Y n.y + Z n.z,
    /// each term red, green and blue.
    /// </summary>
    internal sealed record Terms(float[] Constant, float[] X, float[] Y, float[] Z);

    /// <summary>The constant and linear terms closest to the source's light over every facing.</summary>
    internal static Terms Fit(BlockTexture.Radiance cube, float[] environment, float[] upper, float[] lower)
    {
        foreach (var colour in new[] { environment, upper, lower })
            if (colour.Length != 3 || !colour.All(float.IsFinite)) throw new InvalidDataException("Character light colours must be three finite values");
        if (cube.Faces.Length != 6 || cube.Size <= 0 || cube.Faces.Any(f => f.Length != cube.Size * cube.Size * 3)) throw new InvalidDataException("Character light needs a six-faced cube");
        // The closest constant is the mean over all facings, and the closest term along an axis three times the mean of
        // the light times that axis. Each texel counts by the share of all directions it covers.
        var sums = new double[4, 3];
        double total = 0;
        for (int face = 0; face < 6; face++)
            for (int y = 0; y < cube.Size; y++)
                for (int x = 0; x < cube.Size; x++)
                {
                    double u = (x + 0.5) * 2 / cube.Size - 1, v = (y + 0.5) * 2 / cube.Size - 1, length = Math.Sqrt(1 + u * u + v * v);
                    // How a cube face's columns and rows lie in space: +X, -X, +Y, -Y, +Z, -Z.
                    var (dx, dy, dz) = face switch { 0 => (1, -v, -u), 1 => (-1, -v, u), 2 => (u, 1, v), 3 => (u, -1, -v), 4 => (u, -v, 1), _ => (-u, -v, -1d) };
                    double share = 1 / (length * length * length);
                    total += share;
                    for (int c = 0; c < 3; c++)
                    {
                        double light = cube.Faces[face][(y * cube.Size + x) * 3 + c] * share;
                        sums[0, c] += light; sums[1, c] += 3 * light * dx / length; sums[2, c] += 3 * light * dy / length; sums[3, c] += 3 * light * dz / length;
                    }
                }
        float[] Term(int term) => Enumerable.Range(0, 3).Select(c => (float)(sums[term, c] / total * environment[c])).ToArray();
        var constant = Term(0); var up = Term(2);
        // The hemisphere colour is lower + (upper - lower)(n.y + 1) / 2: already a constant and an upward term.
        for (int c = 0; c < 3; c++) { constant[c] += (upper[c] + lower[c]) / 2; up[c] += (upper[c] - lower[c]) / 2; }
        return new(constant, Term(1), up, Term(3));
    }
}
