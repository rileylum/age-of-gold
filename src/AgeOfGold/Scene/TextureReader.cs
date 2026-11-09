using SoulsFormats;

namespace AgeOfGold.Scene;

internal static class TextureReader
{
    internal static IEnumerable<(string Name, byte[] Bytes)> Read(Install source, IEnumerable<string> names)
    {
        var needed = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(source.Locate("map/m10"), "*.tpfbhd").Order(StringComparer.Ordinal))
        {
            var header = Path.GetRelativePath(source.Root, path);
            var data = Path.ChangeExtension(header, ".tpfbdt");
            source.Fingerprint(header);
            using var binder = new BXF3Reader(source.Locate(header), source.Locate(data));
            bool used = false;
            foreach (var file in binder.Files.Where(f => needed.Contains(Stem(Stem(f.Name)))))
            {
                used = true;
                foreach (var texture in ReadPackage(binder.ReadFile(file)))
                {
                    if (!needed.Contains(texture.Name)) continue;
                    if (!found.Add(texture.Name)) throw new InvalidDataException($"Ambiguous source texture: {texture.Name}");
                    yield return texture;
                }
            }
            if (used) source.Fingerprint(data);
        }
        if (!found.SetEquals(needed))
            throw new InvalidDataException("Source textures not found: " + string.Join(", ", needed.Except(found).Order(StringComparer.Ordinal)));
    }

    // PC containers already hold DDS files; console reconstruction and its dependencies are unnecessary.
    internal static IEnumerable<(string Name, byte[] Bytes)> ReadPackage(byte[] bytes)
    {
        using var reader = new BinaryReaderEx(false, DCX.Is(bytes) ? DCX.Decompress(bytes) : bytes);
        reader.AssertASCII("TPF\0");
        reader.ReadInt32();
        int count = reader.ReadInt32();
        reader.AssertByte(0);
        reader.AssertByte(0, 1, 2, 3);
        byte encoding = reader.AssertByte(0, 1, 2);
        reader.AssertByte(0);
        if (count < 0 || count > (reader.Length - 16) / 20) throw new InvalidDataException("Invalid PC texture count");
        for (int i = 0; i < count; i++)
        {
            uint offset = reader.ReadUInt32();
            int length = reader.ReadInt32();
            reader.ReadByte();
            reader.AssertByte(0, 1, 2);
            reader.ReadByte();
            byte compression = reader.AssertByte(0, 1, 2, 3, 128);
            uint nameOffset = reader.ReadUInt32();
            if (reader.ReadInt32() != 0) throw new InvalidDataException("Unclassified PC texture extension");
            var name = encoding == 1 ? reader.GetUTF16(nameOffset) : reader.GetShiftJIS(nameOffset);
            Install.Relative(name);
            if (name.Contains('/') || name.Contains('\\')) throw new InvalidDataException("Texture name must not contain a directory");
            if (length < 0 || offset > reader.Length - length) throw new InvalidDataException($"Texture range exceeds package: {name}");
            var content = reader.GetBytes(offset, length);
            if (compression is 2 or 3)
            {
                content = DCX.Decompress(content, out var info);
                if (info.Type != DCX.Type.DCP_EDGE) throw new InvalidDataException($"Unclassified texture compression: {name}");
            }
            if (content.Length < 128 || !content.AsSpan().StartsWith("DDS "u8)) throw new InvalidDataException($"PC texture lacks a DDS header: {name}");
            yield return (name, content);
        }
    }

    private static string Stem(string path) => Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
}
