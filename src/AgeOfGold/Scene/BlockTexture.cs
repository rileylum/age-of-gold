using SoulsFormats;

namespace AgeOfGold.Scene;

/// <summary>
/// Decodes and encodes block-compressed textures, for the textures a build must make rather than pass through: the
/// source keeps a character's metal and roughness in one map and the host wants them in two others.
/// </summary>
internal static class BlockTexture
{
    /// <param name="Rgba">Four bytes a texel, rows from the top.</param>
    internal sealed record Image(int Width, int Height, byte[] Rgba);

    /// <summary>
    /// The largest level of a texture in one of the source's block formats: colour with one-bit coverage, two channels, or
    /// the high-quality four-channel format.
    /// </summary>
    internal static Image Decode(byte[] dds)
    {
        var header = new DDS(dds);
        var format = header.GetDXGIFormat();
        int width = header.dwWidth, height = header.dwHeight;
        bool two = format == DDS.DXGI_FORMAT.BC5_UNORM, four = format is DDS.DXGI_FORMAT.BC7_UNORM or DDS.DXGI_FORMAT.BC7_UNORM_SRGB;
        if (!two && !four && format is not (DDS.DXGI_FORMAT.BC1_UNORM or DDS.DXGI_FORMAT.BC1_UNORM_SRGB)) throw new InvalidDataException("Unsupported texture format to decode: " + format);
        if (width <= 0 || height <= 0 || header.dwDepth > 1 || header.dwCaps2 != 0) throw new InvalidDataException("Unsupported texture dimensions to decode");
        int across = (width + 3) / 4, down = (height + 3) / 4, block = two || four ? 16 : 8;
        if (dds.Length - header.DataOffset < across * down * block) throw new InvalidDataException("Texture is shorter than its largest level");
        var rgba = new byte[width * height * 4];
        var texels = new byte[16 * 4];
        for (int by = 0; by < down; by++)
            for (int bx = 0; bx < across; bx++)
            {
                var data = dds.AsSpan(header.DataOffset + (by * across + bx) * block, block);
                if (four) Four(data, texels);
                else if (two) { Single(data[..8], texels, 0); Single(data[8..], texels, 1); for (int i = 0; i < 16; i++) { texels[i * 4 + 2] = 0; texels[i * 4 + 3] = 255; } }
                else Colour(data, texels);
                for (int i = 0; i < 16; i++)
                {
                    int x = bx * 4 + i % 4, y = by * 4 + i / 4;
                    if (x < width && y < height) texels.AsSpan(i * 4, 4).CopyTo(rgba.AsSpan((y * width + x) * 4));
                }
            }
        return new(width, height, rgba);
    }

    // Two colours of five, six and five bits, and two bits a texel choosing between them and two blends.
    private static void Colour(ReadOnlySpan<byte> data, byte[] texels)
    {
        int c0 = data[0] | data[1] << 8, c1 = data[2] | data[3] << 8;
        Span<byte> palette = stackalloc byte[16];
        static void Expand(int c, Span<byte> to) { int r = c >> 11, g = c >> 5 & 63, b = c & 31; to[0] = (byte)(r << 3 | r >> 2); to[1] = (byte)(g << 2 | g >> 4); to[2] = (byte)(b << 3 | b >> 2); to[3] = 255; }
        Expand(c0, palette); Expand(c1, palette[4..]);
        for (int k = 0; k < 3; k++)
        {
            int a = palette[k], b = palette[4 + k];
            palette[8 + k] = (byte)(c0 > c1 ? (2 * a + b) / 3 : (a + b) / 2);
            palette[12 + k] = (byte)(c0 > c1 ? (a + 2 * b) / 3 : 0);
        }
        palette[11] = 255; palette[15] = (byte)(c0 > c1 ? 255 : 0);
        uint indices = (uint)(data[4] | data[5] << 8 | data[6] << 16 | data[7] << 24);
        for (int i = 0; i < 16; i++) palette.Slice((int)(indices >> 2 * i & 3) * 4, 4).CopyTo(texels.AsSpan(i * 4));
    }

