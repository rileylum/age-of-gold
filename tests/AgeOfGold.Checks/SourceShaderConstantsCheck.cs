using System.Buffers.Binary;
using System.Text;
using AgeOfGold.Shaders;

internal static class SourceShaderConstantsCheck
{
    internal static void Run()
    {
        var (input, locations) = Fixture();
        var first = SourceShaderConstants.Normalize(input);
        if (!first.SequenceEqual(SourceShaderConstants.Normalize(input))) throw new Exception("Constant normalization differs across runs");
        foreach (var bytes in new[] { input, first })
        {
            var buffer = SourceShaderConstants.Read(bytes).Single();
            if (buffer != new SourceShaderConstants.Buffer("Authored", 3, 64)) throw new Exception("Constant binding or byte extent changed");
        }
        if (!Instructions(input).SequenceEqual(Instructions(first))) throw new Exception("Constant normalization altered executable instructions");
        Reject("size", 0, "extent");
        Reject("variableTable", uint.MaxValue, "range");
        Reject("offset", 64, "exceeds");
        Reject("offset", 2, "exceeds");
        Reject("secondOffset", 0, "overlap");
        Reject("type", uint.MaxValue, "range");
        Reject("typeClass", 5u | (3u << 16), "type");
        Reject("typeClass", 1u | (39u << 16), "type");
        Reject("elements", 500, "extent");
        foreach (string value in new[] { "aog_registers_0.x", "aog_registers_2[8].w", "aog_registers_3[r0.x + 2].y" })
        {
            string source = "\tswitch (" + value + ") {\r\n";
            string rewritten = SourceShaderConstants.PreserveIntegerSelectors(source);
            if (rewritten != "\tswitch (asuint(" + value + ")) {\r\n"
                || SourceShaderConstants.PreserveIntegerSelectors(rewritten) != rewritten)
                throw new Exception("Normalized constant switch does not preserve integer bits");
        }
        Console.WriteLine("PASS: constant register extents and executable instructions survive normalization; malformed layouts are rejected");

        void Reject(string key, uint value, string diagnostic)
        {
            var damaged = input.ToArray();
            if (key == "elements") BinaryPrimitives.WriteUInt16LittleEndian(damaged.AsSpan(locations[key]), (ushort)value);
            else BinaryPrimitives.WriteUInt32LittleEndian(damaged.AsSpan(locations[key]), value);
            try { SourceShaderConstants.Normalize(damaged); }
            catch (InvalidDataException e) when (e.Message.Contains(diagnostic, StringComparison.OrdinalIgnoreCase)) { return; }
            throw new Exception("Constant layout accepted or misdiagnosed: " + key);
        }
    }

    private static byte[] Instructions(byte[] program)
    {
        for (int i = 0; i < BitConverter.ToInt32(program, 28); i++)
        {
            int at = BitConverter.ToInt32(program, 32 + i * 4);
            if (program.AsSpan(at, 4).SequenceEqual("SHEX"u8) || program.AsSpan(at, 4).SequenceEqual("SHDR"u8))
                return program.AsSpan(at + 8, BitConverter.ToInt32(program, at + 4)).ToArray();
        }
        throw new Exception("Authored instruction stream missing");
    }

