using System.Numerics;
using AgeOfGold.Shaders;

internal static class PixelShaderCheck
{
    private const string Vertex = """
        struct Input { float3 position : POSITION; uint4 normal : NORMAL; uint4 tangent : TANGENT; float4 colour : COLOR0;
            float4 shade : COLOR1; int2 uv : TEXCOORD0; float4 second : TEXCOORD1; };
        struct Output { float4 position : SV_Position; float4 normal : NORMAL; float4 tangent : TANGENT; float4 colour : TEXCOORD0;
            float u : TEXCOORD4; float v : TEXCOORD5; float4 extra : TEXCOORD7; float range : TEXCOORD15; };
        cbuffer Camera : register(b0) { float4 Clip[4]; float4 Jitter; float4 Eye; };
        [RootSignature("RootFlags(ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT),CBV(b0)")]
        Output Main(Input i) {
            Output o;
            float4 world = float4(i.position * 2 + float3(1, 2, 3), 1);
            float4 clip = float4(dot(Clip[0], world), dot(Clip[1], world), dot(Clip[2], world), dot(Clip[3], world));
            o.position = float4(clip.xy + Jitter.xy * clip.w, clip.zw);
            o.range = length(Eye.xyz - world.xyz);
            o.normal = float4(float4(i.normal).xyz / 127 - 1, 0);
            float4 t = float4(i.tangent) / 64 - 2;
            o.tangent = float4(dot(t.xyz, float3(.5, 2, -1)), dot(t.xyz, float3(-2, .25, 1.5)), dot(t.xyz, float3(1, -3, .75)), t.w);
            o.colour = i.colour;
            o.u = i.uv.x / 2048.0; o.v = i.uv.y / 2048.0;
            o.extra = float4(i.second.xyz, i.shade.x);
            return o;
        }
        """;

    // An authored two-layer surface: one coordinate set, an edge-texture blend, a packed normal and five targets.
    private const string Pixel = """
        cbuffer Tiling : register(b1) { float4 Scale[5]; };
        cbuffer Surface : register(b3) { float4 Tint0; float4 Tint1; float4 Blend; };
        Texture2D Edge : register(t0); Texture2D Colour0 : register(t1); Texture2D Normal0 : register(t2);
        Texture2D Normal1 : register(t3); Texture2D Colour1 : register(t4);
        SamplerState Filter : register(s0);
        struct Input { float4 position : SV_Position; float4 normal : NORMAL; float4 tangent : TANGENT; float4 colour : TEXCOORD0;
            float u : TEXCOORD4; float v : TEXCOORD5; float4 extra : TEXCOORD7; bool front : SV_IsFrontFace; };
        struct Output { float4 a : SV_Target0; float4 b : SV_Target1; float4 c : SV_Target2; float4 d : SV_Target3; float4 e : SV_Target4; };
        [RootSignature("CBV(b1),CBV(b3),DescriptorTable(SRV(t0,numDescriptors=5)),DescriptorTable(Sampler(s0))")]
        Output Main(Input i) {
            float3 n = normalize(i.normal.xyz), t = normalize(i.tangent.xyz);
            float3 b = normalize(cross(n, t) * i.tangent.w);
            if (!i.front) n = -n;
            float2 uv = float2(i.u, i.v);
            float3 first = Colour0.Sample(Filter, uv * Scale[1].xy + Scale[1].zw).rgb * Tint0.rgb;
            float3 second = Colour1.Sample(Filter, uv * Scale[4].xy + Scale[4].zw).rgb * Tint1.rgb;
            float edge = Edge.Sample(Filter, uv * Scale[0].xy + Scale[0].zw).r;
            float w = saturate((i.extra.x - Blend.x * (1 - i.extra.x) * (edge + 0.01)) * (Blend.y * i.colour.w + 1));
            float3 colour = lerp(first, second, w);
            float3 m = lerp(Normal0.Sample(Filter, uv * Scale[2].xy + Scale[2].zw).rgb,
                            Normal1.Sample(Filter, uv * Scale[3].xy + Scale[3].zw).rgb, w);
            float2 xy = m.xy * 2 - 1;
            float3 world = b * xy.x + t * xy.y + n * sqrt(1 - saturate(dot(xy, xy)));
            Output o;
            o.a = float4(normalize(world) * 0.5 + 0.5, 1);
            o.b = float4(0, saturate(m.z), 0, 1);
            o.c = float4(saturate(colour) * Blend.z, 1);
            o.d = float4(0, 0, 0, 1);
            o.e = float4(0, 0, 0, 1);
            return o;
        }
        """;

