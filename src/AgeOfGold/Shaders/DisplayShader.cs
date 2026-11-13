using System.Globalization;
using AgeOfGold.Params;
using System.Text.RegularExpressions;

namespace AgeOfGold.Shaders;

/// <summary>Rewrites the installed host tone-map stage to show the scene as the source game displays it.</summary>
internal static class DisplayShader
{
    private const string Name = @"%[\w.$-]+";

    /// <summary>
    /// The source multiplies its linear scene by an exposure, clamps it and shows it through the display's gamma, then
    /// corrects contrast about mid-grey, brightness and saturation. The host's colour scale, tone curve, bloom and
    /// colour grade no longer reach the output.
    /// </summary>
    // lazy: one fixed exposure and standard-range output; the source's adaptation and bloom are separate translations.
    // The correction's order and grey weights are inferred: they reproduce the source's displayed values in matched captures.
    internal static string DisplayLikeSource(string module, float exposure, SourceAtmosphereReader.Correction correction)
    {
        if (correction.Hue != 0) throw new InvalidDataException("Source hue rotation is not translated");
        if (correction.Brightness.Concat(correction.Contrast).Append(correction.Saturation).Any(v => !float.IsFinite(v) || v < 0))
            throw new InvalidDataException("Invalid source tone correction");
        if (!(exposure > 0) || !float.IsFinite(exposure)) throw new InvalidDataException("Display exposure must be positive");
        if (module.Contains("%aog_", StringComparison.Ordinal)) throw new InvalidDataException("Display stage is already rewritten");
        var signatures = new ShaderSignatures(module, "ps");
        int target = signatures.Find(true, "SV_Target", 0).Id;
        var lines = signatures.Lines.ToList();
        int start = lines.FindIndex(l => l == $"define void @{signatures.Entry}() {{");
        int end = lines.FindIndex(start + 1, l => l == "}");
        if (start < 0 || end < 0) throw new InvalidDataException("Expected one display entry function");
        string Code(int line) => lines[line].Split(';')[0].Trim();
        var instructions = new Dictionary<string, (int Line, string Text)>();
        for (int i = start + 1; i < end; i++)
        {
            var match = Regex.Match(Code(i), $"^({Name}) = (.+)$");
            if (match.Success) instructions[match.Groups[1].Value] = (i, match.Groups[2].Value);
        }
        int firstLabel = Enumerable.Range(start + 1, end - start - 1).FirstOrDefault(i => Regex.IsMatch(lines[i], @"^; <label>:|^[\w.$-]+:"), end);
        // The scene is the one texture whose channels are each scaled by a different component of one constant: the host's
        // exposed colour scale. Bloom is added with a single strength.
        var scenes = new List<string>();
        foreach (var (name, sample) in instructions.Where(p => Regex.IsMatch(p.Value.Text, @"^call %dx\.types\.ResRet\.f32 @dx\.op\.sample(Level)?\.f32\(")))
        {
            var channels = instructions.Where(p => Regex.IsMatch(p.Value.Text, $@"^extractvalue %dx\.types\.ResRet\.f32 {Regex.Escape(name)}, [0-2]$")).Select(p => p.Key).ToArray();
            var scales = channels.SelectMany(channel => instructions.Values.Select(i => Regex.Match(i.Text, $"^fmul(?: fast)? float ({Name}), ({Name})$"))
                .Where(m => m.Success && (m.Groups[1].Value == channel || m.Groups[2].Value == channel))
                .Select(m => m.Groups[1].Value == channel ? m.Groups[2].Value : m.Groups[1].Value)).ToArray();
            var constants = scales.Select(scale => instructions.TryGetValue(scale, out var i)
                ? Regex.Match(i.Text, $@"^extractvalue %dx\.types\.CBufRet\.f32 ({Name}), ([0-2])$") : Match.Empty).ToArray();
            if (channels.Length == 3 && scales.Length == 3 && constants.All(m => m.Success) && constants.Select(m => m.Groups[1].Value).Distinct().Count() == 1
                && constants.Select(m => m.Groups[2].Value).Distinct().Count() == 3 && sample.Line < firstLabel) scenes.Add(name);
        }
        if (scenes.Count != 1) throw new InvalidDataException("Display stage does not expose exactly one scene texture");
        var stored = new Regex($@"^call void @dx\.op\.storeOutput\.f32\(i32 5, i32 {target}, i32 0, i8 (?<column>[0-2]), float (?<value>\S+)\)$");
        var stores = new Dictionary<int, int>();
        for (int i = start + 1; i < end; i++)
        {
            var store = stored.Match(Code(i));
            if (store.Success) stores[store.Groups["column"].Value[0] - '0'] = i;
        }
        int lastLabel = Enumerable.Range(start + 1, end - start - 1).LastOrDefault(i => Regex.IsMatch(lines[i], @"^; <label>:|^[\w.$-]+:"), start);
        if (stores.Count != 3 || stores.Values.Any(line => line < lastLabel)) throw new InvalidDataException("Display stage does not write its colour in its final block");
        string Float(float value) => "0x" + BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture);
        var body = new List<string>();
        for (int c = 0; c < 3; c++)
        {
            body.Add($"  %aog_scene{c} = extractvalue %dx.types.ResRet.f32 {scenes[0]}, {c}");
            body.Add($"  %aog_exposed{c} = fmul fast float %aog_scene{c}, {Float(exposure)}");
            body.Add($"  %aog_clamped{c} = call float @dx.op.unary.f32(i32 7, float %aog_exposed{c})");
            body.Add($"  %aog_log{c} = call float @dx.op.unary.f32(i32 23, float %aog_clamped{c})");
            body.Add($"  %aog_gamma{c} = fmul fast float %aog_log{c}, {Float(1 / 2.2f)}");
            body.Add($"  %aog_shown{c} = call float @dx.op.unary.f32(i32 21, float %aog_gamma{c})");
            body.Add($"  %aog_centred{c} = fadd fast float %aog_shown{c}, -5.000000e-01");
            body.Add($"  %aog_contrast{c} = call float @dx.op.tertiary.f32(i32 46, float %aog_centred{c}, float {Float(correction.Contrast[c])}, float 5.000000e-01)");
            body.Add($"  %aog_bright{c} = fmul fast float %aog_contrast{c}, {Float(correction.Brightness[c])}");
        }
        body.Add($"  %aog_grey = call float @dx.op.dot3.f32(i32 55, float %aog_bright0, float %aog_bright1, float %aog_bright2, float {Float(0.299f)}, float {Float(0.587f)}, float {Float(0.114f)})");
        for (int c = 0; c < 3; c++)
        {
            body.Add($"  %aog_chroma{c} = fsub fast float %aog_bright{c}, %aog_grey");
            body.Add($"  %aog_saturated{c} = call float @dx.op.tertiary.f32(i32 46, float %aog_chroma{c}, float {Float(correction.Saturation)}, float %aog_grey)");
            body.Add($"  %aog_final{c} = call float @dx.op.unary.f32(i32 7, float %aog_saturated{c})");
            lines[stores[c]] = $"  call void @dx.op.storeOutput.f32(i32 5, i32 {target}, i32 0, i8 {c}, float %aog_final{c})";
        }
        lines.InsertRange(stores.Values.Min(), body);
        foreach (var (function, declaration) in new[] { ("unary.f32", "declare float @dx.op.unary.f32(i32, float)"),
            ("tertiary.f32", "declare float @dx.op.tertiary.f32(i32, float, float, float)"),
            ("dot3.f32", "declare float @dx.op.dot3.f32(i32, float, float, float, float, float, float)") })
            if (!lines.Any(l => l.StartsWith("declare ", StringComparison.Ordinal) && l.Contains($"@dx.op.{function}(", StringComparison.Ordinal))) lines.Add(declaration);
        return string.Join('\n', lines) + "\n";
    }
}