    // The eight modes of the high-quality format: regions, bits choosing how they divide the block, bits saying which
    // channel changes places with coverage, whether a bit swaps the two sets of choices, colour bits, coverage bits, a
    // low bit an end, a low bit a region, and the bits of a choice in the first and second sets.
    private static readonly (int Regions, int Division, int Rotation, int Swap, int Colour, int Coverage, int EndBit, int RegionBit, int Index, int Second)[] FourModes =
    [
        (3, 4, 0, 0, 4, 0, 1, 0, 3, 0), (2, 6, 0, 0, 6, 0, 0, 1, 3, 0), (3, 6, 0, 0, 5, 0, 0, 0, 2, 0), (2, 6, 0, 0, 7, 0, 1, 0, 2, 0),
        (1, 0, 2, 1, 5, 6, 0, 0, 2, 3), (1, 0, 2, 0, 7, 8, 0, 0, 2, 2), (1, 0, 0, 0, 7, 7, 1, 0, 4, 0), (2, 6, 0, 0, 5, 5, 1, 0, 2, 0),
    ];
    // The 64 ways a block divides into two regions (the first 32 are the HDR format's) and into three, a texel's region
    // by its place; and the anchor texel of the second region of two, and of the second and third of three.
    private static readonly string[] TwoRegions =
    [
        "0011001100110011", "0001000100010001", "0111011101110111", "0001001100110111", "0000000100010011", "0011011101111111", "0001001101111111", "0000000100110111",
        "0000000000010011", "0011011111111111", "0000000101111111", "0000000000010111", "0001011111111111", "0000000011111111", "0000111111111111", "0000000000001111",
        "0000100011101111", "0111000100000000", "0000000010001110", "0111001100010000", "0011000100000000", "0000100011001110", "0000000010001100", "0111001100110001",
        "0011000100010000", "0000100010001100", "0110011001100110", "0011011001101100", "0001011111101000", "0000111111110000", "0111000110001110", "0011100110011100",
        "0101010101010101", "0000111100001111", "0101101001011010", "0011001111001100", "0011110000111100", "0101010110101010", "0110100101101001", "0101101010100101",
        "0111001111001110", "0001001111001000", "0011001001001100", "0011101111011100", "0110100110010110", "0011110011000011", "0110011010011001", "0000011001100000",
        "0100111001000000", "0010011100100000", "0000001001110010", "0000010011100100", "0110110010010011", "0011011011001001", "0110001110011100", "0011100111000110",
        "0110110011001001", "0110001100111001", "0111111010000001", "0001100011100111", "0000111100110011", "0011001111110000", "0010001011101110", "0100010001110111",
    ];
    private static readonly string[] ThreeRegions =
    [
        "0011001102212222", "0001001122112221", "0000200122112211", "0222002200110111", "0000000011221122", "0011001100220022", "0022002211111111", "0011001122112211",
        "0000000011112222", "0000111111112222", "0000111122222222", "0012001200120012", "0112011201120112", "0122012201220122", "0011011211221222", "0011200122002220",
        "0001001101121122", "0111001120012200", "0000112211221122", "0022002200221111", "0111011102220222", "0001000122212221", "0000001101220122", "0000110022102210",
        "0122012200110000", "0012001211222222", "0110122112210110", "0000011012211221", "0022110211020022", "0110011020022222", "0011012201220011", "0000200022112221",
        "0000000211221222", "0222002200120011", "0011001200220222", "0120012001200120", "0000111122220000", "0120120120120120", "0120201212010120", "0011220011220011",
        "0011112222000011", "0101010122222222", "0000000021212121", "0022112200221122", "0022001100220011", "0220122102201221", "0101222222220101", "0000212121212121",
        "0101010101012222", "0222011102220111", "0002111200021112", "0000211221122112", "0222011101110222", "0002111211120002", "0110011001102222", "0000000021122112",
        "0110011022222222", "0022001100110022", "0022112211220022", "0000000000002112", "0002000100020001", "0222122202221222", "0101222222222222", "0111201122012220",
    ];
    private static readonly int[] TwoAnchors = [15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 2, 8, 2, 2, 8, 8, 15, 2, 8, 2, 2, 8, 8, 2, 2, 15, 15, 6, 8, 2, 8, 15, 15, 2, 8, 2, 2, 2, 15, 15, 6, 6, 2, 6, 8, 15, 15, 2, 2, 15, 15, 15, 15, 15, 2, 2, 15];
    private static readonly int[] ThreeSecondAnchors = [3, 3, 15, 15, 8, 3, 15, 15, 8, 8, 6, 6, 6, 5, 3, 3, 3, 3, 8, 15, 3, 3, 6, 10, 5, 8, 8, 6, 8, 5, 15, 15, 8, 15, 3, 5, 6, 10, 8, 15, 15, 3, 15, 5, 15, 15, 15, 15, 3, 15, 5, 5, 5, 8, 5, 10, 5, 10, 8, 13, 15, 12, 3, 3];
    private static readonly int[] ThreeThirdAnchors = [15, 8, 8, 3, 15, 15, 3, 8, 15, 15, 15, 15, 15, 15, 15, 8, 15, 8, 15, 3, 15, 8, 15, 8, 3, 15, 6, 10, 15, 15, 10, 8, 15, 3, 15, 10, 10, 8, 9, 10, 6, 15, 8, 15, 3, 6, 6, 8, 15, 3, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 3, 15, 15, 8];
    private static readonly int[] Blend4 = [0, 21, 43, 64];

