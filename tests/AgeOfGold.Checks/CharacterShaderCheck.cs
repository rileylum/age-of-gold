using System.Numerics;
using AgeOfGold.Shaders;

internal static class CharacterShaderCheck
{
    // An authored character vertex stage of the host's shape: the position is taken relative to the camera before it is
    // projected, and no length of that vector is written anywhere.
    private const string Vertex = """
        struct Input { float3 position : POSITION; uint4 normal : NORMAL; uint4 tangent : TANGENT; int4 uv : TEXCOORD0; };
        struct Output { float4 position : SV_Position; float4 clip : POSITION1; float4 normal : NORMAL; float4 tangent : TANGENT;
            float4 uv : TEXCOORD0; float fade : TEXCOORD4; float cut : SV_ClipDistance0; float edge : SV_ClipDistance1; };
        cbuffer Scene : register(b8) { float4 Other[3]; float4 Eye; float4 Clip[4]; };
        [RootSignature("RootFlags(ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT),CBV(b8)")]
        Output Main(Input i) {
            Output o;
            float3 world = i.position * 2 + float3(1, 2, 3);
            float3 seen = world - Eye.xyz;
            float4 clip = float4(dot(Clip[0].xyz, seen) + Clip[0].w, dot(Clip[1].xyz, seen) + Clip[1].w, dot(Clip[2].xyz, seen) + Clip[2].w, dot(Clip[3].xyz, seen) + Clip[3].w);
            o.position = clip; o.clip = clip;
            o.normal = float4(float4(i.normal).xyz / 127 - 1, 0);
            o.tangent = float4(i.tangent) / 127 - 1;
            o.uv = float4(i.uv) / 2048.0;
            o.fade = Other[0].x;
            o.cut = dot(clip, Other[1]); o.edge = dot(clip, Other[2]);
            return o;
        }
        """;

    // An authored deferred character stage of the host's shape: colour, metal and relief maps, a relief-bent normal that
    // follows the face seen, two colours of the material's, and five targets.
    private const string Pixel = """
        cbuffer Tiling : register(b1) { float4 Scale[3]; };
        cbuffer cbMtdParam : register(b3) { float4 FC_DifMapMulCol; float4 Other[8]; float4 Kind_snp_0_color_0; float Kind_snp_0_float_0; float Kind_snp_0_float_1; };
        Texture2D Colour : register(t0); Texture2D Metal : register(t1); Texture2D Relief : register(t2);
        SamplerState Filter0 : register(s0); SamplerState Filter1 : register(s1); SamplerState Filter2 : register(s2);
        struct Input { float4 position : SV_Position; float4 clip : POSITION1; float4 normal : NORMAL; float4 tangent : TANGENT;
            float4 uv : TEXCOORD0; float fade : TEXCOORD4; bool front : SV_IsFrontFace; };
        struct Output { float4 a : SV_Target0; float4 b : SV_Target1; float4 c : SV_Target2; float4 d : SV_Target3; float4 e : SV_Target4; };
        [RootSignature("CBV(b1),CBV(b3),DescriptorTable(SRV(t0,numDescriptors=6)),DescriptorTable(Sampler(s0,numDescriptors=3))")]
        Output Main(Input i) {
            float3 n = normalize(i.normal.xyz), t = normalize(i.tangent.xyz);
            float3 b = normalize(cross(n, t) * i.tangent.w);
            if (!i.front) n = -n;
            float3 colour = Colour.Sample(Filter0, i.uv.xy * Scale[0].xy + Scale[0].zw).rgb * Kind_snp_0_color_0.rgb * FC_DifMapMulCol.rgb;
            float metal = Metal.Sample(Filter1, i.uv.xy * Scale[1].xy + Scale[1].zw).r;
            float3 m = Relief.Sample(Filter2, i.uv.xy * Scale[2].xy + Scale[2].zw).rgb;
            float2 xy = m.xy * 2 - 1;
            float3 world = b * xy.x + t * xy.y + n * sqrt(1 - saturate(dot(xy, xy)));
            Output o;
            o.a = float4(normalize(world) * 0.5 + 0.5002, i.fade);
            o.b = float4(saturate(metal), saturate(m.z), 0, i.fade);
            o.c = float4(saturate(colour), i.fade);
            o.d = float4(0, 0, 0, i.fade);
            o.e = float4(0, 0, 0, i.fade);
            return o;
        }
        """;

