using AgeOfGold.Characters;

/// <summary>The source's hemisphere light on a character, restated as a constant and a term along the up axis.</summary>
internal static class CharacterLightCheck
{
    internal static void Run()
    {
        float[] upper = [0.5f, 0.5f, 0.9f], lower = [0.1f, 0.3f, 0.1f];
        var light = SourceCharacterLight.Hemisphere(upper, lower);
        void Near(string what, float[] got, params float[] expected)
        {
            for (int i = 0; i < 3; i++)
                if (!(Math.Abs(got[i] - expected[i]) <= 1e-6f)) throw new Exception($"{what} is {string.Join(", ", got)}; expected {string.Join(", ", expected)}");
        }
        // A surface facing up takes the upper colour, one facing down the lower, one facing sideways their mean, whichever way it turns.
        Near("Light from above", light.Along([0, 1, 0]), upper);
        Near("Light from below", light.Along([0, -1, 0]), lower);
        Near("Light from the side", light.Along([0.6f, 0, -0.8f]), 0.3f, 0.4f, 0.5f);
        Near("Light on a tilted surface", light.Along([0.6f, 0.8f, 0]), 0.46f, 0.48f, 0.82f);
        foreach (var bad in new Action[] { () => SourceCharacterLight.Hemisphere([0.5f, 0.5f], lower), () => SourceCharacterLight.Hemisphere(upper, [0.1f, float.NaN, 0.1f]), () => SourceCharacterLight.Hemisphere(upper, [0.1f, -0.2f, 0.1f]) })
        {
            try { bad(); throw new Exception("An unusable hemisphere colour was accepted"); } catch (InvalidDataException) { }
        }
        Console.WriteLine("PASS: the source's hemisphere light is a constant and a term along the up axis");
    }
}
