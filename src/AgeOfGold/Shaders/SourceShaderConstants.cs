using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace AgeOfGold.Shaders;

internal static class SourceShaderConstants
{
    internal sealed record Buffer(string Name, uint Slot, int Bytes);
    internal sealed record Parameter(string Buffer, uint Slot, string Name, int Offset, int Bytes,
        int Scalar, int Category, int Rows, int Columns, int Elements, bool Used);

    // Keep the instruction-addressed registers; variable names must not change byte or component placement.
    internal static byte[] Normalize(byte[] program)
    {
        var reflection = new Reflection(program);
        var buffers = reflection.Buffers();
        using var data = new MemoryStream();
        data.Write(reflection.Data);
        using var writer = new BinaryWriter(data, Encoding.ASCII, true);
        foreach (var buffer in buffers)
        {
            int name = checked((int)data.Position);
            writer.Write(Encoding.ASCII.GetBytes("aog_registers_" + buffer.Slot)); writer.Write((byte)0);
            while (data.Position % 4 != 0) writer.Write((byte)0);
            int type = checked((int)data.Position);
            foreach (ushort value in new ushort[] { 1, 3, 1, 4, checked((ushort)(buffer.Bytes / 16)), 0 }) writer.Write(value);
            writer.Write(0);
            if (reflection.Major == 5) writer.Write(new byte[20]);
            int variable = checked((int)data.Position);
            foreach (uint value in new uint[] { (uint)name, 0, (uint)buffer.Bytes, 2, (uint)type, 0 }) writer.Write(value);
            if (reflection.Major == 5)
                foreach (uint value in new uint[] { uint.MaxValue, 0, uint.MaxValue, 0 }) writer.Write(value);
            long end = data.Position;
            data.Position = reflection.Record(buffer.Name) + 4;
            writer.Write(1); writer.Write(variable);
            data.Position = end;
        }
        while (data.Position % 4 != 0) writer.Write((byte)0);
        return reflection.Replace(data.ToArray());
    }

    internal static Buffer[] Read(byte[] program) => new Reflection(program).Buffers();

    internal static (Buffer[] Buffers, Parameter[] Parameters) Layout(byte[] program)
    {
        var reflection = new Reflection(program);
        var buffers = reflection.Buffers();
        return (buffers, reflection.Parameters.ToArray());
    }

    internal static string PreserveIntegerSelectors(string source) => Regex.Replace(source,
        @"(?m)^(?<prefix>[ \t]*switch \()(?<value>aog_registers_[^\r\n]+)(?<suffix>\) \{\r?)$", match =>
        {
            string value = match.Groups["value"].Value;
            if (!Regex.IsMatch(value, @"^aog_registers_\d+(?:\[[^\[\]\r\n;]+\])?\.[xyzw]$"))
                throw new InvalidDataException("Unsupported normalized constant selector");
            // Switch compares register bits; a float-to-integer conversion changes its branch.
            return match.Groups["prefix"].Value + "asuint(" + value + ")" + match.Groups["suffix"].Value;
        });

    private sealed class Reflection
    {
        internal byte[] Data { get; }
        internal int Major { get; }
        internal List<Parameter> Parameters { get; } = [];
        private readonly byte[] program;
        private readonly int part;
        private readonly Dictionary<string, int> records = new(StringComparer.Ordinal);
        private readonly SourceShaderBindings.Binding[] bindings;
        private sealed record Field(uint Offset, uint Bytes, bool Used, ushort Category, ushort Scalar,
            ushort Rows, ushort Columns, int Elements);

        private static bool UnusedPrefix(Field first, Field second)
        {
            var unused = first.Used ? second : first;
            var array = first.Used ? first : second;
            return !unused.Used && array.Used && unused.Elements == 0 && array.Elements > 0
                && unused.Offset == array.Offset && unused.Bytes < array.Bytes
                && (unused.Category, unused.Scalar, unused.Rows, unused.Columns)
                    == (array.Category, array.Scalar, array.Rows, array.Columns);
        }

        internal Reflection(byte[] program)
        {
            bindings = SourceShaderBindings.Read(program);
            this.program = program;
            uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(program.AsSpan(at, 4));
            for (int i = 0; i < Word(28); i++)
            {
                int at = (int)Word(32 + i * 4);
                if (program.AsSpan(at, 4).SequenceEqual("RDEF"u8)) part = at;
                if (program.AsSpan(at, 4).SequenceEqual("SHEX"u8) || program.AsSpan(at, 4).SequenceEqual("SHDR"u8))
                    Major = (int)((Word(at + 8) >> 4) & 15);
            }
            Data = program.AsSpan(part + 8, (int)Word(part + 4)).ToArray();
            uint count = U32(0), table = U32(4);
            if (count > 1024) throw new InvalidDataException("Too many source constant buffers");
            Range(table, count * 24);
            for (uint i = 0; i < count; i++)
            {
                int record = checked((int)(table + i * 24));
                uint kind = U32(record + 20);
                if (kind == 3) continue; // Structured-resource records do not describe constant registers.
                if (kind != 0) throw new InvalidDataException("Unsupported source constant buffer kind");
                string name = Name(U32(record));
                if (!records.TryAdd(name, record)) throw new InvalidDataException("Ambiguous source constant buffer");
            }
            if (!records.Keys.Order(StringComparer.Ordinal).SequenceEqual(bindings.Where(b => b.Kind == 0).Select(b => b.Name).Order(StringComparer.Ordinal)))
                throw new InvalidDataException("Source constant buffer declarations and bindings differ");
        }