    private static readonly CharacterShader.Slots Slots = new(0, 1, 2, 3, 4, 5);
    private static readonly PixelShader.Targets Targets = new(0, 2, 4, -2);
    // Fog by view depth and scattering along the view, on the gamma-encoded light, as the scenery's check states them.
    private static readonly AgeOfGold.Params.SourceAir.Values Air = new([0.004f, 0.005f, 0.007f], [4e-6f, 7e-6f, 1.5e-5f], [8e-4f, 1e-3f, 1.5e-3f], [0.84f, 1.4f, 0.8f],
        [0.17f, -0.94f, -0.3f], [0.8f, 2.3f, 2.6f], [0.8f, 0.9f, 1f], 1.3f, 0.5f, 0.9f, [0.17f, 0.18f, 0.16f], 2, 1 / 12f, 0.65f);
    private static readonly CharacterShader.Light Clear = new(
        Diffuse: [2.5f, 2.75f, 3f], Sky: [0.9f, 0.95f, 1f], SkyLevels: 8, Upper: [0.12f, 0.14f, 0.2f], Lower: [0.2f, 0.16f, 0.12f],
        Sun: [0.36f, 0.8f, -0.48f], ShadeStart: 3, ShadeLength: 7, Shade: [0.8f, 0.7f, 0.6f], ShadeTurn: 0.2f, ShadeEdge: 5, Glow: 1.5f, TableSize: 256, Air: null);
    private static readonly CharacterShader.Light Light = Clear with { Air = Air };
    private static readonly Vector3 Plain = new(0.6f, 0.55f, 0.5f);
    // The material's metal colour: a dim one and a bright one, so that plain texels reflect both less and more than the
    // level at which the source's grazing reflection is whole.
    private static Vector3 Metallic;
    private static bool Older;
    // The share of a shadow the material's definition takes: a different one for each way of reading the specular map.
    private static float Share => Older ? 1 : 0.75f;

