using System.Buffers.Binary;
using System.Diagnostics;
using AgeOfGold.Shaders;

internal static class ShaderCheck
{
    internal static void Run()
    {
        var compiler = new ShaderCompiler(Environment.GetEnvironmentVariable("AOG_DXC") ?? throw new Exception("Set AOG_DXC"),
            Environment.GetEnvironmentVariable("AOG_DXC_ASSEMBLER") ?? throw new Exception("Set AOG_DXC_ASSEMBLER"));
        const string source = """
            Texture2D<float4> Colour : register(t2);
            SamplerState Repeat : register(s1);
            cbuffer Lighting : register(b3) { float4 Tint; };
            [RootSignature("DescriptorTable(SRV(t2)),DescriptorTable(Sampler(s1)),CBV(b3)")]
            float4 Main(float2 uv : TEXCOORD0) : SV_Target0 {
                return Colour.Sample(Repeat, uv) * Tint;
            }
            """;
        var first = compiler.Compile(source, "Main", "ps_6_0");
        var second = compiler.Compile(source, "Main", "ps_6_0");
        if (!first.SequenceEqual(second)) throw new Exception("Shader compilation depends on temporary directory");
        var module = compiler.Disassemble(first);
        var rebuilt = compiler.Assemble(module, first);
        if (!rebuilt.SequenceEqual(compiler.Assemble(module, first))) throw new Exception("Shader assembly is not deterministic");
        foreach (var part in new[] { "RTS0", "ISG1", "OSG1", "PSV0" })
            if (!Part(first, part).SequenceEqual(Part(rebuilt, part))) throw new Exception("Shader binding contract changed: " + part);
        var actual = compiler.Disassemble(rebuilt);
        foreach (var binding in new[] { "t2", "s1", "cb3", "sample.f32", "fmul" })
            if (!actual.Contains(binding)) throw new Exception("Rebuilt shader lost " + binding);
        try
        {
            compiler.Compile("this is not shader source", "Main", "ps_6_0");
            throw new Exception("Invalid shader source was accepted");
        }
        catch (InvalidDataException) { }
        var directory = Directory.CreateTempSubdirectory("aog-shader-output-").FullName;
        try
        {
            var input = Path.Combine(directory, "source.ll");
            var template = Path.Combine(directory, "template.dxil");
            var output = Path.Combine(directory, "existing.dxil");
            File.WriteAllText(input, module);
            File.WriteAllBytes(template, first);
            File.WriteAllText(output, "preserve previous output");
            // A write-only file defeats a readability check; fresh output must be enforced on creation.
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(output, UnixFileMode.UserWrite);
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("AOG_DXC_ASSEMBLER")!)
                { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { input, output, template, "--refresh" }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(output, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            if (process.ExitCode == 0 || File.ReadAllText(output) != "preserve previous output")
                throw new Exception("Shader assembler overwrote existing output: " + stdout.Result + stderr.Result);
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("PASS: authored shader compiles and reassembles deterministically, retains root and resource bindings, and rejects invalid source");
    }

    private static byte[] Part(byte[] shader, string tag)
    {
        int Word(int at) => BinaryPrimitives.ReadInt32LittleEndian(shader.AsSpan(at, 4));
        for (int i = 0; i < Word(28); i++)
        {
            int offset = Word(32 + i * 4);
            if (System.Text.Encoding.ASCII.GetString(shader, offset, 4) == tag)
                return shader[(offset + 8)..(offset + 8 + Word(offset + 4))];
        }
        throw new Exception("Missing authored container part " + tag);
    }
}