        internal int Record(string name) => records[name];

        internal Buffer[] Buffers() => records.Select(pair =>
        {
            int record = pair.Value;
            uint size = U32(record + 12), count = U32(record + 4), table = U32(record + 8);
            if (size == 0 || size > 65536 || size % 16 != 0 || count == 0 || count > size / 4)
                throw new InvalidDataException("Invalid source constant buffer extent");
            int stride = Major == 5 ? 40 : 24;
            Range(table, checked(count * (uint)stride));
            var fields = new HashSet<string>(StringComparer.Ordinal);
            var ranges = new List<Field>();
            for (int i = 0; i < count; i++)
            {
                int at = checked((int)table + i * stride);
                string name = Name(U32(at));
                if (!fields.Add(name)) throw new InvalidDataException("Ambiguous source constant field");
                uint offset = U32(at + 4), length = U32(at + 8), type = U32(at + 16), defaults = U32(at + 20);
                if (offset % 4 != 0 || length == 0 || length % 4 != 0 || offset > size || length > size - offset)
                    throw new InvalidDataException("Source constant field exceeds its buffer");
                if (defaults != 0) Range(defaults, length);
                Range(type, Major == 5 ? 36u : 16u);
                int t = checked((int)type);
                ushort category = U16(t), scalar = U16(t + 2), rows = U16(t + 4), columns = U16(t + 6);
                // lazy: primitive 32-bit scalar/vector/matrix fields; structs and other widths need GPU proof.
                if (scalar is not (1 or 2 or 3 or 19) || category > 3 || rows is < 1 or > 4 || columns is < 1 or > 4
                    || (category < 2 && rows != 1) || (category == 0 && columns != 1) || U16(t + 10) != 0)
                    throw new InvalidDataException("Unsupported source constant field type");
                int vectors = category == 2 ? rows : category == 3 ? columns : 1;
                int components = category == 3 ? rows : columns;
                int elementBytes = (vectors - 1) * 16 + components * 4;
                int elements = U16(t + 8);
                int extent = elements == 0 ? elementBytes : checked((elements - 1) * ((elementBytes + 15) / 16 * 16) + elementBytes);
                if (extent > length || (category >= 2 || elements != 0 ? offset % 16 != 0 : offset % 16 + elementBytes > 16))
                    throw new InvalidDataException("Source constant type exceeds its declared extent");
                var field = new Field(offset, length, (U32(at + 12) & 2) != 0, category, scalar, rows, columns, elements);
                // Some reflection retains an unused name for the first element of a used array.
                if (ranges.Any(r => offset < r.Offset + r.Bytes && r.Offset < offset + length && !UnusedPrefix(r, field)))
                    throw new InvalidDataException("Overlapping source constant fields");
                ranges.Add(field);
                Parameters.Add(new(pair.Key, bindings.Single(b => b.Name == pair.Key).Slot, name,
                    (int)offset, (int)length, scalar, category, rows, columns, elements, field.Used));
            }
            return new Buffer(pair.Key, bindings.Single(b => b.Name == pair.Key).Slot, (int)size);
        }).ToArray();

        internal byte[] Replace(byte[] data)
        {
            uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(program.AsSpan(at, 4));
            int count = checked((int)Word(28));
            var parts = new List<byte[]>();
            for (int i = 0; i < count; i++)
            {
                int at = (int)Word(32 + i * 4);
                if (at != part) parts.Add(program.AsSpan(at, checked(8 + (int)Word(at + 4))).ToArray());
                else
                {
                    var replacement = new byte[data.Length + 8];
                    "RDEF"u8.CopyTo(replacement); BinaryPrimitives.WriteInt32LittleEndian(replacement.AsSpan(4), data.Length);
                    data.CopyTo(replacement, 8); parts.Add(replacement);
                }
            }
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            // This checksum-free intermediate goes only to the pinned converter; DXC builds the final container.
            writer.Write("DXBC"u8); writer.Write(new byte[16]); writer.Write(1);
            int offset = 32 + 4 * count;
            writer.Write(checked(offset + parts.Sum(p => p.Length))); writer.Write(count);
            foreach (var bytes in parts) { writer.Write(offset); offset = checked(offset + bytes.Length); }
            foreach (var bytes in parts) writer.Write(bytes);
            return stream.ToArray();
        }

        private uint U32(int at) { Range((uint)at, 4); return BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(at, 4)); }
        private ushort U16(int at) { Range((uint)at, 2); return BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(at, 2)); }
        private void Range(uint at, uint length)
        {
            if (at > Data.Length || length > Data.Length - at) throw new InvalidDataException("Invalid source constant reflection range");
        }
        private string Name(uint at)
        {
            Range(at, 1);
            int end = Array.IndexOf(Data, (byte)0, (int)at);
            if (end < 0 || end - at > 256) throw new InvalidDataException("Invalid source constant name length");
            string name = Encoding.ASCII.GetString(Data, (int)at, end - (int)at);
            if (!Regex.IsMatch(name, @"^(?:\$Globals|[A-Za-z_][A-Za-z_0-9]*)$")) throw new InvalidDataException("Unsupported source constant name");
            return name;
        }
    }
}
