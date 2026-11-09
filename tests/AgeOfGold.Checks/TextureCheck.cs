using System.Text;
using AgeOfGold;
using AgeOfGold.Scene;
using SoulsFormats;

internal static class TextureCheck
{
    internal static void Run(string root)
    {
        var source = Path.Combine(root, "authored-textures");
        Directory.CreateDirectory(Path.Combine(source, "map/m10"));
        File.WriteAllText(Path.Combine(source, "DarkSoulsRemastered.exe"), "authored executable");
        var pixels = new byte[] { 0, 248, 224, 7, 0, 0, 0, 0 };
        var dds = new DDS { dwWidth = 4, dwHeight = 4, dwMipMapCount = 1 };
        dds.ddspf.dwFourCC = "DXT1";
        dds.ddspf.dwFlags = DDS.DDPF.FOURCC;
        var expected = dds.Write(pixels);
        var name = Encoding.Unicode.GetBytes("authored-colour\0");
        var dataOffset = (36 + name.Length + 3) / 4 * 4;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
        {
            writer.Write("TPF\0"u8); writer.Write(expected.Length); writer.Write(1);
            writer.Write(new byte[] { 0, 3, 1, 0 });
            writer.Write(dataOffset); writer.Write(expected.Length); writer.Write(new byte[] { 0, 0, 1, 0 });
            writer.Write(36); writer.Write(0); writer.Write(name);
            writer.Write(new byte[dataOffset - stream.Length]); writer.Write(expected);
        }
        var binder = new BXF3 { Version = "authored", Files = [new BinderFile(Binder.FileFlags.Flag1, 0, "authored-colour.tpf", stream.ToArray())] };
        binder.Write(Path.Combine(source, "map/m10/authored.tpfbhd"), Path.Combine(source, "map/m10/authored.tpfbdt"));
        var install = new Install(source, "DarkSoulsRemastered.exe");
        var texture = TextureReader.Read(install, ["authored-colour"]).Single();
        if (texture.Name != "authored-colour" || !texture.Bytes.SequenceEqual(expected))
            throw new Exception("Texture extraction must preserve all DDS bytes and its name");
        if (install.Fingerprints.Count != 3) throw new Exception("Texture inputs were not fingerprinted");
        try
        {
            TextureReader.Read(install, ["absent"]).ToArray();
            throw new Exception("Missing requested texture was accepted");
        }
        catch (InvalidDataException) { }
        var cube = new DDS { dwWidth = 4, dwHeight = 4, dwMipMapCount = 1, dwCaps2 = DDS.CUBEMAP_ALLFACES };
        cube.ddspf.dwFlags = DDS.DDPF.FOURCC; cube.ddspf.dwFourCC = "DX10";
        cube.header10 = new() { dxgiFormat = DDS.DXGI_FORMAT.BC6H_UF16, miscFlag = DDS.RESOURCE_MISC.TEXTURECUBE };
        var environment = cube.Write(Enumerable.Range(0, 96).Select(i => (byte)i).ToArray());
        var loose = TextureWriterCheck.Fixture(environment); loose[24] = 113; loose[25] = 1;
        File.WriteAllBytes(Path.Combine(source, "map/m10/authored-environment.tpf.dcx"), loose);
        var mixed = TextureReader.Read(install, ["authored-colour", "x"]).ToDictionary(t => t.Name, t => t.Bytes);
        if (!mixed["x"].SequenceEqual(environment) || !mixed["authored-colour"].SequenceEqual(expected) || install.Fingerprints.Count != 4)
            throw new Exception("Loose environment package was not read exactly and fingerprinted");
        File.WriteAllBytes(Path.Combine(source, "map/m10/authored-duplicate.tpf.dcx"), loose);
        try
        {
            TextureReader.Read(install, ["x"]).ToArray();
            throw new Exception("Ambiguous loose texture was accepted");
        }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: source texture archives preserve exact DDS contents, record both files and reject missing names");
        Console.WriteLine("PASS: loose environment textures share the checked lookup and reject ambiguous names");
    }
}
