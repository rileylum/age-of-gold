using System.Numerics;
using System.Text;
using SoulsFormats;

namespace AgeOfGold.Scene;

internal sealed class TextureWriter
{
    private readonly byte flag;
    private readonly byte encoding;
    private readonly DCX.CompressionInfo? compression;

    internal TextureWriter(byte[] template)
    {
        var bytes = DCX.Is(template) ? DCX.Decompress(template, out compression) : template;
        if (bytes.Length < 16 || bytes[12] != 0)
            throw new InvalidDataException("Host texture template must be a PC container");
        if (TextureReader.ReadPackage(bytes).Count() == 0) throw new InvalidDataException("Host texture template is empty");
        flag = bytes[13]; encoding = bytes[14];
    }

    internal byte[] Write(string name, byte[] texture, bool srgb)
    {
        if (name.Length == 0 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new InvalidDataException("Texture name must contain only ASCII letters, digits and underscores");
        if (texture.Length < 128 || !texture.AsSpan().StartsWith("DDS "u8))
            throw new InvalidDataException("Texture requires a DDS header");
        var dds = new DDS(texture);
        // lazy: surface textures are single 2D images; cube, volume and array packaging needs its own check.
        if (dds.dwWidth <= 0 || dds.dwHeight <= 0 || dds.dwDepth is < 0 or > 1 || dds.dwCaps2 != 0
            || (dds.header10 is not null && (dds.header10.arraySize != 1 || dds.header10.resourceDimension != DDS.DIMENSION.TEXTURE2D || dds.header10.miscFlag != 0)))
            throw new InvalidDataException("Unsupported surface texture dimensions");
        var format = dds.GetDXGIFormat();
        var (code, block, linear, encoded) = format switch {
            DDS.DXGI_FORMAT.BC1_UNORM or DDS.DXGI_FORMAT.BC1_UNORM_SRGB => (0, 8, DDS.DXGI_FORMAT.BC1_UNORM, DDS.DXGI_FORMAT.BC1_UNORM_SRGB),
            DDS.DXGI_FORMAT.BC5_UNORM when !srgb => (36, 16, DDS.DXGI_FORMAT.BC5_UNORM, DDS.DXGI_FORMAT.BC5_UNORM),
            DDS.DXGI_FORMAT.BC7_UNORM or DDS.DXGI_FORMAT.BC7_UNORM_SRGB => (112, 16, DDS.DXGI_FORMAT.BC7_UNORM, DDS.DXGI_FORMAT.BC7_UNORM_SRGB),
            _ => throw new InvalidDataException($"Unsupported surface texture format or sampling: {format}, sRGB={srgb}"),
        };
        int mipCount = Math.Max(1, dds.dwMipMapCount);
        if (dds.dwMipMapCount < 0 || mipCount > 1 + BitOperations.Log2((uint)Math.Max(dds.dwWidth, dds.dwHeight)))
            throw new InvalidDataException("Invalid texture mip count");
        long expected = 0;
        for (int mip = 0; mip < mipCount; mip++)
            expected = checked(expected + ((Math.Max(1, dds.dwWidth >> mip) + 3L) / 4) * ((Math.Max(1, dds.dwHeight >> mip) + 3L) / 4) * block);
        var payload = texture.AsSpan(dds.DataOffset);
        if (payload.Length != expected) throw new InvalidDataException("Texture mip payload has an unexpected length");
        dds.ddspf.dwFourCC = "DX10"; dds.ddspf.dwFlags |= DDS.DDPF.FOURCC;
        dds.header10 ??= new DDS.HEADER_DXT10();
        dds.header10.dxgiFormat = srgb ? encoded : linear;
        var content = dds.Write(payload.ToArray());
        var label = (encoding == 1 ? Encoding.Unicode : Encoding.ASCII).GetBytes(name + '\0');
        int contentOffset = checked((36 + label.Length + 3) / 4 * 4);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write("TPF\0"u8); writer.Write(checked(content.Length + contentOffset - 36 - label.Length)); writer.Write(1);
            writer.Write(new byte[] { 0, flag, encoding, 0 });
            writer.Write(contentOffset); writer.Write(content.Length);
            writer.Write(new byte[] { (byte)code, 0, (byte)dds.dwMipMapCount, 0 });
            writer.Write(36); writer.Write(0); writer.Write(label);
            writer.Write(new byte[contentOffset - stream.Length]); writer.Write(content);
        }
        var result = stream.ToArray();
        return compression is null ? result : DCX.Compress(result, compression);
    }
}
