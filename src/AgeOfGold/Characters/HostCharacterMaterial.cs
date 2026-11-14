using SoulsFormats;

namespace AgeOfGold.Characters;

/// <summary>Material definitions of the build's own for characters, made from installed host definitions.</summary>
internal static class HostCharacterMaterial
{
    /// <summary>
    /// A plain host character definition that also cuts its surface out where the colour map's coverage falls below a
    /// threshold, as the source's edged materials do for torn cloth, hair and mail. The program is the same one; the
    /// threshold alone turns the cut-out on.
    /// </summary>
    /// <param name="reference">The coverage, of 255, below which nothing is drawn.</param>
    internal static byte[] Cutout(byte[] plain, int reference)
    {
        if (reference is <= 0 or > 255) throw new InvalidDataException("A cut-out's coverage threshold lies between 1 and 255");
        var definition = MATBIN.Read(plain);
        var threshold = definition.Params.SingleOrDefault(p => p.Name == "g_AlphaRef");
        if (threshold is not { Type: MATBIN.ParamType.Int2, Value: int[] { Length: 2 } values } || definition.Samplers.Any(s => s.Path.Length != 0))
            throw new InvalidDataException("Host character definition has no coverage threshold, or names textures of its own");
        threshold.Value = new[] { reference, values[1] };
        return definition.Write();
    }
}