    internal static byte[] WithUnusedPrefix(byte[] program)
    {
        int U32(byte[] bytes, int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at, 4));
        var parts = new List<byte[]>();
        for (int i = 0; i < U32(program, 28); i++)
        {
            int at = U32(program, 32 + 4 * i), length = U32(program, at + 4);
            if (!program.AsSpan(at, 4).SequenceEqual("RDEF"u8))
            {
                parts.Add(program.AsSpan(at, 8 + length).ToArray());
                continue;
            }
            var data = program.AsSpan(at + 8, length).ToArray();
            int buffer = U32(data, 4);
            if (U32(data, 0) != 1 || U32(data, buffer + 4) != 1) throw new Exception("Authored matrix buffer inventory changed");
            var variable = data.AsSpan(U32(data, buffer + 8), 40).ToArray();
            var type = data.AsSpan(U32(variable, 16), 36).ToArray();
            if (!type.AsSpan(0, 12).SequenceEqual(new byte[] { 2, 0, 3, 0, 2, 0, 3, 0, 2, 0, 0, 0 }))
                throw new Exception("Authored matrix array type changed");
            type[8] = 0;
            using var reflection = new MemoryStream();
            reflection.Write(data);
            using var w = new BinaryWriter(reflection);
            int name = (int)reflection.Position; w.Write(Encoding.ASCII.GetBytes("unusedPrefix\0"));
            while (reflection.Position % 4 != 0) w.Write((byte)0);
            int typeOffset = (int)reflection.Position; w.Write(type);
            int variables = (int)reflection.Position; w.Write(variable);
            BinaryPrimitives.WriteInt32LittleEndian(variable, name);
            BinaryPrimitives.WriteInt32LittleEndian(variable.AsSpan(8), 28);
            BinaryPrimitives.WriteInt32LittleEndian(variable.AsSpan(12), 0);
            BinaryPrimitives.WriteInt32LittleEndian(variable.AsSpan(16), typeOffset);
            w.Write(variable);
            var rewritten = reflection.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(rewritten.AsSpan(buffer + 4), 2);
            BinaryPrimitives.WriteInt32LittleEndian(rewritten.AsSpan(buffer + 8), variables);
            using var chunk = new MemoryStream();
            using var c = new BinaryWriter(chunk);
            c.Write("RDEF"u8); c.Write(rewritten.Length); c.Write(rewritten); parts.Add(chunk.ToArray());
        }
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write("DXBC"u8); writer.Write(new byte[16]); writer.Write(1);
        int offset = 32 + 4 * parts.Count;
        writer.Write(offset + parts.Sum(p => p.Length)); writer.Write(parts.Count);
        foreach (var p in parts) { writer.Write(offset); offset += p.Length; }
        foreach (var p in parts) writer.Write(p);
        var result = output.ToArray();
        if (!Instructions(program).SequenceEqual(Instructions(result))) throw new Exception("Authored alias changed instructions");
        return result;
    }

    private static (byte[], Dictionary<string, int>) Fixture()
    {
        using var reflection = new MemoryStream();
        using var w = new BinaryWriter(reflection);
        w.Write(1); w.Write(28); w.Write(1); w.Write(52); w.Write(0xffff0500u); w.Write(0); w.Write(0);
        w.Write(0); w.Write(2); w.Write(84); w.Write(64); w.Write(0); w.Write(0);
        w.Write(0); w.Write(0); w.Write(new byte[12]); w.Write(3); w.Write(1); w.Write(0);
        // Two scalar fields with an intentionally large gap, followed by one primitive type.
        foreach (int offset in new[] { 0, 48 })
        {
            w.Write(0); w.Write(offset); w.Write(4); w.Write(2); w.Write(164); w.Write(0);
            w.Write(uint.MaxValue); w.Write(0); w.Write(uint.MaxValue); w.Write(0);
        }
        foreach (ushort field in new ushort[] { 0, 3, 1, 1, 0, 0 }) w.Write(field);
        w.Write(new byte[24]);
        int AddName(string name) { int at = (int)reflection.Position; w.Write(Encoding.ASCII.GetBytes(name)); w.Write((byte)0); return at; }
        int buffer = AddName("Authored"), first = AddName("first"), second = AddName("last");
        var data = reflection.ToArray();
        foreach (var (at, name) in new[] { (28, buffer), (52, buffer), (84, first), (124, second) })
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at), name);
        var instructions = SourceShaderCheck.Program(0x50, 0x0100003e);
        using var container = new MemoryStream();
        using var c = new BinaryWriter(container);
        c.Write("DXBC"u8); c.Write(new byte[16]); c.Write(1); c.Write(68 + data.Length);
        c.Write(2); c.Write(40); c.Write(60); c.Write(instructions.AsSpan(36)); c.Write("RDEF"u8); c.Write(data.Length); c.Write(data);
        return (container.ToArray(), new Dictionary<string, int> {
            ["size"] = 68 + 40, ["variableTable"] = 68 + 36, ["offset"] = 68 + 88,
            ["secondOffset"] = 68 + 128, ["type"] = 68 + 100, ["typeClass"] = 68 + 164, ["elements"] = 68 + 172 });
    }
}