    internal static void Run()
    {
        var compiler = new ShaderCompiler(Required("AOG_DXC"), Required("AOG_DXC_ASSEMBLER"));
        var vertexProgram = compiler.Compile(Vertex, "Main", "vs_6_0");
        var plain = compiler.Disassemble(vertexProgram);
        var viewed = VertexShader.AddView(plain, VertexShader.FreeOutputRow(plain));
        if (!viewed.Equals(VertexShader.AddView(plain, VertexShader.FreeOutputRow(plain)), StringComparison.Ordinal)) throw new Exception("View forwarding does not repeat");
        var vertex = compiler.Disassemble(compiler.Assemble(viewed, vertexProgram));
        {
            // The forwarded vector runs from the surface to the camera, with the view depth beside it.
            var signatures = new ShaderSignatures(vertex);
            float[][] clip = [[1.2f, 0, 0.9f, 0.5f], [0.3f, 2.0f, -0.4f, -1], [0.1f, 0.2f, 0.3f, 0.4f], [0.6f, 0, -0.8f, 2]];
            var evaluator = new ShaderEvaluator
            {
                Constants = (register, index) => register == 8 ? index == 3 ? [10, 20, 30, 0] : index >= 4 ? clip[index - 4] : [0.25f, 0, 0, 0] : throw new Exception("Unexpected vertex constants"),
                Input = (id, column) => id == signatures.Find(false, "POSITION", 0).Id ? new[] { 0.5f, -1f, 2f }[column] : (object)100,
            };
            evaluator.Run(vertex);
            Vector3 world = new(2, 0, 7), seen = world - new Vector3(10, 20, 30);
            float depth = clip[3][0] * seen.X + clip[3][1] * seen.Y + clip[3][2] * seen.Z + clip[3][3];
            int id = signatures.Find(true, "TANGENT", 8).Id;
            var forwarded = new Vector4(evaluator.Outputs[(id, 0)], evaluator.Outputs[(id, 1)], evaluator.Outputs[(id, 2)], evaluator.Outputs[(id, 3)]);
            if (Vector4.Distance(forwarded, new(-seen, depth)) > 1e-4f) throw new Exception($"Forwarded view vector or depth differs: {forwarded}");
            var still = compiler.Disassemble(compiler.Compile(Vertex.Replace("float3 seen = world - Eye.xyz;", "float3 seen = world;"), "Main", "vs_6_0"));
            Reject(() => VertexShader.AddView(still, VertexShader.FreeOutputRow(still)), "camera");
        }

        var original = compiler.Compile(Pixel, "Main", "ps_6_0");
        var module = compiler.Disassemble(original);
        var colours = CharacterShader.FindColours(module);
        if (colours != new CharacterShader.Colours(3, 0, 9, 10, 0, 10, 1)) throw new Exception("The material's two colours and two numbers were not located: " + colours);
        var composed = CharacterShader.Compose(module, vertex, Slots, Targets, colours, Light);
        var output = compiler.Assemble(composed, original);
        if (!output.SequenceEqual(compiler.Assemble(CharacterShader.Compose(module, vertex, Slots, Targets, colours, Light), original))) throw new Exception("Character composition does not repeat");
        var text = compiler.Disassemble(output);
        // Besides the lit fixture: one in clear air, and one whose shadows' light is nearly side-on to the surface.
        var side = Vector3.Normalize(new(0.9f, -0.15f, 0.25f));
        var lights = new[] { Light, Clear, Clear with { Sun = [side.X, side.Y, side.Z] } };
        var texts = lights.Select(l => l == Light ? text : compiler.Disassemble(compiler.Assemble(CharacterShader.Compose(module, vertex, Slots, Targets, colours, l), original))).ToArray();

        bool grazes = false, mirrors = false, facesAway = false, facesLight = false, turns = false, glows = false;
        foreach (var light in lights)
        foreach (bool older in new[] { false, true })
        foreach (var metallic in new[] { new Vector3(0.3f, 0.35f, 0.25f), new Vector3(0.9f, 1f, 0.8f) })
        foreach (bool front in new[] { true, false })
            foreach (var view in new[] { new Vector3(0.4f, 1.5f, -1.2f), new Vector3(-3f, 2f, 4f), new Vector3(9f, 1f, -8f) })
                foreach (var uv in new[] { new Vector2(0.37f, 1.21f), new Vector2(0.05f, 0.4f), new Vector2(0.3f, 0.3473f) })
                {
                    Metallic = metallic; Older = older;
                    var inputs = new Dictionary<(string, int), float[]>
                    {
                        [("NORMAL", 0)] = [0.3f, 0.9f, -0.2f, 0], [("TANGENT", 0)] = [0.8f, -0.1f, 0.5f, 1], [("TEXCOORD", 0)] = [uv.X, uv.Y, 0, 0],
                        [("TEXCOORD", 4)] = [0.75f], [("TANGENT", 8)] = [view.X, view.Y, view.Z, view.Length() * 0.9f],
                    };
                    var host = Evaluate(module, inputs, front);
                    var actual = Evaluate(texts[Array.IndexOf(lights, light)], inputs, front);

                    Vector3 surface = Vector3.Normalize(new(0.3f, 0.9f, -0.2f)) * (front ? 1 : -1), tangent = Vector3.Normalize(new(0.8f, -0.1f, 0.5f));
                    var bitangent = Vector3.Normalize(Vector3.Cross(Vector3.Normalize(new(0.3f, 0.9f, -0.2f)), tangent));
                    var relief = Texel(2, Tile(uv, 2));
                    float x = relief.X * 2 - 1, y = relief.Y * 2 - 1;
                    var normal = Vector3.Normalize(bitangent * x + tangent * y + surface * MathF.Sqrt(1 - Math.Clamp(x * x + y * y, 0, 1)));
                    // The unedited fixture anchors the evaluator itself.
                    Near(host, 0, normal * 0.5f + new Vector3(0.5002f), "evaluator disagrees with the authored fixture");

                    // The source's character light, restated from its own terms.
                    var colour = Texel(0, Tile(uv, 0)); var specular = Texel(1, Tile(uv, 1));
                    // The source reads a specular map one of two ways, as its definition says: roughness, metal, a reflecting
                    // level and openness; or, the older way, a reflection colour with roughness in the fourth channel, on a
                    // surface that scatters all of its colour.
                    float rough = older ? specular.W : specular.X, metal = older ? 0 : specular.Y, level = specular.Z, open = older ? 1 : specular.W;
                    var tint = Vector3.Lerp(Plain, Metallic, metal);
                    var raw = new Vector3(colour.X, colour.Y, colour.Z) * tint;
                    var basis = new Vector3(MathF.Pow(raw.X, 2.2f), MathF.Pow(raw.Y, 2.2f), MathF.Pow(raw.Z, 2.2f));
                    var diffuse = (1 - metal) * basis * open;
                    float dull = Math.Clamp(0.2f * level * MathF.Max(Vector3.Dot(Metallic, new(0.2126729f, 0.7151522f, 0.072175f)), 0.001f), 0, 1);
                    var mapped = Vector3.Clamp(new Vector3(specular.X, specular.Y, specular.Z) * Metallic, Vector3.Zero, Vector3.One);
                    var facing = older ? new Vector3(MathF.Pow(mapped.X, 2.2f), MathF.Pow(mapped.Y, 2.2f), MathF.Pow(mapped.Z, 2.2f))
                        : Vector3.Clamp(open * Vector3.Lerp(new(dull), basis, metal), Vector3.Zero, Vector3.One);
                    float grazing = MathF.Min(50 * 0.33f * (facing.X + facing.Y + facing.Z), 1);
                    grazes |= grazing < 0.9f; mirrors |= grazing == 1;
                    var eye = Vector3.Normalize(view); float distance = view.Length();
                    float towards = Vector3.Dot(normal, eye);
                    var mirrored = 2 * towards * normal - eye;
                    float sharp = Math.Clamp(1 - rough, 0, 1);
                    var looked = (rough + MathF.Sqrt(sharp)) * sharp * (mirrored - normal) + normal;
                    float horizon = Math.Clamp(1.3f * Vector3.Dot(surface, mirrored) + 1, 0, 1); horizon *= horizon;
                    float blur = light.SkyLevels - 3 + 1.2f * MathF.Log2(rough);
                    var table = Table(new(rough * 255 / 256 + 0.5f / 256, Math.Clamp(towards, 0, 1) * 255 / 256 + 0.5f / 256));
                    var sky = Sky(looked, blur) * new Vector3(light.Sky) * horizon * (facing * table.X + new Vector3(grazing * table.Y));
                    var lit = (Cube(normal) * new Vector3(light.Diffuse) * diffuse + sky) * open;
                    // Shadow leaves the hemisphere's light alone, and what the surface gives off of itself: ten times its
                    // colour where its specular map is not open, by the source's own multiplier.
                    var glow = 10 * (1 - open) * light.Glow * basis;
                    glows |= glow.Length() > 0.01f;
                    var steady = Vector3.Lerp(new(light.Lower), new(light.Upper), normal.Y * 0.5f + 0.5f) * diffuse + glow;
                    // Shade reaches a set distance from the camera and fades out over the last of it.
                    float reach = Math.Clamp((light.ShadeStart + light.ShadeLength - distance) / light.ShadeLength, 0, 1);
                    // A surface turned from the light that casts the shadows is in its own shadow, by its relief-bent normal:
                    // wholly once it is side-on, not at all a fifth of the way towards the light.
                    float turned = Math.Clamp((light.ShadeTurn - Vector3.Dot(normal, new Vector3(light.Sun))) * light.ShadeEdge, 0, 1);
                    facesAway |= turned == 1; facesLight |= turned == 0; turns |= turned is > 0.1f and < 0.9f;
                    // The source darkens its sky light by the square of what the shadow leaves, then passes the whole through
                    // its air. The host supplies the shadow later, so the stage emits the wholly shadowed light and stores
                    // what a surface out of the host's shadow shows above that.
                    Vector3 Shown(float shadowed)
                    {
                        var left = Vector3.One - new Vector3(light.Shade) * Share * reach * MathF.Min(shadowed + turned, 1);
                        return Aired(light.Air, lit * left * left + steady, view, view.Length() * 0.9f);
                    }
                    var emitted = Shown(1);
                    var stored = Shown(0) - emitted;
                    Near(actual, 0, normal * 0.5f + new Vector3(0.5002f), "stored normal changed");
                    Near(actual, 2, stored, "stored colour is not what the host's shadow removes");
                    Near(actual, 4, emitted, "emitted light");
                    // The mark: whole metal and no gloss.
                    if (actual[(4, 3)] != -2 || actual[(1, 0)] != 1 || actual[(1, 1)] != 0 || actual[(1, 2)] != host[(1, 2)]) throw new Exception("Character mark differs");
                    foreach (int target in new[] { 0, 1, 2, 3 })
                        if (actual[(target, 3)] != host[(target, 3)]) throw new Exception("A target's fourth value changed");
                }

        if (!grazes || !mirrors) throw new Exception("The check's texels do not cover both a dull and a mirror-like grazing reflection");
        if (!facesAway || !facesLight || !turns) throw new Exception("The check's surfaces do not face towards, away from and side-on to the light");
        if (!glows) throw new Exception("The check's texels do not cover a surface that glows");
        Reject(() => CharacterShader.Compose(composed, vertex, Slots, Targets, colours, Light), "already");
        Reject(() => CharacterShader.Compose(module, plain, Slots, Targets, colours, Light), "semantic");
        Reject(() => CharacterShader.Compose(module, vertex, Slots with { Sky = 2 }, Targets, colours, Light), "slot");
        Reject(() => CharacterShader.Compose(module, vertex, Slots with { Specular = 7 }, Targets, colours, Light), "slot");
        Reject(() => CharacterShader.Compose(module, vertex, Slots, Targets, colours, Light with { Shade = [0.5f, 1.5f, 0.5f] }), "shadow");
        Reject(() => CharacterShader.Compose(module, vertex, Slots, Targets, colours, Light with { ShadeEdge = 0 }), "edge");
        Reject(() => CharacterShader.Compose(module, vertex, Slots, Targets, colours, Light with { Sun = [0.5f, 0.5f, 0.5f] }), "unit");
        Reject(() => CharacterShader.Compose(module, vertex, Slots, Targets, colours with { Register = 5 }, Light), "constant");
        Console.WriteLine("PASS: a character's deferred stage emits the source's wholly shadowed light through its air, stores what the host's shadow removes, and marks itself");
    }

