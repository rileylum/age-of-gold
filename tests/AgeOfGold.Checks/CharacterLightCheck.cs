using AgeOfGold.Characters;
using AgeOfGold.Scene;

/// <summary>The source's light on a character, restated as a constant and three linear terms, against values worked out by hand.</summary>
internal static class CharacterLightCheck
{
    private static BlockTexture.Radiance Cube(int size, Func<int, int, int, float> value) =>
        new(size, Enumerable.Range(0, 6).Select(face => Enumerable.Range(0, size * size * 3).Select(i => value(face, i / 3 % size, i / 3 / size)).ToArray()).ToArray());

    private static void Near(string what, float[] got, params float[] expected)
    {
        for (int i = 0; i < 3; i++)
            if (!(Math.Abs(got[i] - expected[i]) <= 2e-3f)) throw new Exception($"{what} is {string.Join(", ", got)}; expected {string.Join(", ", expected)}");
    }

    internal static void Run()
    {
        float[] none = [0, 0, 0], one = [1, 1, 1];
        // The same light from every side, and the same hemisphere colour above and below: a constant and no direction.
        var even = SourceCharacterLight.Fit(Cube(16, (_, _, _) => 0.25f), [2, 1, 0.5f], [0.1f, 0.2f, 0.3f], [0.1f, 0.2f, 0.3f]);
        Near("Even light's constant", even.Constant, 0.6f, 0.45f, 0.425f);
        Near("Even light's X", even.X, 0, 0, 0); Near("Even light's Y", even.Y, 0, 0, 0); Near("Even light's Z", even.Z, 0, 0, 0);
        // The hemisphere alone runs from the lower colour on a surface facing down to the upper on one facing up:
        // lower + (upper - lower)(y + 1) / 2, so the constant is their mean and the upward term half their difference.
        var hemisphere = SourceCharacterLight.Fit(Cube(16, (_, _, _) => 0f), one, [0.5f, 0.5f, 0.9f], [0.1f, 0.1f, 0.1f]);
        Near("The hemisphere's constant", hemisphere.Constant, 0.3f, 0.3f, 0.5f);
        Near("The hemisphere's Y", hemisphere.Y, 0.2f, 0.2f, 0.4f);
        Near("The hemisphere's X", hemisphere.X, 0, 0, 0); Near("The hemisphere's Z", hemisphere.Z, 0, 0, 0);
        // One face lit: each face fills a sixth of all directions, so the constant is 1/6; the term along the face's axis is
        // 3/(4 pi) times the face's integral of that axis, 2 sqrt 2 atan(1 / sqrt 2) = 1.74084, which is 0.41559.
        // Faces are stored +X, -X, +Y, -Y, +Z, -Z.
        float[][] axes = [[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]];
        for (int lit = 0; lit < 6; lit++)
        {
            var face = SourceCharacterLight.Fit(Cube(16, (f, _, _) => f == lit ? 1f : 0f), one, none, none);
            Near($"Face {lit}'s constant", face.Constant, 1 / 6f, 1 / 6f, 1 / 6f);
            Near($"Face {lit}'s X", face.X, Enumerable.Repeat(0.41559f * axes[lit][0], 3).ToArray());
            Near($"Face {lit}'s Y", face.Y, Enumerable.Repeat(0.41559f * axes[lit][1], 3).ToArray());
            Near($"Face {lit}'s Z", face.Z, Enumerable.Repeat(0.41559f * axes[lit][2], 3).ToArray());
        }
        // Within the top face a texel's column runs with X and its row with Z, as cube faces are laid out. The half of the
        // face toward +X carries 3/(4 pi) times its integral of x, 0.35019 by fine summation: 0.08360.
        var right = SourceCharacterLight.Fit(Cube(16, (f, x, _) => f == 2 && x >= 8 ? 1f : 0f), one, none, none);
        Near("The top face's +X half, X", right.X, 0.0836f, 0.0836f, 0.0836f); Near("The top face's +X half, Z", right.Z, 0, 0, 0);
        var far = SourceCharacterLight.Fit(Cube(16, (f, _, y) => f == 2 && y >= 8 ? 1f : 0f), one, none, none);
        Near("The top face's +Z half, Z", far.Z, 0.0836f, 0.0836f, 0.0836f); Near("The top face's +Z half, X", far.X, 0, 0, 0);
        // On the +X face a column runs toward -Z and a row downward.
        var side = SourceCharacterLight.Fit(Cube(16, (f, x, _) => f == 0 && x >= 8 ? 1f : 0f), one, none, none);
        Near("The +X face's right half, Z", side.Z, -0.0836f, -0.0836f, -0.0836f);
        var low = SourceCharacterLight.Fit(Cube(16, (f, _, y) => f == 0 && y >= 8 ? 1f : 0f), one, none, none);
        Near("The +X face's lower half, Y", low.Y, -0.0836f, -0.0836f, -0.0836f);
        // Each colour takes its own environment gain.
        var tinted = SourceCharacterLight.Fit(Cube(16, (f, _, _) => f == 2 ? 1f : 0f), [3, 0, 1.5f], none, none);
        Near("A tinted face's constant", tinted.Constant, 0.5f, 0, 0.25f);
        Console.WriteLine("PASS: the source's character light restates as a constant and three linear terms that follow the cube's faces and the hemisphere");
    }
}
