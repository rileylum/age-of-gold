using SoulsFormats;

namespace AgeOfGold.Scene;

/// <summary>
/// Block-compressed textures a build must make or read rather than pass through: the source's reflectance table as two
/// channels, a block of one colour, and the light a source cube holds.
/// </summary>
internal static class BlockTexture
{
    /// <param name="Rgba">Four bytes a texel, rows from the top.</param>
    internal sealed record Image(int Width, int Height, byte[] Rgba);

    // A channel's eight values: its two ends, and six blends between them when the first is the greater.
    private static void Palette(int a0, int a1, Span<byte> palette)
    {
        palette[0] = (byte)a0; palette[1] = (byte)a1;
        if (a0 > a1) for (int k = 1; k <= 6; k++) palette[k + 1] = (byte)(((7 - k) * a0 + k * a1) / 7);
        else { for (int k = 1; k <= 4; k++) palette[k + 1] = (byte)(((5 - k) * a0 + k * a1) / 5); palette[6] = 0; palette[7] = 255; }
    }

    /// <summary>A texture half the size each way (never below one texel), each texel the mean of the four it covers.</summary>
    internal static Image Half(Image image)
    {
        int width = Math.Max(1, image.Width / 2), height = Math.Max(1, image.Height / 2);
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                for (int c = 0; c < 4; c++)
                {
                    int x1 = Math.Min(image.Width - 1, 2 * x + 1), y1 = Math.Min(image.Height - 1, 2 * y + 1), x0 = Math.Min(image.Width - 1, 2 * x), y0 = Math.Min(image.Height - 1, 2 * y);
                    int sum = image.Rgba[(y0 * image.Width + x0) * 4 + c] + image.Rgba[(y0 * image.Width + x1) * 4 + c] + image.Rgba[(y1 * image.Width + x0) * 4 + c] + image.Rgba[(y1 * image.Width + x1) * 4 + c];
                    rgba[(y * width + x) * 4 + c] = (byte)((sum + 2) / 4);
                }
        return new(width, height, rgba);
    }

    /// <summary>The red and green channels of an image as the two-channel block format, with every smaller level.</summary>
    internal static byte[] EncodeTwo(Image image) => Encode(image, DDS.DXGI_FORMAT.BC5_UNORM, 16, (texels, block) => { One(texels, block[..8], 0); One(texels, block[8..], 1); });

    // One channel of a block: its highest and lowest values and, for each texel, the nearest of eight steps between them.
    private static void One(ReadOnlySpan<byte> texels, Span<byte> block, int channel)
    {
        int low = 255, high = 0;
        for (int i = 0; i < 16; i++) { int v = texels[i * 4 + channel]; low = Math.Min(low, v); high = Math.Max(high, v); }
        Span<byte> palette = stackalloc byte[8];
        Palette(high, low, palette);
        block[0] = (byte)high; block[1] = (byte)low;
        ulong indices = 0;
        for (int i = 0; i < 16; i++)
        {
            int v = texels[i * 4 + channel], best = 0;
            // With both values equal every index reads the same; the first is used.
            if (high > low) for (int k = 1; k < 8; k++) if (Math.Abs(palette[k] - v) < Math.Abs(palette[best] - v)) best = k;
            indices |= (ulong)best << 3 * i;
        }
        for (int i = 0; i < 6; i++) block[2 + i] = (byte)(indices >> 8 * i);
    }

    private static void Put(Span<byte> into, ref int at, int value, int bits) { for (int b = 0; b < bits; b++, at++) if ((value >> b & 1) != 0) into[at >> 3] |= (byte)(1 << (at & 7)); }

    private delegate void BlockEncoder(ReadOnlySpan<byte> texels, Span<byte> block);