    private static Vector3 Aired(AgeOfGold.Params.SourceAir.Values? air, Vector3 light, Vector3 toCamera, float depth)
    {
        if (air is null) return light;
        float distance = toCamera.Length(), cosine = Vector3.Dot(toCamera / distance, new Vector3(air.SunDirection));
        float fog = Math.Clamp(Math.Clamp((depth - air.FogBegin) * air.FogScale, 0, 1) * air.FogLimit, 0, 1);
        float mie = air.Phase[0] / MathF.Pow(air.Phase[1] - air.Phase[2] * cosine, 1.5f);
        var result = new float[3];
        for (int c = 0; c < 3; c++)
        {
            float encoded = MathF.Pow(light[c], 1 / 2.2f);
            encoded += (air.FogColour[c] - encoded) * fog;
            float through = MathF.Pow(2, -distance * air.Extinction[c] * air.Distance * 2.0813689f);
            float added = (air.BetaDashRayleigh[c] * (1 + cosine * cosine) + air.BetaDashMie[c] * mie) * (1 - through) / air.Extinction[c] * air.ReflectanceStrength * air.SunColour[c];
            encoded += (encoded * through * air.Reflectance[c] + added - encoded) * air.Blend;
            result[c] = MathF.Pow(encoded, 2.2f);
        }
        return new(result);
    }

