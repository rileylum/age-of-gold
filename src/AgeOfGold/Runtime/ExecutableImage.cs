using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;

namespace AgeOfGold.Runtime;

internal sealed class ExecutableImage
{
    internal sealed record Section(int Start, int Length, int MemoryLength, int Raw, SectionCharacteristics Flags);
    internal sealed record Function(int Start, int End);
    internal byte[] Bytes { get; }
    internal ulong Base { get; }
    internal uint Size { get; }
    internal Section[] Sections { get; }
    internal Function[] Functions { get; }
    internal Version Version { get; }

    internal ExecutableImage(byte[] bytes)
    {
        Bytes = bytes;
        using var pe = new PEReader(new MemoryStream(bytes, false));
        var h = pe.PEHeaders;
        var optional = h.PEHeader ?? throw new InvalidDataException("Host has no PE header");
        if (h.CoffHeader.Machine != Machine.Amd64 || optional.Magic != PEMagic.PE32Plus)
            throw new InvalidDataException("Host runtime requires an x64 PE executable");
        Base = optional.ImageBase;
        if (optional.SizeOfImage <= 0) throw new InvalidDataException("Invalid host image size");
        Size = (uint)optional.SizeOfImage;
        Sections = h.SectionHeaders.Select(s => new Section(s.VirtualAddress,
            s.SizeOfRawData, Math.Max(s.SizeOfRawData, s.VirtualSize), s.PointerToRawData, s.SectionCharacteristics)).ToArray();
        foreach (var s in Sections)
        {
            if (s.Start < optional.SizeOfHeaders || s.Length < 0 || s.MemoryLength < 0
                || (s.Length > 0 && s.Raw < optional.SizeOfHeaders)
                || (long)s.Raw + s.Length > bytes.Length || (long)s.Start + s.MemoryLength > Size)
                throw new InvalidDataException("Invalid host section bounds");
            if (Sections.Any(t => !ReferenceEquals(s, t) &&
                (Overlap(s.Start, s.MemoryLength, t.Start, t.MemoryLength) || Overlap(s.Raw, s.Length, t.Raw, t.Length))))
                throw new InvalidDataException("Overlapping host sections");
        }
        Version = ReadVersion(optional.ResourceTableDirectory);
        var directory = optional.ExceptionTableDirectory;
        if (directory.Size <= 0 || directory.Size % 12 != 0)
            throw new InvalidDataException("Host function directory is missing or malformed");
        var raw = Raw(directory.RelativeVirtualAddress, directory.Size);
        Functions = Enumerable.Range(0, directory.Size / 12).Select(i =>
            new Function(checked((int)U32(raw + i * 12)), checked((int)U32(raw + i * 12 + 4)))).ToArray();
        var end = 0;
        foreach (var f in Functions)
        {
            if (f.Start < end || f.End <= f.Start || !Has(f.Start, f.End - f.Start, SectionCharacteristics.MemExecute))
                throw new InvalidDataException("Invalid host function bounds");
            Raw(f.Start, f.End - f.Start);
            end = f.End;
        }
    }