    private static byte[] Encode(Image image, DDS.DXGI_FORMAT format, int blockSize, BlockEncoder encode)
    {
        if (image.Width <= 0 || image.Height <= 0 || image.Rgba.Length != image.Width * image.Height * 4) throw new InvalidDataException("An image needs four bytes for each texel");
        using var payload = new MemoryStream();
        int levels = 0, largest = 0;
        Span<byte> texels = stackalloc byte[64]; Span<byte> block = stackalloc byte[blockSize];
        for (var level = image; ; level = Half(level))
        {
            for (int by = 0; by < (level.Height + 3) / 4; by++)
                for (int bx = 0; bx < (level.Width + 3) / 4; bx++)
                {
                    // A block at the edge of a level repeats the level's last texels.
                    for (int i = 0; i < 16; i++)
                    {
                        int x = Math.Min(level.Width - 1, bx * 4 + i % 4), y = Math.Min(level.Height - 1, by * 4 + i / 4);
                        level.Rgba.AsSpan((y * level.Width + x) * 4, 4).CopyTo(texels[(i * 4)..]);
                    }
                    encode(texels, block);
                    payload.Write(block);
                }
            if (levels++ == 0) largest = (int)payload.Length;
            if (level.Width == 1 && level.Height == 1) break;
        }
        var dds = new DDS
        {
            dwWidth = image.Width, dwHeight = image.Height, dwMipMapCount = levels, dwPitchOrLinearSize = largest,
            ddspf = new DDS.PIXELFORMAT { dwFourCC = "DX10", dwFlags = DDS.DDPF.FOURCC }, header10 = new DDS.HEADER_DXT10 { dxgiFormat = format },
        };
        dds.dwFlags |= DDS.DDSD.LINEARSIZE | DDS.DDSD.MIPMAPCOUNT;
        return dds.Write(payload.ToArray());
    }

    /// <summary>One block of the host's high-quality format holding a single colour, kept to seven bits, and coverage, kept whole.</summary>
    internal static byte[] Solid(byte red, byte green, byte blue, byte coverage)
    {
        var block = new byte[16];
        int at = 0;
        // The one-line layout with coverage apart: both ends alike, and every texel choosing the first.
        Put(block, ref at, 32, 6); Put(block, ref at, 0, 2);
        foreach (byte colour in new[] { red, green, blue }) { Put(block, ref at, colour >> 1, 7); Put(block, ref at, colour >> 1, 7); }
        Put(block, ref at, coverage, 8); Put(block, ref at, coverage, 8);
        return block;
    }

    /// <param name="Faces">Six faces in the stored order, each of Size × Size texels of three values, rows from the top.</param>
    internal sealed record Radiance(int Size, float[][] Faces);

    /// <summary>The largest level of each face of a cube in the unsigned HDR block format, as the source keeps its environment light.</summary>
    internal static Radiance DecodeRadiance(byte[] dds)
    {
        var header = new DDS(dds);
        int size = header.dwWidth, across = (size + 3) / 4;
        if (header.GetDXGIFormat() != DDS.DXGI_FORMAT.BC6H_UF16 || header.dwCaps2 != DDS.CUBEMAP_ALLFACES || size <= 0 || header.dwHeight != size)
            throw new InvalidDataException("Unsupported environment texture to decode: not an unsigned HDR cube");
        // A face's smaller levels follow its largest.
        int chain = 0;
        for (int level = 0; level < Math.Max(1, header.dwMipMapCount); level++) { int blocks = (Math.Max(1, size >> level) + 3) / 4; chain += blocks * blocks * 16; }
        if (dds.Length - header.DataOffset < 6 * chain) throw new InvalidDataException("Environment cube is shorter than its six faces");
        var faces = new float[6][];
        var texels = new float[48];
        for (int face = 0; face < 6; face++)
        {
            faces[face] = new float[size * size * 3];
            for (int by = 0; by < across; by++)
                for (int bx = 0; bx < across; bx++)
                {
                    Radiant(dds.AsSpan(header.DataOffset + face * chain + (by * across + bx) * 16, 16), texels);
                    for (int i = 0; i < 16; i++)
                    {
                        int x = bx * 4 + i % 4, y = by * 4 + i / 4;
                        if (x < size && y < size) texels.AsSpan(i * 3, 3).CopyTo(faces[face].AsSpan((y * size + x) * 3));
                    }
                }
        }
        return new(size, faces);
    }