    private static readonly PixelShader.LayerSlots Slots = new(0, [1, 4], [2, 3]);
    private static readonly PixelShader.Targets Targets = new(0, 2, 4, -0.5f);
    private static readonly PixelShader.GroundLighting Lighting = new([9.5f, 10.25f, 11f], [0.2f, 0.25f, 0.3f], [0.05f, 0.0625f, 0.075f], [0.5f, 0.375f, 0.25f]);

    internal static void Run()
    {
        var compiler = new ShaderCompiler(Required("AOG_DXC"), Required("AOG_DXC_ASSEMBLER"));
        var vertex = VertexShader.AddLayerFrames(compiler.Disassemble(compiler.Compile(Vertex, "Main", "vs_6_0")));
        var forwarded = VertexShader.Forwarded(vertex, "TEXCOORD", 1);
        if (forwarded.Length != 4 || forwarded[0] != ("TEXCOORD", 7, 0) || forwarded[1] != ("TEXCOORD", 7, 1) || forwarded[3] is not null
            || VertexShader.Forwarded(vertex, "COLOR", 0)[3] != ("TEXCOORD", 0, 3))
            throw new Exception("Vertex forwarding of coordinates or colour was not recognised");
        // The pixel stage needs the surface-to-camera vector the installed vertex programs already compute, and the view depth.
        float[] sun = [0.6f, -0.64f, 0.48f];
        var viewed = VertexShader.AddView(vertex, VertexShader.FreeOutputRow(vertex));
        var viewProgram = compiler.Disassemble(compiler.Assemble(viewed, compiler.Compile(Vertex, "Main", "vs_6_0")));
        {
            var signatures = new ShaderSignatures(viewProgram);
            float[][] clip = [[1.2f, 0, 0.9f, 0.5f], [0.3f, 2.0f, -0.4f, -1], [0.1f, 0.2f, 0.3f, 0.4f], [0.6f, 0, -0.8f, 2]];
            var evaluator = new ShaderEvaluator
            {
                Constants = (register, index) => register == 0 ? index < 4 ? clip[index] : index == 4 ? [0.25f, -0.5f, 0, 0] : [10, 20, 30, 0] : throw new Exception("Unexpected vertex constants"),
                Input = (id, column) =>
                {
                    if (id == signatures.Find(false, "POSITION", 0).Id) return new[] { 0.5f, -1f, 2f }[column];
                    return signatures.Members(false).Single(m => m.Id == id).ComponentType == 9 ? 0.25f : (object)100;
                },
            };
            evaluator.Run(viewProgram);
            Vector3 world = new(2, 0, 7);
            float depth = clip[3][0] * world.X + clip[3][1] * world.Y + clip[3][2] * world.Z + clip[3][3];
            int id = signatures.Find(true, "TANGENT", 8).Id;
            var camera = new Vector4(evaluator.Outputs[(id, 0)], evaluator.Outputs[(id, 1)], evaluator.Outputs[(id, 2)], evaluator.Outputs[(id, 3)]);
            if (Vector4.Distance(camera, new(new Vector3(10, 20, 30) - world, depth)) > 1e-5f) throw new Exception($"Forwarded view vector or depth differs: {camera}");
            if (!viewed.Equals(VertexShader.AddView(vertex, VertexShader.FreeOutputRow(vertex)), StringComparison.Ordinal)) throw new Exception("View forwarding does not repeat");
            Reject(() => VertexShader.AddView(viewed, VertexShader.FreeOutputRow(viewed)), "already");
            var eyeless = VertexShader.AddLayerFrames(compiler.Disassemble(compiler.Compile(Vertex.Replace("o.range = length(Eye.xyz - world.xyz);", "o.range = Eye.x;"), "Main", "vs_6_0")));
            Reject(() => VertexShader.AddView(eyeless, VertexShader.FreeOutputRow(eyeless)), "camera");
        }
        var original = compiler.Compile(Pixel, "Main", "ps_6_0");
        var module = compiler.Disassemble(original);
        var composed = PixelShader.ComposeLayeredGround(module, vertex, Slots, Targets, Lighting);
        var output = compiler.Assemble(composed, original);
        if (!output.SequenceEqual(compiler.Assemble(PixelShader.ComposeLayeredGround(module, vertex, Slots, Targets, Lighting), original)))
            throw new Exception("Ground pixel composition does not repeat");
        var text = compiler.Disassemble(output);

        foreach (bool front in new[] { true, false })
            foreach (float alpha in new[] { 0f, 0.35f, 1f })
            {
                var inputs = new Dictionary<(string, int), float[]>
                {
                    [("NORMAL", 0)] = [0.3f, 0.9f, -0.2f, 0], [("TANGENT", 0)] = [0.8f, -0.1f, 0.5f, 1],
                    [("TEXCOORD", 0)] = [0.9f, 0.8f, 0.7f, alpha], [("TEXCOORD", 4)] = [0.37f], [("TEXCOORD", 5)] = [1.21f],
                    [("TEXCOORD", 7)] = [-0.6f, 0.45f, 0, 0.25f],
                    [("TANGENT", 4)] = [0.1f, 0.3f, 0.9f, 0], [("TANGENT", 6)] = [-0.7f, 0.2f, 0.6f, 0], [("TANGENT", 2)] = [0, 0, 0, 0],
                };
                var host = Evaluate(module, inputs, front, cube: false);
                var actual = Evaluate(text, inputs, front, cube: true);

                // The unedited fixture anchors the evaluator itself.
                Vector2 uv0 = new(0.37f, 1.21f), uv1 = new(-0.6f, 0.45f);
                Vector3 first = Colour(1, Tile(uv0, 1)) * Tint(0), secondHost = Colour(4, Tile(uv0, 4)) * Tint(1);
                float weight = Math.Clamp((-0.6f - Blend.X * 1.6f * (Colour(0, Tile(uv0, 0)).X + 0.01f)) * (Blend.Y * alpha + 1), 0, 1);
                var hostColour = Vector3.Clamp(Vector3.Lerp(first, secondHost, weight), Vector3.Zero, Vector3.One) * Blend.Z;
                Near(host, 2, hostColour, "evaluator disagrees with the authored fixture");

                Vector3 normal = Vector3.Normalize(new(0.3f, 0.9f, -0.2f)) * (front ? 1 : -1), tangent = Vector3.Normalize(new(0.8f, -0.1f, 0.5f));
                Vector3 Layer(int slot, Vector2 uv, Vector3 bitangent, out float packed)
                {
                    var m = Colour(slot, Tile(uv, slot)); packed = m.Z;
                    float x = m.X * 2 - 1, y = m.Y * 2 - 1, z = MathF.Sqrt(1 - Math.Clamp(x * x + y * y, 0, 1));
                    return Vector3.Normalize(Vector3.Normalize(bitangent) * x + tangent * y + normal * z);
                }
                var n0 = Layer(2, uv0, new(0.1f, 0.3f, 0.9f), out float p0); var n1 = Layer(3, uv1, new(-0.7f, 0.2f, 0.6f), out float p1);
                var world = Vector3.Normalize(Vector3.Lerp(n0, n1, alpha));
                var blended = Vector3.Lerp(first, Colour(4, Tile(uv1, 4)) * Tint(1), alpha) * new Vector3(0.9f, 0.8f, 0.7f);
                var colour = new Vector3(MathF.Pow(blended.X, 2.2f), MathF.Pow(blended.Y, 2.2f), MathF.Pow(blended.Z, 2.2f));
                var environment = new Vector3(0.5f) + 0.25f * world;
                var lower = new Vector3(Lighting.Lower); var upper = new Vector3(Lighting.Upper);
                var light = environment * new Vector3(Lighting.Environment) + Vector3.Lerp(lower, upper, world.Y * 0.5f + 0.5f);
                Near(actual, 0, world * 0.5f + new Vector3(0.5f), "blended world normal");
                Near(actual, 2, Vector3.Zero, "colour target must be cleared for emitted light");
                Near(actual, 4, light * colour * new Vector3(Lighting.Diffuse), "source-lit colour");
                if (actual[(4, 3)] != -0.5f || MathF.Abs(actual[(1, 1)] - Math.Clamp(p0 + (p1 - p0) * alpha, 0, 1)) > 2e-5f
                    || actual[(0, 3)] != host[(0, 3)] || actual[(3, 3)] != host[(3, 3)])
                    throw new Exception("Lit marker, packed normal channel or untouched targets differ");
            }

        // An alpha-tested variant branches around a discard after its normal; the lighting must still reach the stores.
        var cutoutProgram = compiler.Compile(Pixel.Replace("Output o;", "if (normalize(world).y * Blend.x > i.extra.w - 2) discard;\n    Output o;"), "Main", "ps_6_0");
        var cutoutModule = compiler.Disassemble(cutoutProgram);
        if (!System.Text.RegularExpressions.Regex.IsMatch(cutoutModule, @"(?m)^  br i1 "))
            throw new Exception("Cutout fixture lost its branch: " + string.Join(" | ", cutoutModule.Split('\n').Where(l => l.Contains("discard") || l.Contains("br "))));
        var cutout = compiler.Disassemble(compiler.Assemble(PixelShader.ComposeLayeredGround(cutoutModule, vertex, Slots, Targets, Lighting), cutoutProgram));
        foreach (float threshold in new[] { 1.5f, 2.75f })
        {
            var inputs = new Dictionary<(string, int), float[]>
            {
                [("NORMAL", 0)] = [0.3f, 0.9f, -0.2f, 0], [("TANGENT", 0)] = [0.8f, -0.1f, 0.5f, 1], [("TEXCOORD", 0)] = [0.9f, 0.8f, 0.7f, 0.35f],
                [("TEXCOORD", 4)] = [0.37f], [("TEXCOORD", 5)] = [1.21f], [("TEXCOORD", 7)] = [-0.6f, 0.45f, 0, threshold],
                [("TANGENT", 4)] = [0.1f, 0.3f, 0.9f, 0], [("TANGENT", 6)] = [-0.7f, 0.2f, 0.6f, 0],
            };
            var plain = Evaluate(text, inputs, true, cube: true, out _);
            var tested = Evaluate(cutout, inputs, true, cube: true, out bool discarded);
            float height = plain[(0, 1)] * 2 - 1;
            if (discarded != (height * Blend.X > threshold - 2) || !tested.OrderBy(p => p.Key).SequenceEqual(plain.OrderBy(p => p.Key)))
                throw new Exception("Alpha-tested ground variant discards wrongly or shades differently");
        }
        // A host value computed only on one side of a branch cannot feed lighting that runs on both.
        var guarded = new System.Text.RegularExpressions.Regex(@"(?m)^(  %[\w.]+ = call float @dx\.op\.unary\.f32\(i32 25, .*)$",
            System.Text.RegularExpressions.RegexOptions.RightToLeft | System.Text.RegularExpressions.RegexOptions.Multiline).Replace(module,
            "  br i1 undef, label %authored_side, label %authored_join\n\nauthored_side:\n$1\n  br label %authored_join\n\nauthored_join:", 1);
        Reject(() => PixelShader.ComposeLayeredGround(guarded, vertex, Slots, Targets, Lighting), "conditional");
        // A cutout discards where the blended coverage of its two colour samples falls below the reference.
        var covered = compiler.Disassemble(compiler.Assemble(PixelShader.ComposeLayeredGround(module, vertex, Slots, Targets, Lighting, 0.5f), original));
        int kept = 0, removed = 0;
        foreach (float alpha in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            var inputs = new Dictionary<(string, int), float[]>
            {
                [("NORMAL", 0)] = [0.3f, 0.9f, -0.2f, 0], [("TANGENT", 0)] = [0.8f, -0.1f, 0.5f, 1], [("TEXCOORD", 0)] = [0.9f, 0.8f, 0.7f, alpha],
                [("TEXCOORD", 4)] = [0.37f], [("TEXCOORD", 5)] = [1.21f], [("TEXCOORD", 7)] = [-0.6f, 0.45f, 0, 0.25f],
                [("TANGENT", 4)] = [0.1f, 0.3f, 0.9f, 0], [("TANGENT", 6)] = [-0.7f, 0.2f, 0.6f, 0],
            };
            var shaded = Evaluate(covered, inputs, true, cube: true, out bool discarded);
            float coverage = Coverage(1, Tile(new(0.37f, 1.21f), 1)) * (1 - alpha) + Coverage(4, Tile(new(-0.6f, 0.45f), 4)) * alpha;
            if (discarded != (coverage < 0.5f) || !shaded.OrderBy(p => p.Key).SequenceEqual(Evaluate(text, inputs, true, cube: true).OrderBy(p => p.Key)))
                throw new Exception("Cutout coverage test or shading differs");
            if (discarded) removed++; else kept++;
        }
        if (kept == 0 || removed == 0) throw new Exception("Cutout fixture does not exercise both outcomes");
        Reject(() => PixelShader.ComposeLayeredGround(module, vertex, Slots, Targets, Lighting, 1.5f), "reference");
        // Source air: fog by view depth, then scattering along the view vector, both on the gamma-encoded light.
        var air = new AgeOfGold.Params.SourceAir.Values([0.004f, 0.005f, 0.007f], [4e-6f, 7e-6f, 1.5e-5f], [8e-4f, 1e-3f, 1.5e-3f], [0.84f, 1.4f, 0.8f],
            sun, [0.8f, 2.3f, 2.6f], [0.8f, 0.9f, 1f], 1.3f, 0.5f, 0.9f, [0.17f, 0.18f, 0.16f], -50, 1 / 120f, 0.65f);
        var hazy = compiler.Disassemble(compiler.Assemble(PixelShader.ComposeLayeredGround(module, viewed, Slots, Targets, Lighting, null, air), original));
        foreach (var (toCamera, depth) in new[] { (new Vector3(3, -2, 25), 24f), (new Vector3(-40, 10, 160), 150f) })
        {
            var inputs = new Dictionary<(string, int), float[]>
            {
                [("NORMAL", 0)] = [0.3f, 0.9f, -0.2f, 0], [("TANGENT", 0)] = [0.8f, -0.1f, 0.5f, 1], [("TEXCOORD", 0)] = [0.9f, 0.8f, 0.7f, 0.35f],
                [("TEXCOORD", 4)] = [0.37f], [("TEXCOORD", 5)] = [1.21f], [("TEXCOORD", 7)] = [-0.6f, 0.45f, 0, 0.25f],
                [("TANGENT", 4)] = [0.1f, 0.3f, 0.9f, 0], [("TANGENT", 6)] = [-0.7f, 0.2f, 0.6f, 0],
                [("TANGENT", 8)] = [toCamera.X, toCamera.Y, toCamera.Z, depth],
            };
            var clear = Evaluate(text, inputs, true, cube: true);
            var actual = Evaluate(hazy, inputs, true, cube: true);
            var expected = new float[3];
            float distance = toCamera.Length(), cosine = Vector3.Dot(toCamera / distance, new Vector3(sun[0], sun[1], sun[2]));
            float fog = Math.Clamp(Math.Clamp((depth - air.FogBegin) * air.FogScale, 0, 1) * air.FogLimit, 0, 1);
            float mie = air.Phase[0] / MathF.Pow(air.Phase[1] - air.Phase[2] * cosine, 1.5f);
            for (int c = 0; c < 3; c++)
            {
                float encoded = MathF.Pow(clear[(4, c)], 1 / 2.2f);
                encoded += (air.FogColour[c] - encoded) * fog;
                float through = MathF.Pow(2, -distance * air.Extinction[c] * air.Distance * 2.0813689f);
                float added = (air.BetaDashRayleigh[c] * (1 + cosine * cosine) + air.BetaDashMie[c] * mie) * (1 - through) / air.Extinction[c] * air.ReflectanceStrength * air.SunColour[c];
                encoded += (encoded * through * air.Reflectance[c] + added - encoded) * air.Blend;
                expected[c] = MathF.Pow(encoded, 2.2f);
            }
            Near(actual, 4, new(expected), "fogged and scattered light");
            if (actual.Where(p => p.Key.Item1 != 4 || p.Key.Item2 == 3).Any(p => clear[p.Key] != p.Value)) throw new Exception("Source air changed other targets");
        }
        Reject(() => PixelShader.ComposeLayeredGround(module, vertex, Slots, Targets, Lighting, null, air), "semantic");
        Reject(() => PixelShader.ComposeLayeredGround(composed, vertex, Slots, Targets, Lighting), "already");
        Reject(() => PixelShader.ComposeLayeredGround(module, compiler.Disassemble(compiler.Compile(Vertex, "Main", "vs_6_0")), Slots, Targets, Lighting), "semantic");
        Reject(() => PixelShader.ComposeLayeredGround(module, vertex, Slots with { Colour = [1, 1] }, Targets, Lighting), "distinct");
        Reject(() => PixelShader.ComposeLayeredGround(module, vertex, Slots with { Environment = 9 }, Targets, Lighting), "texture slot");
        var twice = compiler.Disassemble(compiler.Compile(Pixel.Replace("float3 colour = lerp(first, second, w);",
            "float3 colour = lerp(first, second, w) + Colour1.Sample(Filter, uv).rgb;"), "Main", "ps_6_0"));
        Reject(() => PixelShader.ComposeLayeredGround(twice, vertex, Slots, Targets, Lighting), "sample");
        var unblended = compiler.Disassemble(compiler.Compile(Pixel.Replace("float3 colour = lerp(first, second, w);", "float3 colour = first * second;"), "Main", "ps_6_0"));
        Reject(() => PixelShader.ComposeLayeredGround(unblended, vertex, Slots, Targets, Lighting), "blend");
        Reject(() => PixelShader.ComposeLayeredGround(module, vertex, Slots, Targets, Lighting with { Upper = [1, float.NaN, 1] }), "finite");
        Console.WriteLine("PASS: layered ground pixel composition matches independent arithmetic on both faces, repeats, and rejects ambiguous or unsupported programs");
    }

