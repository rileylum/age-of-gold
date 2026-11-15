using AgeOfGold.Characters;
using AgeOfGold.Scene;
using SoulsFormats;

/// <summary>Block-compressed textures decoded and encoded, against values worked out by hand from the formats' definitions.</summary>
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

    internal static void Run()
    {
        // Colour with one-bit coverage: red and blue at full, and the two blends a third of the way between them.
        // Texels 0 to 3 take the four choices in turn; the rest take the first.
        var colour = BlockTexture.Decode(Dds(DDS.DXGI_FORMAT.BC1_UNORM, 4, 4, [0x00, 0xF8, 0x1F, 0x00, 0b11100100, 0, 0, 0]));
        byte[][] expected = [[255, 0, 0, 255], [0, 0, 255, 255], [170, 0, 85, 255], [85, 0, 170, 255], [255, 0, 0, 255]];
        for (int i = 0; i < 5; i++) if (!colour.Rgba.AsSpan(i * 4, 4).SequenceEqual(expected[i])) throw new Exception($"Colour texel {i} decoded to {string.Join(",", colour.Rgba.AsSpan(i * 4, 4).ToArray())}");
        // With the first colour not the greater, the third choice is the mean and the fourth is nothing at all.
        var punched = BlockTexture.Decode(Dds(DDS.DXGI_FORMAT.BC1_UNORM, 4, 4, [0x1F, 0x00, 0x00, 0xF8, 0b11100100, 0, 0, 0]));
        if (!punched.Rgba.AsSpan(8, 8).SequenceEqual(new byte[] { 127, 0, 127, 255, 0, 0, 0, 0 })) throw new Exception("Colour without coverage decoded wrongly");
        // Two channels: each two values and six blends between them; here 200 and 100, with texel 1 choosing the second and texel 2 the first blend.
        byte[] channel = [200, 100, 0b10_001_000, 0, 0, 0, 0, 0];
        var two = BlockTexture.Decode(Dds(DDS.DXGI_FORMAT.BC5_UNORM, 4, 4, [.. channel, .. channel]));
        if (two.Rgba[0] != 200 || two.Rgba[4] != 100 || two.Rgba[8] != (6 * 200 + 100) / 7 || two.Rgba[9] != two.Rgba[8] || two.Rgba[2] != 0 || two.Rgba[3] != 255) throw new Exception("Two-channel texels decoded wrongly");
        // A texture narrower than a block keeps only the texels it has.
        if (BlockTexture.Decode(Dds(DDS.DXGI_FORMAT.BC1_UNORM, 2, 1, [0x00, 0xF8, 0x1F, 0x00, 0b0100, 0, 0, 0])).Rgba.Length != 8) throw new Exception("A small texture kept texels it does not have");
        Console.WriteLine("PASS: the source's colour and two-channel block textures decode to the values their formats define");

        // An image of smooth slopes in each channel, 8 by 8.
        var slopes = new BlockTexture.Image(8, 8, Enumerable.Range(0, 64).SelectMany(i => new byte[] { (byte)(30 + i % 8 * 20), (byte)(220 - i / 8 * 25), (byte)(100 + (i % 8 + i / 8) * 5), 255 }).ToArray());
        // One channel: levels 8, 4, 2 and 1 across, one block each but the first's four; each texel within a fourteenth of the block's spread.
        var single = BlockTexture.EncodeSingle(slopes, 1);
        var singleHeader = new DDS(single);
        if (singleHeader.GetDXGIFormat() != DDS.DXGI_FORMAT.BC4_UNORM || singleHeader.dwMipMapCount != 4 || single.Length != singleHeader.DataOffset + (4 + 1 + 1 + 1) * 8) throw new Exception("One-channel texture has the wrong format, levels or length");
        for (int by = 0; by < 2; by++)
            for (int bx = 0; bx < 2; bx++)
            {
                // Read back through the two-channel decoder: the same block twice.
                var data = single.AsSpan(singleHeader.DataOffset + (by * 2 + bx) * 8, 8).ToArray();
                var back = BlockTexture.Decode(Dds(DDS.DXGI_FORMAT.BC5_UNORM, 4, 4, [.. data, .. data]));
                for (int i = 0; i < 16; i++)
                {
                    int want = slopes.Rgba[((by * 4 + i / 4) * 8 + bx * 4 + i % 4) * 4 + 1];
                    if (Math.Abs(back.Rgba[i * 4] - want) > 6) throw new Exception($"One-channel texel is {back.Rgba[i * 4]}, wanted {want}");
                }
            }
        // A flat image comes back exactly, at every level.
        var flat = BlockTexture.EncodeSingle(new(4, 4, Enumerable.Repeat(new byte[] { 9, 77, 9, 9 }, 16).SelectMany(t => t).ToArray()), 1);
        var flatBlock = flat.AsSpan(new DDS(flat).DataOffset, 8).ToArray();
        if (BlockTexture.Decode(Dds(DDS.DXGI_FORMAT.BC5_UNORM, 4, 4, [.. flatBlock, .. flatBlock])).Rgba.Where((_, i) => i % 4 == 0).Any(v => v != 77)) throw new Exception("A flat one-channel image did not come back exactly");

        // Four channels: every block in the one-line mode; smooth slopes come back within a few steps, and a channel that falls as another rises is kept.
        var four = BlockTexture.EncodeFour(slopes);
        var fourHeader = new DDS(four);
        if (fourHeader.GetDXGIFormat() != DDS.DXGI_FORMAT.BC7_UNORM || fourHeader.dwMipMapCount != 4 || four.Length != fourHeader.DataOffset + 7 * 16) throw new Exception("Four-channel texture has the wrong format, levels or length");
        int worst = 0;
        for (int by = 0; by < 2; by++)
            for (int bx = 0; bx < 2; bx++)
            {
                var texels = ReadOneLine(four.AsSpan(fourHeader.DataOffset + (by * 2 + bx) * 16, 16));
                for (int i = 0; i < 16; i++)
                    for (int c = 0; c < 4; c++) worst = Math.Max(worst, Math.Abs(texels[i * 4 + c] - slopes.Rgba[((by * 4 + i / 4) * 8 + bx * 4 + i % 4) * 4 + c]));
            }
        // Three channels each sloping its own way is the hardest case for these modes: within a fifth of the widest slope.
        if (worst > 16) throw new Exception($"Four-channel texels are up to {worst} from the image");
        // One slope alone (as a relief map's gloss over a flat normal) is within three steps.
        var oneSlope = new BlockTexture.Image(4, 4, Enumerable.Range(0, 16).SelectMany(i => new byte[] { 128, 128, (byte)(40 + i % 4 * 50), 255 }).ToArray());
        var oneBack = ReadOneLine(BlockTexture.EncodeFour(oneSlope).AsSpan(new DDS(BlockTexture.EncodeFour(oneSlope)).DataOffset, 16));
        for (int i = 0; i < 64; i++) if (Math.Abs(oneBack[i] - oneSlope.Rgba[i]) > 3) throw new Exception($"A single slope came back {oneBack[i]} for {oneSlope.Rgba[i]}");
        // A flat colour comes back exactly when its channels share a low bit, and within one step otherwise.
        // A flat colour comes back within one step, and covered to within one step of full.
        var even = ReadOneLine(BlockTexture.EncodeFour(new(4, 4, Enumerable.Repeat(new byte[] { 128, 64, 200, 9 }, 16).SelectMany(t => t).ToArray())).AsSpan(148, 16));
        if (Math.Abs(even[0] - 128) > 1 || Math.Abs(even[1] - 64) > 1 || Math.Abs(even[2] - 200) > 1 || even[3] < 254) throw new Exception("A flat colour did not come back: " + string.Join(",", even.Take(4)));
        // Red and green on one slope with blue switching on its own: blue is exact and the slope within a few steps.
        var mixed = new BlockTexture.Image(4, 4, Enumerable.Range(0, 16).SelectMany(i => new byte[] { (byte)(100 + i % 4 * 33), (byte)(199 - i % 4 * 33), (byte)(i / 4 % 2 == 0 ? 0 : 255), 255 }).ToArray());
        var mixedBack = ReadOneLine(BlockTexture.EncodeFour(mixed).AsSpan(148, 16));
        for (int i = 0; i < 16; i++) if (mixedBack[i * 4 + 2] != mixed.Rgba[i * 4 + 2] || Math.Abs(mixedBack[i * 4] - mixed.Rgba[i * 4]) > 3 || Math.Abs(mixedBack[i * 4 + 1] - mixed.Rgba[i * 4 + 1]) > 3)
            throw new Exception($"Blue switching on its own came back {mixedBack[i * 4]},{mixedBack[i * 4 + 1]},{mixedBack[i * 4 + 2]} at {i}");
        // Halving takes the mean of each four texels.
        var half = BlockTexture.Half(new(2, 2, [10, 0, 0, 255, 20, 0, 0, 255, 30, 0, 0, 255, 41, 0, 0, 255]));
        if (half.Width != 1 || half.Height != 1 || half.Rgba[0] != 25 || half.Rgba[3] != 255) throw new Exception("Halving is not the mean of four texels");
        foreach (var bad in new Action[]
        {
            () => BlockTexture.Decode(Dds(DDS.DXGI_FORMAT.BC3_UNORM, 4, 4, new byte[16])),
            () => BlockTexture.Decode(Dds(DDS.DXGI_FORMAT.BC1_UNORM, 8, 8, new byte[8])),
            () => BlockTexture.EncodeSingle(new(4, 4, new byte[60]), 0),
        })
        {
            try { bad(); throw new Exception("An unusable texture was accepted"); }
            catch (InvalidDataException) { }
        }
        Console.WriteLine("PASS: one-channel and four-channel block textures encode with every smaller level and read back close to the image");

        // A character's relief map: gloss is the reverse of the source specular map's red, in the relief map's blue.
        // Specular: red 255 (rough) and green 0 in the first colour; red 0 and green 255 (metal) in the second; texel 1 takes the second.
        byte[] specular = Dds(DDS.DXGI_FORMAT.BC1_UNORM, 4, 4, [0x00, 0xF8, 0xE0, 0x07, 0b0100, 0, 0, 0]);
        byte[] relief = Dds(DDS.DXGI_FORMAT.BC5_UNORM, 4, 4, [.. channel, .. channel]);
        var glossy = HostCharacterTextures.ReliefAndGloss(relief, specular);
        var glossBack = ReadOneLine(glossy.AsSpan(new DDS(glossy).DataOffset, 16));
        // Texel 0: relief 200, rough, so no gloss. Texel 1: relief 100, smooth, so full gloss.
        if (Math.Abs(glossBack[0] - 200) > 4 || Math.Abs(glossBack[1] - 200) > 4 || glossBack[2] > 4 || Math.Abs(glossBack[4] - 100) > 4 || glossBack[6] < 251 || glossBack[3] < 251)
            throw new Exception("Relief and gloss differ: " + string.Join(",", glossBack.Take(8)));
        // Without a specular map a surface has no gloss; a specular map of another size is read where it covers the same place.
        var dull = ReadOneLine(HostCharacterTextures.ReliefAndGloss(relief, null).AsSpan(148, 16));
        if (dull[2] != 0 || dull[6] != 0) throw new Exception("A surface without a specular map was given gloss");
        byte[] wideRelief = Dds(DDS.DXGI_FORMAT.BC5_UNORM, 8, 4, [.. channel, .. channel, .. channel, .. channel]);
        var wide = HostCharacterTextures.ReliefAndGloss(wideRelief, specular);
        var wideBack = ReadOneLine(wide.AsSpan(new DDS(wide).DataOffset, 16));
        // The relief's texels 2 and 3 lie over the specular map's texel 1, the smooth one.
        if (wideBack[2] > 4 || wideBack[2 * 4 + 2] < 251 || wideBack[3 * 4 + 2] < 251 || new DDS(wide).dwWidth != 8) throw new Exception("A specular map of another size was not read where it covers the same place");
        // A relief map smaller than the specular map is spread to the specular map's size.
        byte[] wideSpecular = Dds(DDS.DXGI_FORMAT.BC1_UNORM, 8, 4, [0x00, 0xF8, 0xE0, 0x07, 0, 0, 0, 0, 0x00, 0xF8, 0xE0, 0x07, 0x55, 0x55, 0x55, 0x55]);
        var spread = HostCharacterTextures.ReliefAndGloss(relief, wideSpecular);
        var spreadRight = ReadOneLine(spread.AsSpan(new DDS(spread).DataOffset + 16, 16));
        if (new DDS(spread).dwWidth != 8 || spreadRight[2] < 251 || Math.Abs(spreadRight[0] - 185) > 12) throw new Exception("A smaller relief map was not spread to the specular map's size");
        Console.WriteLine("PASS: a character's gloss is the reverse of the source specular map's red, in the relief map's blue");
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

    /// <summary>The source's colour maps are often in the high-quality format, in any of its modes; expected values are worked by hand from the format's published definition.</summary>
    internal static void Colour()
    {
        byte[] Texel(BlockTexture.Image image, int texel) => image.Rgba.AsSpan(texel * 4, 4).ToArray();
        void Expect(string what, BlockTexture.Image image, int texel, params byte[] expected)
        {
            if (!Texel(image, texel).SequenceEqual(expected)) throw new Exception($"{what} texel {texel} decoded to {string.Join(",", Texel(image, texel))}; expected {string.Join(",", expected)}");
        }
        // Two regions, six-bit colour and one extra low bit a region (mode 1), divided top half and bottom half (choice 13,
        // whose second anchor is texel 15). First region: (0,63,32) to (63,0,32) with the low bit set; second: 10 to 50 without.
        // A six-bit value with its low bit widens as seven bits: 0|1 -> 2, 63|1 -> 255, 32|1 -> 131, 10|0 -> 40, 50|0 -> 201.
        var two = BlockTexture.Decode(Dds(DDS.DXGI_FORMAT.BC7_UNORM, 4, 4, Block((1, 1, 1), (2, 6, 13),
            (8, 6, 0), (14, 6, 63), (20, 6, 10), (26, 6, 50), (32, 6, 63), (38, 6, 0), (44, 6, 10), (50, 6, 50), (56, 6, 32), (62, 6, 32), (68, 6, 10), (74, 6, 50),
            (80, 1, 1), (81, 1, 0), (84, 3, 7), (87, 3, 3), (108, 3, 7), (111, 3, 4), (126, 2, 1))));
        Expect("Two regions", two, 0, 2, 255, 131, 255);
        Expect("Two regions", two, 1, 255, 2, 131, 255);
        // Choice 3 of 8 has weight 27: (37 x 2 + 27 x 255 + 32) >> 6 = 109 and (37 x 255 + 27 x 2 + 32) >> 6 = 148.
        Expect("Two regions", two, 2, 109, 148, 131, 255);
        Expect("Two regions", two, 8, 40, 40, 40, 255);
        Expect("Two regions", two, 9, 201, 201, 201, 255);
        // Weight 37: (27 x 40 + 37 x 201 + 32) >> 6 = 133. The anchor's choice 1 has weight 9: (55 x 40 + 9 x 201 + 32) >> 6 = 63.
        Expect("Two regions", two, 10, 133, 133, 133, 255);
        Expect("Two regions", two, 15, 63, 63, 63, 255);
        // One region with coverage apart (mode 4), the colour taking the three-bit choices and coverage the two-bit ones,
        // and red and coverage changing places afterwards. Five-bit colour 0, 31, 16 widens to 0, 255, 132; six-bit coverage 63 to 255.
        var apart = BlockTexture.Decode(Dds(DDS.DXGI_FORMAT.BC7_UNORM, 4, 4, Block((4, 1, 1), (5, 2, 1), (7, 1, 1),
            (8, 5, 0), (13, 5, 31), (18, 5, 31), (23, 5, 0), (28, 5, 16), (33, 5, 16), (38, 6, 0), (44, 6, 63), (51, 2, 1), (83, 3, 7))));
        Expect("Coverage apart", apart, 0, 0, 255, 132, 0);
        // Colour (255, 0, 132) at its last choice; coverage at choice 1 of 4, weight 21: (21 x 255 + 32) >> 6 = 84; then red and coverage swap.
        Expect("Coverage apart", apart, 1, 84, 0, 132, 255);
        // Three regions, four-bit colour and a low bit an end (mode 0), choice 0: rows "0011", "0011", "0221", "2222", with
        // anchors at texels 3 and 15. First region red 15 with its bit to 0 without (255 to 0), green and blue 0 with the
        // bit (8) to 0; second 5 without (82) to 10 with (173); third 8 without at both ends (132).
        var three = BlockTexture.Decode(Dds(DDS.DXGI_FORMAT.BC7_UNORM, 4, 4, Block((0, 1, 1), (1, 4, 0),
            (5, 4, 15), (9, 4, 0), (13, 4, 5), (17, 4, 10), (21, 4, 8), (25, 4, 8), (37, 4, 5), (41, 4, 10), (45, 4, 8), (49, 4, 8), (61, 4, 5), (65, 4, 10), (69, 4, 8), (73, 4, 8),
            (77, 1, 1), (80, 1, 1), (85, 3, 7), (88, 3, 7))));
        Expect("Three regions", three, 0, 255, 8, 8, 255);
        Expect("Three regions", three, 1, 0, 0, 0, 255);
        Expect("Three regions", three, 2, 173, 173, 173, 255);
        Expect("Three regions", three, 3, 82, 82, 82, 255);
        Expect("Three regions", three, 9, 132, 132, 132, 255);
        Expect("Three regions", three, 12, 132, 132, 132, 255);
        // What the build itself writes reads back through the same reader.
        var rgba = new byte[64]; for (int i = 0; i < 16; i++) { rgba[i * 4] = (byte)(i * 16); rgba[i * 4 + 1] = (byte)(255 - i * 16); rgba[i * 4 + 2] = 77; rgba[i * 4 + 3] = 255; }
        var back = BlockTexture.Decode(BlockTexture.EncodeFour(new BlockTexture.Image(4, 4, rgba)));
        for (int i = 0; i < 64; i++) if (Math.Abs(back.Rgba[i] - rgba[i]) > 6) throw new Exception($"A written block reads back {back.Rgba[i]} for {rgba[i]} at byte {i}");
        Console.WriteLine("PASS: the high-quality colour format decodes with one, two or three regions, low bits, coverage apart and channels changing places");
    }

    /// <summary>
    /// A surface's host colour and metal maps made texel by texel from the source's maps, so that the host's diffuse light
    /// and reflectance are the source's. The host lights colour × (1 - metal) and reflects 4 % blended towards the colour by metal.
    /// </summary>
    internal static void Surface()
    {
        // A whole block of one colour, in the source's colour format: five, six and five bits.
        static byte[] Flat(int r, int g, int b) { int c = r >> 3 << 11 | g >> 2 << 5 | b >> 3; return [(byte)c, (byte)(c >> 8), (byte)c, (byte)(c >> 8), 0, 0, 0, 0]; }
        (byte[] Colour, byte Metal) Made(byte[] colour, byte[]? specular, float metal = 1)
        {
            var made = HostCharacterTextures.Surface(Dds(DDS.DXGI_FORMAT.BC1_UNORM, 4, 4, colour), specular is null ? null : Dds(DDS.DXGI_FORMAT.BC1_UNORM, 4, 4, specular), [0.6f, 0.6f, 0.6f], [metal, metal, metal]);
            if (new DDS(made.Colour).GetDXGIFormat() != DDS.DXGI_FORMAT.BC7_UNORM || new DDS(made.Metal).GetDXGIFormat() != DDS.DXGI_FORMAT.BC4_UNORM) throw new Exception("A made surface is not a four-channel colour map and a one-channel metal map");
            var metalBlock = made.Metal.AsSpan(new DDS(made.Metal).DataOffset, 8).ToArray();
            return (BlockTexture.Decode(made.Colour).Rgba, BlockTexture.Decode(Dds(DDS.DXGI_FORMAT.BC5_UNORM, 4, 4, [.. metalBlock, .. metalBlock])).Rgba[0]);
        }
        void Expect(string what, (byte[] Colour, byte Metal) made, int grey, int metal, int coverage = 255)
        {
            if (Math.Abs(made.Colour[0] - grey) > 2 || Math.Abs(made.Colour[1] - grey) > 2 || Math.Abs(made.Colour[2] - grey) > 2 || Math.Abs(made.Colour[3] - coverage) > 2 || Math.Abs(made.Metal - metal) > 2)
                throw new Exception($"{what}: colour {made.Colour[0]},{made.Colour[1]},{made.Colour[2]},{made.Colour[3]} metal {made.Metal}; expected {grey} and {metal}");
        }
        byte[] white = Flat(255, 255, 255);
        // Not metal, reflecting nothing: the source lights (1 × 0.6)^2.2 = 0.325. The host cannot reflect less than its 4 %, so
        // the surface is plain, its colour 0.325, which the host's display curve stores as 154.
        Expect("A plain surface", Made(white, Flat(255, 0, 0)), 154, 0);
        // Dark, not metal, reflecting nothing: (66/255 × 0.6)^2.2 = 0.0166, stored as 35. It stays plain: its colour is
        // not given up to bring the host's reflection down.
        Expect("A dark plain surface", Made(Flat(64, 64, 64), Flat(255, 0, 0)), 35, 0);
        // No specular map: the same.
        Expect("A surface with no specular map", Made(white, null), 154, 0);
        // Metal, with a metal colour of a half: the source reflects (1 × 0.5)^2.2 = 0.2176 and lights nothing: all metal,
        // coloured 0.2176, stored as 128.
        Expect("A metal surface", Made(white, Flat(0, 255, 0), 0.5f), 128, 255);
        // Black, not metal, at the full specular level: the source reflects 0.2 and lights nothing. The host reflects that
        // much only from a metal coloured 0.2, stored as 124.
        Expect("A black surface that reflects", Made(Flat(0, 0, 0), Flat(255, 0, 255)), 124, 255);
        // Lit 0.325 and reflecting 0.2: with x = 1 - metal, 0.04 x^2 - 0.525 x + 0.325 = 0 gives x = 0.6513, metal 0.3487 (89),
        // and the colour 0.325 + 0.2 - 0.04 x = 0.499, stored as 187. Then colour × x = 0.325 and 0.04 x + colour × metal = 0.2.
        Expect("A lit surface that reflects", Made(white, Flat(255, 0, 255)), 187, 89);
        // The maps can carry several times the source's reflectance, for a host that then reflects that many times more
        // weakly: at four times, the black surface reflects 0.8 (stored 231) and the metal one 0.870 (stored 240).
        byte Colour(byte[] colour, byte[] specular, float metal) => BlockTexture.Decode(HostCharacterTextures.Surface(Dds(DDS.DXGI_FORMAT.BC1_UNORM, 4, 4, colour), Dds(DDS.DXGI_FORMAT.BC1_UNORM, 4, 4, specular), [0.6f, 0.6f, 0.6f], [metal, metal, metal], 4).Colour).Rgba[0];
        if (Math.Abs(Colour(Flat(0, 0, 0), Flat(255, 0, 255), 1) - 231) > 2 || Math.Abs(Colour(white, Flat(0, 255, 0), 0.5f) - 240) > 2)
            throw new Exception($"Reflectance carried four times over is stored as {Colour(Flat(0, 0, 0), Flat(255, 0, 255), 1)} and {Colour(white, Flat(0, 255, 0), 0.5f)}, not 231 and 240");
        // A plain surface is not changed by it.
        if (Math.Abs(Colour(white, Flat(255, 0, 0), 1) - 154) > 2) throw new Exception("Carrying more reflectance changed a surface that reflects nothing");
        // Where the source's colour map leaves a hole, the made one does.
        var holed = HostCharacterTextures.Surface(Dds(DDS.DXGI_FORMAT.BC1_UNORM, 4, 4, [0x00, 0x00, 0xFF, 0xFF, 0b11_01_01_01, 0x55, 0x55, 0x55]), null, [0.6f, 0.6f, 0.6f], [1f, 1f, 1f]);
        var holes = BlockTexture.Decode(holed.Colour).Rgba;
        if (holes[3] < 250 || holes[3 * 4 + 3] > 5) throw new Exception($"A hole in the colour map is not kept: coverage {holes[3]} and {holes[3 * 4 + 3]}");
        foreach (var bad in new Action[] { () => HostCharacterTextures.Surface(Dds(DDS.DXGI_FORMAT.BC1_UNORM, 4, 4, white), null, [0.6f, 0.6f], [1f, 1f, 1f]), () => HostCharacterTextures.Surface(Dds(DDS.DXGI_FORMAT.BC1_UNORM, 4, 4, white), null, [0.6f, 0.6f, 0.6f], [1f, -1f, 1f]) })
        {
            try { bad(); throw new Exception("An unusable surface colour was accepted"); } catch (InvalidDataException) { }
        }
        Console.WriteLine("PASS: a surface's colour and metal maps are made so the host lights and reflects what the source does, texel by texel");
    }
}
