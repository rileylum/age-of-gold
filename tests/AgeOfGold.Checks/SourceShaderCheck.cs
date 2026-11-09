using System.Buffers.Binary;
using System.Text.Json.Nodes;
using AgeOfGold.Shaders;

internal static class SourceShaderCheck
{
    internal static void Compile(string input, string output)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Fresh shader output required");
        var compiler = new ShaderCompiler(Required("AOG_DXC"), Required("AOG_DXC_ASSEMBLER"));
        var converter = new SourceShaderConverter(Required("AOG_SHADER_CONVERTER"), compiler);
        CheckTool(compiler);
        var results = new Dictionary<string, byte[]>();
        foreach (string stage in new[] { "pixel", "vertex", "resources", "constants", "packed", "matrices", "constants-sm4", "selector", "matrices-column", "values" })
        {
            var source = File.ReadAllBytes(Path.Combine(input, stage + ".dxbc"));
            if (stage == "matrices") source = SourceShaderConstantsCheck.WithUnusedPrefix(source);
            var first = converter.Convert(source);
            if (!first.SequenceEqual(converter.Convert(source))) throw new Exception("Converted shader differs across runs");
            ShaderArchive.ValidateProgram(first);
            if (!compiler.Disassemble(first).Contains("main")) throw new Exception("Converted shader has no entry point");
            results.Add(stage, first);
        }
        foreach (string stage in new[] { "compute", "writable" })
        {
            try { converter.Convert(File.ReadAllBytes(Path.Combine(input, stage + ".dxbc"))); }
            catch (InvalidDataException e) when (e.Message.Contains("vertex and pixel") || e.Message.Contains("writable")) { continue; }
            throw new Exception("Unsupported authored program was accepted");
        }
        Directory.CreateDirectory(output);
        foreach (var pair in results) File.WriteAllBytes(Path.Combine(output, pair.Key + ".dxil"), pair.Value);
        File.WriteAllText(Path.Combine(output, "tool-sha256.txt"), converter.Fingerprint + "\n");
        SourceShaderParametersCheck.Fixtures(input, Path.Combine(output, "parameters"), converter);
        foreach (string directory in Directory.GetDirectories(Path.Combine(output, "parameters")))
        {
            var specialized = File.ReadAllBytes(Path.Combine(directory, "specialized.dxil"));
            ShaderArchive.ValidateProgram(specialized);
            string instructions = compiler.Disassemble(specialized);
            if (instructions.Contains("cbufferLoad") || instructions.Contains("cbuffer "))
                throw new Exception("Specialized shader retained constant-buffer dependencies");
        }
        Console.WriteLine("PASS: authored vertex/pixel conversion repeats exactly; compute and writable graphics are rejected");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new Exception("Set " + name);

    private static void CheckTool(ShaderCompiler compiler)
    {
        var root = Directory.CreateTempSubdirectory("aog-shader-tool-check-").FullName;
        try
        {
            string tool = Required("AOG_SHADER_CONVERTER");
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(tool, "manifest.json")))!;
            var files = manifest["files"]!.AsObject();
            var required = new[] { OperatingSystem.IsWindows() ? "HlslDecompiler.exe" : "HlslDecompiler",
                "HlslDecompiler.dll", "HlslDecompiler.deps.json", "HlslDecompiler.runtimeconfig.json", "HlslDecompiler-LICENSE.txt" };
            foreach (var name in files.Select(p => p.Key).ToArray())
                if (!required.Contains(name)) files.Remove(name);
            foreach (string name in required) File.Copy(Path.Combine(tool, name), Path.Combine(root, name));
            string path = Path.Combine(root, "manifest.json");
            File.WriteAllText(path, manifest.ToJsonString());
            _ = new SourceShaderConverter(root, compiler);
            File.AppendAllText(Path.Combine(root, "HlslDecompiler.dll"), "authored corruption");
            RejectTool("fingerprint");
            manifest["revision"] = "authored unsupported revision";
            File.WriteAllText(path, manifest.ToJsonString());
            RejectTool("revision");

