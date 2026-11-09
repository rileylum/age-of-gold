using System.Buffers.Binary;
using System.Text;
using AgeOfGold.Shaders;
using SoulsFormats;

internal static class ShaderMetadataCheck
{
    internal static void Run()
    {
        var source = Fixture(2, "base"); var donor = Fixture(4, "extra");
        var first = ShaderMetadata.AppendTextures(source, donor, 3, 2);
        var result = METAPARAM.Read(first);
        if (!result.Textures.Select(t => t.Name).SequenceEqual(new[] { "base_0", "base_1", "extra_2", "extra_3" })
            || !result.Textures.Select(t => (int)t.Slot).SequenceEqual(new[] { 0, 1, 2, 3 })
            || result.Textures[2].UvGroupName != "authored-uv" || result.Textures[2].DefaultTexturePath != "authored-default"
            || result.RenderPasses.Entries[0].SamplerMask != 15 || result.RenderPasses.Entries[1].SamplerMask != 0
            || result.RenderPasses.Entries[0].VertexSamplerMask != 1 || result.RenderPasses.Entries[0].TessellationSamplerMask != 2
            || result.CaParams.Single().Name != "authored-component" || result.CaParams[0].DefaultValue != 0.375f
            || result.MtdParams.Single().Name != "authored-value" || result.MtdParams[0].DefaultValue[3] != 0.625f
            || result.ShaderId != 179 || result.CBufferSize != 80 || result.GxmdKey != 211)
            throw new Exception("Extended material metadata lost declarations, pass bindings or existing parameters");
        if (!first.SequenceEqual(ShaderMetadata.AppendTextures(source, donor, 2, 3))
            || !source.SequenceEqual(Fixture(2, "base")) || !donor.SequenceEqual(Fixture(4, "extra")))
            throw new Exception("Metadata extension changed its input or repeat bytes");
        Reject(() => ShaderMetadata.AppendTextures(source, donor, 2, 2));
        Reject(() => ShaderMetadata.AppendTextures(source, donor, 1));
        Reject(() => ShaderMetadata.AppendTextures(source, donor, 3));
        var bad = (byte[])source.Clone(); BinaryPrimitives.WriteInt64LittleEndian(bad.AsSpan(32), long.MaxValue);
        Reject(() => ShaderMetadata.AppendTextures(bad, donor, 2));
        bad = (byte[])source.Clone(); BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(12), int.MaxValue);
        Reject(() => ShaderMetadata.AppendTextures(bad, donor, 2));
        bad = (byte[])source.Clone(); BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(8), 7);
        Reject(() => ShaderMetadata.AppendTextures(bad, donor, 2));
        bad = (byte[])source.Clone(); bad[152 + 32] = 1;
        Reject(() => ShaderMetadata.AppendTextures(bad, donor, 2));
        Reject(() => ShaderMetadata.AppendTextures(source[..^1], donor, 2));
        Console.WriteLine("PASS: shader texture declarations extend consistently, preserve parameters and alternate stages, repeat and reject unsupported layouts");
    }

    internal static byte[] Fixture(int textures, string prefix, string[]? names = null, string[]? groups = null, byte[]? types = null)
    {
        int pass = 152 + textures * 48, entries = pass + 40, ca = entries + 48, mtd = ca + 96;
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Encoding.Unicode, true);
        w.Write(new byte[mtd + 80]);
        void Int(int at, int value) { stream.Position = at; w.Write(value); }
        void Long(int at, long value) { stream.Position = at; w.Write(value); }
        void String(int at, string value)
        {
            long start = stream.Length; Long(at, start); stream.Position = start; w.Write(Encoding.Unicode.GetBytes(value + '\0'));
        }
        stream.Position = 0; w.Write("SMD\0"u8); Int(8, 6); Int(12, textures);
        Long(16, ca); Long(24, ca); Long(32, mtd); Int(40, 1); Int(44, 1);
        Int(48, 1025); Int(52, 80); Int(56, 16); Int(64, 2); Int(72, 211); Int(76, 16); Int(84, 1); Int(88, 179);
        for (int i = 0; i < textures; i++)
        {
            int at = 152 + i * 48;
            String(at, names?[i] ?? prefix + "_" + i); String(at + 16, "authored-default"); String(at + 24, groups?[i] ?? "authored-uv");
            stream.Position = at + 8; w.Write((byte)73); w.Write((byte)i); w.Write((byte)0); w.Write(types?[i] ?? (byte)METAPARAM.TextureType.Metallic);
            Int(at + 12, -1);
        }
        Long(pass, entries); Int(pass + 8, 2);
        Int(entries, 19); Int(entries + 8, (1 << textures) - 1); Int(entries + 12, 2); Int(entries + 16, 1);
        Int(entries + 24, 23);
        String(ca, "authored-component"); Int(ca + 40, 3); Int(ca + 44, 5); Int(ca + 48, 1);
        Int(ca + 52, BitConverter.SingleToInt32Bits(0.375f)); Int(ca + 60, 137);
        String(mtd, "authored-value"); Int(mtd + 36, 7); Int(mtd + 40, 2);
        stream.Position = mtd + 44; w.Write((byte)METAPARAM.ParamValueType.Float4);
        Int(mtd + 48, 139); Int(mtd + 64, BitConverter.SingleToInt32Bits(0.625f));
        return stream.ToArray();
    }

    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Unsupported shader texture metadata accepted");
    }
}