    // One block of the high-quality format, in any mode: up to three lines through colour space, each texel a blend along
    // its region's line, with coverage on the same line or on one of its own.
    private static void Four(ReadOnlySpan<byte> data, byte[] texels)
    {
        UInt128 block = 0;
        for (int i = 15; i >= 0; i--) block = block << 8 | data[i];
        int mode = 0;
        while (mode < 8 && (data[0] >> mode & 1) == 0) mode++;
        // A block naming no mode decodes to nothing.
        if (mode == 8) { Array.Clear(texels); return; }
        int at = mode + 1;
        int Take(int count) { int value = (int)(uint)(block >> at & ((UInt128.One << count) - 1)); at += count; return value; }
        var m = FourModes[mode];
        int division = Take(m.Division), rotation = Take(m.Rotation), swap = Take(m.Swap);
        Span<int> ends = stackalloc int[24];
        for (int c = 0; c < 3; c++) for (int e = 0; e < 2 * m.Regions; e++) ends[e * 4 + c] = Take(m.Colour);
        for (int e = 0; e < 2 * m.Regions; e++) ends[e * 4 + 3] = m.Coverage == 0 ? 0 : Take(m.Coverage);
        int colourBits = m.Colour, coverageBits = m.Coverage;
        if (m.EndBit != 0)
        {
            for (int e = 0; e < 2 * m.Regions; e++) { int low = Take(1); for (int c = 0; c < (m.Coverage == 0 ? 3 : 4); c++) ends[e * 4 + c] = ends[e * 4 + c] << 1 | low; }
            colourBits++; if (m.Coverage != 0) coverageBits++;
        }
        if (m.RegionBit != 0)
        {
            for (int region = 0; region < m.Regions; region++) { int low = Take(1); for (int e = 2 * region; e < 2 * region + 2; e++) for (int c = 0; c < 3; c++) ends[e * 4 + c] = ends[e * 4 + c] << 1 | low; }
            colourBits++;
        }
        // An end widens to eight bits by repeating its high bits below itself.
        for (int e = 0; e < 2 * m.Regions; e++)
        {
            for (int c = 0; c < 3; c++) ends[e * 4 + c] = ends[e * 4 + c] << 8 - colourBits | ends[e * 4 + c] >> 2 * colourBits - 8;
            ends[e * 4 + 3] = m.Coverage == 0 ? 255 : ends[e * 4 + 3] << 8 - coverageBits | ends[e * 4 + 3] >> 2 * coverageBits - 8;
        }
        string? regions = m.Regions == 2 ? TwoRegions[division] : m.Regions == 3 ? ThreeRegions[division] : null;
        int second = m.Regions == 2 ? TwoAnchors[division] : m.Regions == 3 ? ThreeSecondAnchors[division] : -1, third = m.Regions == 3 ? ThreeThirdAnchors[division] : -1;
        Span<int> first = stackalloc int[16], other = stackalloc int[16];
        // Each region's anchor texel is stored without its highest bit, which is always clear.
        for (int i = 0; i < 16; i++) first[i] = Take(i == 0 || i == second || i == third ? m.Index - 1 : m.Index);
        if (m.Second != 0) for (int i = 0; i < 16; i++) other[i] = Take(i == 0 ? m.Second - 1 : m.Second);
        static int[] Weights(int bits) => bits == 2 ? Blend4 : bits == 3 ? Blend8 : Blend16;
        for (int i = 0; i < 16; i++)
        {
            int region = regions is null ? 0 : regions[i] - '0';
            // With two sets of choices, colour takes one and coverage the other.
            var (colour, colourSet, coverage, coverageSet) = m.Second == 0 ? (first[i], m.Index, first[i], m.Index) : swap == 0 ? (first[i], m.Index, other[i], m.Second) : (other[i], m.Second, first[i], m.Index);
            for (int c = 0; c < 4; c++)
            {
                int weight = Weights(c < 3 ? colourSet : coverageSet)[c < 3 ? colour : coverage];
                texels[i * 4 + c] = (byte)((64 - weight) * ends[2 * region * 4 + c] + weight * ends[(2 * region + 1) * 4 + c] + 32 >> 6);
            }
            if (rotation != 0) (texels[i * 4 + rotation - 1], texels[i * 4 + 3]) = (texels[i * 4 + 3], texels[i * 4 + rotation - 1]);
        }
    }

