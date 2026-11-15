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

    /// <summary>
    /// A definition for one kind of character surface, drawn by a family of the build's own: the host's plain definition
    /// with the source definition's plain and metal colours as its two colour settings, the textures the build's program
    /// adds, and optionally a coverage threshold.
    /// </summary>
    /// <param name="added">The added texture inputs, by the names the family's metadata gives them; the donor definition declares them.</param>
    /// <param name="older">Whether the source reads the surface's specular map the older way, as a reflection colour; the definition's first number of its own says so.</param>
    /// <param name="share">The share of a shadow's density the surface takes; the definition's second number of its own says so.</param>
    internal static byte[] Own(byte[] plain, byte[] donor, IReadOnlyList<string> added, string hostFamily, string family, float[] plainColour, float[] metalColour, int? cutout, bool older, float share)
    {
        var definition = MATBIN.Read(cutout is { } reference ? Cutout(plain, reference) : plain);
        if (!definition.SourcePath.Contains(hostFamily, StringComparison.Ordinal) || !definition.ShaderPath.Contains(hostFamily, StringComparison.Ordinal))
            throw new InvalidDataException("Host character definition is not drawn by " + hostFamily);
        definition.SourcePath = definition.SourcePath.Replace(hostFamily, family, StringComparison.Ordinal);
        definition.ShaderPath = definition.ShaderPath.Replace(hostFamily, family, StringComparison.Ordinal);
        var colour = definition.Params.SingleOrDefault(p => p.Name == "g_DiffuseMapColor");
        var kind = definition.Params.SingleOrDefault(p => p.Name.EndsWith("_snp_0_color_0", StringComparison.Ordinal));
        if (colour is not { Type: MATBIN.ParamType.Float3, Value: float[] { Length: 3 } } || kind is not { Type: MATBIN.ParamType.Float5, Value: float[] { Length: 5 } rest }
            || plainColour.Length != 3 || metalColour.Length != 3 || !plainColour.Concat(metalColour).All(float.IsFinite))
            throw new InvalidDataException("Host character definition lacks its two colour settings, or a colour is not three numbers");
        var reading = definition.Params.SingleOrDefault(p => p.Name.EndsWith("_snp_0_float_0", StringComparison.Ordinal));
        var shared = definition.Params.SingleOrDefault(p => p.Name.EndsWith("_snp_0_float_1", StringComparison.Ordinal));
        if (reading is not { Type: MATBIN.ParamType.Float, Value: float } || shared is not { Type: MATBIN.ParamType.Float, Value: float } || !(share >= 0) || !float.IsFinite(share))
            throw new InvalidDataException("Host character definition lacks two numbers of its own, or the share of a shadow is not a number from nought up");
        reading.Value = older ? 1f : 0f; shared.Value = share;
        colour.Value = plainColour.ToArray();
        kind.Value = new[] { metalColour[0], metalColour[1], metalColour[2], rest[3], rest[4] };
        var declared = MATBIN.Read(donor).Samplers;
        foreach (string name in added)
        {
            var sampler = declared.SingleOrDefault(s => s.Type == name);
            if (sampler is null || definition.Samplers.Any(s => s.Type == name)) throw new InvalidDataException("Added character texture is not declared once: " + name);
            definition.Samplers.Add(sampler);
        }
        return definition.Write();
    }
}
