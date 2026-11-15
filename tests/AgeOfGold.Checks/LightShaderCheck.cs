using AgeOfGold.Shaders;

internal static class LightShaderCheck
{
    // An authored stage of the host's shape for one directional light: it reads the stored normal, metal and gloss and
    // colour, the light set the pixel names, and a screen's worth of shadow, and adds diffuse and specular light.
    private const string Sun = """
        cbuffer cbLight : register(b5) { float4 Lights[56]; };
        Texture2D g_ShadowTexture : register(t32); Texture2D g_GBuffer0Texture : register(t0);
        Texture2D g_GBuffer1Texture : register(t3); Texture2D g_GBuffer2Texture : register(t4);
        SamplerState Filter : register(s8);
        struct Lit { float4 diffuse : SV_Target0; float4 specular : SV_Target1; };
        [RootSignature("CBV(b5),DescriptorTable(SRV(t0,numDescriptors=5)),DescriptorTable(SRV(t32)),DescriptorTable(Sampler(s8))")]
        Lit Main(float4 position : SV_Position, float2 uv : TEXCOORD0) {
            int3 at = int3(position.xy, 0);
            float3 n = normalize(g_GBuffer0Texture.Load(at).xyz * 2 - 1);
            float4 surface = g_GBuffer1Texture.Load(at); float4 colour = g_GBuffer2Texture.Load(at);
            int set = (int)(colour.a * 255) * 14;
            float4 mask = g_ShadowTexture.Sample(Filter, uv);
            float shadow = min(mask.r, mask.g);
            float3 mirror = lerp(0.04, colour.rgb, surface.r);
            float towards = saturate(dot(n, Lights[set].xyz));
            float3 diffuse = Lights[set + 4].rgb * towards * shadow;
            if (surface.b > 0) diffuse *= 1 + surface.b * 10;
            Lit lit;
            lit.diffuse = float4(diffuse, 1);
            lit.specular = float4(Lights[set + 5].rgb * mirror * towards * (0.25 + surface.g) * shadow, 1);
            return lit;
        }
        """;

    // The host's stage that multiplies accumulated light by a surface's colour where it is not metal.
    private const string Combine = """
        Texture2D g_GBuffer1Texture : register(t3); Texture2D g_GBuffer2Texture : register(t4);
        SamplerState Filter : register(s7);
        [RootSignature("DescriptorTable(SRV(t3,numDescriptors=2)),DescriptorTable(Sampler(s7))")]
        float4 Main(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target0 {
            return float4(g_GBuffer2Texture.Sample(Filter, uv).rgb * (1 - g_GBuffer1Texture.Sample(Filter, uv).r), 1);
        }
        """;