    private static readonly Vector3 Blend = new(0.4f, 0.5f, 0.75f);
    private static Vector2 Tile(Vector2 uv, int slot) => uv * new Vector2(1 + slot * 0.25f, 2 - slot * 0.125f) + new Vector2(slot * 0.0625f, -slot * 0.03125f);
    private static Vector3 Tint(int layer) => layer == 0 ? new(0.9f, 0.85f, 0.8f) : new(0.7f, 0.95f, 0.6f);
    // Smooth authored texture contents, distinct per slot.
    private static Vector3 Colour(int slot, Vector2 uv) => new(0.2f + 0.05f * slot + 0.1f * MathF.Sin(uv.X), 0.3f + 0.04f * slot + 0.1f * MathF.Cos(uv.Y), 0.6f - 0.05f * slot + 0.05f * MathF.Sin(uv.X + uv.Y));

    private static float Coverage(int slot, Vector2 uv) => slot == 1 ? 0.2f : slot == 4 ? 0.9f : 1;

    private static Dictionary<(int, int), float> Evaluate(string module, Dictionary<(string, int), float[]> inputs, bool front, bool cube) =>
        Evaluate(module, inputs, front, cube, out _);

    private static Dictionary<(int, int), float> Evaluate(string module, Dictionary<(string, int), float[]> inputs, bool front, bool cube, out bool discarded)
    {
        var signatures = new ShaderSignatures(module, "ps");
        var evaluator = new ShaderEvaluator();
        var byId = inputs.Where(p => HasInput(signatures, p.Key)).ToDictionary(p => signatures.Find(false, p.Key.Item1, p.Key.Item2).Id, p => p);
        int face = signatures.Find(false, "SV_IsFrontFace", 0).Id;
        evaluator.Input = (id, column) =>
        {
            if (id == face) return front ? -1 : 0;
            return byId[id].Value[column];
        };
        evaluator.Constants = (register, index) => register switch
        {
            1 => [1 + index * 0.25f, 2 - index * 0.125f, index * 0.0625f, -index * 0.03125f],
            3 => index switch { 0 => [0.9f, 0.85f, 0.8f, 0], 1 => [0.7f, 0.95f, 0.6f, 0], _ => [Blend.X, Blend.Y, Blend.Z, 0] },
            _ => throw new Exception("Unexpected constant register"),
        };
        evaluator.Sample = (slot, at, level) =>
        {
            if (at.Length == 3 != (cube && slot == 0)) throw new Exception("Texture sampled with the wrong dimension");
            if (at.Length == 3) return [0.5f + 0.25f * at[0], 0.5f + 0.25f * at[1], 0.5f + 0.25f * at[2], 1];
            var c = Colour(slot, new(at[0], at[1]));
            return [c.X, c.Y, c.Z, Coverage(slot, new(at[0], at[1]))];
        };
        evaluator.Run(module);
        discarded = evaluator.Discarded;
        var targets = Enumerable.Range(0, 5).ToDictionary(i => signatures.Find(true, "SV_Target", i).Id);
        return evaluator.Outputs.ToDictionary(p => (targets[p.Key.Id], p.Key.Column), p => p.Value);
    }

    private static bool HasInput(ShaderSignatures signatures, (string, int) key)
    {
        try { signatures.Find(false, key.Item1, key.Item2); return true; }
        catch (InvalidDataException) { return false; }
    }

    private static void Near(Dictionary<(int, int), float> actual, int target, Vector3 expected, string what)
    {
        var value = new Vector3(actual[(target, 0)], actual[(target, 1)], actual[(target, 2)]);
        if (!(Vector3.Distance(value, expected) <= 3e-5f * MathF.Max(1, expected.Length())))
            throw new Exception($"Ground pixel check failed: {what}: {value} != {expected}");
    }

    private static void Reject(Action action, string reason)
    {
        try { action(); }
        catch (InvalidDataException e) when (e.Message.Contains(reason, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new Exception("Expected pixel shader rejection: " + reason);
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new Exception("Set " + name);
}
