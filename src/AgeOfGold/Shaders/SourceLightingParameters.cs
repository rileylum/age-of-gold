using AgeOfGold.Params;

namespace AgeOfGold.Shaders;

internal static class SourceLightingParameters
{
    internal static Dictionary<(string Buffer, string Field), Array> Values(
        SourceLightingReader.LightSet lighting, SourceRendererSettings settings) => new()
        {
            [("$Globals", "gFC_EnvDifMapMulCol")] = Linear(lighting.DiffuseEnvironment.Colour, 1),
            [("$Globals", "gFC_EnvSpcMapMulCol")] = Linear(lighting.SpecularEnvironment.Colour, 1),
            [("$Globals", "gFC_HemAmbCol_u")] = Linear(lighting.Upper, settings.HemisphereStrength),
            [("$Globals", "gFC_HemAmbCol_d")] = Linear(lighting.Lower, settings.HemisphereStrength),
        };

    private static float[] Linear(SourceLightingReader.Colour colour, float strength)
    {
        // Intensity participates in the gamma curve; hemisphere strength scales the linear result.
        float Channel(float value) => MathF.Pow(value * colour.Intensity, 2.2f) * strength;
        return [Channel(colour.Red), Channel(colour.Green), Channel(colour.Blue), 0];
    }
}
