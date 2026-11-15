using AgeOfGold.Characters;
using AgeOfGold.Scene;
using SoulsFormats;

/// <summary>The block-compressed textures a build makes or reads, against values worked out by hand from the formats' definitions.</summary>
internal static class BlockTextureCheck
{
    private static byte[] Dds(DDS.DXGI_FORMAT format, int width, int height, byte[] payload)
    {
        var dds = new DDS { dwWidth = width, dwHeight = height, dwMipMapCount = 1, dwPitchOrLinearSize = payload.Length,
            ddspf = new DDS.PIXELFORMAT { dwFourCC = "DX10", dwFlags = DDS.DDPF.FOURCC }, header10 = new DDS.HEADER_DXT10 { dxgiFormat = format } };
        return dds.Write(payload);
    }

    // The host's high-quality format read back by the definitions of the two modes the build writes, written apart from the encoder.
    internal static byte[] ReadOneLine(ReadOnlySpan<byte> block)
    {
        int at = 0;
        int Take(ReadOnlySpan<byte> from, int bits) { int value = 0; for (int b = 0; b < bits; b++, at++) value |= (from[at >> 3] >> (at & 7) & 1) << b; return value; }
        var texels = new byte[64];
        if ((block[0] & 0x7f) == 0x40)
        {
            Take(block, 7);
            var ends = new int[2, 4];
            for (int c = 0; c < 4; c++) { ends[0, c] = Take(block, 7); ends[1, c] = Take(block, 7); }
            int p0 = Take(block, 1), p1 = Take(block, 1);
            for (int c = 0; c < 4; c++) { ends[0, c] = ends[0, c] << 1 | p0; ends[1, c] = ends[1, c] << 1 | p1; }
            int[] weights = [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];
            for (int i = 0; i < 16; i++)
            {
                int w = weights[Take(block, i == 0 ? 3 : 4)];
                for (int c = 0; c < 4; c++) texels[i * 4 + c] = (byte)(((64 - w) * ends[0, c] + w * ends[1, c] + 32) >> 6);
            }
            return texels;
        }
        if ((block[0] & 0x3f) != 0x20) throw new Exception("A block is in neither mode the build writes");
        Take(block, 6);
        int rotation = Take(block, 2);
        var colour = new int[2, 3];
        for (int c = 0; c < 3; c++) for (int e = 0; e < 2; e++) { int q = Take(block, 7); colour[e, c] = q << 1 | q >> 6; }
        int a0 = Take(block, 8), a1 = Take(block, 8);
        int[] two = [0, 21, 43, 64];
        for (int i = 0; i < 16; i++) { int w = two[Take(block, i == 0 ? 1 : 2)]; for (int c = 0; c < 3; c++) texels[i * 4 + c] = (byte)(((64 - w) * colour[0, c] + w * colour[1, c] + 32) >> 6); }
        for (int i = 0; i < 16; i++) { int w = two[Take(block, i == 0 ? 1 : 2)]; texels[i * 4 + 3] = (byte)(((64 - w) * a0 + w * a1 + 32) >> 6); }
        // The rotation says which colour channel changed places with coverage.
        if (rotation != 0) for (int i = 0; i < 16; i++) (texels[i * 4 + rotation - 1], texels[i * 4 + 3]) = (texels[i * 4 + 3], texels[i * 4 + rotation - 1]);
        return texels;
    }

    // The two-channel block format read back by its definition, written apart from the encoder: each channel two values
    // and three bits a texel choosing between them and their blends.
    private static byte[] ReadTwo(byte[] dds, out int width, out int height)
    {
        var header = new DDS(dds);
        width = header.dwWidth; height = header.dwHeight;
        var values = new byte[width * height * 2];
        for (int by = 0; by < (height + 3) / 4; by++)
            for (int bx = 0; bx < (width + 3) / 4; bx++)
                for (int channel = 0; channel < 2; channel++)
                {
                    var data = dds.AsSpan(header.DataOffset + (by * ((width + 3) / 4) + bx) * 16 + channel * 8, 8);
                    int a0 = data[0], a1 = data[1];
                    var palette = new int[8]; palette[0] = a0; palette[1] = a1;
                    if (a0 > a1) for (int k = 1; k <= 6; k++) palette[k + 1] = ((7 - k) * a0 + k * a1) / 7;
                    else { for (int k = 1; k <= 4; k++) palette[k + 1] = ((5 - k) * a0 + k * a1) / 5; palette[6] = 0; palette[7] = 255; }
                    ulong indices = 0; for (int i = 0; i < 6; i++) indices |= (ulong)data[2 + i] << 8 * i;
                    for (int i = 0; i < 16; i++)
                    {
                        int x = bx * 4 + i % 4, y = by * 4 + i / 4;
                        if (x < width && y < height) values[(y * width + x) * 2 + channel] = (byte)palette[(int)(indices >> 3 * i & 7)];
                    }
                }
        return values;
    }