    // Where each mode of the HDR format keeps its line ends, in stored order: a channel, an end (0 and 1 the first
    // region's, 2 and 3 the second's) and the bits it takes, written highest first as they fill; P chooses the regions.
    // With each mode, the width of the first end and of the other ends in red, green and blue.
    private static readonly Dictionary<int, (int[] Width, (char Channel, int End, int High, int Low)[] Fields)> RadiantModes = new (int Mode, int[] Width, string Layout)[]
    {
        (0, [10, 5, 5, 5], "G2.4 B2.4 B3.4 R0.9:0 G0.9:0 B0.9:0 R1.4:0 G3.4 G2.3:0 G1.4:0 B3.0 G3.3:0 B1.4:0 B3.1 B2.3:0 R2.4:0 B3.2 R3.4:0 B3.3 P0.4:0"),
        (1, [7, 6, 6, 6], "G2.5 G3.4 G3.5 R0.6:0 B3.0 B3.1 B2.4 G0.6:0 B2.5 B3.2 G2.4 B0.6:0 B3.3 B3.5 B3.4 R1.5:0 G2.3:0 G1.5:0 G3.3:0 B1.5:0 B2.3:0 R2.5:0 R3.5:0 P0.4:0"),
        (2, [11, 5, 4, 4], "R0.9:0 G0.9:0 B0.9:0 R1.4:0 R0.10 G2.3:0 G1.3:0 G0.10 B3.0 G3.3:0 B1.3:0 B0.10 B3.1 B2.3:0 R2.4:0 B3.2 R3.4:0 B3.3 P0.4:0"),
        (6, [11, 4, 5, 4], "R0.9:0 G0.9:0 B0.9:0 R1.3:0 R0.10 G3.4 G2.3:0 G1.4:0 G0.10 G3.3:0 B1.3:0 B0.10 B3.1 B2.3:0 R2.3:0 B3.0 B3.2 R3.3:0 G2.4 B3.3 P0.4:0"),
        (10, [11, 4, 4, 5], "R0.9:0 G0.9:0 B0.9:0 R1.3:0 R0.10 B2.4 G2.3:0 G1.3:0 G0.10 B3.0 G3.3:0 B1.4:0 B0.10 B2.3:0 R2.3:0 B3.1 B3.2 R3.3:0 B3.4 B3.3 P0.4:0"),
        (14, [9, 5, 5, 5], "R0.8:0 B2.4 G0.8:0 G2.4 B0.8:0 B3.4 R1.4:0 G3.4 G2.3:0 G1.4:0 B3.0 G3.3:0 B1.4:0 B3.1 B2.3:0 R2.4:0 B3.2 R3.4:0 B3.3 P0.4:0"),
        (18, [8, 6, 5, 5], "R0.7:0 G3.4 B2.4 G0.7:0 B3.2 G2.4 B0.7:0 B3.3 B3.4 R1.5:0 G2.3:0 G1.4:0 B3.0 G3.3:0 B1.4:0 B3.1 B2.3:0 R2.5:0 R3.5:0 P0.4:0"),
        (22, [8, 5, 6, 5], "R0.7:0 B3.0 B2.4 G0.7:0 G2.5 G2.4 B0.7:0 G3.5 B3.4 R1.4:0 G3.4 G2.3:0 G1.5:0 G3.3:0 B1.4:0 B3.1 B2.3:0 R2.4:0 B3.2 R3.4:0 B3.3 P0.4:0"),
        (26, [8, 5, 5, 6], "R0.7:0 B3.1 B2.4 G0.7:0 B2.5 G2.4 B0.7:0 B3.5 B3.4 R1.4:0 G3.4 G2.3:0 G1.4:0 B3.0 G3.3:0 B1.5:0 B2.3:0 R2.4:0 B3.2 R3.4:0 B3.3 P0.4:0"),
        (30, [6, 6, 6, 6], "R0.5:0 G3.4 B3.0 B3.1 B2.4 G0.5:0 G2.5 B2.5 B3.2 G2.4 B0.5:0 G3.5 B3.3 B3.5 B3.4 R1.5:0 G2.3:0 G1.5:0 G3.3:0 B1.5:0 B2.3:0 R2.5:0 R3.5:0 P0.4:0"),
        (3, [10, 10, 10, 10], "R0.9:0 G0.9:0 B0.9:0 R1.9:0 G1.9:0 B1.9:0"),
        (7, [11, 9, 9, 9], "R0.9:0 G0.9:0 B0.9:0 R1.8:0 R0.10 G1.8:0 G0.10 B1.8:0 B0.10"),
        (11, [12, 8, 8, 8], "R0.9:0 G0.9:0 B0.9:0 R1.7:0 R0.10:11 G1.7:0 G0.10:11 B1.7:0 B0.10:11"),
        (15, [16, 4, 4, 4], "R0.9:0 G0.9:0 B0.9:0 R1.3:0 R0.10:15 G1.3:0 G0.10:15 B1.3:0 B0.10:15"),
    }.ToDictionary(m => m.Mode, m => (m.Width, m.Layout.Split(' ').Select(field =>
    {
        var bits = field[3..].Split(':').Select(int.Parse).ToArray();
        return (field[0], field[1] - '0', bits[0], bits[^1]);
    }).ToArray()));

