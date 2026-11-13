using SoulsFormats;

namespace AgeOfGold.Scene;

/// <summary>Authored one-block textures for host texture roles a source material does not have.</summary>
internal static class NeutralTextures
{
    /// <summary>A normal map pointing straight out of the surface.</summary>
    internal const string FlatNormal = "aog:flat-normal";
    /// <summary>An opaque black packed map.</summary>
    internal const string Neutral = "aog:neutral";

    internal static bool Is(string name) => name is FlatNormal or Neutral;

    internal static byte[] Bytes(string name)
    {
        // One 4×4 block: both normal channels at mid-range, or black colour endpoints with full coverage.
        var (format, payload) = name switch
        {
            FlatNormal => (DDS.DXGI_FORMAT.BC5_UNORM, new byte[] { 128, 128, 0, 0, 0, 0, 0, 0, 128, 128, 0, 0, 0, 0, 0, 0 }),
            Neutral => (DDS.DXGI_FORMAT.BC1_UNORM, new byte[8]),
            _ => throw new InvalidDataException("Unknown neutral texture: " + name),
        };
        var dds = new DDS { dwWidth = 4, dwHeight = 4, dwMipMapCount = 1, dwPitchOrLinearSize = payload.Length,
            ddspf = new DDS.PIXELFORMAT { dwFourCC = "DX10", dwFlags = DDS.DDPF.FOURCC }, header10 = new DDS.HEADER_DXT10 { dxgiFormat = format } };
        dds.dwFlags |= DDS.DDSD.LINEARSIZE;
        return dds.Write(payload);
    }
}