    internal static void Run()
    {
        var compiler = new ShaderCompiler(Required("AOG_DXC"), Required("AOG_DXC_ASSEMBLER"));
        var sunProgram = compiler.Compile(Sun, "Main", "ps_6_0");
        string sun = compiler.Disassemble(sunProgram), shaded = LightShader.ShadeMarked(sun);
        var built = compiler.Assemble(shaded, sunProgram);
        if (!built.SequenceEqual(compiler.Assemble(LightShader.ShadeMarked(sun), sunProgram))) throw new Exception("The sun stage's rewrite does not repeat");
        string shadedText = compiler.Disassemble(built);
        var combineProgram = compiler.Compile(Combine, "Main", "ps_6_0");
        string combine = compiler.Disassemble(combineProgram), combinedText = compiler.Disassemble(compiler.Assemble(LightShader.CombineMarked(combine), combineProgram));

        // The host's stage for a sun that casts shadows also reads the mask's fourth value, the cast shadow, and mixes it
        // in by a strength of the scene's.
        string casting = Sun.Replace("float shadow = min(mask.r, mask.g);", "float shadow = min(mask.r, mask.g) * lerp(1, mask.a, Lights[55].x);");
        var castProgram = compiler.Compile(casting, "Main", "ps_6_0");
        string cast = compiler.Disassemble(castProgram), castText = compiler.Disassemble(compiler.Assemble(LightShader.ShadeMarked(cast), castProgram));
        float[] normal = [0.6f, 0.9f, 0.4f, 0], colour = [0.3f, 0.5f, 0.7f, 2 / 255f], mask = [0.6f, 0.35f, 0.9f, 0.5f];
        Dictionary<(int, int), float> Lit(string module, float[] surface, float sunStrength)
        {
            var evaluator = new ShaderEvaluator
            {
                Input = (_, column) => column == 0 ? 12.5f : 7.5f,
                // The pixel names light set two: its rows start at twenty-eight.
                Constants = (register, row) => register != 5 ? throw new Exception("Unexpected constants") : row == 28 ? [0.36f, 0.8f, -0.48f, 0] : row == 32 ? [0.9f * sunStrength, 0.8f * sunStrength, 0.7f * sunStrength, 0]
                    : row == 33 ? [0.5f, 0.6f, 0.4f, 0] : row == 55 ? [0.75f, 0, 0, 0] : throw new Exception("A row of another light set was read: " + row),
                Sample = (slot, _, _) => slot switch { 0 => normal, 3 => surface, 4 => colour, 32 => mask, _ => throw new Exception("Unexpected texture") },
            };
            evaluator.Run(module);
            return evaluator.Outputs;
        }
        // A character's surface is marked by whole metal and no gloss. Its accumulated light is the shadow alone, scaled up
        // so that nothing else the host adds counts beside it, and nothing if the set's light is out; it takes no highlight.
        float[] marked = [1, 0, 0, 0];
        var lit = Lit(shadedText, marked, 1); var unlit = Lit(shadedText, marked, 0);
        for (int c = 0; c < 3; c++)
            if (lit[(0, c)] != LightShader.Scale * 0.35f || unlit[(0, c)] != 0 || lit[(1, c)] != 0 || unlit[(1, c)] != 0)
                throw new Exception($"A marked surface's channel {c} takes {lit[(0, c)]} and {lit[(1, c)]} lit, {unlit[(0, c)]} with the light out");
        // Where the stage reads the cast shadow, a marked surface takes it whole, whatever strength the scene mixes it in by.
        var shadowed = Lit(castText, marked, 1);
        for (int c = 0; c < 3; c++)
            if (shadowed[(0, c)] != LightShader.Scale * 0.35f * 0.5f || shadowed[(1, c)] != 0) throw new Exception($"A marked surface under a casting sun takes {shadowed[(0, c)]} and {shadowed[(1, c)]} in channel {c}");
        foreach (var surface in new[] { new[] { 0f, 0.5f, 0, 0 }, new[] { 0.98f, 0f, 0, 0 } })
        {
            var host = Lit(cast, surface, 1); var kept = Lit(castText, surface, 1);
            if (host.Count != 8 || host.Any(p => kept[p.Key] != p.Value)) throw new Exception("An unmarked surface's light under a casting sun changed");
        }
        // Every other surface is lit as the host lights it, whichever way its values lean.
        foreach (var surface in new[] { new[] { 0f, 0.5f, 0, 0 }, new[] { 1f, 0.02f, 0, 0 }, new[] { 0.98f, 0f, 0, 0 }, new[] { 0.4f, 0.3f, 0.05f, 0 } })
        {
            var host = Lit(sun, surface, 1); var kept = Lit(shadedText, surface, 1);
            if (host.Count != 8 || host.Any(p => kept[p.Key] != p.Value)) throw new Exception("An unmarked surface's light changed");
        }
        if (lit[(0, 3)] != 1 || lit[(1, 3)] != 1) throw new Exception("A marked surface's coverage changed");

        Dictionary<(int, int), float> Combined(string module, float[] surface)
        {
            var evaluator = new ShaderEvaluator { Input = (_, column) => 0.5f, Sample = (slot, _, _) => slot switch { 3 => surface, 4 => colour, _ => throw new Exception("Unexpected texture") } };
            evaluator.Run(module);
            return evaluator.Outputs;
        }
        // The stored colour of a marked surface is its light: it multiplies the accumulated shadow back down by the same scale.
        var own = Combined(combinedText, marked);
        for (int c = 0; c < 3; c++)
            if (MathF.Abs(own[(0, c)] * LightShader.Scale - colour[c]) > 1e-6f) throw new Exception("A marked surface's stored light is not scaled back");
        // Shadow times stored light, with the host's other light lost beside it.
        if (MathF.Abs(lit[(0, 0)] * own[(0, 0)] - 0.35f * colour[0]) > 1e-6f) throw new Exception("A marked surface does not show its stored light times the shadow");
        foreach (var surface in new[] { new[] { 0f, 0.5f, 0, 0 }, new[] { 1f, 0.02f, 0, 0 }, new[] { 0.98f, 0f, 0, 0 } })
        {
            var host = Combined(combine, surface); var kept = Combined(combinedText, surface);
            if (host.Count != 4 || host.Any(p => kept[p.Key] != p.Value)) throw new Exception("An unmarked surface's colour changed");
        }
        foreach (var bad in new Action[] { () => LightShader.ShadeMarked(shaded), () => LightShader.CombineMarked(LightShader.CombineMarked(combine)), () => LightShader.ShadeMarked(combine), () => LightShader.CombineMarked(sun.Replace("g_GBuffer2Texture", "g_Other")) })
        {
            try { bad(); throw new Exception("An unusable light stage was rewritten"); } catch (InvalidDataException) { }
        }
        Console.WriteLine("PASS: the host's sun stage hands a marked surface the shadow alone, with the cast shadow where the stage reads it, and its colour stage scales the stored light back; other surfaces are lit as before");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new Exception("Set " + name);
}