    private static void Palette(int a0, int a1, Span<byte> palette)
    {
        palette[0] = (byte)a0; palette[1] = (byte)a1;
        if (a0 > a1) for (int k = 1; k <= 6; k++) palette[k + 1] = (byte)(((7 - k) * a0 + k * a1) / 7);
        else { for (int k = 1; k <= 4; k++) palette[k + 1] = (byte)(((5 - k) * a0 + k * a1) / 5); palette[6] = 0; palette[7] = 255; }
    }

    // One channel: two values, and three bits a texel choosing between them and their blends.
    private static void Single(ReadOnlySpan<byte> data, byte[] texels, int channel)
    {
        Span<byte> palette = stackalloc byte[8];
        Palette(data[0], data[1], palette);
        ulong indices = 0; for (int i = 0; i < 6; i++) indices |= (ulong)data[2 + i] << 8 * i;
        for (int i = 0; i < 16; i++) texels[i * 4 + channel] = palette[(int)(indices >> 3 * i & 7)];
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

    /// <summary>One channel of an image as the host's one-channel block format, with every smaller level.</summary>
    internal static byte[] EncodeSingle(Image image, int channel) => Encode(image, DDS.DXGI_FORMAT.BC4_UNORM, 8, (texels, block) => One(texels, block, channel));

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

    private static readonly int[] Weights = [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

    private static readonly int[] TwoBitWeights = [0, 21, 43, 64];

    /// <summary>
    /// The red, green and blue channels of an image as the host's high-quality block format, fully covered, with every
    /// smaller level. Each block is written in whichever of two of the format's modes loses less: one line through all the
    /// channels, or a line through two of them with the third placed on its own (relief's two channels vary apart).
    /// lazy: no blocks split into regions, so a block where all three vary apart is coarse; add the format's two-region
    /// mode if relief looks blocky.
    /// </summary>
    /// <param name="coverage">Keep the image's coverage; a block that is not fully covered is written on one line through all four channels.</param>
    internal static byte[] EncodeFour(Image image, bool coverage = false) => Encode(image, DDS.DXGI_FORMAT.BC7_UNORM, 16, (texels, block) =>
    {
        if (coverage) { bool covered = true; for (int i = 0; i < 16; i++) covered &= texels[i * 4 + 3] == 255; if (!covered) { OneLine(texels, block, true); return; } }
        Span<byte> other = stackalloc byte[16];
        long least = OneLine(texels, block);
        for (int alone = 0; alone < 3; alone++)
        {
            long error = Apart(texels, other, alone);
            if (error < least) { least = error; other.CopyTo(block); }
        }
    });

    private static void Put(Span<byte> into, ref int at, int value, int bits) { for (int b = 0; b < bits; b++, at++) if ((value >> b & 1) != 0) into[at >> 3] |= (byte)(1 << (at & 7)); }

    // Two end colours of seven bits a channel and one shared low bit each, and four bits a texel placing it between them.
    private static long OneLine(ReadOnlySpan<byte> texels, Span<byte> block, bool coverage = false)
    {
        Span<int> low = stackalloc int[4], high = stackalloc int[4];
        for (int c = 0; c < 4; c++) { low[c] = 255; high[c] = 0; }
        for (int i = 0; i < 16; i++) for (int c = 0; c < 4; c++) { int v = c == 3 && !coverage ? 255 : texels[i * 4 + c]; low[c] = Math.Min(low[c], v); high[c] = Math.Max(high[c], v); }
        // The line runs between opposite corners of the block's box of colours: for each channel, whichever way it rises with
        // the channel that varies most.
        int widest = 0; for (int c = 1; c < (coverage ? 4 : 3); c++) if (high[c] - low[c] > high[widest] - low[widest]) widest = c;
        Span<int> from = stackalloc int[4], to = stackalloc int[4];
        for (int c = 0; c < 4; c++)
        {
            long rises = 0;
            if (c < 3 || coverage) for (int i = 0; i < 16; i++) rises += (texels[i * 4 + c] * 2 - low[c] - high[c]) * (long)(texels[i * 4 + widest] * 2 - low[widest] - high[widest]);
            (from[c], to[c]) = rises >= 0 ? (low[c], high[c]) : (high[c], low[c]);
        }
        // Each end keeps seven bits a channel and one low bit shared by its channels: the low bit that loses least.
        static int Shared(Span<int> end)
        {
            int Loss(Span<int> e, int p) { int loss = 0; foreach (int v in e) loss += Math.Abs((Math.Clamp((v - p + 1) / 2, 0, 127) << 1 | p) - v); return loss; }
            int shared = Loss(end, 1) < Loss(end, 0) ? 1 : 0;
            for (int c = 0; c < 4; c++) end[c] = Math.Clamp((end[c] - shared + 1) / 2, 0, 127) << 1 | shared;
            return shared;
        }
        int p0 = Shared(from), p1 = Shared(to);
        Span<int> indices = stackalloc int[16]; long total = 0;
        for (int i = 0; i < 16; i++)
        {
            int best = 0; long bestError = long.MaxValue;
            for (int k = 0; k < 16; k++)
            {
                long error = 0;
                for (int c = 0; c < 4; c++) { int d = (((64 - Weights[k]) * from[c] + Weights[k] * to[c] + 32) >> 6) - (c == 3 && !coverage ? 255 : texels[i * 4 + c]); error += d * d; }
                if (error < bestError) { bestError = error; best = k; }
            }
            indices[i] = best; total += bestError;
        }
        // The first texel's index is stored a bit short, so it must lie in the line's first half: otherwise the ends change places.
        if (indices[0] >= 8)
        {
            for (int c = 0; c < 4; c++) (from[c], to[c]) = (to[c], from[c]);
            (p0, p1) = (p1, p0);
            for (int i = 0; i < 16; i++) indices[i] = 15 - indices[i];
        }
        block.Clear();
        int at = 0;
        Put(block, ref at, 1 << 6, 7);
        for (int c = 0; c < 4; c++) { Put(block, ref at, from[c] >> 1, 7); Put(block, ref at, to[c] >> 1, 7); }
        Put(block, ref at, p0, 1); Put(block, ref at, p1, 1);
        Put(block, ref at, indices[0], 3);
        for (int i = 1; i < 16; i++) Put(block, ref at, indices[i], 4);
        return total;
    }

    // One channel placed on its own between two values of eight bits, and a line through the other two of seven bits a
    // channel; two bits a texel for each. The format keeps the separate channel as coverage and is told which colour
    // channel it changes places with.
    private static long Apart(ReadOnlySpan<byte> texels, Span<byte> block, int alone)
    {
        int first = alone == 0 ? 1 : 0, second = alone == 2 ? 1 : 2;
        Span<int> low = stackalloc int[3], high = stackalloc int[3];
        for (int c = 0; c < 3; c++) { low[c] = 255; high[c] = 0; }
        for (int i = 0; i < 16; i++) for (int c = 0; c < 3; c++) { low[c] = Math.Min(low[c], texels[i * 4 + c]); high[c] = Math.Max(high[c], texels[i * 4 + c]); }
        long rises = 0;
        for (int i = 0; i < 16; i++) rises += (texels[i * 4 + second] * 2 - low[second] - high[second]) * (long)(texels[i * 4 + first] * 2 - low[first] - high[first]);
        // Seven bits widen to eight by repeating the top bit below.
        static int Seven(int v) => (v * 127 + 127) / 255;
        static int Widen(int q) => q << 1 | q >> 6;
        // The line's ends, as the three stored colour channels: the separate channel's place holds full coverage.
        Span<int> from = stackalloc int[3], to = stackalloc int[3];
        from[alone] = to[alone] = 127;
        (from[first], to[first]) = (Seven(low[first]), Seven(high[first]));
        (from[second], to[second]) = rises >= 0 ? (Seven(low[second]), Seven(high[second])) : (Seven(high[second]), Seven(low[second]));
        int aloneFrom = low[alone], aloneTo = high[alone];
        Span<int> line = stackalloc int[16], single = stackalloc int[16]; long total = 0;
        for (int i = 0; i < 16; i++)
        {
            long bestLine = long.MaxValue, bestSingle = long.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                long error = 0;
                foreach (int c in (ReadOnlySpan<int>)[first, second]) { int d = (((64 - TwoBitWeights[k]) * Widen(from[c]) + TwoBitWeights[k] * Widen(to[c]) + 32) >> 6) - texels[i * 4 + c]; error += d * d; }
                if (error < bestLine) { bestLine = error; line[i] = k; }
                long apart = (((64 - TwoBitWeights[k]) * aloneFrom + TwoBitWeights[k] * aloneTo + 32) >> 6) - texels[i * 4 + alone];
                if (apart * apart < bestSingle) { bestSingle = apart * apart; single[i] = k; }
            }
            total += bestLine + bestSingle;
        }
        // Each set of indices stores its first a bit short: it must lie in the first half, or its ends change places.
        if (line[0] >= 2) { for (int c = 0; c < 3; c++) (from[c], to[c]) = (to[c], from[c]); for (int i = 0; i < 16; i++) line[i] = 3 - line[i]; }
        if (single[0] >= 2) { (aloneFrom, aloneTo) = (aloneTo, aloneFrom); for (int i = 0; i < 16; i++) single[i] = 3 - single[i]; }
        block.Clear();
        int at = 0;
        Put(block, ref at, 1 << 5, 6);
        Put(block, ref at, alone + 1, 2);
        for (int c = 0; c < 3; c++) { Put(block, ref at, from[c], 7); Put(block, ref at, to[c], 7); }
        Put(block, ref at, aloneFrom, 8); Put(block, ref at, aloneTo, 8);
        Put(block, ref at, line[0], 1); for (int i = 1; i < 16; i++) Put(block, ref at, line[i], 2);
        Put(block, ref at, single[0], 1); for (int i = 1; i < 16; i++) Put(block, ref at, single[i], 2);
        return total;
    }

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