    private static Vector2 Tile(Vector2 uv, int slot) => new(uv.X * (1 + slot * 0.25f) + slot * 0.0625f, uv.Y * (2 - slot * 0.125f) - slot * 0.03125f);

    private static Vector4 Texel(int slot, Vector2 at)
    {
        float Wave(float phase) => 0.5f + 0.45f * MathF.Sin(at.X * 3.1f + at.Y * 1.7f + phase + slot);
        // The specular map's levels are low enough that a dull, plain texel reflects less at a grazing angle than a mirror would.
        return slot == 2 ? new(0.5f + 0.2f * MathF.Sin(at.X * 2 + 1), 0.5f + 0.15f * MathF.Cos(at.Y * 3), Wave(2), 1) : new(Wave(0), Wave(1.3f), Wave(2.6f) * (slot == 1 ? 0.2f : 1), Wave(3.9f));
    }

    private static Vector3 Cube(Vector3 along) => new Vector3(0.5f) + 0.25f * along;
    private static Vector3 Sky(Vector3 along, float level) => new Vector3(0.4f + 0.2f * along.X, 0.5f + 0.1f * along.Y, 0.6f - 0.2f * along.Z) * (1 + 0.1f * level);
    private static Vector2 Table(Vector2 at) => new(0.3f + 0.5f * at.X, 0.2f + 0.4f * at.Y);

