using AgeOfGold.Params;
using AgeOfGold.Shaders;

internal static class DisplayShaderCheck
{
    // An authored tone-map stage of the host's general shape: scene scaled per channel plus bloom by one strength, a curve, display gamma and a grade.
    private const string Fixture = """
        cbuffer Screen : register(b4) { float4 Scale; };
        cbuffer Tone : register(b1) { float4 Bloom; float4 Exposure; float4 Gamma; };
        Texture2D Glow : register(t0); Texture2D Curve : register(t1); Texture2D Scene : register(t3); Texture3D Grade : register(t2);
        SamplerState Filter : register(s1);
        [RootSignature("CBV(b1),CBV(b4),DescriptorTable(SRV(t0,numDescriptors=4)),DescriptorTable(Sampler(s1))")]
        float4 Main(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target0 {
            float2 at = uv * Scale.xy;
            float3 colour = Scene.SampleLevel(Filter, at, 0).rgb * Exposure.rgb + Glow.SampleLevel(Filter, at, 0).rgb * Bloom.x;
            colour = max(colour, 0);
            float3 toned = float3(Curve.SampleLevel(Filter, float2(colour.r / (colour.r + 0.2), 0), 0).r,
                Curve.SampleLevel(Filter, float2(colour.g / (colour.g + 0.2), 0), 0).r, Curve.SampleLevel(Filter, float2(colour.b / (colour.b + 0.2), 0), 0).r);
            float3 encoded = pow(max(toned, 0), Gamma.z);
            return float4(Grade.Sample(Filter, encoded * 0.9375 + 0.03125).rgb, 1);
        }
        """;

    internal static void Run()
    {
        var compiler = new ShaderCompiler(Required("AOG_DXC"), Required("AOG_DXC_ASSEMBLER"));
        var original = compiler.Compile(Fixture, "Main", "ps_6_0");
        var module = compiler.Disassemble(original);
        var correction = new SourceAtmosphereReader.Correction([1.1f, 1f, 0.9f], [1.15f, 1.2f, 1.25f], 1.2f, 0);
        var rewritten = DisplayShader.DisplayLikeSource(module, 2.5f, correction);
        var output = compiler.Assemble(rewritten, original);
        if (!output.SequenceEqual(compiler.Assemble(DisplayShader.DisplayLikeSource(module, 2.5f, correction), original))) throw new Exception("Display rewrite does not repeat");
        var text = compiler.Disassemble(output);
        foreach (var scene in new[] { new[] { 0.02f, 0.1f, 0.3f }, new[] { 0.5f, 0.05f, 0.39f } })
        {
            var evaluator = new ShaderEvaluator
            {
                Input = (_, column) => column == 0 ? 0.25f : 0.75f,
                Constants = (register, index) => register == 4 ? [2, 2, 0, 0] : index switch { 0 => [0.5f, 0.6f, 0.7f, 0], 1 => [3, 0, 0, 0], _ => [0, 0, 1 / 2.2f, 0] },
                // Scene at register three; every other texture returns a value that would be noticed if it reached the output.
                Sample = (slot, _, _) => slot == 3 ? [scene[0], scene[1], scene[2], 1] : [0.9f, 0.8f, 0.7f, 1],
            };
            evaluator.Run(text);
            // The source shows exposure times the scene, clamped, through the display's gamma: no host scale, curve, bloom or grade.
            // Its tone correction then adjusts contrast about mid-grey, brightness and saturation on the displayed values.
            var shown = new float[3];
            for (int c = 0; c < 3; c++)
                shown[c] = ((MathF.Pow(Math.Clamp(scene[c] * 2.5f, 0, 1), 1 / 2.2f) - 0.5f) * correction.Contrast[c] + 0.5f) * correction.Brightness[c];
            float grey = 0.299f * shown[0] + 0.587f * shown[1] + 0.114f * shown[2];
            for (int c = 0; c < 3; c++)
            {
                float expected = Math.Clamp(grey + (shown[c] - grey) * correction.Saturation, 0, 1);
                if (MathF.Abs(evaluator.Outputs[(0, c)] - expected) > 2e-5f) throw new Exception($"Displayed channel {c} is {evaluator.Outputs[(0, c)]}, expected {expected}");
            }
            if (evaluator.Outputs[(0, 3)] != 1) throw new Exception("Display alpha changed");
        }
        Reject(() => DisplayShader.DisplayLikeSource(rewritten, 2.5f, correction), "already");
        Reject(() => DisplayShader.DisplayLikeSource(module, 0, correction), "exposure");
        Reject(() => DisplayShader.DisplayLikeSource(module, 2.5f, correction with { Hue = 10 }), "hue");
        var unexposed = compiler.Disassemble(compiler.Compile(Fixture.Replace("* Exposure.rgb +", "* Exposure.x +"), "Main", "ps_6_0"));
        Reject(() => DisplayShader.DisplayLikeSource(unexposed, 2.5f, correction), "scene");
        Console.WriteLine("PASS: the display stage shows exposed, clamped, gamma-encoded scene light; curve, bloom tint and grade no longer reach the output");
    }

    private static void Reject(Action action, string reason)
    {
        try { action(); }
        catch (InvalidDataException e) when (e.Message.Contains(reason, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new Exception("Expected display shader rejection: " + reason);
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new Exception("Set " + name);
}
