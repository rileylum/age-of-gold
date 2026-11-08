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
        Console.WriteLine("PASS: source texture archives preserve exact DDS contents, record both files and reject missing names");
    }
}
