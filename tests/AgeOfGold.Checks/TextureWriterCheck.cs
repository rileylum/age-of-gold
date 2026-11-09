using System.Buffers.Binary;
using System.Text;
using AgeOfGold.Scene;
using SoulsFormats;

internal static class TextureWriterCheck
{
    internal static void Run()
    {
        foreach (var (format, block, packageFormat) in new[] {
            (DDS.DXGI_FORMAT.BC1_UNORM,8,0), (DDS.DXGI_FORMAT.BC5_UNORM,16,36), (DDS.DXGI_FORMAT.BC7_UNORM,16,112) })
        {
            var payload = Enumerable.Range(0, 7 * block).Select(i => (byte)(i * 3)).ToArray();
            var dds = new DDS { dwWidth = 8, dwHeight = 8, dwMipMapCount = 4 };
            dds.ddspf.dwFlags = DDS.DDPF.FOURCC;
            if (format == DDS.DXGI_FORMAT.BC1_UNORM) dds.ddspf.dwFourCC = "DXT1";
            else { dds.ddspf.dwFourCC = "DX10"; dds.header10 = new() { dxgiFormat = format }; }
            var source = dds.Write(payload);
            var template = Fixture(source);
            var writer = new TextureWriter(template);
            var output = writer.Write("AUTHORED_test_a", source, false);
            if (!output.SequenceEqual(writer.Write("AUTHORED_test_a", source, false))) throw new Exception("Texture packaging repeat differs");
            Check(output, payload, packageFormat, format);
            if (format == DDS.DXGI_FORMAT.BC5_UNORM)
                Reject(() => writer.Write("AUTHORED_test_a", source, true), "sRGB normal format");
            else
            {
                var encoded = format == DDS.DXGI_FORMAT.BC1_UNORM ? DDS.DXGI_FORMAT.BC1_UNORM_SRGB : DDS.DXGI_FORMAT.BC7_UNORM_SRGB;
                Check(writer.Write("AUTHORED_test_a", source, true), payload, packageFormat, encoded);
            }
            Reject(() => writer.Write("../outside", source, false), "texture path");
            Reject(() => writer.Write("AUTHORED_test_a", source[..^1], false), "truncated mip data");
            dds.dwCaps2 = DDS.DDSCAPS2.CUBEMAP;
            Reject(() => writer.Write("AUTHORED_test_a", dds.Write(payload), false), "unsupported cube map");
            dds.dwCaps2 = 0; dds.dwDepth = -1;
            Reject(() => writer.Write("AUTHORED_test_a", dds.Write(payload), false), "negative texture depth");
            if (!template.SequenceEqual(Fixture(source))) throw new Exception("Template changed");
        }
        var invalid = Fixture(new byte[128]); invalid[12] = 4;
        Reject(() => new TextureWriter(invalid), "console template");
        Console.WriteLine("PASS: host PC texture packages preserve mip payloads, choose linear/sRGB explicitly, repeat exactly and reject unsupported or malformed inputs");
    }

    private static void Check(byte[] bytes, byte[] payload, int format, DDS.DXGI_FORMAT sampling)
    {
        if (!bytes.AsSpan(0,4).SequenceEqual("TPF\0"u8) || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)) != 1
            || !bytes.AsSpan(12,4).SequenceEqual(new byte[] { 0,3,1,0 })
            || !bytes.AsSpan(24,4).SequenceEqual(new byte[] { (byte)format,0,4,0 }))
            throw new Exception("PC container identity, format or mip count differs");
        int offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16));
        int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(20));
        int nameOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(28));
        var name = Encoding.Unicode.GetString(bytes, nameOffset, 2 * "AUTHORED_test_a".Length);
        if (name != "AUTHORED_test_a" || offset % 4 != 0 || offset + size != bytes.Length)
            throw new Exception("PC texture name or extent differs");
        var ddsBytes = bytes[offset..]; var dds = new DDS(ddsBytes);
        if (dds.GetDXGIFormat() != sampling || dds.dwWidth != 8 || dds.dwHeight != 8 || dds.dwMipMapCount != 4
            || !ddsBytes.AsSpan(dds.DataOffset).SequenceEqual(payload))
            throw new Exception("Texture format, dimensions or mip payload differs");
    }

    internal static byte[] Fixture(byte[] dds)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write("TPF\0"u8); writer.Write(dds.Length); writer.Write(1); writer.Write(new byte[] { 0,3,1,0 });
        writer.Write(40); writer.Write(dds.Length); writer.Write(new byte[] { 0,0,1,0 });
        writer.Write(36); writer.Write(0); writer.Write(new byte[] { 120,0,0,0 }); writer.Write(dds);
        return stream.ToArray();
    }

    private static void Reject(Action action, string reason)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Accepted " + reason);
    }
}
