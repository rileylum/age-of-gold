using System.Buffers.Binary;
using System.Text;
using AgeOfGold.Shaders;

internal static class SourceShaderBindingsCheck
{
    internal static void Run()
    {
        var resources = Program(("nearTexture", 2, 0, 1), ("farTexture", 2, 4, 1), ("sampling", 3, 6, 1));
        const string source = "Texture2D nearTexture;\nTexture2D farTexture : register(t4);\nSamplerState sampling;\n";
        string fixedSource = SourceShaderBindings.Preserve(resources, source);
        if (!fixedSource.Contains("nearTexture : register(t0);") || !fixedSource.Contains("sampling : register(s6);"))
            throw new Exception("Explicit source register placement missing");
        if (SourceShaderBindings.Preserve(resources, fixedSource) != fixedSource)
            throw new Exception("Binding preservation does not repeat");
        Reject(() => SourceShaderBindings.Preserve(resources, source.Replace("t4", "t5")), "different binding");
        Reject(() => SourceShaderBindings.Preserve(resources, source + "Texture2D nearTexture;\n"), "ambiguous");
        Reject(() => SourceShaderBindings.Preserve(resources, source.Replace("SamplerState sampling;", "")), "declaration");
        Reject(() => SourceShaderBindings.Read(Program(("data", 2, 4, 2))), "arrays");
        Reject(() => SourceShaderBindings.Read(Program(("data[0]", 2, 4, 1))), "name");
        Reject(() => SourceShaderBindings.Read(Program(("data", 2, 128, 1))), "range");
        Reject(() => SourceShaderBindings.Read(Program(("sampling", 3, 16, 1))), "range");
        Reject(() => SourceShaderBindings.Read(Program(("same", 2, 1, 1), ("other", 2, 1, 1))), "ambiguous");
        Reject(() => SourceShaderBindings.Read(Program(("same", 2, 1, 1), ("same", 3, 1, 1))), "ambiguous");
        Reject(() => SourceShaderBindings.Read(Program(("data", 4, 1, 1))), "unsupported");
        Reject(() => SourceShaderBindings.Preserve(Program(("$Globals", 0, 3, 1)), "float amount;\n"), "globals");
        var invalid = resources.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(80), uint.MaxValue);
        Reject(() => SourceShaderBindings.Read(invalid), "range");
        invalid = resources.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(96), uint.MaxValue);
        Reject(() => SourceShaderBindings.Read(invalid), "offset");
        invalid = resources.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(48), 0x51);
        Reject(() => SourceShaderBindings.Read(invalid), "version");
        Console.WriteLine("PASS: source bindings keep sparse slots and reject ambiguous, unsupported and malformed declarations");
    }

    private static byte[] Program(params (string Name, uint Kind, uint Slot, uint Count)[] bindings)
    {
        var shader = SourceShaderCheck.Program(0x50, 0x0100003e);
        using var reflection = new MemoryStream();
        using var writer = new BinaryWriter(reflection);
        writer.Write(0); writer.Write(0); writer.Write(bindings.Length); writer.Write(28);
        writer.Write(new byte[12]);
        int offset = 28 + bindings.Length * 32;
        foreach (var binding in bindings)
        {
            writer.Write(offset); writer.Write(binding.Kind); writer.Write(new byte[12]);
            writer.Write(binding.Slot); writer.Write(binding.Count); writer.Write(0);
            offset += Encoding.ASCII.GetByteCount(binding.Name) + 1;
        }
        foreach (var binding in bindings) { writer.Write(Encoding.ASCII.GetBytes(binding.Name)); writer.Write((byte)0); }
        using var container = new MemoryStream();
        using var outer = new BinaryWriter(container);
        outer.Write("DXBC"u8); outer.Write(new byte[16]); outer.Write(1);
        outer.Write(40 + shader.Length - 36 + 8 + (int)reflection.Length);
        outer.Write(2); outer.Write(40); outer.Write(40 + shader.Length - 36);
        outer.Write(shader.AsSpan(36)); outer.Write("RDEF"u8); outer.Write((int)reflection.Length);
        outer.Write(reflection.ToArray());
        return container.ToArray();
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException e) when (e.Message.Contains(message, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new Exception("Source binding accepted or misdiagnosed: " + message);
    }
}
