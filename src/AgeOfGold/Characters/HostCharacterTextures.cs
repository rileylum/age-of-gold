using AgeOfGold.Scene;

namespace AgeOfGold.Characters;

/// <summary>Textures a build's characters share, made from the source's.</summary>
internal static class HostCharacterTextures
{
    /// <summary>
    /// The source's reflectance table (how much of a reflection a surface returns, by roughness across and by the angle to
    /// the eye down) as a two-channel texture the host can load. The source keeps two half-precision numbers a texel, all
    /// between nothing and one.
    /// </summary>
    internal static byte[] Table(byte[] sourceTable)
    {
        int Word(int at) => BitConverter.ToInt32(sourceTable, at);
        // A plain header naming the two-channel half-precision format by its number, 112.
        if (sourceTable.Length < 128 || !sourceTable.AsSpan().StartsWith("DDS "u8) || (Word(80) & 4) == 0 || Word(84) != 112)
            throw new InvalidDataException("The source's reflectance table is not two half-precision channels");
        int height = Word(12), width = Word(16);
        if (width <= 0 || height <= 0 || (long)width * height * 4 > sourceTable.Length - 128) throw new InvalidDataException("The source's reflectance table is shorter than its size");
        var rgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            for (int channel = 0; channel < 2; channel++)
            {
                float value = (float)BitConverter.ToHalf(sourceTable, 128 + i * 4 + channel * 2);
                if (!(value >= 0 && value <= 1.001f)) throw new InvalidDataException("The source's reflectance table holds a value outside nothing to one");
                rgba[i * 4 + channel] = (byte)MathF.Round(Math.Min(value, 1) * 255);
            }
            rgba[i * 4 + 3] = 255;
        }
        return BlockTexture.EncodeTwo(new(width, height, rgba));
    }
}
