using AgeOfGold.Scene;

namespace AgeOfGold.Characters;

/// <summary>
/// Makes the two maps the host's character material wants from the source's. The remastered source shades by roughness
/// and metalness and keeps both in its specular map: red is roughness, green is metalness. The host wants metalness in a
/// map of its own and gloss, the reverse of roughness, in the blue channel of the relief map.
/// </summary>
internal static class HostCharacterTextures
{
    /// <summary>The share of a surface that is metal: the mean of the source specular map's green.</summary>
    internal static float Metalness(byte[] sourceSpecular)
    {
        var image = BlockTexture.Decode(sourceSpecular);
        long sum = 0;
        for (int texel = 0; texel < image.Width * image.Height; texel++) sum += image.Rgba[texel * 4 + 1];
        return sum / (255f * image.Width * image.Height);
    }

    /// <summary>The host's metal map: the source specular map's green channel.</summary>
    internal static byte[] Metal(byte[] sourceSpecular) => BlockTexture.EncodeSingle(BlockTexture.Decode(sourceSpecular), 1);

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
}