    private Version ReadVersion(DirectoryEntry resource)
    {
        if (resource.Size < 16) throw new InvalidDataException("Host version resource is missing");
        var raw = Raw(resource.RelativeVirtualAddress, resource.Size);
        int Bound(uint offset, int length)
        {
            if ((ulong)offset + (uint)length > (uint)resource.Size) throw new InvalidDataException("Invalid version resource bounds");
            return raw + (int)offset;
        }
        uint[] Entries(uint offset, int? identity)
        {
            var at = Bound(offset, 16);
            var named = BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(at + 12));
            var count = BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(at + 14));
            Bound(offset, checked(16 + (named + count) * 8));
            var entries = new List<uint>();
            for (var i = 0; i < named + count; i++)
                if (identity is null || U32(at + 16 + i * 8) == identity)
                    entries.Add(U32(at + 20 + i * 8));
            if (entries.Count == 0 || entries.Count > 32) throw new InvalidDataException("Missing or unsupported version resource entries");
            return entries.ToArray();
        }
        uint Directory(uint value)
        {
            if ((value & 0x80000000) == 0) throw new InvalidDataException("Invalid version resource tree");
            return value & 0x7fffffff;
        }
        var roots = Entries(0, 16);
        if (roots.Length != 1) throw new InvalidDataException("Ambiguous version resource");
        var versions = new HashSet<Version>();
        foreach (var name in Entries(Directory(roots[0]), null))
            foreach (var language in Entries(Directory(name), null))
            {
                if ((language & 0x80000000) != 0) throw new InvalidDataException("Invalid version resource leaf");
                var entry = Bound(language, 16);
                var length = checked((int)U32(entry + 4));
                var value = Raw(checked((int)U32(entry)), length);
                if (length < 92 || BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(value)) > length
                    || BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(value)) < 92
                    || BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(value + 2)) != 52
                    || BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(value + 4)) != 0
                    || !Bytes.AsSpan(value + 6, 32).SequenceEqual(Encoding.Unicode.GetBytes("VS_VERSION_INFO\0"))
                    || U32(value + 40) != 0xfeef04bd || U32(value + 44) != 0x10000)
                    throw new InvalidDataException("Invalid fixed version information");
                var high = U32(value + 48); var low = U32(value + 52);
                versions.Add(new((int)(high >> 16), (int)(high & 65535), (int)(low >> 16), (int)(low & 65535)));
            }
        if (versions.Count != 1) throw new InvalidDataException("Ambiguous host file version");
        return versions.Single();
    }

    internal uint U32(int raw) => BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(raw, 4));
    internal ulong U64(int raw) => BinaryPrimitives.ReadUInt64LittleEndian(Bytes.AsSpan(raw, 8));
    internal int Raw(int rva, int length = 1)
    {
        var s = Sections.SingleOrDefault(s => rva >= s.Start && length >= 0 && (long)rva + length <= (long)s.Start + s.Length)
            ?? throw new InvalidDataException("Host address is outside backed sections");
        return s.Raw + rva - s.Start;
    }

    internal bool Has(int rva, int length, SectionCharacteristics required, SectionCharacteristics forbidden = 0) =>
        Sections.Any(s => rva >= s.Start && length > 0 && (long)rva + length <= (long)s.Start + s.MemoryLength
            && (s.Flags & required) == required && (s.Flags & forbidden) == 0);

    internal IEnumerable<int> Find(byte[] value)
    {
        foreach (var s in Sections.Where(s => (s.Flags & SectionCharacteristics.MemRead) != 0
            && (s.Flags & SectionCharacteristics.MemExecute) == 0))
        {
            var at = 0;
            while (at <= s.Length - value.Length)
            {
                var index = Bytes.AsSpan(s.Raw + at, s.Length - at).IndexOf(value);
                if (index < 0) break;
                at += index;
                yield return s.Start + at;
                at++;
            }
        }
    }

    internal Function? Containing(int rva)
    {
        var lo = 0; var hi = Functions.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var f = Functions[mid];
            if (rva < f.Start) hi = mid - 1;
            else if (rva >= f.End) lo = mid + 1;
            else return f;
        }
        return null;
    }

    internal IEnumerable<int> RelativeReferences(HashSet<int> targets)
    {
        foreach (var s in Sections.Where(s => (s.Flags & SectionCharacteristics.MemExecute) != 0))
            for (var i = 0; i <= s.Length - 4; i++)
            {
                var at = s.Start + i;
                var target = unchecked((int)U32(s.Raw + i) + at + 4);
                if (targets.Contains(target)) yield return at;
            }
    }

    private static bool Overlap(int a, int al, int b, int bl) => al > 0 && bl > 0 && a < (long)b + bl && b < (long)a + al;
}