    private static Dictionary<(int, int), float> Evaluate(string module, Dictionary<(string, int), float[]> inputs, bool front)
    {
        var signatures = new ShaderSignatures(module, "ps");
        var byId = inputs.Where(p => signatures.Members(false).Any(m => m.Name == p.Key.Item1 && m.Index == p.Key.Item2))
            .ToDictionary(p => signatures.Find(false, p.Key.Item1, p.Key.Item2).Id, p => p.Value);
        int face = signatures.Find(false, "SV_IsFrontFace", 0).Id;
        var evaluator = new ShaderEvaluator
        {
            Input = (id, column) => id == face ? front ? -1 : 0 : byId[id][column],
            Constants = (register, index) => register switch
            {
                1 => [1 + index * 0.25f, 2 - index * 0.125f, index * 0.0625f, -index * 0.03125f],
                3 => index == 0 ? [Plain.X, Plain.Y, Plain.Z, 1] : index == 9 ? [Metallic.X, Metallic.Y, Metallic.Z, 1] : index == 10 ? [Older ? 1 : 0, Share, 0, 0] : throw new Exception("Unexpected material constant"),
                _ => throw new Exception("Unexpected constant register"),
            },
            Sample = (slot, at, _) =>
            {
                if (slot > 2 || at.Length != 2) throw new Exception("A surface map was sampled as something else");
                var texel = Texel(slot, new(at[0], at[1]));
                return [texel.X, texel.Y, texel.Z, texel.W];
            },
            SampleLevel = (slot, at, level) =>
            {
                Vector3 along = at.Length == 3 ? new(at[0], at[1], at[2]) : default;
                if (slot == 3 && at.Length == 3 && level == 0) { var c = Cube(along); return [c.X, c.Y, c.Z, 1]; }
                if (slot == 4 && at.Length == 3) { var c = Sky(along, level); return [c.X, c.Y, c.Z, 1]; }
                if (slot == 5 && at.Length == 2 && level == 0) { var t = Table(new(at[0], at[1])); return [t.X, t.Y, 0, 1]; }
                throw new Exception($"Unexpected sample of slot {slot} at level {level}");
            },
        };
        evaluator.Run(module);
        var targets = Enumerable.Range(0, 5).ToDictionary(i => signatures.Find(true, "SV_Target", i).Id);
        return evaluator.Outputs.ToDictionary(p => (targets[p.Key.Id], p.Key.Column), p => p.Value);
    }

    private static void Near(Dictionary<(int, int), float> actual, int target, Vector3 expected, string what)
    {
        var value = new Vector3(actual[(target, 0)], actual[(target, 1)], actual[(target, 2)]);
        if (!(Vector3.Distance(value, expected) <= 1e-4f * MathF.Max(1, expected.Length())))
            throw new Exception($"Character stage check failed: {what}: {value} != {expected}");
    }

    private static void Reject(Action action, string reason)
    {
        try { action(); }
        catch (InvalidDataException e) when (e.Message.Contains(reason, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new Exception("Expected character shader rejection: " + reason);
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new Exception("Set " + name);
}
