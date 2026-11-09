using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace AgeOfGold.Shaders;

internal static class SourceShaderBindings
{
    internal sealed record Binding(string Name, uint Kind, uint Slot)
    {
        internal char Register => Kind switch { 0 => 'b', 3 => 's', _ => 't' };
    }

    internal static Binding[] Read(byte[] program)
    {
        SourceShaderConverter.Inspect(program);
        uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(program.AsSpan(at, 4));
        var definitions = new List<(int Start, int Size)>();
        for (int i = 0; i < Word(28); i++)
        {
            int at = checked((int)Word(32 + i * 4));
            if (program.AsSpan(at, 4).SequenceEqual("RDEF"u8))
                definitions.Add((at + 8, checked((int)Word(at + 4))));
        }
        if (definitions.Count != 1) throw new InvalidDataException("Expected one source resource definition");
        var (start, size) = definitions[0];
        if (size < 28) throw new InvalidDataException("Truncated source resource definition");
        uint count = Word(start + 8), table = Word(start + 12);
        // lazy: one-resource bindings from SM4/5; arrays need a separately checked declaration mapping.
        if (count > 1024 || table > size || count > (size - table) / 32)
            throw new InvalidDataException("Invalid source resource binding range");
        var bindings = new List<Binding>();
        for (int i = 0; i < count; i++)
        {
            int at = checked(start + (int)table + i * 32);
            uint offset = Word(at), kind = Word(at + 4), slot = Word(at + 20), length = Word(at + 24);
            if (offset >= size) throw new InvalidDataException("Invalid source resource name offset");
            int nameStart = checked(start + (int)offset);
            int end = Array.IndexOf(program, (byte)0, nameStart, size - (int)offset);
            if (end < 0 || end - nameStart > 256) throw new InvalidDataException("Invalid source resource name length");
            string name = Encoding.ASCII.GetString(program, nameStart, end - nameStart);
            if (!Regex.IsMatch(name, @"^(?:\$Globals|[A-Za-z_][A-Za-z_0-9]*)$"))
                throw new InvalidDataException("Unsupported source resource name");
            if (kind is not (0 or 2 or 3 or 5 or 7) || (name == "$Globals" && kind != 0))
                throw new InvalidDataException("Unsupported source resource binding kind");
            if (length != 1) throw new InvalidDataException("Source resource arrays are not supported");
            uint limit = kind switch { 0 => 14, 3 => 16, _ => 128 };
            if (slot >= limit) throw new InvalidDataException("Source resource slot exceeds its register range");
            bindings.Add(new(name, kind, slot));
        }
        if (bindings.Select(b => b.Name).Distinct(StringComparer.Ordinal).Count() != bindings.Count
            || bindings.Select(b => (b.Register, b.Slot)).Distinct().Count() != bindings.Count)
            throw new InvalidDataException("Ambiguous source resource binding");
        return bindings.ToArray();
    }

    internal static string Preserve(byte[] program, string source)
    {
        var bindings = Read(program);
        foreach (var binding in bindings)
        {
            if (binding.Name == "$Globals")
            {
                // The pinned converter leaves these declarations outside a named buffer.
                if (binding.Slot != 0) throw new InvalidDataException("Unsupported implicit globals buffer slot");
                continue;
            }
            string type = binding.Kind switch
            {
                0 => "cbuffer",
                2 => @"(?:Texture(?:1D|2D|3D|Cube)(?:MS|Array|MSArray)?|Buffer)(?:<[^<>\r\n;]+>)?",
                3 => "(?:SamplerState|SamplerComparisonState)",
                5 => @"StructuredBuffer<[^<>\r\n;]+>",
                7 => "ByteAddressBuffer",
                _ => throw new InvalidDataException("Unsupported source declaration kind")
            };
            string end = binding.Kind == 0 ? "" : ";";
            string pattern = $@"^(?<declaration>{type}\s+{Regex.Escape(binding.Name)})(?:\s*:\s*register\((?<register>[bst])(?<slot>\d+)\))?{end}\r?$";
            var matches = Regex.Matches(source, pattern, RegexOptions.Multiline);
            if (matches.Count != 1) throw new InvalidDataException("Missing or ambiguous converted resource declaration: " + binding.Name);
            var match = matches[0];
            if (match.Groups["register"].Success && (match.Groups["register"].Value != binding.Register.ToString()
                || !uint.TryParse(match.Groups["slot"].Value, out uint slot) || slot != binding.Slot))
                throw new InvalidDataException("Converted resource claims a different binding: " + binding.Name);
            // Explicit slots throughout prevent the compiler filling earlier gaps with later resources.
            string replacement = $"{match.Groups["declaration"].Value} : register({binding.Register}{binding.Slot}){end}";
            source = source[..match.Index] + replacement + source[(match.Index + match.Length)..];
        }
        return source;
    }
}