    // The 32 ways a block of two regions is divided, a texel's region by its place; and the second region's anchor texel.
    private static readonly string[] Regions =
    [
        "0011001100110011", "0001000100010001", "0111011101110111", "0001001100110111", "0000000100010011", "0011011101111111", "0001001101111111", "0000000100110111",
        "0000000000010011", "0011011111111111", "0000000101111111", "0000000000010111", "0001011111111111", "0000000011111111", "0000111111111111", "0000000000001111",
        "0000100011101111", "0111000100000000", "0000000010001110", "0111001100010000", "0011000100000000", "0000100011001110", "0000000010001100", "0111001100110001",
        "0011000100010000", "0000100010001100", "0110011001100110", "0011011001101100", "0001011111101000", "0000111111110000", "0111000110001110", "0011100110011100",
    ];
    private static readonly int[] Anchors = [15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 2, 8, 2, 2, 8, 8, 15, 2, 8, 2, 2, 8, 8, 2, 2];
    private static readonly int[] Blend8 = [0, 9, 18, 27, 37, 46, 55, 64], Blend16 = [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

    // One HDR block: one or two lines through colour space, each texel a blend along its region's line.
    private static void Radiant(ReadOnlySpan<byte> data, float[] texels)
    {
        UInt128 block = 0;
        for (int i = 15; i >= 0; i--) block = block << 8 | data[i];
        int at = 0;
        int Take(int count) { int value = (int)(uint)(block >> at & ((UInt128.One << count) - 1)); at += count; return value; }
        int mode = Take(2);
        if (mode >= 2) mode |= Take(3) << 2;
        // A mode outside the format's fourteen decodes to nothing.
        if (!RadiantModes.TryGetValue(mode, out var layout)) { Array.Clear(texels); return; }
        Span<int> ends = stackalloc int[12];
        int choice = 0;
        foreach (var (channel, end, high, low) in layout.Fields)
        {
            int count = Math.Abs(high - low) + 1, value = Take(count);
            // Bits written lowest first are stored in the reverse order.
            if (high < low) { int reversed = 0; for (int i = 0; i < count; i++) reversed |= (value >> i & 1) << count - 1 - i; value = reversed; }
            if (channel == 'P') choice = value; else ends["RGB".IndexOf(channel) * 4 + end] |= value << Math.Min(high, low);
        }
        bool two = layout.Fields.Any(f => f.Channel == 'P');
        // Two modes store every end whole; the others store the first whole and the rest as signed differences from it.
        if (mode is not (30 or 3))
            for (int c = 0; c < 3; c++)
                for (int end = 1; end < (two ? 4 : 2); end++)
                {
                    int width = layout.Width[1 + c], difference = ends[c * 4 + end];
                    if (difference >> width - 1 != 0) difference -= 1 << width;
                    ends[c * 4 + end] = ends[c * 4] + difference & (1 << layout.Width[0]) - 1;
                }
        int first = layout.Width[0];
        int Widen(int value) => first >= 15 ? value : value == 0 ? 0 : value == (1 << first) - 1 ? 0xFFFF : (value << 16) + 0x8000 >> first;
        var regions = two ? Regions[choice] : null;
        int anchor = two ? Anchors[choice] : -1, indexBits = two ? 3 : 4;
        var weights = two ? Blend8 : Blend16;
        for (int i = 0; i < 16; i++)
        {
            // Each region's anchor texel is stored without its highest bit, which is always clear.
            int weight = weights[Take(i == 0 || i == anchor ? indexBits - 1 : indexBits)], region = two ? regions![i] - '0' : 0;
            for (int c = 0; c < 3; c++)
            {
                int blended = Widen(ends[c * 4 + 2 * region]) * (64 - weight) + Widen(ends[c * 4 + 2 * region + 1]) * weight + 32 >> 6;
                texels[i * 3 + c] = (float)BitConverter.UInt16BitsToHalf((ushort)(blended * 31 >> 6));
            }
        }
    }
}
