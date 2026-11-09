using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AgeOfGold.Shaders;

internal static class SourceShaderParameters
{
    internal static string Specialize(byte[] program, string source,
        IReadOnlyDictionary<(string Buffer, string Field), Array> values)
    {
        var bound = Bind(program, values);
        foreach (var buffer in SourceShaderConstants.Read(program))
        {
            string name = "aog_registers_" + buffer.Slot;
            int count = buffer.Bytes / 16;
            string shape = count == 1 ? "" : $@"\[{count}\]";
            string declaration = $@"[ \t]*float4 {name}{shape};\r?";
            string pattern = buffer.Name == "$Globals" ? "^" + declaration + "$"
                : $@"^cbuffer {Regex.Escape(buffer.Name)} : register\(b{buffer.Slot}\)\r?\n\{{\r?\n{declaration}\n\}};\r?$";
            var matches = Regex.Matches(source, pattern, RegexOptions.Multiline);
            if (matches.Count != 1)
                throw new InvalidDataException("Missing or ambiguous normalized parameter declaration: " + buffer.Name);
            var vectors = new string[count];
            for (int vector = 0; vector < count; vector++)
            {
                var components = new string[4];
                for (int component = 0; component < 4; component++)
                    components[component] = BinaryPrimitives.ReadUInt32LittleEndian(
                        bound[buffer.Slot].AsSpan(vector * 16 + component * 4)).ToString(CultureInfo.InvariantCulture) + "u";
                // Decimal floats would lose integer register bits, signed zero and some flag encodings.
                vectors[vector] = "asfloat(uint4(" + string.Join(", ", components) + "))";
            }
            string replacement = count == 1 ? $"static const float4 {name} = {vectors[0]};"
                : $"static const float4 {name}[{count}] = {{ {string.Join(", ", vectors)} }};";
            var match = matches[0];
            source = source[..match.Index] + replacement + source[(match.Index + match.Length)..];
        }
        return source;
    }

    internal static SortedDictionary<uint, byte[]> Bind(byte[] program,
        IReadOnlyDictionary<(string Buffer, string Field), Array> values)
    {
        var layout = SourceShaderConstants.Layout(program);
        var result = new SortedDictionary<uint, byte[]>();
        foreach (var buffer in layout.Buffers) result.Add(buffer.Slot, new byte[buffer.Bytes]);
        var active = layout.Parameters.Where(p => p.Used).ToArray();
        if (!active.Select(p => (p.Buffer, p.Name)).ToHashSet().SetEquals(values.Keys))
            throw new InvalidDataException("Source parameter values must name every active field and no others");
        foreach (var field in active)
        {
            var value = values[(field.Buffer, field.Name)];
            bool typed = field.Scalar switch {
                1 => value is bool[], 2 => value is int[], 3 => value is float[] floats && floats.All(float.IsFinite),
                19 => value is uint[], _ => false };
            int elements = Math.Max(1, field.Elements);
            if (!typed || value.Length != elements * field.Rows * field.Columns)
                throw new InvalidDataException($"Invalid source parameter type or shape: {field.Buffer}.{field.Name}");
            int vectors = field.Category == 3 ? field.Columns : field.Rows;
            int components = field.Category == 3 ? field.Rows : field.Columns;
            int stride = ((vectors - 1) * 16 + components * 4 + 15) / 16 * 16;
            // Callers supply matrices in logical row order; reflection chooses their stored vector order.
            for (int element = 0; element < elements; element++)
                for (int row = 0; row < field.Rows; row++)
                    for (int column = 0; column < field.Columns; column++)
                    {
                        int index = (element * field.Rows + row) * field.Columns + column;
                        uint bits = value switch {
                            bool[] booleans => booleans[index] ? 1u : 0u,
                            int[] integers => unchecked((uint)integers[index]),
                            uint[] integers => integers[index],
                            float[] floats => BitConverter.SingleToUInt32Bits(floats[index]),
                            _ => throw new InvalidOperationException("Validated parameter type changed") };
                        int at = field.Offset + element * stride + (field.Category == 3 ? column * 16 + row * 4 : row * 16 + column * 4);
                        BinaryPrimitives.WriteUInt32LittleEndian(result[field.Slot].AsSpan(at), bits);
                    }
        }
        return result;
    }
}
