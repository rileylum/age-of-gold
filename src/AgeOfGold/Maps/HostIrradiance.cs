using System.Numerics;
using System.Text.RegularExpressions;
using AgeOfGold.Characters;
using AgeOfGold.Scene;
using SoulsFormats;

namespace AgeOfGold.Maps;

/// <summary>
/// The host's baked light for characters. Beside each of a map's environment points it keeps four volume textures, a
/// constant term and one along each axis, and a cube to reflect. Baked for the host's own scenery, they are replaced by
/// one light and one sky for the whole map.
/// </summary>
internal static class HostIrradiance
{
    // Measured in the host on a flat grey body: a term stored at full colour and this scale lights a surface at this
    // share of its own colour, and each step of scale multiplies the light by e to this power. Environment boxes are
    // left at the host's own multiplier of one, which the measurement was made at.
    private const int ReferenceScale = 96;
    private const float AtReference = 0.234f, Step = 0.0507f;

    /// <summary>A term as the host stores it: red, green and blue signed about mid-grey, then one scale for the three.</summary>
    internal static byte[] Encode(float[] term)
    {
        if (term.Length != 3 || !term.All(float.IsFinite)) throw new InvalidDataException("A light term must be three finite values");
        float largest = term.Max(MathF.Abs);
        if (largest == 0) return [128, 128, 128, 0];
        // The smallest scale that holds the largest colour keeps the most of the others.
        int scale = Math.Max(0, (int)MathF.Ceiling(ReferenceScale + MathF.Log(largest / AtReference) / Step));
        if (scale > 255) throw new InvalidDataException("A light term is beyond what the host's volumes hold");
        float full = AtReference * MathF.Exp(Step * (scale - ReferenceScale));
        return [.. term.Select(value => (byte)Math.Clamp(MathF.Round(127.5f + 127.5f * value / full), 0, 255)), (byte)scale];
    }

    /// <summary>
    /// An environment binder with every volume holding this light throughout, and every reflection cube holding the
    /// levels of <paramref name="sky"/> that are its own size.
    /// </summary>
    internal static byte[] Uniform(byte[] binder, SourceCharacterLight.Terms light, byte[] sky)
    {
        var from = new DDS(sky);
        if (from.GetDXGIFormat() != DDS.DXGI_FORMAT.BC6H_UF16 || from.dwCaps2 != DDS.CUBEMAP_ALLFACES || from.dwWidth != from.dwHeight || from.dwWidth <= 0)
            throw new InvalidDataException("The sky to reflect is not an unsigned HDR cube");
        // The bytes of a run of a face's levels, each level a square of blocks.
        static int Levels(int size, int first, int count) => Enumerable.Range(first, count).Sum(level => { int blocks = (Math.Max(1, size >> level) + 3) / 4; return blocks * blocks * 16; });
        int skyFace = Levels(from.dwWidth, 0, Math.Max(1, from.dwMipMapCount));
        if (sky.Length - from.DataOffset < 6 * skyFace) throw new InvalidDataException("The sky to reflect is shorter than its six faces");
        var environment = BND4.Read(binder);
        foreach (var file in environment.Files)
        {
            string name = Path.GetFileNameWithoutExtension(file.Name.Replace('\\', '/'));
            if (Regex.IsMatch(name, @"_GILM\d{4}_\d{2}_rem$"))
            {
                DCX.CompressionInfo? cubePacked = null;
                var cubePackage = DCX.Is(file.Bytes) ? DCX.Decompress(file.Bytes, out cubePacked) : (byte[])file.Bytes.Clone();
                if (cubePackage.Length < 40 || !cubePackage.AsSpan(0, 4).SequenceEqual("TPF\0"u8) || BitConverter.ToInt32(cubePackage, 8) != 1 || cubePackage[27] is not (0 or 128))
                    throw new InvalidDataException("Unsupported reflection cube package: " + name);
                int cubeOffset = BitConverter.ToInt32(cubePackage, 16), cubeSize = BitConverter.ToInt32(cubePackage, 20);
                if (cubeOffset < 40 || cubeSize < 148 || cubeOffset > cubePackage.Length - cubeSize) throw new InvalidDataException("Reflection cube lies outside its package: " + name);
                var to = new DDS(cubePackage.AsSpan(cubeOffset, cubeSize).ToArray());
                int levels = Math.Max(1, to.dwMipMapCount), skip = BitOperations.Log2((uint)Math.Max(1, from.dwWidth / Math.Max(1, to.dwWidth)));
                int face = Levels(to.dwWidth, 0, levels);
                if (to.GetDXGIFormat() != DDS.DXGI_FORMAT.BC6H_UF16 || to.dwCaps2 != DDS.CUBEMAP_ALLFACES || to.dwWidth << skip != from.dwWidth || skip + levels > Math.Max(1, from.dwMipMapCount)
                    || cubeSize - to.DataOffset != 6 * face)
                    throw new InvalidDataException("The sky to reflect has no levels of the size of the host's cube: " + name);
                for (int f = 0; f < 6; f++) sky.AsSpan(from.DataOffset + f * skyFace + Levels(from.dwWidth, 0, skip), face).CopyTo(cubePackage.AsSpan(cubeOffset + to.DataOffset + f * face));
                file.Bytes = cubePacked is null ? cubePackage : DCX.Compress(cubePackage, cubePacked);
                continue;
            }
            var volume = Regex.Match(name, @"_GIIV\d{4}_\d{2}_([WXYZ])$");
            if (!volume.Success) throw new InvalidDataException("Unclassified environment member: " + name);
            var stored = Encode(volume.Groups[1].Value switch { "W" => light.Constant, "X" => light.X, "Y" => light.Y, _ => light.Z });
            var block = BlockTexture.Solid(stored[0], stored[1], stored[2], stored[3]);
            DCX.CompressionInfo? packed = null;
            var package = DCX.Is(file.Bytes) ? DCX.Decompress(file.Bytes, out packed) : (byte[])file.Bytes.Clone();
            // One texture, stored plainly.
            if (package.Length < 40 || !package.AsSpan(0, 4).SequenceEqual("TPF\0"u8) || BitConverter.ToInt32(package, 8) != 1 || package[27] is not (0 or 128))
                throw new InvalidDataException("Unsupported volume package: " + name);
            int offset = BitConverter.ToInt32(package, 16), size = BitConverter.ToInt32(package, 20);
            if (offset < 40 || size < 148 || offset > package.Length - size) throw new InvalidDataException("Volume texture lies outside its package: " + name);
            var header = new DDS(package.AsSpan(offset, size).ToArray());
            if (header.GetDXGIFormat() != DDS.DXGI_FORMAT.BC7_UNORM || header.dwMipMapCount > 1 || header.dwCaps2 != DDS.DDSCAPS2.VOLUME || (size - header.DataOffset) % 16 != 0)
                throw new InvalidDataException("Unsupported volume texture: " + name);
            for (int at = offset + header.DataOffset; at < offset + size; at += 16) block.CopyTo(package, at);
            file.Bytes = packed is null ? package : DCX.Compress(package, packed);
        }
        return environment.Write();
    }
}
