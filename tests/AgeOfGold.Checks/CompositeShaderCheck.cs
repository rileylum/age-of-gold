using AgeOfGold.Shaders;

internal static class CompositeShaderCheck
{
    // An authored composition stage of the host's shape: the scene is accumulated diffuse, specular and emitted light, and
    // a second target takes the emitted light times its weight, for glow.
    private const string Fixture = """
        Texture2D<float4> Diffuse : register(t6); Texture2D<float4> Specular : register(t7); Texture2D<float4> Emitted : register(t8);
        struct Composed { float4 scene : SV_Target0; float4 glow : SV_Target1; };
        [RootSignature("DescriptorTable(SRV(t6,numDescriptors=3))")]
        Composed Main(float4 position : SV_Position) {
            int3 at = int3(position.xy, 0);
            float4 emitted = Emitted.Load(at);
            Composed composed;
            composed.scene = float4(max(Diffuse.Load(at).rgb + Specular.Load(at).rgb + emitted.rgb, 0), 1);
            composed.glow = float4(emitted.rgb * emitted.a, 1);
            return composed;
        }
        """;

    internal static void Run()
    {
        var compiler = new ShaderCompiler(Required("AOG_DXC"), Required("AOG_DXC_ASSEMBLER"));
        var original = compiler.Compile(Fixture, "Main", "ps_6_0");
        var module = compiler.Disassemble(original);
        var rewritten = CompositeShader.KeepSourceLight(module);
        var output = compiler.Assemble(rewritten, original);
        if (!output.SequenceEqual(compiler.Assemble(CompositeShader.KeepSourceLight(module), original))) throw new Exception("Composition rewrite does not repeat");
        // The host's own stage works out each glow value only after it has written the scene: the same program with every
        // stored value's working moved down to just above its store.
        var late = module.Split('\n').ToList();
        foreach (var store in late.Where(l => l.Contains("@dx.op.storeOutput.f32(", StringComparison.Ordinal) && !l.StartsWith("declare", StringComparison.Ordinal)).ToArray())
        {
            string value = System.Text.RegularExpressions.Regex.Match(store, @"float (%[\w.]+)\)").Groups[1].Value;
            int made = late.FindIndex(l => l.StartsWith($"  {value} = ", StringComparison.Ordinal));
            if (value.Length == 0 || made < 0 || !late[made].Contains("fmul", StringComparison.Ordinal)) continue;
            string working = late[made]; late.RemoveAt(made); late.Insert(late.IndexOf(store), working);
        }
        var lateModule = string.Join('\n', late);
        if (lateModule == module) throw new Exception("The check's second ordering is the first");
        foreach (var text in new[] { compiler.Disassemble(output), compiler.Disassemble(compiler.Assemble(CompositeShader.KeepSourceLight(lateModule), original)) })
        {
        float[] diffuse = [0.2f, 0.3f, 0.4f, 1], specular = [0.05f, 0.5f, 0.02f, 1];
        foreach (var (emitted, marked) in new[] { (new[] { 0.1f, 0.6f, 0.25f, -1f }, true), (new[] { 0.1f, 0.6f, 0.25f, 0.5f }, false), (new[] { 0.3f, 0f, 0.7f, 0f }, false) })
        {
            var evaluator = new ShaderEvaluator
            {
                Input = (_, column) => column == 0 ? 12.5f : 7.5f,
                Sample = (slot, _, _) => slot switch { 6 => diffuse, 7 => specular, 8 => emitted, _ => throw new Exception("Unexpected texture") },
            };
            evaluator.Run(text);
            for (int c = 0; c < 3; c++)
            {
                // Where the emitted light is marked by a weight below nothing it is the build's own lighting, shown alone
                // and without glow; elsewhere the host composes as it did.
                float scene = marked ? emitted[c] : diffuse[c] + specular[c] + emitted[c], glow = marked ? 0 : emitted[c] * emitted[3];
                if (MathF.Abs(evaluator.Outputs[(0, c)] - scene) > 1e-6f || MathF.Abs(evaluator.Outputs[(1, c)] - glow) > 1e-6f)
                    throw new Exception($"Composed channel {c} is {evaluator.Outputs[(0, c)]} with glow {evaluator.Outputs[(1, c)]}; expected {scene} and {glow}");
            }
            if (evaluator.Outputs[(0, 3)] != 1 || evaluator.Outputs[(1, 3)] != 1) throw new Exception("Composition coverage changed");
        }
        }
        try { CompositeShader.KeepSourceLight(rewritten); throw new Exception("A composition stage was rewritten twice"); } catch (InvalidDataException) { }
        var weightless = compiler.Disassemble(compiler.Compile(Fixture.Replace("emitted.rgb * emitted.a", "emitted.rgb"), "Main", "ps_6_0"));
        try { CompositeShader.KeepSourceLight(weightless); throw new Exception("A composition stage with no emitted weight was accepted"); } catch (InvalidDataException) { }
        Console.WriteLine("PASS: the composition stage shows marked emitted light alone and without glow, and composes everything else as the host does");
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new Exception("Set " + name);
}
