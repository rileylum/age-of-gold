using System.Globalization;
using System.Text.RegularExpressions;

namespace AgeOfGold.Shaders;

/// <summary>
/// Rewrites the installed host stages that accumulate a directional light and that multiply accumulated light by a
/// surface's colour. A build's character stores, as its colour, the part of the source's light that shadow darkens, and
/// marks itself with whole metal and no gloss (which the host would otherwise give no diffuse light at all). For a marked
/// surface the sun's stage accumulates its shadow alone and the colour stage multiplies the stored light back in, so the
/// frame shows the stored light times the host's own shadow, whatever the surface's shape or the viewing angle.
/// The host keeps a screen's worth of shadow in one texture: screen-space occlusion and a value of the material's in
/// its first two values, of which a stage takes the lesser, and the sun's cast shadow in its fourth.
/// </summary>
internal static class LightShader
{
    /// <summary>
    /// What a marked surface's accumulated shadow is scaled up by, and its stored light down by. Beside it the baked light
    /// and point lights the host also accumulates on the surface come to less than a thousandth of themselves.
    /// </summary>
    internal const float Scale = 1024;

    private const string Name = @"%[\w.$-]+";
    // Whole metal and no gloss, each to within half a step of an eight-bit target.
    private static readonly string Metal = Float(254.5f / 255), Gloss = Float(0.5f / 255);

    /// <summary>
    /// The stage for one directional light: a marked surface takes the shadow alone, scaled up, while the pixel's light set
    /// has its light on; no highlight. Where the stage itself reads the cast shadow, which the host mixes in by a strength
    /// of the scene's, a marked surface takes it whole: the source's own strength is already in what the surface stores.
    /// </summary>
    internal static string ShadeMarked(string module)
    {
        var stage = new Stage(module);
        string surface = stage.Texel("g_GBuffer1Texture"), shadow = stage.Texel("g_ShadowTexture");
        // The shadow is the lesser of the mask's first two values, as the host itself takes it.
        var least = stage.Lines.Select(l => Regex.Match(l.Split(';')[0].Trim(), $@"^({Name}) = call float @dx\.op\.binary\.f32\(i32 36, float ({Name}), float ({Name})\)$"))
            .Where(m => m.Success && new[] { m.Groups[2].Value, m.Groups[3].Value }.Order().SequenceEqual(new[] { stage.Part(shadow, 0), stage.Part(shadow, 1) }.Order())).ToArray();
        if (least.Length != 1) throw new InvalidDataException("Light stage does not take the lesser of its shadow mask's two values once");
        // A light set's rows follow the number the pixel names, fourteen to a set; the fifth is the light's diffuse colour.
        var set = stage.One($@"^({Name}) = mul(?: nuw| nsw)* i32 {Name}, 14$", "light set");
        var row = stage.One($@"^({Name}) = add(?: nuw| nsw)* i32 {Regex.Escape(set)}, 4$", "light colour row");
        var colour = stage.One($@"^({Name}) = call %dx\.types\.CBufRet\.f32 @dx\.op\.cbufferLoadLegacy\.f32\(i32 59, %dx\.types\.Handle {Name}, i32 {Regex.Escape(row)}\)$", "light colour");
        var body = stage.Mark(surface);
        for (int c = 0; c < 3; c++) body.Add($"  %aog_light{c} = extractvalue %dx.types.CBufRet.f32 {colour}, {c}");
        body.Add("  %aog_lightsum = fadd fast float %aog_light0, %aog_light1");
        body.Add("  %aog_lighttotal = fadd fast float %aog_lightsum, %aog_light2");
        body.Add("  %aog_on = fcmp fast ogt float %aog_lighttotal, 0.000000e+00");
        string shade = least[0].Groups[1].Value;
        if (stage.Reads(shadow, 3) is { } cast) { body.Add($"  %aog_cast = fmul fast float {shade}, {cast}"); shade = "%aog_cast"; }
        body.Add($"  %aog_scaled = fmul fast float {shade}, {Float(Scale)}");
        body.Add("  %aog_shadow = select i1 %aog_on, float %aog_scaled, float 0.000000e+00");
        return stage.Rewrite(body, (target, _) => target == 0 ? "%aog_shadow" : target == 1 ? "0.000000e+00" : null, 2);
    }

