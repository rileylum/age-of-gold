using System.Buffers.Binary;
using AgeOfGold.Scene;
using SoulsFormats;

internal static class EnvironmentTextureCheck
{
    internal static void Run()
    {
        var dds = new DDS { dwWidth = 8, dwHeight = 8, dwMipMapCount = 4, dwCaps2 = DDS.CUBEMAP_ALLFACES };
        dds.ddspf.dwFlags = DDS.DDPF.FOURCC; dds.ddspf.dwFourCC = "DX10";
        dds.header10 = new() { dxgiFormat = DDS.DXGI_FORMAT.BC6H_UF16, miscFlag = DDS.RESOURCE_MISC.TEXTURECUBE };
        var payload = Enumerable.Range(0, 6 * 7 * 16).Select(i => (byte)(i * 7 + i / 112)).ToArray();
        var source = dds.Write(payload);
        var template = TextureWriterCheck.Fixture(source); template[24] = 113; template[25] = 1;
        var writer = new TextureWriter(template);
        var result = writer.Write("AUTHORED_cube", source, false);
        Check(result, payload, 8, 4, 113);
        if (!result.SequenceEqual(writer.Write("AUTHORED_cube", source, false))) throw new Exception("Cube build does not repeat");
        template[24] = 100;
        Check(new TextureWriter(template).Write("AUTHORED_cube", source, false), payload, 8, 4, 100);
        Reject(() => writer.Write("AUTHORED_cube", source, true));
        Reject(() => writer.Write("AUTHORED_cube", source[..^16], false));
        dds.dwCaps2 &= ~DDS.DDSCAPS2.CUBEMAP_NEGATIVEY;
        Reject(() => writer.Write("AUTHORED_cube", dds.Write(payload), false));
        dds.dwCaps2 = DDS.CUBEMAP_ALLFACES; dds.dwHeight = 4;
        Reject(() => writer.Write("AUTHORED_cube", dds.Write(payload), false));
        dds.dwHeight = 8; dds.header10.arraySize = 2;
        Reject(() => writer.Write("AUTHORED_cube", dds.Write(payload.Concat(payload).ToArray()), false));
        dds.header10.arraySize = 1; dds.header10.miscFlag = 0;
        Reject(() => writer.Write("AUTHORED_cube", dds.Write(payload), false));
        Console.WriteLine("PASS: HDR cubemap packaging preserves six ordered mip chains, derives host format, repeats and rejects incomplete/array/sRGB inputs");
    }

    internal static void Check(byte[] package, byte[] payload, int width, int mips, int format)
    {
        var bytes = DCX.Is(package) ? DCX.Decompress(package) : package;
        if (bytes[24] != format || bytes[25] != 1 || bytes[26] != mips) throw new Exception("Host cube header differs");
        int offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16));
        var dds = new DDS(bytes[offset..]);
        if (dds.dwWidth != width || dds.dwHeight != width || dds.dwMipMapCount != mips || dds.dwCaps2 != DDS.CUBEMAP_ALLFACES
            || dds.GetDXGIFormat() != DDS.DXGI_FORMAT.BC6H_UF16 || dds.header10.arraySize != 1
            || dds.header10.miscFlag != DDS.RESOURCE_MISC.TEXTURECUBE
            || !bytes.AsSpan(offset + dds.DataOffset).SequenceEqual(payload)) throw new Exception("Cube faces, mip levels or interpretation changed");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Unsupported cubemap accepted");
    }
}
