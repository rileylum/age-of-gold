using AgeOfGold.Params;
using AgeOfGold.Shaders;

internal static class SourceLightingParametersCheck
{
    internal static void Run()
    {
        var lighting = new SourceLightingReader.LightSet(7, [], new(0, 0, new(0, 0, 0, 0)),
            new(0.25f, 0.5f, 1, 2), new(1.25f, 0, 0.75f, 0.5f),
            new(new(0.125f, 0.375f, 0.875f, 4), []), new(new(0.5f, 1, 2, 1), []));
        var actual = SourceLightingParameters.Values(lighting, new(0.625f, 0.875f));
        Require(actual.Count == 4, "Expected four ambient lighting parameters");
        // Independently calculated with decimal arithmetic, before rounding to float.
        Check("gFC_HemAmbCol_u", [0.13602353f, 0.625f, 2.8717458f, 0]);
        Check("gFC_HemAmbCol_d", [0.22223684f, 0, 0.07223519f, 0]);
        Check("gFC_EnvDifMapMulCol", [0.21763764f, 2.4400615f, 15.738006f, 0]);
        Check("gFC_EnvSpcMapMulCol", [0.21763764f, 1, 4.5947934f, 0]);
        var disabled = SourceLightingParameters.Values(lighting, new(0, 1));
        Require(((float[])disabled[("$Globals", "gFC_HemAmbCol_u")]).All(v => v == 0), "Zero hemisphere strength remains lit");
        Require(((float[])disabled[("$Globals", "gFC_EnvDifMapMulCol")]).SequenceEqual((float[])actual[("$Globals", "gFC_EnvDifMapMulCol")]),
            "Hemisphere strength changed environment lighting");
        Console.WriteLine("PASS: source ambient colour conversion, HDR intensity, separate hemisphere multiplier and zero strength");
        void Check(string name, float[] expected)
        {
            Require(actual.TryGetValue(("$Globals", name), out var value) && value is float[] { Length: 4 }, "Missing ambient field " + name);
            var observed = (float[])value!;
            for (var i = 0; i < expected.Length; i++)
                Require(MathF.Abs(observed[i] - expected[i]) < 0.000002f, $"Ambient field {name} component {i} differs: {observed[i]}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
