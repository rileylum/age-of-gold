using AgeOfGold.Scene;

namespace AgeOfGold.Characters;

/// <summary>
/// Makes the two maps the host's character material wants from the source's. The remastered source shades by roughness
/// and metalness and keeps both in its specular map: red is roughness, green is metalness. The host wants metalness in a
/// map of its own and gloss, the reverse of roughness, in the blue channel of the relief map.
/// </summary>
internal static class HostCharacterTextures
{
    /// <summary>The host's relief map: the source relief map's two channels, with gloss from the source specular map's red channel.</summary>
    /// <param name="sourceSpecular">Null for a surface with no specular map: it is given no gloss.</param>
    internal static byte[] ReliefAndGloss(byte[] sourceRelief, byte[]? sourceSpecular)
    {
        var relief = BlockTexture.Decode(sourceRelief);
        var specular = sourceSpecular is null ? null : BlockTexture.Decode(sourceSpecular);
        // The two maps may differ in size; the result is the larger's, each read at the texel that covers the same place.
        int width = Math.Max(relief.Width, specular?.Width ?? 0), height = Math.Max(relief.Height, specular?.Height ?? 0);
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int at = (y * width + x) * 4, from = (y * relief.Height / height * relief.Width + x * relief.Width / width) * 4;
                rgba[at] = relief.Rgba[from]; rgba[at + 1] = relief.Rgba[from + 1]; rgba[at + 3] = 255;
                if (specular is not null) rgba[at + 2] = (byte)(255 - specular.Rgba[(y * specular.Height / height * specular.Width + x * specular.Width / width) * 4]);
            }
        return BlockTexture.EncodeFour(new(width, height, rgba));
    }

    /// <summary>
    /// The host's colour map and metal map for one surface, made texel by texel so that what the host lights and what it
    /// reflects are what the source does. The source lights (colour map × a colour of its definition's)^2.2 where the
    /// specular map says not metal, and reflects that where it says metal, or a fifth of the specular map's blue elsewhere.
    /// </summary>
    /// <param name="plain">What the definition multiplies the colour map by where the surface is not metal, before the display curve.</param>
    /// <param name="metal">What it multiplies by where the surface is metal.</param>
    /// <param name="reflectance">How many times the source's reflectance the maps carry, for a host set to reflect that many times more weakly.</param>
    internal static (byte[] Colour, byte[] Metal) Surface(byte[] sourceColour, byte[]? sourceSpecular, float[] plain, float[] metal, float reflectance = 1)
    {
        if (!(reflectance >= 1) || !float.IsFinite(reflectance)) throw new InvalidDataException("Reflectance is carried once or more");
        foreach (var factor in new[] { plain, metal })
            if (factor.Length != 3 || factor.Any(v => !float.IsFinite(v) || v < 0)) throw new InvalidDataException("A surface colour is three finite values, none negative");
        var colour = BlockTexture.Decode(sourceColour);
        var specular = sourceSpecular is null ? null : BlockTexture.Decode(sourceSpecular);
        int width = Math.Max(colour.Width, specular?.Width ?? 0), height = Math.Max(colour.Height, specular?.Height ?? 0);
        static float Brightness(ReadOnlySpan<float> rgb) => 0.2126729f * rgb[0] + 0.7151522f * rgb[1] + 0.072175f * rgb[2];
        // Where the surface is not metal the source reflects a fifth of the specular map's blue, times the brightness of
        // the definition's metal colour.
        float level = 0.2f * Math.Max(Brightness(metal), 0.001f);
        // The host's display curve, which its colour maps are stored through.
        static byte Stored(float linear) => (byte)MathF.Round(255 * Math.Clamp(linear <= 0.0031308f ? 12.92f * linear : 1.055f * MathF.Pow(linear, 1 / 2.4f) - 0.055f, 0, 1));
        var made = new byte[width * height * 4];
        var metals = new byte[width * height * 4];
        Span<float> lit = stackalloc float[3], reflected = stackalloc float[3];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int from = (y * colour.Height / height * colour.Width + x * colour.Width / width) * 4, to = (y * width + x) * 4;
                float metalness = 0, blue = 0;
                if (specular is not null)
                {
                    int at = (y * specular.Height / height * specular.Width + x * specular.Width / width) * 4;
                    metalness = specular.Rgba[at + 1] / 255f; blue = specular.Rgba[at + 2] / 255f;
                }
                for (int c = 0; c < 3; c++)
                {
                    float surface = MathF.Pow(colour.Rgba[from + c] / 255f * (plain[c] + (metal[c] - plain[c]) * metalness), 2.2f);
                    lit[c] = (1 - metalness) * surface;
                    reflected[c] = Math.Min(1, reflectance * (Math.Clamp(level * blue, 0, 1) * (1 - metalness) + surface * metalness));
                }
                // The host lights colour × share and reflects 4 % × share + colour × (1 - share), where share is what is not
                // metal. Both match the source where 0.04 share² - (lit + reflected) share + lit = 0 has a root no greater than
                // one. Where the source reflects less than the host's 4 % allows there is none: the surface stays plain and
                // keeps its colour, and the host reflects more than the source would.
                float d = Brightness(lit), f = Brightness(reflected), share = 1;
                float root = (d + f) * (d + f) - 0.16f * d;
                if (root >= 0 && (d + f - MathF.Sqrt(root)) / 0.08f is var exact && exact < 1) share = Math.Max(0, exact);
                for (int c = 0; c < 3; c++) made[to + c] = Stored(share == 1 ? lit[c] : lit[c] + reflected[c] - 0.04f * share);
                made[to + 3] = colour.Rgba[from + 3];
                metals[to] = (byte)MathF.Round(255 * (1 - share));
            }
        // lazy: the colour map is written in the encoder's two one-line modes, coarser than the source's own blocks where a
        // block holds two unlike colours; add a two-region mode if seams show.
        return (BlockTexture.EncodeFour(new(width, height, made), coverage: true), BlockTexture.EncodeSingle(new(width, height, metals), 0));
    }
}