    /// <summary>The stage that multiplies accumulated light by surface colour: a marked surface's stored light is scaled back down and applied whole.</summary>
    internal static string CombineMarked(string module)
    {
        var stage = new Stage(module);
        string surface = stage.Texel("g_GBuffer1Texture"), colour = stage.Texel("g_GBuffer2Texture");
        var body = stage.Mark(surface);
        for (int c = 0; c < 3; c++)
        {
            body.Add($"  %aog_stored{c} = extractvalue %dx.types.ResRet.f32 {colour}, {c}");
            body.Add($"  %aog_light{c} = fmul fast float %aog_stored{c}, {Float(1 / Scale)}");
        }
        return stage.Rewrite(body, (target, column) => target == 0 ? "%aog_light" + column : null, 1);
    }

    private sealed class Stage
    {
        internal readonly List<string> Lines;
        private readonly ShaderSignatures signatures;
        private readonly int start, end;
        private readonly Dictionary<string, int> registers = new(StringComparer.Ordinal);

        internal Stage(string module)
        {
            if (module.Contains("%aog_", StringComparison.Ordinal)) throw new InvalidDataException("Light stage is already rewritten");
            signatures = new ShaderSignatures(module, "ps");
            Lines = signatures.Lines.ToList();
            start = Lines.FindIndex(l => l == $"define void @{signatures.Entry}() {{");
            end = Lines.FindIndex(start + 1, l => l == "}");
            if (start < 0 || end < 0) throw new InvalidDataException("Expected one light stage entry function");
            // The disassembly lists each texture's name beside the register it is bound to.
            foreach (string line in Lines)
            {
                var bound = Regex.Match(line, @"^; (\w+)\s+texture\s+\S+\s+\S+\s+T\d+\s+t(\d+)\s+1$");
                if (bound.Success) registers[bound.Groups[1].Value] = int.Parse(bound.Groups[2].Value, CultureInfo.InvariantCulture);
            }
        }

        private string Code(int line) => Lines[line].Split(';')[0].Trim();

        internal string One(string pattern, string what)
        {
            var matches = Enumerable.Range(start + 1, end - start - 1).Select(i => Regex.Match(Code(i), pattern)).Where(m => m.Success).ToArray();
            return matches.Length == 1 ? matches[0].Groups[1].Value : throw new InvalidDataException("Light stage does not have exactly one " + what);
        }

        /// <summary>The one texel the stage reads of a named texture.</summary>
        internal string Texel(string texture)
        {
            if (!registers.TryGetValue(texture, out int register)) throw new InvalidDataException("Light stage does not bind " + texture);
            string handle = One($@"^({Name}) = call %dx\.types\.Handle @dx\.op\.createHandle\(i32 57, i8 0, i32 \d+, i32 {register}, i1 false\)$", "handle for " + texture);
            return One($@"^({Name}) = call %dx\.types\.ResRet\.f32 @dx\.op\.(?:textureLoad\.f32\(i32 66|sample\.f32\(i32 60), %dx\.types\.Handle {Regex.Escape(handle)},", "read of " + texture);
        }

        /// <summary>The value the stage takes of a texel's part, if it takes it at all.</summary>
        internal string? Reads(string texel, int part)
        {
            var taken = Enumerable.Range(start + 1, end - start - 1).Select(i => Regex.Match(Code(i), $@"^({Name}) = extractvalue %dx\.types\.ResRet\.f32 {Regex.Escape(texel)}, {part}$")).Where(m => m.Success).ToArray();
            return taken.Length == 0 ? null : taken.Length == 1 ? taken[0].Groups[1].Value : throw new InvalidDataException("Light stage takes one value of its shadow mask more than once");
        }

