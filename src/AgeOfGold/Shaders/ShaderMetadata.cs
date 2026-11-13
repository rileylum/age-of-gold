using System.Buffers.Binary;
using System.Text;
using SoulsFormats;

namespace AgeOfGold.Shaders;

internal static class ShaderMetadata
{
    internal static METAPARAM Read(byte[] bytes) => new Layout(bytes).Material;

    /// <summary>Marks the family as writing the emission target, which the host otherwise leaves unbound for it.</summary>
    internal static byte[] EnableEmission(byte[] metadata)
    {
        _ = new Layout(metadata);
        var result = metadata.ToArray();
        // Bit 11 of the family's identity word selects emission writes in the checked revision.
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x58), BinaryPrimitives.ReadUInt32LittleEndian(metadata.AsSpan(0x58)) | 0x800);
        return result;
    }

    internal static byte[] AppendTextures(byte[] template, byte[] donor, params int[] slots)
    {
        var before = new Layout(template); var extra = new Layout(donor);
        var selected = slots.Order().ToArray();
        if (selected.Length == 0 || selected.Distinct().Count() != selected.Length
            || !selected.SequenceEqual(Enumerable.Range(before.Material.Textures.Count, selected.Length))
            || selected[^1] >= 32)
            throw new InvalidDataException("Added shader texture slots must extend the contiguous material table");
        var additions = selected.Select(slot => extra.Material.Textures.SingleOrDefault(t => t.Slot == slot)
            ?? throw new InvalidDataException($"Donor shader lacks texture slot {slot}")).ToArray();
        if (before.Material.Textures.Select(t => t.Name).Intersect(additions.Select(t => t.Name), StringComparer.Ordinal).Any())
            throw new InvalidDataException("Added shader texture names conflict with the material table");

        // Insert records without reserializing opaque host fields; relocate the format's absolute offsets.
        int insertion = before.Passes, shift = selected.Length * 48;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, true);
        writer.Write(template.AsSpan(0, insertion)); writer.Write(new byte[shift]); writer.Write(template.AsSpan(insertion));
        int Move(int at) => at >= insertion ? at + shift : at;
        void Long(int at, long value) { stream.Position = at; writer.Write(value); }
        void Int(int at, int value) { stream.Position = at; writer.Write(value); }
        foreach (int field in before.Pointers)
        {
            long value = before.Long(field);
            Long(Move(field), value == 0 ? 0 : Move((int)value));
        }
        Int(12, before.Material.Textures.Count + selected.Length);
        for (int i = 0; i < additions.Length; i++)
        {
            int from = 152 + extra.Material.Textures.IndexOf(additions[i]) * 48, at = insertion + 48 * i;
            stream.Position = at; writer.Write(donor.AsSpan(from, 48));
            foreach (int field in new[] { 0, 16, 24 })
            {
                long end = stream.Length;
                Long(at + field, end); stream.Position = end;
                writer.Write(Encoding.Unicode.GetBytes(extra.String(from + field) + '\0'));
            }
        }
        int mask = selected.Aggregate(0, (value, slot) => value | (1 << slot));
        for (int i = 0; i < before.Material.RenderPasses.Entries.Count; i++)
        {
            int previous = before.Material.RenderPasses.Entries[i].SamplerMask;
            if (previous != 0) Int(Move((int)before.Long(before.Passes)) + 24 * i + 8, previous | mask);
        }
        var result = stream.ToArray();
        _ = new Layout(result);
        return result;
    }

    private sealed class Layout
    {
        private readonly byte[] bytes;
        internal readonly METAPARAM Material;
        internal readonly List<int> Pointers = [16, 24, 32];
        internal readonly int Passes;
        private readonly List<(int Start, int End)> tables = [];

        internal Layout(byte[] bytes)
        {
            this.bytes = bytes;
            // lazy: only the checked version-six material declarations; later revisions need new readback fixtures.
            if (bytes.Length < 192 || !bytes.AsSpan().StartsWith("SMD\0"u8) || Int(8) != 6)
                throw new InvalidDataException("Unsupported shader material metadata revision");
            int count = Int(12);
            if (count is < 0 or > 32) throw new InvalidDataException("Unsupported shader texture table size");
            Passes = 152 + count * 48;
            Table(0, 1, Passes + 40);
            Table(Long(Passes), Int(Passes + 8), 24);
            Table(Long(24), Int(40), 96); Table(Long(32), Int(44), 80);
            if (Long(16) != Long(24)) throw new InvalidDataException("Unsupported shader metadata section reference");
            Pointers.Add(Passes);
            for (int i = 0; i < count; i++)
            {
                if (bytes.AsSpan(152 + 48 * i + 32, 16).IndexOfAnyExcept((byte)0) >= 0)
                    throw new InvalidDataException("Unclassified shader texture extension");
                foreach (int field in new[] { 0, 16, 24 }) Pointers.Add(152 + 48 * i + field);
            }
            for (int i = 0; i < Int(40); i++) Pointers.Add((int)Long(24) + 96 * i);
            for (int i = 0; i < Int(44); i++) Pointers.Add((int)Long(32) + 80 * i);
            foreach (int field in Pointers.Skip(4)) _ = String(field);
            Material = METAPARAM.Read(bytes);
            if (Material.Textures.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() != count
                || Material.Textures.Any(t => string.IsNullOrWhiteSpace(t.Name))
                || !Material.Textures.Select(t => (int)t.Slot).Order().SequenceEqual(Enumerable.Range(0, count)))
                throw new InvalidDataException("Ambiguous or noncontiguous shader texture table");
            uint allowed = count == 32 ? uint.MaxValue : (1u << count) - 1;
            if (Material.RenderPasses.Entries.Select(p => p.PassId).Distinct().Count() != Material.RenderPasses.Entries.Count
                || Material.RenderPasses.Entries.Any(p => ((uint)(p.SamplerMask | p.VertexSamplerMask | p.TessellationSamplerMask) & ~allowed) != 0))
                throw new InvalidDataException("Ambiguous shader pass or undeclared texture binding");
        }

        private void Table(long at, int count, int size)
        {
            if (at < 0 || at > bytes.Length || count < 0 || count > (bytes.Length - at) / size)
                throw new InvalidDataException("Shader metadata table is outside the input");
            int start = (int)at, end = start + count * size;
            if (count == 0) return;
            if (tables.Any(t => start < t.End && t.Start < end)) throw new InvalidDataException("Shader metadata tables overlap");
            tables.Add((start, end));
        }

        internal string String(int field)
        {
            long value = Long(field);
            if (value < 0 || value > bytes.Length - 2 || value % 2 != 0)
                throw new InvalidDataException("Shader metadata string is outside the input");
            int at = (int)value, end = at;
            while (end <= bytes.Length - 2 && (bytes[end] != 0 || bytes[end + 1] != 0)) end += 2;
            if (end > bytes.Length - 2 || tables.Any(t => at < t.End && t.Start < end + 2))
                throw new InvalidDataException("Unterminated or overlapping shader metadata string");
            try { return new UnicodeEncoding(false, false, true).GetString(bytes, at, end - at); }
            catch (DecoderFallbackException e) { throw new InvalidDataException("Invalid shader metadata string", e); }
        }

        private int Int(int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at, 4));
        internal long Long(int at) => BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(at, 8));
    }
}
