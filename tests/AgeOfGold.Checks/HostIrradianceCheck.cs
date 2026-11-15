using AgeOfGold.Characters;
using AgeOfGold.Maps;
using SoulsFormats;

/// <summary>The host's baked ambient light for characters, made uniform from terms of the build's own.</summary>
internal static class HostIrradianceCheck
{
    // What the host was measured to do with a stored term (docs/research, 15 November 2026): each colour is signed about
    // mid-grey; at full colour and scale 96 a surface is lit at 0.234 of its own colour; each step of scale multiplies by e^0.0507.
    private static float Shown(byte colour, byte scale) => (colour - 127.5f) / 127.5f * 0.234f * MathF.Exp(0.0507f * (scale - 96));

    private static byte[] Volume(int width, int height, int depth, byte fill)
    {
        var dds = new DDS { dwWidth = width, dwHeight = height, dwDepth = depth, dwMipMapCount = 1, dwCaps2 = DDS.DDSCAPS2.VOLUME,
            ddspf = new DDS.PIXELFORMAT { dwFourCC = "DX10", dwFlags = DDS.DDPF.FOURCC },
            header10 = new DDS.HEADER_DXT10 { dxgiFormat = DDS.DXGI_FORMAT.BC7_UNORM, resourceDimension = DDS.DIMENSION.TEXTURE3D } };
        return TextureWriterCheck.Fixture(dds.Write(Enumerable.Repeat(fill, (width + 3) / 4 * ((height + 3) / 4) * depth * 16).ToArray()));
    }

