using System.Buffers.Binary;
using System.Text;
using HKLib.Reflection.hk2018;
using HKLib.Serialization.hk2018.Binary;

namespace AgeOfGold.Collision;

internal sealed class SourceTagfile
{
    private readonly byte[] bytes;
    private readonly Section data;
    private readonly Item[] items;
    private sealed record Section(string Name, int Start, int End)
    {
        internal int Content => Start + 8;
    }
    private sealed record Item(HavokType Type, int Offset, int Count, int Kind);

    internal SourceTagfile(byte[] bytes)
    {
        this.bytes = bytes;
        var container = Sections(0, bytes.Length).Single();
        if (container.Name != "TAG0") throw new InvalidDataException("Expected a source tagfile");
        var sections = Sections(container.Content, container.End);
        if (!sections.Select(s => s.Name).SequenceEqual(new[] { "SDKV", "DATA", "TYPE", "INDX" }))
            throw new InvalidDataException("Unsupported source tagfile sections");
        if (Encoding.ASCII.GetString(bytes[sections[0].Content..sections[0].End]) != "20150100")
            throw new InvalidDataException("Expected Havok 2015 source collision");
        data = sections[1];
        var type = sections[2];
        // Both versions serialize type descriptions in this form; no generated 2015 classes are needed.
        var wrapper = new byte[24 + type.End - type.Start];
        BinaryPrimitives.WriteInt32BigEndian(wrapper, wrapper.Length);
        "TCM0"u8.CopyTo(wrapper.AsSpan(4));
        BinaryPrimitives.WriteInt32BigEndian(wrapper.AsSpan(8), 0x40000010);
        "TCID"u8.CopyTo(wrapper.AsSpan(12));
        bytes.AsSpan(type.Start, type.End - type.Start).CopyTo(wrapper.AsSpan(24));
        foreach (var section in Sections(type.Content, type.End))
        {
            // The older names encode the same type-name and body tables.
            int at = 24 + section.Start - type.Start + 4;
            if (section.Name == "TNAM") "TNA1"u8.CopyTo(wrapper.AsSpan(at));
            if (section.Name == "TBOD") "TBDY"u8.CopyTo(wrapper.AsSpan(at));
        }
        var types = new HavokBinarySerializer().ReadCompendium(new MemoryStream(wrapper)).Types;
        var index = Sections(sections[3].Content, sections[3].End);
        if (!index.Select(s => s.Name).SequenceEqual(new[] { "ITEM", "PTCH" }))
            throw new InvalidDataException("Unsupported source collision index");
        var entries = index[0];
        if ((entries.End - entries.Content) % 12 != 0) throw new InvalidDataException("Truncated collision item table");
        items = new Item[(entries.End - entries.Content) / 12];
        if (items.Length < 2 || bytes.AsSpan(entries.Content, 12).ContainsAnyExcept((byte)0))
            throw new InvalidDataException("Missing collision root or null item");
        for (int i = 1; i < items.Length; i++)
        {
            var span = bytes.AsSpan(entries.Content + 12 * i, 12);
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(span);
            int typeIndex = (int)(flags & 0xffffff), kind = (int)(flags >> 28);
            if ((flags & 0x0f000000) != 0 || kind is not (1 or 2) || typeIndex <= 0 || typeIndex >= types.Count)
                throw new InvalidDataException("Invalid collision item type or kind");
            int offset = BinaryPrimitives.ReadInt32LittleEndian(span[4..]);
            int count = BinaryPrimitives.ReadInt32LittleEndian(span[8..]);
            var description = types[typeIndex];
            if (offset < 0 || count < 0 || description.Size <= 0 || description.Alignment <= 0
                || offset % description.Alignment != 0
                || (long)count * description.Size > data.End - data.Content - (long)offset)
                throw new InvalidDataException("Collision item exceeds its data section or is unaligned");
            items[i] = new Item(description, data.Content + offset, count, kind);
        }
        if (items[1].Kind != 1 || items[1].Count != 1) throw new InvalidDataException("Expected one collision root");
        int patch = index[1].Content;
        while (patch < index[1].End)
        {
            if (index[1].End - patch < 8) throw new InvalidDataException("Truncated collision patch table");
            int typeIndex = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(patch));
            int count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(patch + 4));
            patch += 8;
            if (typeIndex <= 0 || typeIndex >= types.Count || count < 0 || count > (index[1].End - patch) / 4)
                throw new InvalidDataException("Invalid collision patch table");
            for (int i = 0; i < count; i++, patch += 4)
            {
                uint offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(patch));
                if ((long)offset + types[typeIndex].Size > data.End - data.Content)
                    throw new InvalidDataException("Collision patch exceeds data section");
            }
        }
    }

    internal Value Root => new(this, items[1].Type, items[1].Offset);

    private Section[] Sections(int start, int end)
    {
        var result = new List<Section>();
        while (start < end)
        {
            if (end - start < 8) throw new InvalidDataException("Truncated collision section header");
            uint header = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(start));
            int length = (int)(header & 0x0fffffff);
            if ((header & 0xb0000000) != 0 || length < 8 || length > end - start)
                throw new InvalidDataException("Invalid collision section size or flags");
            result.Add(new Section(Encoding.ASCII.GetString(bytes, start + 4, 4), start, start + length));
            start += length;
        }
        return result.ToArray();
    }

    private ReadOnlySpan<byte> Data(int offset, int length)
    {
        if (length < 0 || offset < data.Content || offset > data.End - length)
            throw new InvalidDataException("Collision value exceeds data section");
        return bytes.AsSpan(offset, length);
    }

    private Value[] Referenced(ulong index, HavokType? expected, int kind)
    {
        if (index == 0) return [];
        if (index >= (ulong)items.Length) throw new InvalidDataException("Collision reference is outside item table");
        var item = items[(int)index];
        bool compatible = expected is null || expected.Kind is HavokType.TypeKind.Void or HavokType.TypeKind.Opaque;
        for (var type = item.Type; type is not null; type = type.Parent)
            compatible |= type.Identity == expected?.Identity;
        if (!compatible || item.Kind != kind || (kind == 1 && item.Count != 1))
            throw new InvalidDataException("Collision reference has an incompatible type or kind");
        return Enumerable.Range(0, item.Count).Select(i => new Value(this, item.Type, item.Offset + i * item.Type.Size)).ToArray();
    }

    internal sealed class Value(SourceTagfile file, HavokType type, int offset)
    {
        internal string TypeName => Dereference().type.Name;
        private readonly HavokType type = type;

        private Value Dereference()
        {
            if (type.Kind != HavokType.TypeKind.Pointer) return this;
            if (type.Size != 8) throw new InvalidDataException("Unsupported collision pointer width");
            var target = file.Referenced(BinaryPrimitives.ReadUInt64LittleEndian(file.Data(offset, 8)), type.SubType, 1);
            if (target.Length != 1) throw new InvalidDataException("Required collision reference is null");
            if (target[0].type.Kind == HavokType.TypeKind.Pointer)
                throw new InvalidDataException("Nested collision pointers are unsupported");
            return target[0];
        }

        internal Value Field(string name)
        {
            var value = Dereference();
            if (value != this) return value.Field(name);
            if (type.Kind != HavokType.TypeKind.Record) throw new InvalidDataException($"Expected collision record: {type.Identity}");
            var field = type.Fields.SingleOrDefault(f => f.Name == name)
                ?? throw new InvalidDataException($"Missing collision field {type.Identity}.{name}");
            if (field.NonSerializable || field.Offset < 0 || field.Type.Size > type.Size - field.Offset)
                throw new InvalidDataException($"Invalid collision field {type.Identity}.{name}");
            return new Value(file, field.Type, offset + field.Offset);
        }

        internal Value[] Elements()
        {
            if (type.Kind != HavokType.TypeKind.Array || type.SubType is null)
                throw new InvalidDataException($"Expected collision array: {type.Identity}");
            if (type.Name == "hkArray")
            {
                if (type.Size != 16 || BinaryPrimitives.ReadUInt64LittleEndian(file.Data(offset + 8, 8)) != 0)
                    throw new InvalidDataException("Unsupported collision array header");
                return file.Referenced(BinaryPrimitives.ReadUInt64LittleEndian(file.Data(offset, 8)), type.SubType, 2);
            }
            int count = type.Format >> 8;
            if (count <= 0 || type.SubType.Size <= 0 || (long)count * type.SubType.Size != type.Size)
                throw new InvalidDataException($"Unsupported inline collision array: {type.Identity}");
            return Enumerable.Range(0, count).Select(i => new Value(file, type.SubType, offset + i * type.SubType.Size)).ToArray();
        }

        internal long Integer()
        {
            if (type.Kind == HavokType.TypeKind.Bool && type.Size == 1)
            {
                byte value = file.Data(offset, 1)[0];
                return value <= 1 ? value : throw new InvalidDataException("Invalid collision boolean");
            }
            if (type.Kind != HavokType.TypeKind.Int || (type.Format & 0x100) != 0 || type.Format >> 10 != type.Size * 8)
                throw new InvalidDataException($"Unsupported collision integer: {type.Identity}");
            var span = file.Data(offset, type.Size);
            bool signed = (type.Format & 0x200) != 0;
            return (type.Size, signed) switch
            {
                (1, false) => span[0], (1, true) => (sbyte)span[0],
                (2, false) => BinaryPrimitives.ReadUInt16LittleEndian(span), (2, true) => BinaryPrimitives.ReadInt16LittleEndian(span),
                (4, false) => BinaryPrimitives.ReadUInt32LittleEndian(span), (4, true) => BinaryPrimitives.ReadInt32LittleEndian(span),
                (8, false) => checked((long)BinaryPrimitives.ReadUInt64LittleEndian(span)), (8, true) => BinaryPrimitives.ReadInt64LittleEndian(span),
                _ => throw new InvalidDataException("Unsupported collision integer width"),
            };
        }

        internal float[] Floats()
        {
            if (type.Kind == HavokType.TypeKind.Array) return Elements().SelectMany(v => v.Floats()).ToArray();
            if (type.Kind != HavokType.TypeKind.Float || type.Size != 4 || (type.Format & 0x100) != 0)
                throw new InvalidDataException($"Unsupported collision float: {type.Identity}");
            float value = BinaryPrimitives.ReadSingleLittleEndian(file.Data(offset, 4));
            if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite collision value");
            return [value];
        }
    }
}
