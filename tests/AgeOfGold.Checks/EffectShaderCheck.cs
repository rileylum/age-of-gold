using AgeOfGold.Shaders;

internal static class EffectShaderCheck
{
    // An authored particle stage of the host's general shape: two frames of one image blended by a fraction, an alpha
    // bias, the particle colour split into direction and strength, and a screen position whose last value is view depth.
    private const string Fixture = """
        cbuffer PSCommonBuffer : register(b0) { float4 Rows[8]; };
        Texture2D g_diffuseTexture : register(t0); Texture3D g_volumetricFog : register(t16);
        SamplerState g_WrapLinearSampler : register(s8);
        struct Input { float4 colour : COLOR0; float3 first : TEXCOORD0; float3 second : TEXCOORD1; float2 bias : TEXCOORD3; float4 screen : TEXCOORD10; };
        struct Output { float4 scene : SV_Target0; float4 mask : SV_Target1; };
        [RootSignature("CBV(b0),DescriptorTable(SRV(t0)),DescriptorTable(SRV(t16)),DescriptorTable(Sampler(s8))")]
        Output Main(Input i) {
            float4 a = g_diffuseTexture.Sample(g_WrapLinearSampler, i.first.xy), b = g_diffuseTexture.Sample(g_WrapLinearSampler, i.second.xy);
            float4 texel = lerp(a, b, i.second.z);
            float alpha = saturate(texel.a + i.bias.x);
            float strength = length(i.colour.rgb);
            float3 direction = strength <= 0.00001 ? 0 : i.colour.rgb / strength;
            float3 colour = texel.rgb * direction;
            colour = colour * colour * Rows[7].rgb * strength;
            float2 at = i.screen.xy / i.screen.w * 0.5 + 0.5;
            float haze = g_volumetricFog.Sample(g_WrapLinearSampler, float3(at, i.screen.w * Rows[3].x)).r;
            Output o;
            o.scene = float4(colour * haze, alpha * i.colour.a * Rows[7].a);
            o.mask = float4(0, 0, 0, o.scene.a);
            return o;
        }
        """;

    internal static void Run()
    {
        var compiler = new ShaderCompiler(Required("AOG_DXC"), Required("AOG_DXC_ASSEMBLER"));
        var original = compiler.Compile(Fixture, "Main", "ps_6_0");
        var module = compiler.Disassemble(original);
        var fog = new EffectShader.Fog(-150, 1 / 220f, 0.65f);
        const float marker = 32769;
        var rewritten = EffectShader.RespondLikeSource(module, marker, fog);
        var output = compiler.Assemble(rewritten, original);
        if (!output.SequenceEqual(compiler.Assemble(EffectShader.RespondLikeSource(module, marker, fog), original))) throw new Exception("Effect stage rewrite does not repeat");
        var text = compiler.Disassemble(output);
        int responded = 0;
        foreach (float bias in new[] { marker, -marker, 0.25f, 32770f })
            foreach (float depth in new[] { 4f, 120f })
            {
                float[] colour = [5, 2.8f, 1.25f, 0.8f], first = [0.3f, 0.6f, 0], second = [0.4f, 0.7f, 0.35f];
                Dictionary<(int, int), float> Evaluate(string program)
                {
                    var signatures = new ShaderSignatures(program, "ps");
                    int Id(string name, int index) => signatures.Find(false, name, index).Id;
                    var inputs = new Dictionary<int, float[]> { [Id("COLOR", 0)] = colour, [Id("TEXCOORD", 0)] = first, [Id("TEXCOORD", 1)] = second,
                        [Id("TEXCOORD", 3)] = [bias, 0], [Id("TEXCOORD", 10)] = [0.2f * depth, -0.1f * depth, 0, depth] };
                    var evaluator = new ShaderEvaluator
                    {
                        Input = (id, column) => inputs[id][column],
                        Constants = (_, index) => index == 7 ? [0.5f, 0.6f, 0.7f, 0.9f] : [0.01f, 0, 0, 0],
                        Sample = (slot, at, _) => slot == 16 ? [0.75f, 0, 0, 0] : Texel(at[0], at[1]),
                    };
                    evaluator.Run(program);
                    return evaluator.Outputs.ToDictionary(p => (signatures.Members(true).Single(m => m.Id == p.Key.Id).Index, p.Key.Column), p => p.Value);
                }
                var host = Evaluate(module); var actual = Evaluate(text);
                if (MathF.Abs(bias) != marker)
                {
                    // Every other particle keeps the host's own response exactly.
                    if (!host.OrderBy(p => p.Key).SequenceEqual(actual.OrderBy(p => p.Key))) throw new Exception("An unmarked particle changed");
                    continue;
                }
                responded++;
                var a = Texel(first[0], first[1]); var b = Texel(second[0], second[1]);
                var texel = a.Zip(b, (x, y) => x + (y - x) * second[2]).ToArray();
                // The source raises the image times the particle colour to its display power, and fades opacity with its fog.
                float opacity = Math.Clamp(Math.Clamp(texel[3], 0, 1) * colour[3] * (1 - Math.Clamp((depth - fog.Begin) * fog.Scale, 0, 1) * fog.Limit), 0, 1);
                for (int c = 0; c < 3; c++)
                {
                    float expected = MathF.Pow(texel[c] * colour[c], 2.2f);
                    if (MathF.Abs(actual[(0, c)] - expected) > 2e-5f * MathF.Max(1, expected)) throw new Exception($"Source particle colour {c} is {actual[(0, c)]}, expected {expected}");
                }
                if (MathF.Abs(actual[(0, 3)] - opacity) > 1e-6f || actual[(1, 3)] != actual[(0, 3)] || actual[(1, 0)] != 0) throw new Exception("Source particle opacity differs");
            }
        if (responded != 4) throw new Exception("Marked particles were not exercised");
        Reject(() => EffectShader.RespondLikeSource(rewritten, marker, fog), "already");
        Reject(() => EffectShader.RespondLikeSource(module, 0.5f, fog), "marker");
        Reject(() => EffectShader.RespondLikeSource(compiler.Disassemble(compiler.Compile(Fixture.Replace("lerp(a, b, i.second.z)", "a"), "Main", "ps_6_0")), marker, fog), "frames");
        Reject(() => EffectShader.RespondLikeSource(compiler.Disassemble(compiler.Compile(Fixture.Replace("g_diffuseTexture", "g_otherTexture"), "Main", "ps_6_0")), marker, fog), "image");
        Console.WriteLine("PASS: marked particles take the source's colour response and fogged opacity; other particles keep the host's; unsupported stages fail");
    }

    private static float[] Texel(float u, float v) => [0.2f + 0.5f * u, 0.9f - 0.4f * v, 0.1f + 0.3f * u * v, 0.5f + 0.4f * v - 0.2f * u];

    private static void Reject(Action action, string reason)
    {
        try { action(); }
        catch (InvalidDataException e) when (e.Message.Contains(reason, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new Exception("Expected effect stage rejection: " + reason);
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new Exception("Set " + name);
}