            void RejectTool(string diagnostic)
            {
                try { _ = new SourceShaderConverter(root, compiler); }
                catch (InvalidDataException e) when (e.Message.Contains(diagnostic)) { return; }
                throw new Exception("Accepted altered shader converter " + diagnostic);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    internal static void Run()
    {
        SourceShaderBindingsCheck.Run();
        SourceShaderConstantsCheck.Run();
        // Authored token streams use the public Direct3D token format, not a game's programs.
        Require(SourceShaderConverter.Inspect(Program(0x50, 0x0100003e)) == "ps_6_0", "pixel stage");
        Require(SourceShaderConverter.Inspect(Program(0x10050, 0x0100003e)) == "vs_6_0", "vertex stage");
        Require(SourceShaderConverter.Inspect(Program(0x50, 53 | (3u << 11), 3, 0, 0x0100003e)) == "ps_6_0",
            "immediate constants must not be mistaken for instructions");
        foreach (uint stage in new uint[] { 2, 3, 4, 5, 13, 14, 65535 })
            Reject(Program((stage << 16) | 0x50, 0x0100003e), "vertex and pixel");
        foreach (uint version in new uint[] { 0x30, 0x51, 0x60, 0x150 }) Reject(Program(version, 0x0100003e), "version");
        foreach (uint opcode in new uint[] { 155, 156, 157, 158, 159, 160, 163, 164, 166, 168, 169, 178, 189, 190 })
            Reject(Program(0x50, 0x01000000 | opcode), "writable");
        foreach (uint opcode in new uint[] { 107, 112, 142, 209, 218, 225, 2047 })
            Reject(Program(0x50, 0x01000000 | opcode), "opcode");
        foreach (uint kind in new uint[] { 4, 6, 8, 9, 10, 11, 13 })
            Reject(WithResource(Program(0x50, 0x0100003e), kind), "writable");
        Require(SourceShaderConverter.Inspect(WithResource(Program(0x50, 0x0100003e), 5)) == "ps_6_0",
            "read-only structured resource");
        Reject(Program(0x50, 62), "length");
        Reject(Program(0x50, 0x0200003e), "length");
        Reject(Program(0x50, 53), "length");
        Reject(Program(0x50, 53, uint.MaxValue), "length");
        Reject(Program(0x50, 53, 1), "length");
        Reject(Program(0x50, 53 | (127u << 11), 2), "custom");
        var badLength = Program(0x50, 0x0100003e);
        BinaryPrimitives.WriteUInt32LittleEndian(badLength.AsSpan(48), 99);
        Reject(badLength, "length");
        var dxil = Program(0x50, 0x0100003e);
        "DXIL"u8.CopyTo(dxil.AsSpan(36));
        Reject(dxil, "DXBC instructions");
        Console.WriteLine("PASS: source shader stages and instructions are classified; compute, writes and malformed streams are rejected");
    }

    internal static byte[] Program(uint version, params uint[] instructions)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("DXBC"u8); writer.Write(new byte[16]); writer.Write(1);
        writer.Write(52 + instructions.Length * 4); writer.Write(1); writer.Write(36);
        writer.Write("SHEX"u8); writer.Write(8 + instructions.Length * 4);
        writer.Write(version); writer.Write(instructions.Length + 2);
        foreach (uint token in instructions) writer.Write(token);
        return stream.ToArray();
    }

    private static byte[] WithResource(byte[] program, uint kind)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        int firstLength = program.Length - 36;
        writer.Write("DXBC"u8); writer.Write(new byte[16]); writer.Write(1);
        writer.Write(40 + firstLength + 72); writer.Write(2); writer.Write(40); writer.Write(40 + firstLength);
        writer.Write(program.AsSpan(36));
        writer.Write("RDEF"u8); writer.Write(64);
        writer.Write(0); writer.Write(0); writer.Write(1); writer.Write(32); writer.Write(new byte[16]);
        writer.Write(0); writer.Write(kind); writer.Write(new byte[24]);
        return stream.ToArray();
    }

    private static void Reject(byte[] bytes, string diagnostic)
    {
        try { SourceShaderConverter.Inspect(bytes); }
        catch (InvalidDataException e) when (e.Message.Contains(diagnostic, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new Exception("Source shader accepted or misdiagnosed: " + diagnostic);
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
