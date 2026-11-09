using AgeOfGold.Shaders;
using AgeOfGold;

internal static class VertexShaderCheck
{
    private const string Fixture = """
        cbuffer Settings : register(b0) { uint Mode; float3 Unused; };
        struct Input { uint id : SV_VertexID; uint4 tangent : TANGENT0; };
        struct Output { float4 position : SV_Position; float4 tangent : TANGENT0; };
        float4 Transform(uint4 packed) {
            float4 v = float4(packed) / 64 - 2;
            float3 r = float3(dot(v.xyz, float3(.5, 2, -1)),
                              dot(v.xyz, float3(-2, .25, 1.5)),
                              dot(v.xyz, float3(1, -3, .75)));
            [branch] if (Mode == 0) return float4(r, v.w);
            return float4(r * 2 + v.xyz, -v.w);
        }
        [RootSignature("RootFlags(ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT),RootConstants(num32BitConstants=4,b0)")]
        Output Main(Input i) {
            Output o;
            float2 uv = float2((i.id << 1) & 2, i.id & 2);
            o.position = float4(uv * float2(2,-2) + float2(-1,1), 0, 1);
            o.tangent = Transform(i.tangent);
            return o;
        }
        """;

    internal static void Run()
    {
        var compiler = Compiler();
        var original = compiler.Compile(Fixture, "Main", "vs_6_0");
        var module = compiler.Disassemble(original);
        var edited = VertexShader.AddSecondaryTangent(module);
        var output = compiler.Assemble(edited, original);
        if (!output.SequenceEqual(compiler.Assemble(VertexShader.AddSecondaryTangent(module), original)))
            throw new Exception("Secondary tangent shader depends on build order");
        var readback = compiler.Disassemble(output);
        if (!System.Text.RegularExpressions.Regex.IsMatch(readback, @"(?m)^; TANGENT\s+2\s+"))
            throw new Exception("Secondary tangent missing from host signature");
        // Renumber metadata independently of instruction numbering; identities are not a shader contract.
        string renumbered = System.Text.RegularExpressions.Regex.Replace(module, @"!(\d+)",
            m => "!" + (int.Parse(m.Groups[1].Value) + 103));
        var shifted = compiler.Assemble(VertexShader.AddSecondaryTangent(renumbered), original);
        if (!output.SequenceEqual(shifted)) throw new Exception("Metadata numbering changed the rewrite");
        Reject(() => VertexShader.AddSecondaryTangent(edited), "already exists");
        Reject(() => VertexShader.AddSecondaryTangent(module.Replace("!\"TANGENT\"", "!\"MISSING\"")), "semantic");
        Reject(() => VertexShader.AddSecondaryTangent(module.Replace("!\"vs\", i32 6, i32 0", "!\"vs\", i32 6, i32 1")), "6.0");
        var branch = compiler.Compile(Fixture.Replace("Mode == 0", "packed.x == 0"), "Main", "vs_6_0");
        Reject(() => VertexShader.AddSecondaryTangent(compiler.Disassemble(branch)), "control flow");
        var unsupported = compiler.Compile(Fixture.Replace("return float4(r, v.w);", "return float4(sin(r), v.w);"), "Main", "vs_6_0");
        Reject(() => VertexShader.AddSecondaryTangent(compiler.Disassemble(unsupported)), "Unsupported tangent transform");
        var encoding = compiler.Compile(Fixture.Replace("uint4", "float4"), "Main", "vs_6_0");
        Reject(() => VertexShader.AddSecondaryTangent(compiler.Disassemble(encoding)), "encoding");
        Console.WriteLine("PASS: semantic tangent rewriting repeats, ignores metadata numbering and rejects duplicate/missing/unsupported or tangent-dependent flow");
    }

    internal static void Fixtures(string directory)
    {
        if (Directory.Exists(directory)) throw new ArgumentException("Fresh shader fixture output required");
        if (!Install.Within(Install.Canonical(directory), Install.Canonical(Path.GetTempPath())))
            throw new ArgumentException("Shader fixtures must be under system temp");
        Directory.CreateDirectory(directory);
        var compiler = Compiler();
        var original = compiler.Compile(Fixture, "Main", "vs_6_0");
        File.WriteAllBytes(Path.Combine(directory, "input.dxil"), original);
        File.WriteAllText(Path.Combine(directory, "input.ll"), compiler.Disassemble(original));
        File.WriteAllText(Path.Combine(directory, "translated.ll"), VertexShader.AddSecondaryTangent(compiler.Disassemble(original)));
        File.WriteAllBytes(Path.Combine(directory, "translated.dxil"), compiler.Assemble(
            VertexShader.AddSecondaryTangent(compiler.Disassemble(original)), original));
        var reference = Fixture.Replace("uint4 tangent : TANGENT0;", "uint4 tangent : TANGENT0; uint4 second : TANGENT2;")
            .Replace("float4 tangent : TANGENT0;", "float4 tangent : TANGENT0; float4 second : TANGENT2;")
            .Replace("o.tangent = Transform(i.tangent);", "o.tangent = Transform(i.tangent); o.second = Transform(i.second);");
        File.WriteAllBytes(Path.Combine(directory, "reference.dxil"), compiler.Compile(reference, "Main", "vs_6_0"));
        const string pixel = """
            struct Input { float4 position : SV_Position; float4 first : TANGENT0; float4 second : TANGENT2; };
            [RootSignature("RootFlags(ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT),RootConstants(num32BitConstants=4,b0)")]
            float4 Main(Input i) : SV_Target0 { return i.second; }
            """;
        File.WriteAllBytes(Path.Combine(directory, "pixel.dxil"), compiler.Compile(pixel, "Main", "ps_6_0"));
    }

    private static ShaderCompiler Compiler() => new(
        Environment.GetEnvironmentVariable("AOG_DXC") ?? throw new Exception("Set AOG_DXC"),
        Environment.GetEnvironmentVariable("AOG_DXC_ASSEMBLER") ?? throw new Exception("Set AOG_DXC_ASSEMBLER"));

    private static void Reject(Action action, string reason)
    {
        try { action(); }
        catch (InvalidDataException e) when (e.Message.Contains(reason, StringComparison.Ordinal)) { return; }
        throw new Exception("Expected shader rejection: " + reason);
    }
}