    internal static void Run()
    {
        // The reference term is stored at full colour and the reference scale.
        if (!HostIrradiance.Encode([0.234f, 0.234f, 0.234f]).SequenceEqual(new byte[] { 255, 255, 255, 96 })) throw new Exception("The reference term is not stored at full colour and scale 96");
        foreach (float[] term in new float[][] { [0.43f, 0.5f, 0.61f], [0.08f, 0.077f, 0.086f], [-0.105f, -0.104f, -0.05f], [0.01f, -0.01f, 0.004f], [3f, 2f, 0.5f], [0.6f, 0f, -0.6f] })
        {
            var stored = HostIrradiance.Encode(term);
            float largest = term.Max(Math.Abs);
            for (int c = 0; c < 3; c++)
                if (Math.Abs(Shown(stored[c], stored[3]) - term[c]) > 0.015f * largest) throw new Exception($"Term {string.Join(", ", term)} is stored as {string.Join(", ", stored)}, which the host shows as {Shown(stored[c], stored[3])} in colour {c}");
        }
        // A light fainter than the smallest scale holds is stored at that scale, in the colours' fractions.
        var faint = HostIrradiance.Encode([0.0004f, 0.0002f, 0.0001f]);
        if (faint[3] != 0 || !(faint[0] > faint[1] && faint[1] > faint[2] && faint[2] > 128)) throw new Exception("A faint light is not stored at the smallest scale");
        // No light is stored at no scale, where the colour no longer matters.
        if (HostIrradiance.Encode([0, 0, 0])[3] != 0) throw new Exception("No light is not stored at no scale");
        try { HostIrradiance.Encode([2000f, 0, 0]); throw new Exception("A term beyond the host's range was stored"); } catch (InvalidDataException) { }
        try { HostIrradiance.Encode([float.NaN, 0, 0]); throw new Exception("A term that is not a number was stored"); } catch (InvalidDataException) { }

        // A binder of one point's four volumes and its reflection cube, and a second point's volumes of another size.
        var cube = new DDS { dwWidth = 4, dwHeight = 4, dwMipMapCount = 1, dwCaps2 = DDS.CUBEMAP_ALLFACES, ddspf = new DDS.PIXELFORMAT { dwFourCC = "DX10", dwFlags = DDS.DDPF.FOURCC },
            header10 = new DDS.HEADER_DXT10 { dxgiFormat = DDS.DXGI_FORMAT.BC6H_UF16, miscFlag = DDS.RESOURCE_MISC.TEXTURECUBE } };
        var reflection = TextureWriterCheck.Fixture(cube.Write(Enumerable.Range(0, 96).Select(i => (byte)i).ToArray()));
        var binder = new BND4();
        int id = 0;
        foreach (var (point, width, height, depth) in new[] { ("0000", 8, 4, 2), ("0103", 12, 12, 3) })
            foreach (char term in "WXYZ") binder.Files.Add(new BinderFile(Binder.FileFlags.Flag1, id++, $"m99_00_00_00_GIIV{point}_00_{term}.tpf", Volume(width, height, depth, (byte)(17 * id))));
        binder.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 80, "m99_00_00_00_GILM0000_00_rem.tpf", reflection));
        var light = new SourceCharacterLight.Terms([0.43f, 0.5f, 0.61f], [0.08f, 0.077f, 0.086f], [0.41f, 0.5f, 0.59f], [-0.105f, -0.104f, -0.05f]);
        // The sky to reflect: a cube of sixteen texels a side with three levels (16, 8 and 4), every byte telling its place.
        cube.dwWidth = cube.dwHeight = 16; cube.dwMipMapCount = 3;
        var sky = cube.Write(Enumerable.Range(0, 6 * (16 + 4 + 1) * 16).Select(i => (byte)(i * 7 + i / 251)).ToArray());
        var uniform = BND4.Read(HostIrradiance.Uniform(binder.Write(), light, sky));
        if (!uniform.Files.Select(f => (f.ID, f.Name)).SequenceEqual(binder.Files.Select(f => (f.ID, f.Name)))) throw new Exception("A uniform binder does not keep its members");
        // The host's cube is four texels a side with one level: it takes each face's third level of the sky, under its own header.
        var reflected = uniform.Files.Single(f => f.ID == 80).Bytes;
        int skyData = new DDS(sky).DataOffset;
        if (reflected.Length != reflection.Length || !reflected.AsSpan(0, 40 + 148).SequenceEqual(reflection.AsSpan(0, 40 + 148))) throw new Exception("A reflection cube's package or header was changed");
        for (int face = 0; face < 6; face++)
            if (!reflected.AsSpan(188 + face * 16, 16).SequenceEqual(sky.AsSpan(skyData + face * 21 * 16 + 20 * 16, 16))) throw new Exception($"A reflection cube's face {face} is not the sky's level of its size");
        // A sky with no level of the host cube's size is not stretched to fit.
        cube.dwWidth = cube.dwHeight = 16; cube.dwMipMapCount = 2;
        try { HostIrradiance.Uniform(binder.Write(), light, cube.Write(new byte[6 * (16 + 4) * 16])); throw new Exception("A sky without a level of the host cube's size was accepted"); } catch (InvalidDataException) { }
        float[][] terms = [light.Constant, light.X, light.Y, light.Z];
        for (int i = 0; i < 8; i++)
        {
            byte[] before = binder.Files[i].Bytes, after = uniform.Files[i].Bytes;
            // The package and the texture's own header are kept; every block after them is the one stored term.
            if (after.Length != before.Length || !after.AsSpan(0, 40 + 148).SequenceEqual(before.AsSpan(0, 40 + 148))) throw new Exception("A volume's package or header was changed");
            var expected = HostIrradiance.Encode(terms[i % 4]);
            int blocks = (after.Length - 188) / 16;
            if (blocks != (i < 4 ? 2 * 1 * 2 : 3 * 3 * 3)) throw new Exception("A volume fixture is not the size intended");
            for (int block = 0; block < blocks; block++)
            {
                var texels = BlockTextureCheck.ReadOneLine(after.AsSpan(188 + block * 16, 16));
                for (int t = 0; t < 16; t++)
                    if (Math.Abs(texels[t * 4] - expected[0]) > 1 || Math.Abs(texels[t * 4 + 1] - expected[1]) > 1 || Math.Abs(texels[t * 4 + 2] - expected[2]) > 1 || texels[t * 4 + 3] != expected[3])
                        throw new Exception($"Volume {binder.Files[i].Name} block {block} does not hold its term");
            }
        }
        if (!HostIrradiance.Uniform(binder.Write(), light, sky).SequenceEqual(HostIrradiance.Uniform(binder.Write(), light, sky))) throw new Exception("A uniform binder does not repeat");
        // A member that is neither a volume nor a reflection cube is not passed over.
        binder.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 99, "m99_00_00_00_OTHER.tpf", reflection));
        try { HostIrradiance.Uniform(binder.Write(), light, sky); throw new Exception("An unclassified environment member was passed over"); } catch (InvalidDataException) { }
        Console.WriteLine("PASS: a light's terms are stored as the host reads them and fill every volume of an environment binder, whose cubes take the sky's levels of their size");
    }
}