        /// <summary>The value the stage takes of a texel's part.</summary>
        internal string Part(string texel, int part) => One($@"^({Name}) = extractvalue %dx\.types\.ResRet\.f32 {Regex.Escape(texel)}, {part}$", "use of a texel's part");

        /// <summary>Works out whether the surface is marked: whole metal and no gloss.</summary>
        internal List<string> Mark(string surface) =>
        [
            $"  %aog_metal = extractvalue %dx.types.ResRet.f32 {surface}, 0",
            $"  %aog_gloss = extractvalue %dx.types.ResRet.f32 {surface}, 1",
            $"  %aog_whole = fcmp fast ogt float %aog_metal, {Metal}",
            $"  %aog_dull = fcmp fast olt float %aog_gloss, {Gloss}",
            "  %aog_marked = and i1 %aog_whole, %aog_dull",
        ];

        /// <summary>Puts the working above the stage's final stores and makes each named target's colour the marked value where the surface is marked.</summary>
        internal string Rewrite(List<string> body, Func<int, int, string?> marked, int targets)
        {
            var stored = new Regex(@"^call void @dx\.op\.storeOutput\.f32\(i32 5, i32 (\d+), i32 0, i8 ([0-2]), float (\S+)\)$");
            var ids = Enumerable.Range(0, targets).ToDictionary(t => signatures.Find(true, "SV_Target", t).Id, t => t);
            var stores = new Dictionary<(int Target, int Column), (int Line, string Value)>();
            for (int i = start + 1; i < end; i++)
            {
                var store = stored.Match(Code(i));
                if (store.Success && ids.TryGetValue(int.Parse(store.Groups[1].Value, CultureInfo.InvariantCulture), out int target)) stores[(target, store.Groups[2].Value[0] - '0')] = (i, store.Groups[3].Value);
            }
            if (stores.Count != targets * 3) throw new InvalidDataException("Light stage does not write each target's colour");
            int first = stores.Values.Min(s => s.Line);
            // The final stores sit in the stage's last block, which everything read in its first block reaches.
            if (Enumerable.Range(first, end - first).Any(i => Regex.IsMatch(Lines[i], @"^; <label>:|^[\w.$-]+:"))) throw new InvalidDataException("Light stage's final stores are not in its last block");
            int firstLabel = Enumerable.Range(start + 1, end - start - 1).FirstOrDefault(i => Regex.IsMatch(Lines[i], @"^; <label>:|^[\w.$-]+:"), end);
            foreach (string read in body.SelectMany(l => Regex.Matches(l[(l.IndexOf('=') + 1)..], Name).Select(m => m.Value)).Where(n => !n.StartsWith("%aog_", StringComparison.Ordinal) && !n.StartsWith("%dx.", StringComparison.Ordinal)).Distinct())
                if (Lines.FindIndex(start + 1, l => l.Split(';')[0].Trim().StartsWith(read + " = ", StringComparison.Ordinal)) is var made && (made < 0 || made > firstLabel)) throw new InvalidDataException("Light stage reads a needed value on a conditional path");
            var result = new List<string>();
            for (int i = 0; i < Lines.Count; i++)
            {
                if (i == first) result.AddRange(body);
                var key = stores.FirstOrDefault(s => s.Value.Line == i);
                if (key.Value.Line == i && i >= first && marked(key.Key.Target, key.Key.Column) is { } value)
                {
                    string chosen = $"%aog_out{key.Key.Target}{key.Key.Column}";
                    result.Add($"  {chosen} = select i1 %aog_marked, float {value}, float {key.Value.Value}");
                    result.Add($"  call void @dx.op.storeOutput.f32(i32 5, i32 {signatures.Find(true, "SV_Target", key.Key.Target).Id}, i32 0, i8 {key.Key.Column}, float {chosen})");
                }
                else result.Add(Lines[i]);
            }
            return string.Join('\n', result) + "\n";
        }
    }

    private static string Float(float value) => "0x" + BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture);
}
