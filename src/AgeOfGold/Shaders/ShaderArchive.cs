using System.Buffers.Binary;
using SoulsFormats;

namespace AgeOfGold.Shaders;

internal static class ShaderArchive
{
    internal static byte[] Read(byte[] archive, params string[] path)
    {
        CheckPath(path);
        var bytes = archive;
        foreach (var name in path) bytes = ReadEntry(bytes, name);
        ValidateProgram(bytes);
        return bytes;
    }

    internal static byte[] ReadFamily(byte[] archive, string name)
    {
        CheckPath([name]);
        var bytes = ReadEntry(archive, name);
        using var reader = OpenReader(bytes);
        return bytes;
    }

    internal static byte[] Replace(byte[] archive, string[] path, byte[] program)
    {
        CheckPath(path);
        ValidateProgram(program);
        return Edit(archive, 0);

        byte[] Edit(byte[] bytes, int index)
        {
            var binder = Open(bytes);
            var entry = Entry(binder, path[index]);
            if (index == path.Length - 1)
            {
                ValidateProgram(entry.Bytes);
                entry.Bytes = program;
            }
            else entry.Bytes = Edit(entry.Bytes, index + 1);
            return binder switch { BND3 b => b.Write(), BND4 b => b.Write(), _ => throw new InvalidOperationException() };
        }
    }

    /// <summary>Adds a family cloned from an installed one; <paramref name="edit"/> receives each member's name after the family prefix.</summary>
    internal static byte[] AddFamily(byte[] archive, string template, string name, Func<string, byte[], byte[]> edit)
    {
        CheckPath([template, name]);
        var outer = Open(archive);
        if (outer.Files.Any(f => Leaf(f.Name).Equals(name + ".shaderbdle", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Shader family already exists: " + name);
        var original = Entry(outer, template + ".shaderbdle");
        var inner = Open(original.Bytes);
        foreach (var file in inner.Files)
        {
            int start = file.Name.LastIndexOfAny(['/', '\\']) + 1;
            if (!file.Name.AsSpan(start).StartsWith(template, StringComparison.Ordinal))
                throw new InvalidDataException("Shader family member lacks its family prefix: " + Leaf(file.Name));
            string suffix = file.Name[(start + template.Length)..];
            file.Bytes = edit(suffix, file.Bytes);
            file.Name = file.Name[..start] + name + suffix;
        }
        int at = original.Name.LastIndexOfAny(['/', '\\']) + 1;
        outer.Files.Add(new BinderFile(original.Flags, outer.Files.Max(f => f.ID) + 1, original.Name[..at] + name + ".shaderbdle", Write(inner))
            { CompressionInfo = original.CompressionInfo });
        return Write(outer);
    }

    private static string Leaf(string name) => Path.GetFileName(name.Replace('\\', '/'));
    private static byte[] Write(IBinder binder) => binder switch { BND3 b => b.Write(), BND4 b => b.Write(), _ => throw new InvalidOperationException() };

    private static IBinder Open(byte[] bytes)
    {
        DCX.CompressionInfo compression = new DCX.NoCompressionInfo();
        if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes, out compression);
        if (bytes.AsSpan().StartsWith("BND3"u8))
        {
            var binder = BND3.Read(bytes);
            binder.Compression = compression;
            return binder;
        }
        if (bytes.AsSpan().StartsWith("BND4"u8))
        {
            var binder = BND4.Read(bytes);
            binder.Compression = compression;
            return binder;
        }
        throw new InvalidDataException("Unsupported shader archive container");
    }

    private static byte[] Plain(byte[] bytes) => DCX.Is(bytes) ? DCX.Decompress(bytes) : bytes;

    private static BinderReader OpenReader(byte[] bytes)
    {
        bytes = Plain(bytes);
        if (bytes.AsSpan().StartsWith("BND3"u8)) return new BND3Reader(bytes);
        if (bytes.AsSpan().StartsWith("BND4"u8)) return new BND4Reader(bytes);
        throw new InvalidDataException("Unsupported shader archive container");
    }

    private static byte[] ReadEntry(byte[] bytes, string name)
    {
        using var reader = OpenReader(bytes);
        var matches = reader.Files.Where(f =>
            Path.GetFileName(f.Name.Replace('\\', '/')).Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"Expected one shader archive entry {name}, found {matches.Length}");
        return reader.ReadFile(matches[0]);
    }

    private static BinderFile Entry(IBinder binder, string name)
    {
        var matches = binder.Files.Where(f =>
            Path.GetFileName(f.Name.Replace('\\', '/')).Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"Expected one shader archive entry {name}, found {matches.Length}");
        return matches[0];
    }

    private static void CheckPath(string[] path)
    {
        if (path.Length == 0 || path.Any(p => string.IsNullOrWhiteSpace(p) || p is "." or ".." || p.IndexOfAny(['/', '\\', ':', '\0']) >= 0))
            throw new InvalidDataException("Shader archive paths must name entries, one per container");
    }

    // A binder entry is a whole DXBC container; stage payloads and unknown metadata stay opaque.
    internal static void ValidateProgram(byte[] bytes)
    {
        uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at, 4));
        if (bytes.Length < 32 || !bytes.AsSpan(0, 4).SequenceEqual("DXBC"u8) || Word(20) != 1 || Word(24) != bytes.Length)
            throw new InvalidDataException("Invalid shader program container");
        uint count = Word(28);
        if (count > (bytes.Length - 32) / 4)
            throw new InvalidDataException("Truncated shader part index");
        var ranges = new List<(int Start, int End)>();
        int payloads = 0;
        for (int i = 0; i < count; i++)
        {
            uint offset = Word(32 + 4 * i);
            if (offset < 32 + count * 4 || offset > bytes.Length - 8)
                throw new InvalidDataException("Shader part offset is outside the container");
            int at = (int)offset;
            uint length = Word(at + 4);
            if (length > bytes.Length - at - 8)
                throw new InvalidDataException("Truncated shader part");
            int end = at + 8 + (int)length;
            if (ranges.Any(r => at < r.End && r.Start < end))
                throw new InvalidDataException("Overlapping shader parts");
            ranges.Add((at, end));
            var tag = bytes.AsSpan(at, 4);
            if (tag.SequenceEqual("DXIL"u8) || tag.SequenceEqual("SHEX"u8) || tag.SequenceEqual("SHDR"u8))
            {
                if (length == 0) throw new InvalidDataException("Empty shader program payload");
                payloads++;
            }
        }
        if (payloads != 1) throw new InvalidDataException("Expected exactly one shader program payload");
    }
}