    internal static void Run()
    {
        // Halving takes the mean of each four texels.
        var half = BlockTexture.Half(new(2, 2, [10, 0, 0, 255, 20, 0, 0, 255, 30, 0, 0, 255, 41, 0, 0, 255]));
        if (half.Width != 1 || half.Height != 1 || half.Rgba[0] != 25 || half.Rgba[3] != 255) throw new Exception("Halving is not the mean of four texels");
        // Two channels of a flat image come back exactly, with every smaller level: 4, 2 and 1 across, a block each.
        var flat = BlockTexture.EncodeTwo(new(4, 4, Enumerable.Repeat(new byte[] { 9, 77, 9, 9 }, 16).SelectMany(t => t).ToArray()));
        var flatHeader = new DDS(flat);
        if (flatHeader.GetDXGIFormat() != DDS.DXGI_FORMAT.BC5_UNORM || flatHeader.dwMipMapCount != 3 || flat.Length != flatHeader.DataOffset + 3 * 16) throw new Exception("A two-channel texture has the wrong format or levels");
        var flatBack = ReadTwo(flat, out _, out _);
        if (Enumerable.Range(0, 16).Any(i => flatBack[i * 2] != 9 || flatBack[i * 2 + 1] != 77)) throw new Exception("A flat two-channel image did not come back exactly");
        try { BlockTexture.EncodeTwo(new(4, 4, new byte[60])); throw new Exception("An image without four bytes a texel was accepted"); }
        catch (InvalidDataException) { }

        // The source's reflectance table: two half-precision numbers a texel, under a plain header that names the format by number.
        static byte[] Halves(int width, int height, int code, Func<int, int, (float, float)> value)
        {
            var file = new byte[128 + width * height * 4];
            "DDS "u8.CopyTo(file); BitConverter.TryWriteBytes(file.AsSpan(4), 124); BitConverter.TryWriteBytes(file.AsSpan(12), height); BitConverter.TryWriteBytes(file.AsSpan(16), width);
            BitConverter.TryWriteBytes(file.AsSpan(76), 32); BitConverter.TryWriteBytes(file.AsSpan(80), 4); BitConverter.TryWriteBytes(file.AsSpan(84), code);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var (first, second) = value(x, y);
                    BitConverter.TryWriteBytes(file.AsSpan(128 + (y * width + x) * 4), (Half)first); BitConverter.TryWriteBytes(file.AsSpan(130 + (y * width + x) * 4), (Half)second);
                }
            return file;
        }
        // A table as smooth as the source's: a block's eight steps then lie within three levels of every texel.
        (float, float) Slope(int x, int y) => (x / 31f, 1 - (x + y) / 62f);
        var table = HostCharacterTextures.Table(Halves(32, 32, 112, Slope));
        var tableBack = ReadTwo(table, out int tableWidth, out int tableHeight);
        if (new DDS(table).GetDXGIFormat() != DDS.DXGI_FORMAT.BC5_UNORM || tableWidth != 32 || tableHeight != 32) throw new Exception("The reflectance table is not two channels at the source's size");
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                var (first, second) = Slope(x, y);
                if (Math.Abs(tableBack[(y * 32 + x) * 2] - first * 255) > 3 || Math.Abs(tableBack[(y * 32 + x) * 2 + 1] - second * 255) > 3)
                    throw new Exception($"Reflectance table texel {x},{y} reads {tableBack[(y * 32 + x) * 2]},{tableBack[(y * 32 + x) * 2 + 1]}");
            }
        foreach (var bad in new Action[] { () => HostCharacterTextures.Table(Halves(32, 32, 113, Slope)), () => HostCharacterTextures.Table(Halves(32, 32, 112, Slope)[..^4]),
            () => HostCharacterTextures.Table(Halves(32, 32, 112, (x, y) => (2f, 0f))), () => HostCharacterTextures.Table(Halves(32, 32, 112, (x, y) => (float.NaN, 0f))) })
        {
            try { bad(); throw new Exception("An unusable reflectance table was accepted"); } catch (InvalidDataException) { }
        }
        // The dull specular map is one block of the colour format: both its colours full red (five bits, with no green or
        // blue), and every texel choosing the first, so it is fully rough, plain, unreflecting and open (the format's
        // covered texels read whole in the fourth channel).
        var dull = NeutralTextures.Bytes(NeutralTextures.Dull); var dullHeader = new DDS(dull);
        if (dullHeader.GetDXGIFormat() != DDS.DXGI_FORMAT.BC1_UNORM || dullHeader.dwWidth != 4 || dullHeader.dwHeight != 4
            || !dull.AsSpan(dullHeader.DataOffset).SequenceEqual(new byte[] { 0x00, 0xF8, 0x00, 0xF8, 0, 0, 0, 0 })) throw new Exception("The dull specular map is not one block of full red");
        Console.WriteLine("PASS: two channels encode with every smaller level; the source's reflectance table reads back close to it; the dull map is full red");
    }

    // One block of the unsigned HDR format from named fields: each a first bit, a width and a value.
    private static byte[] Block(params (int At, int Bits, int Value)[] fields)
    {
        var block = new byte[16];
        foreach (var (at, bits, value) in fields)
            for (int i = 0; i < bits; i++) if ((value >> i & 1) != 0) block[(at + i) / 8] |= (byte)(1 << (at + i) % 8);
        return block;
    }

    private static byte[] Cube(int size, int levels, byte[] payload)
    {
        var dds = new DDS { dwWidth = size, dwHeight = size, dwMipMapCount = levels, dwPitchOrLinearSize = payload.Length / 6, dwCaps2 = DDS.CUBEMAP_ALLFACES,
            ddspf = new DDS.PIXELFORMAT { dwFourCC = "DX10", dwFlags = DDS.DDPF.FOURCC },
            header10 = new DDS.HEADER_DXT10 { dxgiFormat = DDS.DXGI_FORMAT.BC6H_UF16, miscFlag = DDS.RESOURCE_MISC.TEXTURECUBE } };
        return dds.Write(payload);
    }

    /// <summary>The source's environment cubes are in the unsigned HDR block format; expected values are worked by hand from the format's published definition.</summary>
    internal static void Radiance()
    {
        // One region, both ends stored whole at ten bits (mode 3): ends (0, 1023, 512) and (1023, 0, 512).
        // A ten-bit end widens to sixteen bits as 0 -> 0, 1023 -> 65535, 512 -> ((512 << 16) + 32768) >> 10 = 32800, and a
        // blend of weight w of 64 is (a (64 - w) + b w + 32) >> 6, finished as (v 31) >> 6 and read as a half float.
        // Texel 0 takes the first end, texel 1 the second, texel 2 choice 8 of 16 (weight 34).
        var whole = Block((0, 5, 3), (5, 10, 0), (15, 10, 1023), (25, 10, 512), (35, 10, 1023), (45, 10, 0), (55, 10, 512), (65, 3, 0), (68, 4, 15), (72, 4, 8));
        // Two regions, the other three ends stored as signed differences from the first (mode 0, ten bits and five):
        // first end 512 in every channel; its partner +5 red, -3 green; the second region's ends -16 and +15 red.
        // Region choice 13 puts the top two rows in the first region and the bottom two in the second, whose anchor is texel 15.
        // Texel 1 takes the first region's second end (choice 7 of 8); texel 8 the second region's second end; texel 9 choice 3 (weight 27).
        var parted = Block((0, 2, 0), (5, 10, 512), (15, 10, 512), (25, 10, 512), (35, 5, 5), (45, 5, 29), (65, 5, 16), (71, 5, 15), (77, 5, 13), (84, 3, 7), (105, 3, 7), (108, 3, 3));
        // A reserved mode decodes to nothing.
        var reserved = Block((0, 5, 19), (5, 10, 1023));
        // Six faces of one block each, with a second, smaller level after each face's first: faces 0 and 5 are the blocks above.
        var blank = new byte[16];
        var cube = BlockTexture.DecodeRadiance(Cube(4, 2, [.. whole, .. blank, .. blank, .. blank, .. reserved, .. blank, .. blank, .. blank, .. blank, .. blank, .. parted, .. blank]));
        if (cube.Size != 4 || cube.Faces.Length != 6 || cube.Faces.Any(f => f.Length != 48)) throw new Exception("A decoded cube is not six faces of its size");
        void Expect(int face, int texel, float r, float g, float b)
        {
            var got = cube.Faces[face].AsSpan(texel * 3, 3);
            if (got[0] != r || got[1] != g || got[2] != b) throw new Exception($"Cube face {face} texel {texel} decoded to {got[0]}, {got[1]}, {got[2]}; expected {r}, {g}, {b}");
        }
        // 65535 finishes as 31743, the largest half float; 32800 as 15887 = 1.5146484375.
        Expect(0, 0, 0f, 65504f, 1.5146484375f);
        Expect(0, 1, 65504f, 0f, 1.5146484375f);
        // Weight 34: red (65535 x 34 + 32) >> 6 = 34815 -> 16863 = 2.935546875; green (65535 x 30 + 32) >> 6 = 30720 -> 14880 = 0.765625.
        Expect(0, 2, 2.935546875f, 0.765625f, 1.5146484375f);
        Expect(0, 3, 0f, 65504f, 1.5146484375f);
        for (int texel = 0; texel < 16; texel++) Expect(2, texel, 0f, 0f, 0f);
        // 517 widens to 33120 -> 16042 = 1.666015625; 509 to 32608 -> 15794 = 1.423828125.
        Expect(5, 0, 1.5146484375f, 1.5146484375f, 1.5146484375f);
        Expect(5, 1, 1.666015625f, 1.423828125f, 1.5146484375f);
        // 496 widens to 31776 -> 15391 = 1.0302734375; 527 to 33760 -> 16352 = 1.96875.
        Expect(5, 15, 1.0302734375f, 1.5146484375f, 1.5146484375f);
        Expect(5, 8, 1.96875f, 1.5146484375f, 1.5146484375f);
        // (31776 x 37 + 33760 x 27 + 32) >> 6 = 32613 -> 15796 = 1.42578125.
        Expect(5, 9, 1.42578125f, 1.5146484375f, 1.5146484375f);
        try { BlockTexture.DecodeRadiance(Dds(DDS.DXGI_FORMAT.BC6H_UF16, 4, 4, blank)); throw new Exception("A flat HDR texture was read as a cube"); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: an HDR cube decodes to the values its blocks' definitions give, one region or two");
    }

    /// <summary>A block of one colour and coverage, as a uniform volume is filled.</summary>
    internal static void Solid()
    {
        foreach (var (r, g, b, a) in new (byte, byte, byte, byte)[] { (200, 61, 255, 37), (0, 128, 127, 0), (255, 255, 255, 255), (1, 254, 3, 96) })
        {
            var texels = ReadOneLine(BlockTexture.Solid(r, g, b, a));
            for (int i = 0; i < 16; i++)
                // Colour is kept to seven bits; coverage whole.
                if (Math.Abs(texels[i * 4] - r) > 1 || Math.Abs(texels[i * 4 + 1] - g) > 1 || Math.Abs(texels[i * 4 + 2] - b) > 1 || texels[i * 4 + 3] != a)
                    throw new Exception($"A solid block of {r}, {g}, {b}, {a} reads back {texels[i * 4]}, {texels[i * 4 + 1]}, {texels[i * 4 + 2]}, {texels[i * 4 + 3]} at texel {i}");
        }
        Console.WriteLine("PASS: a solid block reads back its colour within a step and its coverage exactly");
    }
}
